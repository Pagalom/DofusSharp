using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Models;
using Microsoft.AspNetCore.Components;
using Rune = BestCrush.Domain.Models.Rune;

namespace BestCrush.Components.Pages;

public partial class History
{
    protected override async Task OnInitializedAsync()
    {
        LoadAnalyticsPresets();
        await LoadCatalogsAsync();
        await LoadCurrentAsync();
    }

    private async Task LoadCatalogsAsync()
    {
        IReadOnlyCollection<Resource> resources =
            await ItemsService.GetResourcesAsync();

        IReadOnlyCollection<Equipment> equipments =
            await ItemsService.GetAllEquipmentsAsync();

        IReadOnlyCollection<Rune> runes =
            await RunesService.GetLocalRunesAsync();

        _resources = resources.ToDictionary(item => item.DofusDbId);
        _equipments = equipments.ToDictionary(item => item.DofusDbId);
        _runes = runes.OrderBy(rune => rune.Name).ToList();
        _runesById = _runes.ToDictionary(rune => rune.DofusDbId);
    }

    private async Task LoadCurrentAsync()
    {
        _loading = true;
        _errorMessage = null;

        try
        {
            switch (_tab)
            {
                case HistoryTab.Resources:
                    _marketHistory = await HistoryService.GetMarketHistoryAsync(
                        MarketObjectType.Resource,
                        ServerName,
                        _searchText,
                        _levelMin,
                        _levelMax,
                        _sortColumn,
                        _sortDirection,
                        _take
                    );
                    break;

                case HistoryTab.Runes:
                    _marketHistory = await HistoryService.GetMarketHistoryAsync(
                        MarketObjectType.Rune,
                        ServerName,
                        _searchText,
                        _levelMin,
                        _levelMax,
                        _sortColumn,
                        _sortDirection,
                        _take
                    );
                    break;

                case HistoryTab.Items:
                    _itemHistory = await HistoryService.GetItemHistoryAsync(
                        ServerName,
                        _searchText,
                        _levelMin,
                        _levelMax,
                        _equipmentType,
                        _itemRuneFilterId,
                        _sortColumn,
                        _sortDirection,
                        _take
                    );
                    break;

                case HistoryTab.Crushes:
                    _crushHistory = await HistoryService.GetCrushHistoryAsync(
                        ServerName,
                        _searchText,
                        _crushRuneFilterId,
                        _sortColumn,
                        _sortDirection,
                        _take
                    );
                    break;

                case HistoryTab.Analytics:
                    await BuildAnalyticsResultsAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SelectTabAsync(HistoryTab tab)
    {
        if (_tab == tab)
        {
            return;
        }

        _tab = tab;
        _take = InitialTake;
        _sortColumn = HistorySortColumn.Date;
        _sortDirection = HistorySortDirection.Descending;
        _runePickerOpen = false;
        _expandedSessions.Clear();

        await LoadCurrentAsync();
    }

    private async Task RefreshAsync()
    {
        await LoadCurrentAsync();
    }

    private async Task ApplyFiltersAsync()
    {
        NormalizeLevels();
        _take = InitialTake;
        _runePickerOpen = false;
        await LoadCurrentAsync();
    }

    private async Task ResetFiltersAsync()
    {
        _searchText = null;
        _levelMin = null;
        _levelMax = null;
        _equipmentType = null;

        if (_tab == HistoryTab.Items)
        {
            _itemRuneFilterId = null;
        }
        else if (_tab == HistoryTab.Crushes)
        {
            _crushRuneFilterId = null;
        }

        _take = InitialTake;
        _runePickerOpen = false;
        await LoadCurrentAsync();
    }

    private void NormalizeLevels()
    {
        if (_levelMin is < 0)
        {
            _levelMin = 0;
        }

        if (_levelMax is > 200)
        {
            _levelMax = 200;
        }

        if (_levelMin is int min &&
            _levelMax is int max &&
            min > max)
        {
            (_levelMin, _levelMax) = (_levelMax, _levelMin);
        }
    }

    private void OnEquipmentTypeChanged(ChangeEventArgs args)
    {
        string? value = args.Value?.ToString();

        _equipmentType = Enum.TryParse(
            value,
            ignoreCase: true,
            out EquipmentType parsed)
                ? parsed
                : null;
    }

    private void ToggleRunePicker()
    {
        _runePickerOpen = !_runePickerOpen;
    }

    private async Task SelectRuneFilterAsync(long? dofusDbId)
    {
        if (_tab == HistoryTab.Items)
        {
            _itemRuneFilterId = dofusDbId;
        }
        else if (_tab == HistoryTab.Crushes)
        {
            _crushRuneFilterId = dofusDbId;
        }

        _runePickerOpen = false;
        _take = InitialTake;
        await LoadCurrentAsync();
    }

    private Rune? SelectedFilterRune
    {
        get
        {
            long? id = _tab == HistoryTab.Items
                ? _itemRuneFilterId
                : _crushRuneFilterId;

            return id is long value
                ? _runesById.GetValueOrDefault(value)
                : null;
        }
    }

    private async Task SortByAsync(HistorySortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortDirection = _sortDirection == HistorySortDirection.Ascending
                ? HistorySortDirection.Descending
                : HistorySortDirection.Ascending;
        }
        else
        {
            _sortColumn = column;
            _sortDirection = DefaultDirection(column);
        }

        _take = InitialTake;
        await LoadCurrentAsync();
    }

    private static HistorySortDirection DefaultDirection(HistorySortColumn column) =>
        column switch
        {
            HistorySortColumn.Name or
            HistorySortColumn.Level or
            HistorySortColumn.Type or
            HistorySortColumn.Source or
            HistorySortColumn.DataKind or
            HistorySortColumn.Rune => HistorySortDirection.Ascending,

            _ => HistorySortDirection.Descending
        };

    private string SortIndicator(HistorySortColumn column)
    {
        if (_sortColumn != column)
        {
            return "↕";
        }

        return _sortDirection == HistorySortDirection.Ascending
            ? "▲"
            : "▼";
    }

    private string SortButtonClass(HistorySortColumn column) =>
        _sortColumn == column
            ? "history-sort active"
            : "history-sort";

    private async Task LoadMoreAsync()
    {
        _take += PageIncrement;
        await LoadCurrentAsync();
    }

    private bool CurrentHasMore =>
        _tab switch
        {
            HistoryTab.Resources or HistoryTab.Runes => _marketHistory?.HasMore == true,
            HistoryTab.Items => _itemHistory?.HasMore == true,
            HistoryTab.Crushes => _crushHistory?.HasMore == true,
            _ => false
        };

    private string TabClass(HistoryTab tab) =>
        tab == _tab
            ? "btn btn-primary"
            : "btn btn-outline-secondary";

    private IItem? GetMarketItem(MarketHistoryRow row)
    {
        return _tab == HistoryTab.Resources
            ? _resources.GetValueOrDefault(row.DofusDbId)
            : _runesById.GetValueOrDefault(row.DofusDbId);
    }

}
