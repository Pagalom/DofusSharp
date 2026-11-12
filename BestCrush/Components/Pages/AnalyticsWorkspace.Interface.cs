using BestCrush.Domain.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BestCrush.Components.Pages;

public partial class AnalyticsWorkspace
{
    private enum AnalyticsPanel { None, Editor, Details, Characteristic, Options, Name, Delete }
    private enum NameAction { Save, SaveCopy, Rename }
    private AnalyticsPanel _panel;
    private NameAction _nameAction;
    private ElementReference _panelFocus;
    private bool _focusPanel;
    private string? _panelError, _status;
    private string _nameDraft = "", _optionsLot = "all";
    private int _optionsAge = 72;
    private MarketAnalyticsAggregation? _optionsAggregation;
    private MarketAnalyticsSourceMode? _optionsSource;
    private MarketAnalyticsAggregation _defaultAggregation = MarketAnalyticsAggregation.Median;
    private MarketAnalyticsSourceMode _defaultSource = MarketAnalyticsSourceMode.InGameAutomatic;
    private int? _defaultLot;
    private IReadOnlyDictionary<string,string> _plotColors = new Dictionary<string,string>();
    private static readonly string[] CurvePalette = ["#78bae2","#ec939b","#91cba6","#e4c27a","#c2a9e8","#79cfcd","#e5a0cf","#c5d0cf"];

    private string PanelTitle => _panel switch
    {
        AnalyticsPanel.Editor => _editingId is null ? "Ajouter une courbe" : "Modifier la courbe",
        AnalyticsPanel.Details => Inspected?.Name ?? "Objets du groupe",
        AnalyticsPanel.Characteristic => "Comparer une caractéristique",
        AnalyticsPanel.Options => "Options avancées",
        AnalyticsPanel.Name => _nameAction == NameAction.Rename ? "Renommer l’analyse" : _nameAction == NameAction.SaveCopy ? "Enregistrer sous" : "Enregistrer l’analyse",
        AnalyticsPanel.Delete => "Supprimer l’analyse", _ => ""
    };
    private bool NeedsPlotLegend => _visiblePlots.Any(p => _series.Any(s => s.Id == p.DefinitionId && !s.FuseMembers && s.SelectionMode != MarketAnalyticsSelectionMode.Single));
    private string ResolutionLabel => _period switch
    {
        MarketAnalyticsPeriod.Hours24 => "un créneau d’une heure", MarketAnalyticsPeriod.Days7 => "un créneau de 6 h",
        MarketAnalyticsPeriod.Days30 => "un créneau d’un jour", _ => "tout l’historique"
    };
    private static string PeriodName(MarketAnalyticsPeriod period) => period switch
    { MarketAnalyticsPeriod.Hours24 => "24 h", MarketAnalyticsPeriod.Days7 => "7 jours", MarketAnalyticsPeriod.Days30 => "30 jours", _ => "Tout" };
    private static string ModeName(MarketAnalyticsSelectionMode mode) => mode switch
    { MarketAnalyticsSelectionMode.Single => "Un objet précis", MarketAnalyticsSelectionMode.AllOfType => "Une famille entière", MarketAnalyticsSelectionMode.Criteria => "Un groupe par critères", _ => "Une sélection personnelle" };
    private static string AggregationName(MarketAnalyticsAggregation value) => value switch
    { MarketAnalyticsAggregation.Median => "Médiane", MarketAnalyticsAggregation.Mean => "Moyenne", MarketAnalyticsAggregation.FirstQuartile => "Premier quartile", _ => "Troisième quartile" };
    private string DefinitionColor(Guid id) => CurvePalette[Math.Max(0,_series.FindIndex(s => s.Id == id)) % CurvePalette.Length];
    private MarketAnalyticsSeriesDefinition NewDefinition() => new() { Aggregation = _defaultAggregation, SourceMode = _defaultSource, LotQuantity = _defaultLot };
    private IReadOnlyDictionary<string,string> BuildPlotColors()
    {
        Dictionary<string,string> colors = [];
        foreach (var group in _plots.GroupBy(p => p.DefinitionId))
        {
            var definition = _series.First(s => s.Id == group.Key);
            int offset = Math.Max(0,_series.IndexOf(definition)), member = 0;
            foreach (var plot in group)
                colors[plot.Key] = definition.FuseMembers || definition.SelectionMode == MarketAnalyticsSelectionMode.Single
                    ? DefinitionColor(group.Key) : CurvePalette[(offset+member++) % CurvePalette.Length];
        }
        return colors;
    }
    private void ToggleVisibility(Guid id,ChangeEventArgs args)
    { _series.First(s => s.Id == id).IsVisible = args.Value is true; _plotPage = 0; UpdatePlot(); }
    private async Task SetPeriodAsync(MarketAnalyticsPeriod period)
    { if (_period == period) return; _period = period; await RefreshAsync(); }
    private Task SelectPresetByIdAsync(Guid? id) => SelectPresetAsync(new() { Value = id?.ToString() });
    private void SetPanel(AnalyticsPanel panel)
    { if (panel != AnalyticsPanel.Details) ResetHistory(); _panel = panel; _panelError = null; _focusPanel = panel != AnalyticsPanel.None; }
    private void ClosePanel()
    { ResetHistory(); _panel = AnalyticsPanel.None; _panelError = null; _focusPanel = false; _draft = null; _editingId = null; _inspectId = null; }
    private void ShowDetails(Guid id)
    { ResetHistory(); _draft = null; _editingId = null; _inspectId = id; _memberSearch = ""; SetPanel(AnalyticsPanel.Details); }
    private void OpenCharacteristic()
    { _draft = null; _editingId = null; _inspectId = null; SetPanel(AnalyticsPanel.Characteristic); }
    private void SetDraftMode(MarketAnalyticsSelectionMode mode)
    { if (_draft is null) return; _draft.SelectionMode = mode; DraftModeChanged(); if (mode == MarketAnalyticsSelectionMode.Single) BuilderSearchChanged(); }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusPanel || _panel == AnalyticsPanel.None) return;
        _focusPanel = false;
        await _panelFocus.FocusAsync(preventScroll:true);
    }
    private void HandleKeyDown(KeyboardEventArgs args) { if (args.Key == "Escape") ClosePanel(); }
    private void HandleNameKeyDown(KeyboardEventArgs args) { if (args.Key == "Enter") CompleteName(); }
    private void RequestSave()
    { if (_savedId is null) OpenNamePanel(NameAction.Save); else Save(false); }
    private void OpenNamePanel(NameAction action)
    { ClosePanel(); _nameAction = action; _nameDraft = _name == "Nouvelle analyse" ? "" : _name; SetPanel(AnalyticsPanel.Name); }
    private void CompleteName()
    {
        if (string.IsNullOrWhiteSpace(_nameDraft)) { _panelError = "Donnez un nom à l’analyse."; return; }
        _name = _nameDraft.Trim();
        if (_nameAction != NameAction.Rename || _savedId is not null)
        { Save(_nameAction == NameAction.SaveCopy); if (_error is not null) { _panelError = _error; return; } }
        ClosePanel();
    }
    private void OpenDeletePanel() { if (_savedId is null) return; ClosePanel(); SetPanel(AnalyticsPanel.Delete); }
    private void ConfirmDelete() { DeletePreset(); if (_error is null) { ClosePanel(); _status = "Analyse supprimée."; } }
    private void OpenOptions()
    {
        ClosePanel(); _optionsAge = _maxAgeHours;
        var aggregations = _series.Select(s => s.Aggregation).Distinct().ToArray();
        var sources = _series.Select(s => s.SourceMode).Distinct().ToArray();
        var lots = _series.Select(s => s.LotQuantity).Distinct().ToArray();
        _optionsAggregation = aggregations.Length == 1 ? aggregations[0] : _series.Count == 0 ? _defaultAggregation : null;
        _optionsSource = sources.Length == 1 ? sources[0] : _series.Count == 0 ? _defaultSource : null;
        _optionsLot = lots.Length > 1 ? "mixed" : (lots.Length == 1 ? lots[0] : _defaultLot)?.ToString() ?? "all";
        SetPanel(AnalyticsPanel.Options);
    }
    private async Task ApplyOptionsAsync()
    {
        if (_optionsAge is < 1 or > 8760) { _panelError = "La fraîcheur doit être comprise entre 1 et 8 760 heures."; return; }
        bool changed = _maxAgeHours != _optionsAge; _maxAgeHours = _optionsAge;
        if (_optionsAggregation is { } aggregation) _defaultAggregation = aggregation;
        if (_optionsSource is { } source) _defaultSource = source;
        int? lot = int.TryParse(_optionsLot,out int parsed) && parsed > 0 ? parsed : null;
        if (_optionsLot != "mixed") _defaultLot = lot;
        foreach (var definition in _series)
        {
            if (_optionsAggregation is { } a) { changed |= definition.Aggregation != a; definition.Aggregation = a; }
            if (_optionsSource is { } s) { changed |= definition.SourceMode != s; definition.SourceMode = s; }
            if (_optionsLot != "mixed")
            {
                changed |= definition.LotQuantity != lot; definition.LotQuantity = lot;
                if (lot is null) { changed |= definition.ValueMode != MarketAnalyticsValueMode.UnitPrice; definition.ValueMode = MarketAnalyticsValueMode.UnitPrice; }
            }
        }
        ClosePanel(); _status = "Options appliquées.";
        if (changed) await RefreshAsync();
    }
}
