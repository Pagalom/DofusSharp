using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.AspNetCore.Components;

namespace BestCrush.Components.Pages;

public partial class History
{
    private string _period = "7d", _recordKind = "all", _source = "all";
    private DateTime? _dateFrom = DateTime.Today.AddDays(-6), _dateTo = DateTime.Today;
    private int? _lotQuantity;
    private bool _advancedOpen, _includeCleared;
    private IReadOnlyCollection<EquipmentType> _equipmentTypes = [];
    private readonly EquipmentType[] _availableEquipmentTypes = Enum.GetValues<EquipmentType>();
    private int _page;
    private CancellationTokenSource? _searchDelay;
    private Dictionary<long, IItem> _historyItems = [];
    private string TableResetKey => $"{ServerName}:{_tab}:{_page}:{_sortColumn}:{_sortDirection}:{_filterVersion}";
    private int _filterVersion;
    private string SearchPlaceholder => _tab switch
    {
        HistoryTab.Items => "Nom d’un équipement", HistoryTab.Runes => "Nom d’une rune",
        HistoryTab.Resources => "Nom d’une ressource", _ => "Nom d’un équipement ou d’une rune"
    };
    private int CurrentRowCount => _tab switch
    {
        HistoryTab.Items => _itemHistory?.Rows.Count ?? 0,
        HistoryTab.Crushes => _crushHistory?.Rows.Count ?? 0,
        _ => _marketHistory?.Rows.Count ?? 0
    };
    private List<(string Key, string Label)> FilterChips
    {
        get
        {
            List<(string,string)> chips = [];
            if (_levelMin.HasValue || _levelMax.HasValue) chips.Add(("level", $"Niveau : {_levelMin ?? 1} à {_levelMax ?? 200}"));
            if (_equipmentTypes.Count > 0) chips.Add(("types", string.Join(", ",_equipmentTypes.Select(t => t.ToDisplayName()))));
            if (_recordKind != "all") chips.Add(("kind", _recordKind switch { "price" => "Prix uniquement", "coefficient" => "Coefficients uniquement", _ => "Concassages effectués" }));
            if (_source != "all") chips.Add(("source", _source switch { "game" => "Capture en jeu", "manual" => "Saisie manuelle", _ => "DoFocus" }));
            if (_lotQuantity.HasValue) chips.Add(("lot", $"Lot × {_lotQuantity:N0}"));
            if (SelectedFilterRune is { } rune) chips.Add(("rune",rune.Name));
            if (_includeCleared) chips.Add(("cleared","Entrées réinitialisées incluses"));
            return chips;
        }
    }
    private int AdvancedFilterCount => FilterChips.Count;
    private bool HasActiveFilters => AdvancedFilterCount > 0 || !string.IsNullOrWhiteSpace(_searchText) || _period != "7d" || _historyItemId is not null;

    private HistoryQueryOptions QueryOptions()
    {
        DateTime? from = _period switch
        {
            "24h" => DateTime.UtcNow.AddHours(-24), "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30), "custom" => _dateFrom?.Date.ToUniversalTime(), _ => null
        };
        DateTime? before = _period == "custom" ? _dateTo?.Date.AddDays(1).ToUniversalTime() : null;
        return new HistoryQueryOptions
        {
            FromUtc = from, BeforeUtc = before, Skip = _page * InitialTake,
            Source = _tab == HistoryTab.Crushes ? null : _source switch { "game" => HistoryObservationSource.Game, "manual" => HistoryObservationSource.Manual, "dofocus" => HistoryObservationSource.DoFocus, _ => (HistoryObservationSource?)null },
            LotQuantity = _tab == HistoryTab.Crushes ? null : _lotQuantity,
            IncludeCleared = _tab == HistoryTab.Crushes ? false : _includeCleared,
            ItemDataKind = _tab == HistoryTab.Items ? _recordKind switch { "price" => ItemHistoryDataKind.Price, "coefficient" => ItemHistoryDataKind.Coefficient, _ => (ItemHistoryDataKind?)null } : null,
            EquipmentTypes = _tab == HistoryTab.Items ? _equipmentTypes.ToArray() : [],
            CrushKind = _tab == HistoryTab.Crushes ? _recordKind switch { "coefficient" => CrushHistoryEntryKind.Coefficient, "operation" => CrushHistoryEntryKind.Operation, _ => (CrushHistoryEntryKind?)null } : null
        };
    }

    private async Task QueueSearchAsync()
    {
        _searchDelay?.Cancel(); _searchDelay?.Dispose();
        using var delay = new CancellationTokenSource(); _searchDelay = delay;
        _historyLoad.Cancel(); _loading = true;
        try { await Task.Delay(250, delay.Token); if (!_disposed && IsActive) await ApplyFiltersAsync(); }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_searchDelay,delay)) _searchDelay = null; }
    }

    private Task OnEquipmentTypesChangedAsync(IReadOnlyCollection<EquipmentType> types)
    { _equipmentTypes = types; return ApplyFiltersAsync(); }
    private Task OnRuneFilterChangedAsync(ChangeEventArgs args) => SelectRuneFilterAsync(long.TryParse(args.Value?.ToString(), out long id) ? id : null);
    private async Task RemoveFilterAsync(string key)
    {
        switch (key)
        {
            case "level": _levelMin = _levelMax = null; break;
            case "types": _equipmentTypes = []; break;
            case "kind": _recordKind = "all"; break;
            case "source": _source = "all"; break;
            case "lot": _lotQuantity = null; break;
            case "rune": _itemRuneFilterId = _crushRuneFilterId = null; break;
            case "cleared": _includeCleared = false; break;
        }
        await ApplyFiltersAsync();
    }
    private void ClearAdvancedFilters()
    {
        _levelMin = _levelMax = null; _equipmentTypes = [];
        _itemRuneFilterId = _crushRuneFilterId = null; _recordKind = _source = "all";
        _lotQuantity = null; _includeCleared = false;
    }
    private async Task PreviousPageAsync() { if (_page == 0) return; _page--; await LoadCurrentAsync(); }
    private async Task NextPageAsync() { if (!CurrentHasMore) return; _page++; await LoadCurrentAsync(); }
}
