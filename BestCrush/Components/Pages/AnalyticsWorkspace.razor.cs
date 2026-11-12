using BestCrush.Components;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Components.Pages;

public partial class AnalyticsWorkspace : IDisposable
{
    [Parameter] public string ServerName { get; set; } = string.Empty;
    [CascadingParameter] public WorkspaceNavigation? Workspace { get; set; }
    private readonly BackgroundDataLoad<IReadOnlyList<MarketAnalyticsCatalogItem>> _catalogLoad = new();
    private readonly BackgroundDataLoad<MarketAnalyticsComparison> _load = new();
    private IReadOnlyList<MarketAnalyticsCatalogItem> _catalog = [];
    private List<MarketAnalyticsPreset> _presets = [];
    private List<MarketAnalyticsSeriesDefinition> _series = [];
    private MarketAnalyticsComparison? _comparison;
    private MarketAnalyticsSeriesDefinition? _draft;
    private string _name = "Nouvelle analyse", _builderSearch = "", _memberSearch = "";
    private string? _loadedServer, _error, _presetError;
    private Guid? _savedId, _editingId, _inspectId;
    private MarketAnalyticsPeriod _period = MarketAnalyticsPeriod.Days7;
    private MarketAnalyticsTransformation _scale = MarketAnalyticsTransformation.Base100;
    private Characteristic _quickCharacteristic = Characteristic.Wisdom;
    private int _maxAgeHours = 72, _plotPage, _builderPage;
    private int _priceVersion, _noticeQueued;
    private bool _disposed, _catalogReady, _dirty, _reloadPending;
    private IReadOnlyList<MarketAnalyticsPlotSeries> _plots = [], _visiblePlots = [];
    private const int CurvesPerPage = 8, BuilderPageSize = 25;
    private static readonly EquipmentType[] EquipmentTypes = Enum.GetValues<EquipmentType>();
    private static readonly Characteristic[] Characteristics = Enum.GetValues<Characteristic>().OrderBy(StatName).ToArray();
    private bool Busy => _load.IsLoading || _catalogLoad.IsLoading;

    protected override async Task OnInitializedAsync()
    {
        try { _presets = Presets.GetAll().ToList(); }
        catch (Exception error) { Logger.LogWarning(error,"Could not load analytics presets"); _presetError = "Les analyses enregistrées n’ont pas pu être chargées. Leur fichier est conservé."; }
        PriceChanges.Changed += OnPricesChanged;
        if (Workspace is not null) Workspace.Changed += OnWorkspaceChanged;
        await LoadCatalogAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedServer == ServerName) return;
        _loadedServer = ServerName;
        ClosePanel(); _status = null;
        _comparison = null; _plots = _visiblePlots = []; _plotPage = 0; _dirty = false;
        Interlocked.Exchange(ref _noticeQueued,0);
        await RefreshAsync();
    }

    private async Task LoadCatalogAsync()
    {
        bool loaded = await _catalogLoad.RunAsync(async token =>
        {
            using var scope = DataScopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MarketAnalyticsService>().GetCatalogAsync(token);
        });
        if (_disposed) return;
        if (loaded && _catalogLoad.Value is { } catalog) { _catalog = catalog; _catalogReady = true; }
        else if (_catalogLoad.Error is not null) _error = "Le catalogue n’a pas pu être chargé. Réessayez.";
    }

    private async Task RefreshAsync()
    {
        if (_disposed) return;
        if (Workspace?.DataOpen == true) { _reloadPending = true; return; }
        _reloadPending = false;
        if (!_catalogReady) { await LoadCatalogAsync(); if (!_catalogReady) return; }
        _error = null;
        var server = ServerName;
        var definitions = _series.Select(MarketAnalyticsPresetService.Clone).ToArray();
        var request = new MarketAnalyticsRequest(server,definitions,_period,MaxQuoteAgeHours:_maxAgeHours);
        int version = Volatile.Read(ref _priceVersion);
        if (definitions.Length == 0)
        {
            _load.Cancel(); _comparison = null; _plots = _visiblePlots = []; return;
        }
        bool loaded = await _load.RunAsync(async token =>
        {
            using var scope = DataScopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MarketAnalyticsService>().BuildComparisonAsync(request,_catalog,token);
        });
        if (_disposed) return;
        if (loaded && server == ServerName && _load.Value is { } result)
        {
            _comparison = result; _plotPage = 0;
            _dirty = version != Volatile.Read(ref _priceVersion);
            if (!_dirty) Interlocked.Exchange(ref _noticeQueued,0);
            UpdatePlot();
        }
        if (_load.Error is { } error)
        {
            Logger.LogWarning(error,"Failed to build market comparison for {Server}",server);
            _error = error is ArgumentException ? error.Message : "L’analyse n’a pas pu être calculée. Réessayez.";
        }
    }

    private void UpdatePlot()
    {
        _plots = _comparison is null ? [] : MarketAnalyticsProjection.Curves(_comparison,_series.Where(s => s.IsVisible).ToArray());
        _plotPage = Math.Clamp(_plotPage,0,Math.Max(0,(_plots.Count-1)/CurvesPerPage));
        _visiblePlots = _plots.Skip(_plotPage*CurvesPerPage).Take(CurvesPerPage).ToArray();
        _plotColors = BuildPlotColors();
    }
    private void ChangePlotPage(int delta) { _plotPage += delta; UpdatePlot(); }
    private void ToggleFusion(Guid id,ChangeEventArgs args)
    {
        var definition = _series.First(s => s.Id == id); definition.FuseMembers = args.Value is true;
        _plotPage = 0; UpdatePlot();
    }
    private async Task QuickAllRunesAsync()
    {
        var definition = NewDefinition(); definition.SelectionMode = MarketAnalyticsSelectionMode.AllOfType;
        definition.ObjectType = MarketObjectType.Rune; definition.ItemName = "Toutes les runes";
        _series.Add(definition); ClosePanel();
        await RefreshAsync();
    }
    private async Task QuickCharacteristicAsync()
    {
        foreach (var type in new[] { MarketObjectType.Rune,MarketObjectType.Equipment })
        {
            var definition = NewDefinition(); definition.SelectionMode = MarketAnalyticsSelectionMode.Criteria;
            definition.ObjectType = type; definition.Characteristic = _quickCharacteristic;
            definition.ItemName = $"{TypeName(type)} · {StatName(_quickCharacteristic)}"; _series.Add(definition);
        }
        ClosePanel();
        await RefreshAsync();
    }

    private void BeginAdd()
    {
        _editingId = null; _builderSearch = ""; _builderPage = 0;
        _draft = NewDefinition(); SetFirstDraftItem(); _inspectId = null; SetPanel(AnalyticsPanel.Editor);
    }
    private void BeginEdit(Guid id)
    {
        _editingId = id; _builderSearch = ""; _builderPage = 0;
        _draft = MarketAnalyticsPresetService.Clone(_series.First(s => s.Id == id));
        _inspectId = null; SetPanel(AnalyticsPanel.Editor);
    }
    private void CancelDraft() => ClosePanel();
    private void DraftModeChanged() { _draft!.ItemName = ""; _builderPage = 0; }
    private void DraftTypeChanged()
    {
        _draft!.EquipmentTypes.Clear(); _draft.Characteristic = null; _draft.ItemName = "";
        _builderSearch = ""; _builderPage = 0; SetFirstDraftItem();
    }
    private void SetFirstDraftItem()
    {
        if (_draft is null) return;
        _draft.DofusDbId = _catalog.FirstOrDefault(c => c.Key.ObjectType == _draft.ObjectType)?.Key.DofusDbId ?? 0;
    }
    private void BuilderSearchChanged()
    {
        _builderPage = 0;
        if (_draft?.SelectionMode != MarketAnalyticsSelectionMode.Single) return;
        var matches = BuilderMatches;
        if (matches.Any(c => c.Key.DofusDbId == _draft.DofusDbId)) return;
        string? previousName = _catalog.FirstOrDefault(c => c.Key.ObjectType == _draft.ObjectType && c.Key.DofusDbId == _draft.DofusDbId)?.Name;
        if (_draft.ItemName == previousName) _draft.ItemName = "";
        _draft.DofusDbId = matches.FirstOrDefault()?.Key.DofusDbId ?? 0;
    }
    private void SingleItemChanged()
    {
        if (_draft is null) return;
        _draft.ItemName = _catalog.FirstOrDefault(c => c.Key.ObjectType == _draft.ObjectType && c.Key.DofusDbId == _draft.DofusDbId)?.Name ?? "";
    }
    private IReadOnlyList<MarketAnalyticsCatalogItem> BuilderMatches => _draft is null ? [] : _catalog
        .Where(c => c.Key.ObjectType == _draft.ObjectType && (string.IsNullOrWhiteSpace(_builderSearch) || c.Name.Contains(_builderSearch.Trim(),StringComparison.CurrentCultureIgnoreCase))).ToArray();
    private IEnumerable<MarketAnalyticsCatalogItem> SingleChoices => BuilderMatches.Take(50)
        .Concat(_catalog.Where(c => c.Key.ObjectType == _draft!.ObjectType && c.Key.DofusDbId == _draft.DofusDbId)).DistinctBy(c => c.Key);
    private void ToggleMember(MarketAnalyticsCatalogItem item,ChangeEventArgs args)
    {
        if (_draft is null) return;
        _draft.Members.RemoveAll(m => m.Key == item.Key);
        if (args.Value is true) _draft.Members.Add(new(item.Key.ObjectType,item.Key.DofusDbId,item.Name));
    }
    private void AddMatchingMembers()
    {
        if (_draft is null) return;
        _draft.Members = _draft.Members.Concat(BuilderMatches.Select(i => new MarketAnalyticsMember(i.Key.ObjectType,i.Key.DofusDbId,i.Name))).DistinctBy(m => m.Key).ToList();
    }
    private void RemoveMember(MarketAnalyticsItemKey key) => _draft?.Members.RemoveAll(m => m.Key == key);
    private void SetTypes(IReadOnlyCollection<EquipmentType> types) { if (_draft is not null) _draft.EquipmentTypes = types.ToList(); }
    private int PreviewCount { get { try { return _draft is null ? 0 : MarketAnalyticsService.ResolveMembers(_draft,_catalog).Count; } catch (ArgumentException) { return 0; } } }
    private async Task CommitDraftAsync()
    {
        if (_draft is null) return;
        try
        {
            var definition = MarketAnalyticsPresetService.Clone(_draft);
            if (definition.LotQuantity is null) definition.ValueMode = MarketAnalyticsValueMode.UnitPrice;
            var members = MarketAnalyticsService.ResolveMembers(definition,_catalog);
            if (members.Count == 0) { _panelError = _error = "Aucun objet ne correspond à cette sélection."; return; }
            if (string.IsNullOrWhiteSpace(definition.ItemName)) definition.ItemName = definition.SelectionMode == MarketAnalyticsSelectionMode.Single ? members[0].Name : $"{TypeName(definition.ObjectType)} · {members.Count} objets";
            if (_editingId is Guid id) _series[_series.FindIndex(s => s.Id == id)] = definition;
            else _series.Add(definition);
            CancelDraft(); await RefreshAsync();
        }
        catch (ArgumentException error) { _panelError = _error = error.Message; }
    }
    private async Task RemoveSeriesAsync(Guid id)
    { _series.RemoveAll(s => s.Id == id); if (_inspectId == id || _editingId == id) ClosePanel(); await RefreshAsync(); }
    private async Task IsolateAsync(MarketAnalyticsMember member)
    {
        var parent = _series.FirstOrDefault(s => s.Id == _inspectId);
        var definition = parent is null ? new MarketAnalyticsSeriesDefinition() : MarketAnalyticsPresetService.Clone(parent);
        definition.Id = Guid.NewGuid(); definition.SelectionMode = MarketAnalyticsSelectionMode.Single; definition.IsVisible = true;
        definition.ObjectType = member.ObjectType; definition.DofusDbId = member.DofusDbId; definition.ItemName = member.Name;
        definition.Members.Clear(); definition.EquipmentTypes.Clear(); definition.Characteristic = null;
        definition.LevelMin = definition.LevelMax = null; definition.SearchText = null;
        _series.Add(definition); ClosePanel();
        await RefreshAsync();
    }

    private void NewAnalysis()
    {
        ClosePanel(); _status = null;
        _load.Cancel(); _series = []; _comparison = null; _plots = _visiblePlots = [];
        _savedId = null; _draft = null; _inspectId = null; _name = "Nouvelle analyse";
        _error = null; _dirty = false; _period = MarketAnalyticsPeriod.Days7; _scale = MarketAnalyticsTransformation.Base100;
    }
    private async Task SelectPresetAsync(ChangeEventArgs args)
    {
        if (!Guid.TryParse(args.Value?.ToString(),out Guid id)) { NewAnalysis(); return; }
        var preset = _presets.FirstOrDefault(p => p.Id == id); if (preset is null) return;
        ClosePanel(); _status = null;
        _savedId = id; _name = preset.Name; _period = preset.Period;
        _series = preset.Series.Select(MarketAnalyticsPresetService.Clone).ToList();
        _scale = preset.Scale ?? (_series.Count > 0 && _series.All(s => s.Transformation == MarketAnalyticsTransformation.Raw) ? MarketAnalyticsTransformation.Raw : MarketAnalyticsTransformation.Base100);
        _maxAgeHours = Math.Clamp(preset.MaxQuoteAgeHours,1,8760); _draft = null; _inspectId = null;
        await RefreshAsync();
    }
    private void Save(bool copy)
    {
        if (string.IsNullOrWhiteSpace(_name)) { _error = "Donnez un nom à l’analyse."; return; }
        try
        {
            Guid id = copy || _savedId is null ? Guid.NewGuid() : _savedId.Value;
            Presets.Save(new() { Id=id,Name=_name.Trim(),Period=_period,Scale=_scale,MaxQuoteAgeHours=_maxAgeHours,
                Series=_series.Select(MarketAnalyticsPresetService.Clone).ToList() });
            _savedId = id; _presets = Presets.GetAll().ToList(); _error = null; _presetError = null;
            _status = "Analyse enregistrée.";
        }
        catch (Exception error) { Logger.LogWarning(error,"Could not save analytics preset"); _error = "L’analyse n’a pas pu être enregistrée."; }
    }
    private void DeletePreset()
    {
        if (_savedId is not Guid id) return;
        try { Presets.Delete(id); _presets = Presets.GetAll().ToList(); _savedId = null; _presetError = null; }
        catch (Exception error) { Logger.LogWarning(error,"Could not remove analytics preset"); _error = "L’analyse n’a pas pu être supprimée."; }
    }

    private void OnPricesChanged(object? sender,MarketDataChangedEventArgs args)
    {
        if (_disposed || !string.Equals(args.ServerName,ServerName,StringComparison.OrdinalIgnoreCase)) return;
        Interlocked.Increment(ref _priceVersion);
        if (Interlocked.Exchange(ref _noticeQueued,1) != 0) return;
        _ = InvokeAsync(() => { if (!_disposed) { _dirty = true; StateHasChanged(); } });
    }
    private void OnWorkspaceChanged() => _ = InvokeAsync(async () =>
    {
        if (_disposed) return;
        if (Workspace?.DataOpen == true) { _reloadPending |= _load.IsLoading; _load.Cancel(); }
        else if (_reloadPending) await RefreshAsync();
        StateHasChanged();
    });
    public void Dispose()
    {
        _disposed = true; _historyLoad.Dispose(); _load.Dispose(); _catalogLoad.Dispose(); PriceChanges.Changed -= OnPricesChanged;
        if (Workspace is not null) Workspace.Changed -= OnWorkspaceChanged;
    }
    private MarketAnalyticsGroupResult? Inspected => _comparison?.Groups.FirstOrDefault(g => g.DefinitionId == _inspectId);
    private IEnumerable<MarketAnalyticsMemberSeries> InspectedMembers => (Inspected?.Members ?? [])
        .Where(m => string.IsNullOrWhiteSpace(_memberSearch) || m.Member.Name.Contains(_memberSearch,StringComparison.CurrentCultureIgnoreCase))
        .OrderBy(m => m.Samples.LastOrDefault()?.Index is null).ThenBy(m => m.Samples.LastOrDefault()?.Index).ThenBy(m => m.Member.Name);
    private static string TypeName(MarketObjectType type) => type switch { MarketObjectType.Equipment => "Équipements",MarketObjectType.Resource => "Ressources",_ => "Runes" };
    private static string StatName(Characteristic stat) => stat switch
    {
        Characteristic.Wisdom=>"Sagesse",Characteristic.Strength=>"Force",Characteristic.Intelligence=>"Intelligence",Characteristic.Chance=>"Chance",Characteristic.Agility=>"Agilité",
        Characteristic.Vitality=>"Vitalité",Characteristic.Initiative=>"Initiative",Characteristic.Prospecting=>"Prospection",Characteristic.Power=>"Puissance",Characteristic.Healing=>"Soins",
        Characteristic.Ap=>"PA",Characteristic.Mp=>"PM",Characteristic.Range=>"Portée",Characteristic.Summons=>"Invocations",Characteristic.Critical=>"Critiques",Characteristic.Pods=>"Pods",
        Characteristic.Dodge=>"Fuite",Characteristic.Lock=>"Tacle",Characteristic.ApDodge=>"Esquive PA",Characteristic.MpDodge=>"Esquive PM",Characteristic.ApReduction=>"Retrait PA",Characteristic.MpReduction=>"Retrait PM",Characteristic.Hunting=>"Chasse",Characteristic.DamageReflection=>"Renvoi de dommages",
        Characteristic.DamageFlatAll=>"Dommages",Characteristic.DamageFlatNeutral=>"Dommages neutres",Characteristic.DamageFlatEarth=>"Dommages terre",Characteristic.DamageFlatFire=>"Dommages feu",Characteristic.DamageFlatWater=>"Dommages eau",Characteristic.DamageFlatAir=>"Dommages air",
        Characteristic.DamageFlatCritical=>"Dommages critiques",Characteristic.DamageFlatPushback=>"Dommages de poussée",Characteristic.DamageFlatTrap=>"Dommages aux pièges",Characteristic.DamagePercentTrap=>"Dommages aux pièges (%)",
        Characteristic.DamagePercentSpell=>"Dommages aux sorts (%)",Characteristic.DamagePercentWeapon=>"Dommages aux armes (%)",Characteristic.DamagePercentDisance=>"Dommages à distance (%)",Characteristic.DamagePercentMelee=>"Dommages en mêlée (%)",
        Characteristic.ResistanceFlatNeutral=>"Résistance neutre",Characteristic.ResistanceFlatEarth=>"Résistance terre",Characteristic.ResistanceFlatFire=>"Résistance feu",Characteristic.ResistanceFlatWater=>"Résistance eau",Characteristic.ResistanceFlatAir=>"Résistance air",
        Characteristic.ResistanceFlatCritical=>"Résistance critique",Characteristic.ResistanceFlatPushback=>"Résistance poussée",Characteristic.ResistancePercentNeutral=>"Résistance neutre (%)",Characteristic.ResistancePercentEarth=>"Résistance terre (%)",Characteristic.ResistancePercentFire=>"Résistance feu (%)",Characteristic.ResistancePercentWater=>"Résistance eau (%)",Characteristic.ResistancePercentAir=>"Résistance air (%)",
        Characteristic.ResistancePercentDistance=>"Résistance à distance (%)",Characteristic.ResistancePercentMelee=>"Résistance en mêlée (%)",
        _=>stat.ToDisplayName()
    };
    private static string Money(double? value) => value.HasValue ? $"{value:N0} K" : "—";
    private static string Change(MarketAnalyticsMemberSeries member) => member.Samples.LastOrDefault()?.Index is double index ? $"{index-100:+0.##;-0.##;0} %" : "—";
}
