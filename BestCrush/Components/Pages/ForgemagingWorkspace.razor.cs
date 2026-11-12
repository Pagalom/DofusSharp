using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Components.Pages;

public partial class ForgemagingWorkspace : IDisposable
{
    [Parameter] public string? ServerName { get; set; }
    private readonly BackgroundDataLoad<IReadOnlyList<ForgemagingEquipmentChoice>> _catalogLoad = new();
    private readonly BackgroundDataLoad<ForgemagingEquipmentData?> _itemLoad = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private IReadOnlyList<ForgemagingEquipmentChoice> _catalog = [];
    private IReadOnlyList<ForgemagingRune> _runes = [];
    private IReadOnlyList<ForgemagingAdvice> _advice = [];
    private ForgemagingCoachingPlan _coaching = new([], [], null);
    private ForgemagingCrushContext? _crushContext;
    private ForgemagingCrushComparison? _crushComparison;
    private bool _prepareCrushing;
    private ForgemagingSession? _session;
    private ForgemagingAdvice? _pending;
    private List<ForgemagingLine> _actual = [];
    private ForgemagingOutcome? _outcome;
    private double? _attemptPrice;
    private Characteristic? _extraStat;
    private string _search = "", _loadedServer = "";
    private long _selectedItem;
    private string? _error, _saveError, _validation, _attemptError;
    private bool _disposed, _pricesDirty, _confirmedAfter;
    private int _priceNoticeQueued;
    private LiveForgemagingService? _live;
    private bool _gameTracking, _liveLoading, _canGameTrack;
    private long _seenLiveActivation = -1;
    private readonly SemaphoreSlim _trackingGate = new(1, 1);
    private bool CanTrackManually => !_catalogLoad.IsLoading && _catalogLoad.Error is null;
    private string? _liveStatus;
    private bool Busy => _catalogLoad.IsLoading || _itemLoad.IsLoading || (_gameTracking && _liveLoading);
    private IEnumerable<ForgemagingEquipmentChoice> Choices => _catalog.Where(e => e.Name.Contains(_search.Trim(), StringComparison.CurrentCultureIgnoreCase))
        .Take(50).Concat(_catalog.Where(e => e.Id == _selectedItem)).DistinctBy(e => e.Id).OrderBy(e => e.Name);
    private IEnumerable<ForgemagingAdvice> BestByLine => _coaching.Steps.Select(s => s.Advice);
    private bool Complete => _session is { } session && _validation is null && session.Lines.All(l => l.Current >= l.Target);

    protected override async Task OnInitializedAsync()
    {
        PriceChanges.Changed += PricesChanged;
        using (var scope = DataScopes.CreateScope()) _live = scope.ServiceProvider.GetService<LiveForgemagingService>();
        if (_live is not null) _live.Changed += LiveChanged;
        bool loaded = await _catalogLoad.RunAsync(async token =>
        {
            using var scope = DataScopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ForgemagingDataService>().GetCatalogAsync(token);
        });
        if (_disposed) return;
        if (loaded) _catalog = _catalogLoad.Value ?? [];
        else _error = "Le catalogue des équipements n’a pas pu être chargé.";
    }
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedServer == (ServerName ?? "")) return;
        _loadedServer = ServerName ?? "";
        _gameTracking = false; _canGameTrack = false; _seenLiveActivation = -1;
        _itemLoad.Cancel(); _session = null; _pending = null; _selectedItem = 0;
        _advice = []; _runes = []; _crushContext = null; _crushComparison = null; _coaching = new([], [], null);
        _pricesDirty = false; Interlocked.Exchange(ref _priceNoticeQueued, 0); _error = _saveError = _attemptError = null;
        await SynchronizeTrackingAsync();
        if (!_gameTracking && !string.IsNullOrWhiteSpace(ServerName)) await LoadItemAsync();
    }
    private Task SelectItemAsync() => LoadItemAsync();
    private async Task SelectTrackingModeAsync(bool game)
    {
        await _trackingGate.WaitAsync();
        try
        {
            if (_disposed) return;
            var view = _live?.View(ServerName);
            _canGameTrack = view?.CanTrack == true;
            _liveStatus = view?.Status ?? "Le suivi en jeu n’est pas disponible.";
            if (game)
            {
                if (!_canGameTrack || _gameTracking) return;
                await SwitchToGameAsync(view!);
            }
            else if (CanTrackManually && _gameTracking) await SwitchToManualAsync();
        }
        finally { _trackingGate.Release(); }
    }
    private void LiveChanged()
    {
        if (!_disposed) _ = InvokeAsync(async () => { await SynchronizeTrackingAsync(); if (!_disposed) StateHasChanged(); });
    }
    private async Task SynchronizeTrackingAsync()
    {
        await _trackingGate.WaitAsync();
        try
        {
            if (_disposed) return;
            var view = _live?.View(ServerName);
            _canGameTrack = view?.CanTrack == true;
            _liveStatus = view?.Status ?? "Le suivi en jeu n’est pas disponible.";
            if (_gameTracking)
            {
                if (_canGameTrack) ReadLive(view!);
                else await SwitchToManualAsync();
            }
            else if (_canGameTrack && view!.Activation != _seenLiveActivation) await SwitchToGameAsync(view);
        }
        finally { _trackingGate.Release(); }
    }
    private async Task SwitchToGameAsync(LiveForgemagingView view)
    {
        string? server = ServerName;
        // Preserve the manual session before adopting the independent game recording.
        if (_session is not null && _session.GameRecordingId is null) await SaveAsync();
        if (_disposed || server != ServerName) return;
        view = _live?.View(ServerName) ?? view;
        _liveStatus = view.Status;
        if (!view.CanTrack) { _canGameTrack = false; return; }
        CancelAttempt(); _itemLoad.Cancel(); _error = null;
        _gameTracking = true; _seenLiveActivation = view.Activation; ReadLive(view);
    }
    private async Task SwitchToManualAsync()
    {
        CancelAttempt(); _itemLoad.Cancel(); _error = null;
        _gameTracking = false; _liveLoading = false; _session = null; _selectedItem = 0;
        _runes = []; _advice = []; _crushContext = null; _crushComparison = null; _coaching = new([], [], null);
        // Publish unavailability immediately; restoring the manual item can involve background reads.
        StateHasChanged();
        // Keep the passive observer running: choosing manual must not prevent the next workshop detection.
        await LoadItemAsync();
    }
    private void ReadLive(LiveForgemagingView view)
    {
        _liveStatus = view.Status; _session = view.Session; _runes = view.Runes;
        _crushContext = view.Crushing;
        _liveLoading = view.IsLoading;
        _selectedItem = _session?.ItemId ?? 0; Recalculate();
    }
    private async Task LoadItemAsync(bool refresh = false)
    {
        if (_disposed || _gameTracking || string.IsNullOrWhiteSpace(ServerName) || _pending is not null) return;
        string server = ServerName; long item = _selectedItem;
        var current = refresh && _session is { } edited ? ForgemagingSessionStore.Copy(edited) : null;
        _error = null;
        bool loaded = await _itemLoad.RunAsync(async token =>
        {
            var saved = current ?? Store.Get(server, item > 0 ? item : null);
            long id = item > 0 ? item : saved?.ItemId ?? 0;
            if (id == 0) return null;
            using var scope = DataScopes.CreateScope();
            var data = await scope.ServiceProvider.GetRequiredService<ForgemagingDataService>().LoadAsync(id, server, token);
            return saved is { } existing && existing.ItemId == id ? data with { Session = existing } : data;
        });
        if (_disposed || server != ServerName) return;
        if (loaded)
        {
            _session = _itemLoad.Value?.Session; _runes = _itemLoad.Value?.Runes ?? []; _crushContext = _itemLoad.Value?.Crushing;
            _selectedItem = _session?.ItemId ?? 0; _pricesDirty = false; Interlocked.Exchange(ref _priceNoticeQueued, 0); Recalculate();
        }
        else if (_itemLoad.Error is { } error)
        {
            Logger.LogWarning(error, "Could not load FM item for {Server}", server);
            _error = "L’équipement ou la session FM n’a pas pu être chargé. Les fichiers existants sont conservés.";
            _selectedItem = _session?.ItemId ?? 0;
        }
    }
    private async Task RefreshPricesAsync()
    {
        if (!_gameTracking || _live is null) { await LoadItemAsync(true); return; }
        try
        {
            await Task.Run(() => _live.RefreshPricesAsync()); _pricesDirty = false;
            Interlocked.Exchange(ref _priceNoticeQueued, 0); _error = null;
        }
        catch (Exception error) { Logger.LogWarning(error, "Could not refresh live FM prices"); _error = "Les prix n’ont pas pu être actualisés. Le suivi du jet est conservé."; }
    }
    private void Recalculate()
    {
        _validation = _session is { } session ? ForgemagingPlanner.Validate(session) : null;
        _advice = _session is not null && _validation is null ? ForgemagingPlanner.Recommend(_session, _runes) : [];
        _coaching = _session is not null ? ForgemagingCoach.Plan(_session, _runes) : new([], [], null);
        _crushComparison = _prepareCrushing && _session is not null ? ForgemagingCrushAssessment.Compare(_session, _runes, _crushContext) : null;
        if (_crushComparison is { IsComplete: true, PreparationCeiling: <= 0 })
            _coaching = new([], _coaching.Notices, "Conservez le jet actuel pour le concassage : les objectifs choisis n’ajoutent aucune valeur à financer par de la FM.");
    }
    private string FocusName(ForgemagingCrushValue value) => value.Focus is { } stat ? $"Focus {StatName(stat)}" : "Sans focus";
    private async Task EditedAsync()
    {
        Recalculate(); await SaveAsync();
    }
    private async Task SaveAsync()
    {
        if (_session is null || string.IsNullOrWhiteSpace(ServerName)) return;
        if (_gameTracking && _live is not null)
        {
            try { await _live.UpdateGoalsAsync(_session); _saveError = null; }
            catch (Exception error) { Logger.LogWarning(error, "Could not save live FM goals"); _saveError = "Les objectifs n’ont pas pu être enregistrés."; }
            return;
        }
        string server = ServerName;
        var snapshot = ForgemagingSessionStore.Copy(_session);
        await _saveGate.WaitAsync();
        try { await Task.Run(() => Store.Save(server, snapshot)); if (server == ServerName) _saveError = null; }
        catch (Exception error)
        {
            Logger.LogWarning(error, "Could not save FM session");
            if (server == ServerName) _saveError = "La session ne peut pas être enregistrée. Elle reste disponible tant que cet écran est ouvert.";
        }
        finally { _saveGate.Release(); }
    }
    private async Task FillRollAsync(bool maximum)
    {
        if (_session is null || _gameTracking) return;
        foreach (var line in _session.Lines) line.Current = line.IsNative ? maximum ? line.Maximum : line.Minimum : 0;
        _session.Sink = null; await EditedAsync();
    }
    private async Task AddExtraAsync()
    {
        if (_session is null || _extraStat is not { } stat || _session.Lines.Any(l => l.Characteristic == stat)) return;
        _session.Lines.Add(new() { Characteristic = stat, Current = 0, Target = 1 }); _extraStat = null;
        await EditedAsync();
    }
    private void Prepare(ForgemagingAdvice advice)
    {
        if (_session is null || _validation is not null || !CanPrepare(advice)) return;
        _pending = advice; _attemptPrice = advice.Rune.UnitPrice; _outcome = null;
        SetOutcome(); _attemptError = null;
    }
    private bool CanPrepare(ForgemagingAdvice advice) => _session is { } session &&
        (session.Budget is not { } budget || session.KnownSpent < budget &&
            (advice.Rune.UnitPrice is null || session.KnownSpent + advice.Rune.UnitPrice <= budget));
    private void SetOutcome()
    {
        _actual = _session?.Lines.Select(l => l.Copy()).ToList() ?? [];
        if (_pending is { } pending && _outcome is ForgemagingOutcome.Critical or ForgemagingOutcome.Neutral)
            _actual.Single(l => l.Characteristic == pending.Rune.Characteristic).Current += pending.Rune.Gain;
        _confirmedAfter = false; _attemptError = null;
    }
    private void CancelAttempt() { _pending = null; _actual = []; _attemptError = null; }
    private async Task RecordAttemptAsync()
    {
        if (_session is null || _pending is null) return;
        if (_outcome is null) { _attemptError = "Choisissez le résultat obtenu dans Dofus."; return; }
        if (_outcome != ForgemagingOutcome.Critical && !_confirmedAfter)
        { _attemptError = "Vérifiez le jet obtenu avant d’enregistrer."; return; }
        try
        {
            if (_attemptPrice is { } price && (!double.IsFinite(price) || price <= 0))
                throw new ArgumentException("Le coût doit être positif ou laissé vide si inconnu.");
            ForgemagingSessionActions.Record(_session, _pending.Rune with { UnitPrice = _attemptPrice }, _outcome.Value, _actual, DateTime.UtcNow);
            CancelAttempt(); await EditedAsync();
        }
        catch (ArgumentException error) { _attemptError = error.Message; }
    }
    private async Task UndoAsync()
    {
        if (_session is null || _gameTracking) return;
        ForgemagingSessionActions.UndoLast(_session); await EditedAsync();
    }
    private void PricesChanged(object? sender, MarketDataChangedEventArgs args)
    {
        if (_disposed || args.ObjectType != MarketObjectType.Rune || !string.Equals(args.ServerName, ServerName, StringComparison.OrdinalIgnoreCase)) return;
        if (Interlocked.Exchange(ref _priceNoticeQueued, 1) != 0) return;
        _ = InvokeAsync(() => { if (!_disposed) { _pricesDirty = true; StateHasChanged(); } });
    }
    private static string Money(double? value) => value is { } amount ? $"{amount:N2} K" : "Prix manquant";
    private static string OutcomeName(ForgemagingOutcome outcome) => outcome switch
    { ForgemagingOutcome.Critical => "Succès critique", ForgemagingOutcome.Neutral => "Succès neutre", ForgemagingOutcome.Failure => "Échec", _ => "À confirmer" };
    private static IEnumerable<(Characteristic Stat, int Delta)> Deltas(ForgemagingAttempt attempt) =>
        attempt.Before.Select(l => l.Characteristic).Union(attempt.After.Select(l => l.Characteristic))
            .Select(stat => (Stat: stat, Delta: (attempt.After.FirstOrDefault(l => l.Characteristic == stat)?.Current ?? 0) -
                (attempt.Before.FirstOrDefault(l => l.Characteristic == stat)?.Current ?? 0))).Where(x => x.Delta != 0);
    public void Dispose()
    {
        _disposed = true; _catalogLoad.Dispose(); _itemLoad.Dispose(); PriceChanges.Changed -= PricesChanged;
        if (_live is not null) _live.Changed -= LiveChanged;
    }
}
