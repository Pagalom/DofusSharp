using System.Numerics;
using BestCrush.Network.Protocol;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

public sealed class LiveStockService : IDisposable
{
    private readonly LiveStockRepository repository;
    private readonly CurrentDofusClientsState clients;
    private readonly LiveDofusClientNetworkBindings? bindings;
    private readonly ForgemagingSessionStore? _forgemaging;
    private readonly ActivityCatalogService? _catalog;
    private readonly ILogger<LiveStockService>? _logger;
    private readonly HashSet<(string Source, string Container)> _reconciled = [];
    private readonly object _sync = new();
    private readonly string _run = Guid.NewGuid().ToString("N");
    private readonly Dictionary<(long Epoch,long Connection), (string Id, LiveStockBook Book, DofusClientWindowKey Window)> _flows = [];
    private readonly Dictionary<DofusClientWindowKey, string> _windows = [];
    private readonly Dictionary<string,LiveStockSource> _sources = [];
    private long _epoch = -1;
    private readonly Dictionary<DofusClientWindowKey,long> _latestConnections = [];

    public LiveStockService(LiveStockRepository repository, CurrentDofusClientsState clients, LiveDofusClientNetworkBindings? bindings = null,
        ForgemagingSessionStore? forgemaging = null, ILogger<LiveStockService>? logger = null, ActivityCatalogService? catalog = null)
    {
        this.repository = repository;
        this.clients = clients;
        this.bindings = bindings;
        _forgemaging = forgemaging;
        _logger = logger;
        _catalog = catalog;
        if (_forgemaging is not null) clients.Changed += OnClientsChanged;
    }
    public event Action? Changed;
    public IReadOnlyList<LiveStockSource> History => repository.ReadSources();
    public LiveStockSource? Selected => clients.SelectedClient is { } client ? ForWindow(client.Key) : null;
    internal LiveStockSource? SelectedForServer(string? server)
    {
        var source = Selected;
        return server is not null && string.Equals(source?.ServerName, server, StringComparison.OrdinalIgnoreCase) ? source : null;
    }

    // Read each live window independently so header indicators never borrow the selected character’s stock.
    public LiveStockSource? ForWindow(DofusClientWindowKey window)
    {
        if (!clients.Clients.Any(client => client.Key == window)) return null;
        lock (_sync)
        {
            if (!_windows.TryGetValue(window, out string? id)) return null;
            if (bindings is not null && (!_latestConnections.TryGetValue(window, out long connection) ||
                !bindings.IsCurrentConnection(_epoch, connection, window))) return null;
            return _sources.GetValueOrDefault(id);
        }
    }
    internal void Observe(DofusWireMessage message)
    {
        if (message.Direction != "S→C" || message.ConnectionId <= 0 || message.CandidateWindow is not { } window ||
            !LiveStockProtocol.IsStockOpcode(message.Key)) return;
        lock (_sync)
        {
            if (_epoch != message.CaptureEpoch)
            {
                _flows.Clear(); _windows.Clear(); _sources.Clear(); _latestConnections.Clear(); _reconciled.Clear(); _epoch = message.CaptureEpoch;
            }
            if (_latestConnections.TryGetValue(window,out long latest) && message.ConnectionId < latest) return;
            _latestConnections[window] = message.ConnectionId;
            var key = (message.CaptureEpoch,message.ConnectionId);
            if (!_flows.TryGetValue(key,out var flow))
            {
                if (_flows.Count >= 128) return;
                flow = ($"{_run}-{message.CaptureEpoch}-{message.ConnectionId}", new LiveStockBook(),window);
                _flows[key] = flow;
            }
            if (flow.Window != window) return;
            var state = flow.Book.Observe(message.Key,message.Body,message.ObservedAtUtc);
            if (state is null) return;
            var client = clients.Clients.FirstOrDefault(x=>x.Key==window);
            string label = client?.DisplayName ?? "Fenêtre Dofus";
            string? server = message.CaptureLease?.ServerName;
            if (server is not null && client is { HasCharacterTitle: true } &&
                (message.SyntheticWindowForReplay || bindings?.IsCurrentConnection(message.CaptureEpoch, message.ConnectionId, window) == true))
                Reconcile(flow.Id, label, server, state);
            repository.Save(flow.Id,label,server,state);
            _windows[window]=flow.Id;
            _sources[flow.Id]=new(flow.Id,label,server ?? _sources.GetValueOrDefault(flow.Id)?.ServerName,
                message.ObservedAtUtc,flow.Book.Containers);
        }
        Changed?.Invoke();
    }

    private void OnClientsChanged()
    {
        bool updated = false;
        lock (_sync)
        {
            foreach (var client in clients.Clients)
            {
                if (!client.HasCharacterTitle || ForWindow(client.Key) is not { ServerName: { } server } source ||
                    string.Equals(source.Label, client.DisplayName, StringComparison.OrdinalIgnoreCase)) continue;
                // The inventory can arrive while the title still displays the character selection screen.
                // Name an anonymous observation only while its original window and connection are still current.
                if (source.Label is not ("Client Dofus" or "Fenêtre Dofus")) continue;
                foreach (var container in source.Containers)
                {
                    Reconcile(source.Id, client.DisplayName, server, container);
                    repository.Save(source.Id, client.DisplayName, server, container);
                }
                _sources[source.Id] = source with { Label = client.DisplayName };
                updated = true;
            }
        }
        if (updated) Changed?.Invoke();
    }

    private void Reconcile(string source, string label, string server, LiveStockContainer state)
    {
        if (_forgemaging is null || !state.Complete || state.Stale || !_reconciled.Add((source, state.Container))) return;
        try
        {
            if (repository.PreviousContainer(source, label, server, state) is { } previous)
                _forgemaging.ReattachObserved(server, previous, state, _catalog?.ReadCachedForgemagingEffects());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException)
        {
            _reconciled.Remove((source, state.Container));
            _logger?.LogWarning(error, "Could not reconcile the observed equipment histories");
        }
    }
    public BigInteger? Quantity(long itemId, string container, string? server = null) =>
        (server is null ? Selected : SelectedForServer(server))?.QuantityIn(itemId, container);
    public void ClearLive()
    {
        lock(_sync) { _flows.Clear();_windows.Clear();_sources.Clear();_latestConnections.Clear();_reconciled.Clear(); }
        Changed?.Invoke();
    }
    public void Dispose() => clients.Changed -= OnClientsChanged;
}
