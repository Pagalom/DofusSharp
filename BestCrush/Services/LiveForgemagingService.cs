using System.Threading.Channels;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

public sealed record LiveForgemagingView(bool Enabled, string Status, ForgemagingSession? Session,
    IReadOnlyList<ForgemagingRune> Runes, bool IsLoading = false, ForgemagingCrushContext? Crushing = null)
{
    public bool CanTrack { get; init; }
    public long Activation { get; init; }
}

/// <summary>Automatic passive workshop observer. OS/socket identities and instance UIDs stay in RAM.</summary>
public sealed class LiveForgemagingService : IDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly CurrentDofusClientsState _clients;
    private readonly CurrentServerState _servers;
    private readonly LiveDofusClientNetworkBindings _bindings;
    private readonly ActivityCatalogService _catalog;
    private readonly ForgemagingSessionStore _store;
    private readonly ILogger<LiveForgemagingService> _logger;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _save = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<(long Generation, DofusWireMessage Message)> _queue = Channel.CreateBounded<(long, DofusWireMessage)>(512);
    private readonly Task _worker;
    private DofusClientWindowKey? _window;
    private string _server = "", _status = "Déposez un objet dans l’atelier de forgemagie pour activer le suivi en jeu.";
    private long _generation, _epoch = -1, _connection, _activation;
    private NetworkCaptureLease? _flowLease;
    private bool _syntheticReplay;
    private ulong _targetUid, _putUid;
    private ForgemagingWorkshopTracker? _tracker;
    private bool _isLoading;
    private ForgemagingSession? _session;
    private IReadOnlyList<ForgemagingRune> _runes = [];
    private ForgemagingCrushContext? _crushing;
    private Dictionary<ulong, ForgemagingEffectDefinition> _effects = [];
    public event Action? Changed;

    public LiveForgemagingService(IServiceScopeFactory scopes, CurrentDofusClientsState clients, CurrentServerState servers,
        LiveDofusClientNetworkBindings bindings, ActivityCatalogService catalog, ForgemagingSessionStore store,
        ILogger<LiveForgemagingService> logger)
    {
        _scopes = scopes; _clients = clients; _servers = servers; _bindings = bindings;
        _catalog = catalog; _store = store; _logger = logger;
        _clients.Changed += ClientChanged;
        _bindings.Changed += ContextChanged;
        _servers.ServerSelectionChanged += ContextChanged;
        _worker = Task.Run(ConsumeAsync);
    }
    public LiveForgemagingView View(string? server)
    {
        lock (_sync)
        {
            bool active = _window is not null && _clients.SelectedClient is { HasCharacterTitle: true } client && client.Key == _window &&
                string.Equals(server, _server, StringComparison.OrdinalIgnoreCase);
            bool current = active && _flowLease is { } lease && _servers.IsCaptureLeaseActive(lease) &&
                (_syntheticReplay || _bindings.IsCurrentConnection(_epoch, _connection, _window!.Value));
            string unavailable = _clients.SelectedClient?.HasCharacterTitle != true
                ? "Connectez et ciblez un personnage pour le suivi en jeu."
                : "Déposez un objet dans l’atelier de forgemagie sur le serveur du personnage pour activer le suivi en jeu.";
            return new(active, active ? _status : unavailable,
                current && _session is not null ? ForgemagingSessionStore.Copy(_session) : null, current ? _runes.ToArray() : [], current && _isLoading,
                current ? _crushing : null) { CanTrack = current && _tracker is not null && _session is not null, Activation = _activation };
        }
    }
    public void Start(string server)
    {
        lock (_sync)
        {
            if (_clients.SelectedClient is not { } client || string.IsNullOrWhiteSpace(server))
                throw new ArgumentException("Ciblez votre fenêtre Dofus et son serveur avant de démarrer le suivi.");
            _window = client.Key; _server = server; _generation++; ResetFlow();
            _status = "En attente de l’atelier : retirez puis redéposez l’objet si nécessaire.";
        }
        Changed?.Invoke();
    }
    public void Stop()
    {
        lock (_sync) { _generation++; _window = null; ResetFlow(); }
        Changed?.Invoke();
    }
    public void ClearLive()
    {
        lock (_sync) { _generation++; ResetFlow(); _status = "Capture interrompue. Rouvrez l’atelier pour reprendre le suivi."; }
        Changed?.Invoke();
    }
    private void ResetFlow()
    {
        _epoch = -1; _connection = 0; _targetUid = _putUid = 0; _flowLease = null; _syntheticReplay = false;
        _session = null; _runes = []; _crushing = null; _tracker = null; _isLoading = false;
    }
    private void InvalidateJet()
    {
        _tracker?.Invalidate(); _tracker = null; _isLoading = false;
        if (_session is not null) { _session.Sink = null; foreach (var line in _session.Lines) line.Current = null; }
    }
    private void ClientChanged()
    {
        bool stop;
        lock (_sync) stop = _window is not null && _clients.SelectedClient?.Key != _window;
        if (stop) Stop(); else Changed?.Invoke();
    }
    private void ContextChanged() => Changed?.Invoke();
    private bool ValidCandidate(DofusWireMessage message) => _clients.SelectedClient is { HasCharacterTitle: true } client &&
        message.CandidateWindow == client.Key && message.ConnectionId > 0 && message.CaptureLease is { } lease &&
        _servers.IsCaptureLeaseActive(lease) &&
        (message.SyntheticWindowForReplay || _bindings.IsCurrentConnection(message.CaptureEpoch, message.ConnectionId, client.Key));
    private bool Valid(DofusWireMessage message) => ValidCandidate(message) && _window is { } window &&
        message.CandidateWindow == window && string.Equals(message.CaptureLease!.Value.ServerName, _server, StringComparison.OrdinalIgnoreCase);

    internal void Observe(DofusWireMessage message)
    {
        if (!ForgemagingWireDiagnostics.IsWorkshopMessage(message.Key) || message.Body.Length > 16384) return;
        bool overflow = false;
        lock (_sync)
        {
            if (!ValidCandidate(message) || message.CaptureLease is not { } lease) return;
            if (_window != message.CandidateWindow || !string.Equals(_server, lease.ServerName, StringComparison.OrdinalIgnoreCase) ||
                (_flowLease is { } previousLease && previousLease != lease))
            {
                // Only a real item-deposit request can arm a new automatic stream. A result alone is insufficient.
                if (message.Direction != "C→S" || message.Key != "jys") return;
                var fields = LiveForgemagingProtocol.Fields(message.Body);
                if (LiveForgemagingProtocol.Scalar(fields, 1) is not > 0 ||
                    LiveForgemagingProtocol.Scalar(fields, 3) is not (> 0 and <= long.MaxValue)) return;
                _window = message.CandidateWindow; _server = lease.ServerName;
                _generation++; ResetFlow(); _status = "En attente du jet de l’objet déposé dans l’atelier…";
            }
            if (!Valid(message)) return;
            if (!_queue.Writer.TryWrite((_generation, message)))
            { _generation++; ResetFlow(); _status = "Suivi interrompu : trop de messages. Retirez puis redéposez l’objet."; overflow = true; }
        }
        if (overflow) Changed?.Invoke();
    }
    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                try { await ProcessAsync(entry.Generation, entry.Message, _stop.Token); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Could not process workshop observation");
                    lock (_sync) { if (entry.Generation == _generation) { InvalidateJet(); _status = "Observation incomplète. Retirez puis redéposez l’objet pour reprendre."; } }
                    Changed?.Invoke();
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private async Task ProcessAsync(long generation, DofusWireMessage message, CancellationToken token)
    {
        lock (_sync) { if (generation != _generation || !Valid(message)) return; }
        await ForgemagingWireDiagnostics.WriteIfEnabledAsync(message, token);
        ForgemagingWireItem? load = null;
        bool save;
        lock (_sync)
        {
            if (generation != _generation || !Valid(message)) return;
            if (_epoch != message.CaptureEpoch || _connection != message.ConnectionId)
            { ResetFlow(); _epoch = message.CaptureEpoch; _connection = message.ConnectionId; _flowLease = message.CaptureLease; _syntheticReplay = message.SyntheticWindowForReplay; }
            int attempts = _session?.Attempts.Count ?? 0;
            if (message.Direction == "C→S")
            {
                if (message.Key == "jys")
                {
                    var fields = LiveForgemagingProtocol.Fields(message.Body);
                    _putUid = LiveForgemagingProtocol.Scalar(fields, 1) ?? 0;
                    if (_putUid == _targetUid && LiveForgemagingProtocol.Scalar(fields, 3) is { } rawQuantity && unchecked((long)rawQuantity) < 0)
                    { InvalidateJet(); _status = "Objet retiré de l’atelier. Déposez un objet pour reprendre le suivi."; }
                }
                else ApplyTrackingUpdate(_tracker?.Observe(message.Direction, message.Key, message.Body, message.ObservedAtUtc));
            }
            else if (message.Direction == "S→C")
            {
                if (message.Key == "kbr")
                { _tracker = null; _putUid = 0; _isLoading = false; _status = "Atelier fermé. Les résultats enregistrés sont conservés."; }
                else if (message.Key == "kch" && LiveForgemagingProtocol.WorkshopItem(message.Body) is { } observed)
                {
                    if (_runes.Any(r => r.Id == observed.Item.ItemId))
                    { ApplyTrackingUpdate(_tracker?.Observe(message.Direction, message.Key, message.Body, message.ObservedAtUtc)); }
                    else if (observed.Item.Uid == _putUid && observed.Item.Quantity == 1)
                    {
                        if (_targetUid != observed.Item.Uid) _session = null;
                        _targetUid = observed.Item.Uid; _tracker = null;
                        load = observed.Item; _isLoading = true;
                        _status = "Chargement du jet et des prix de runes…";
                    }
                }
                else ApplyTrackingUpdate(_tracker?.Observe(message.Direction, message.Key, message.Body, message.ObservedAtUtc));
            }
            save = (_session?.Attempts.Count ?? 0) > attempts;
        }
        Changed?.Invoke();
        if (load is not null)
        {
            var effects = _effects.Count > 0 ? _effects : (await _catalog.GetForgemagingEffectsAsync(token)).ToDictionary(e => e.Code);
            ForgemagingEquipmentData data;
            using (var scope = _scopes.CreateScope()) data = await scope.ServiceProvider.GetRequiredService<ForgemagingDataService>().LoadAsync(load.ItemId, message.CaptureLease!.Value.ServerName, token);
            lock (_sync)
            {
                if (generation != _generation || !Valid(message) || _targetUid != load.Uid) return;
                _effects = effects; _runes = data.Runes; _crushing = data.Crushing;
                if (Map(load) is not { } values) { InvalidateJet(); _status = "Jet partiellement reconnu. Le suivi attend un relevé complet."; }
                else
                {
                    string key = ForgemagedEquipmentStock.InstanceKey(_server, load.ItemId, load.Uid);
                    if (_session?.GameRecordingId != key)
                        _session = _store.GetObserved(_server, load.ItemId, key) ?? data.Session;
                    _session.GameRecordingId = key;
                    ApplyValues(_session, values);
                    _session.LastObservedRollAtUtc = message.ObservedAtUtc;
                    _session.Sink = LiveForgemagingProtocol.WorkshopItem(message.Body)!.Value.Sink;
                    _tracker = new(_session, load, _runes, Map); _isLoading = false; _activation++;
                    save = true;
                    _status = "Suivi actif : passez vos runes dans Dofus, les résultats sont enregistrés automatiquement.";
                }
            }
            Changed?.Invoke();
        }
        if (save) await SaveAsync(generation);
    }
    private void ApplyTrackingUpdate(ForgemagingTrackingUpdate? update)
    {
        if (update == ForgemagingTrackingUpdate.Gap)
        { InvalidateJet(); _status = "Suivi incomplet : retirez puis redéposez l’objet. Aucun résultat supplémentaire n’a été supposé."; }
        else if (update == ForgemagingTrackingUpdate.Recorded && _session?.Attempts.LastOrDefault() is { } attempt)
        {
            _session.LastObservedRollAtUtc = attempt.AtUtc;
            string result = attempt.Outcome switch { ForgemagingOutcome.Critical => "succès critique", ForgemagingOutcome.Neutral => "succès neutre",
                ForgemagingOutcome.Failure => "échec", _ => "résultat à confirmer" };
            _status = $"Dernier résultat : {result} · {attempt.Rune.Name} consommée. Jet et conseils actualisés.";
        }
    }

    private Dictionary<Characteristic, int>? Map(ForgemagingWireItem item)
        => ForgemagedEquipmentStock.MapEffects(item.Effects.Select(effect => (effect.Code, effect.Value)), _effects);
    private static void ApplyValues(ForgemagingSession session, IReadOnlyDictionary<Characteristic, int> values)
    {
        foreach (var stat in values.Keys.Except(session.Lines.Select(l => l.Characteristic))) session.Lines.Add(new() { Characteristic = stat });
        foreach (var line in session.Lines) line.Current = values.GetValueOrDefault(line.Characteristic);
    }
    public async Task UpdateGoalsAsync(ForgemagingSession edited)
    {
        long generation;
        lock (_sync)
        {
            if (_session is null || _session.GameRecordingId != edited.GameRecordingId) return;
            foreach (var goal in edited.Lines)
            {
                if (_session.Lines.FirstOrDefault(l => l.Characteristic == goal.Characteristic) is { } line) line.Target = goal.Target;
                else _session.Lines.Add(new() { Characteristic = goal.Characteristic, Current = 0, Target = goal.Target });
            }
            _session.Budget = edited.Budget; generation = _generation;
        }
        await SaveAsync(generation);
    }
    public async Task RefreshPricesAsync(CancellationToken token = default)
    {
        long generation, item; string server, recording;
        lock (_sync)
        {
            if (_session?.GameRecordingId is not { } id) return;
            generation = _generation; item = _session.ItemId; server = _server; recording = id; _isLoading = true;
        }
        Changed?.Invoke();
        try
        {
            using var scope = _scopes.CreateScope();
            var data = await scope.ServiceProvider.GetRequiredService<ForgemagingDataService>().LoadAsync(item, server, token);
            lock (_sync)
            {
                if (generation != _generation || _session?.GameRecordingId != recording) return;
                _runes = data.Runes; _crushing = data.Crushing; _tracker?.RefreshRunes(_runes);
            }
        }
        finally
        {
            lock (_sync) { if (generation == _generation) _isLoading = false; }
            Changed?.Invoke();
        }
    }
    private async Task SaveAsync(long generation)
    {
        await _save.WaitAsync(_stop.Token);
        try
        {
            ForgemagingSession snapshot; string server;
            lock (_sync) { if (generation != _generation || _session is null) return; snapshot = ForgemagingSessionStore.Copy(_session); server = _server; }
            _store.Save(server, snapshot);
        }
        finally { _save.Release(); }
    }
    public void Dispose()
    {
        _clients.Changed -= ClientChanged; _bindings.Changed -= ContextChanged; _servers.ServerSelectionChanged -= ContextChanged; _queue.Writer.TryComplete(); _stop.Cancel();
    }
}
