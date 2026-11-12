using BestCrush.Services;
using Microsoft.Extensions.DependencyInjection;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Models;
using Microsoft.AspNetCore.Components;
using Rune = BestCrush.Domain.Models.Rune;

namespace BestCrush.Components.Pages;

public partial class History
{
    private string? _loadedServerName;

    protected override async Task OnInitializedAsync()
    {
        await LoadCatalogsAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        bool becameActive = IsActive && !_wasActive;
        _wasActive = IsActive;
        if (!IsActive)
        {
            _searchDelay?.Cancel();
            _reloadPending |= _historyLoad.IsLoading;
            _historyLoad.Cancel();
            _loading = false;
            return;
        }
        bool selectionChanged = !ReferenceEquals(_lastSelection, InitialSelection);
        if (selectionChanged)
        {
            _lastSelection = InitialSelection;
            _historyItemId = InitialSelection?.ItemId;
            if (InitialSelection is { } target)
            {
                _tab = target.ObjectType switch
                {
                    MarketObjectType.Equipment => HistoryTab.Items,
                    MarketObjectType.Rune => HistoryTab.Runes,
                    _ => HistoryTab.Resources
                };
                _searchText = null;
                ClearAdvancedFilters();
                _period = "all";
                _page = 0;
                _take = InitialTake;
            }
        }
        if (!selectionChanged && !becameActive && !_reloadPending && string.Equals(_loadedServerName, ServerName,
                StringComparison.OrdinalIgnoreCase)) return;

        if (_loadedServerName != ServerName) _page = 0;
        _loadedServerName = ServerName;
        // Never leave old-server history rows visible under a new server
        // label while an asynchronous query loads the new SQL filter.
        _marketHistory = null;
        _itemHistory = null;
        _crushHistory = null;
        await LoadCurrentAsync();
    }

    private bool _catalogsLoading, _disposed, _reloadPending, _wasActive;
    private readonly BackgroundDataLoad<HistoryCatalogs> _catalogLoad = new();
    private readonly BackgroundDataLoad<HistoryRead> _historyLoad = new();
    private sealed record HistoryCatalogs(IReadOnlyCollection<Resource> Resources, IReadOnlyCollection<Equipment> Equipment, IReadOnlyCollection<Rune> Runes);
    private sealed record HistoryRead(HistoryPage<MarketHistoryRow>? Market = null, HistoryPage<ItemHistoryRow>? Items = null,
        HistoryPage<CrushHistoryEntry>? Crushes = null);

    private async Task LoadCatalogsAsync()
    {
        _catalogsLoading = true;
        bool loaded = await _catalogLoad.RunAsync(async token =>
        {
            using var scope = DataScopes.CreateScope();
            var items = scope.ServiceProvider.GetRequiredService<ItemsService>();
            var runes = scope.ServiceProvider.GetRequiredService<RunesService>();
            return new HistoryCatalogs(await items.GetResourcesAsync(token),
                await items.GetEquipmentValueCatalogAsync(craftableOnly: false, cancellationToken: token),
                await runes.GetLocalRunesAsync(token));
        });
        if (_disposed) return;
        if (loaded && _catalogLoad.Value is { } catalog)
        {
            _resources = catalog.Resources.ToDictionary(item => item.DofusDbId);
            _equipments = catalog.Equipment.ToDictionary(item => item.DofusDbId);
            _runes = catalog.Runes.OrderBy(rune => rune.Name).ToList();
            _runesById = _runes.ToDictionary(rune => rune.DofusDbId);
            _historyItems = catalog.Resources.Cast<IItem>().Concat(catalog.Equipment).Concat(catalog.Runes)
                .GroupBy(item => item.DofusDbId).ToDictionary(group => group.Key, group => group.First());
        }
        _catalogsLoading = false;
    }

    private async Task LoadCurrentAsync()
    {
        if (_disposed || !IsActive) { _reloadPending = true; return; }
        _reloadPending = false;
        string server = ServerName;
        var tab = _tab;
        string? search = _searchText;
        int? min = _levelMin, max = _levelMax;
        long? itemRune = _itemRuneFilterId, crushRune = _crushRuneFilterId, itemId = _historyItemId;
        var sort = _sortColumn; var direction = _sortDirection; int take = _take;
        var options = QueryOptions();
        _loading = true;
        _errorMessage = null;
        bool loaded = await _historyLoad.RunAsync(async token =>
        {
            using var scope = DataScopes.CreateScope();
            var history = scope.ServiceProvider.GetRequiredService<HistoryService>();
            switch (tab)
            {
                case HistoryTab.Resources:
                    return new HistoryRead(Market: await history.GetMarketHistoryAsync(MarketObjectType.Resource,
                        server, search, min, max, sort, direction, take, token, itemId, options));
                case HistoryTab.Runes:
                    return new HistoryRead(Market: await history.GetMarketHistoryAsync(MarketObjectType.Rune,
                        server, search, min, max, sort, direction, take, token, itemId, options));
                case HistoryTab.Items:
                    return new HistoryRead(Items: await history.GetItemHistoryAsync(server, search, min, max,
                        null, itemRune, sort, direction, take, token, itemId, options));
                case HistoryTab.Crushes:
                    return new HistoryRead(Crushes: await history.GetCrushingFeedAsync(server, search, crushRune,
                        sort, direction, take, options, token));
                default:
                    throw new ArgumentOutOfRangeException(nameof(tab));
            }
        });
        if (_disposed) return;
        _loading = _historyLoad.IsLoading;
        if (loaded && IsActive && server == ServerName && _historyLoad.Value is { } result)
        {
            _marketHistory = result.Market;
            _itemHistory = result.Items;
            _crushHistory = result.Crushes;
        }
        if (_historyLoad.Error is not null) _errorMessage = "L’historique n’a pas pu être chargé. Rafraîchissez pour réessayer.";
    }

    public void Dispose()
    {
        _disposed = true;
        _searchDelay?.Cancel();
        _catalogLoad.Dispose();
        _historyLoad.Dispose();
    }

    private async Task SelectTabAsync(HistoryTab tab)
    {
        if (_tab == tab)
        {
            return;
        }

        _historyItemId = null;
        _searchDelay?.Cancel();
        _tab = tab;
        ClearAdvancedFilters();
        _page = 0;
        _filterVersion++;
        _take = InitialTake;
        _sortColumn = HistorySortColumn.Date;
        _sortDirection = HistorySortDirection.Descending;

        await LoadCurrentAsync();
    }

    private async Task ClearItemSelectionAsync()
    {
        _historyItemId = null;
        await ResetFiltersAsync();
    }

    private async Task RefreshAsync()
    {
        _searchDelay?.Cancel();
        if (_catalogLoad.Error is not null) await LoadCatalogsAsync();
        await LoadCurrentAsync();
    }

    private async Task ApplyFiltersAsync()
    {
        _searchDelay?.Cancel();
        NormalizeLevels();
        if (_dateFrom > _dateTo) (_dateFrom, _dateTo) = (_dateTo, _dateFrom);
        _page = 0;
        _filterVersion++;
        _take = InitialTake;
        await LoadCurrentAsync();
    }

    private async Task ResetFiltersAsync()
    {
        _searchDelay?.Cancel();
        _historyItemId = null;
        _searchText = null;
        _period = "7d";
        _dateFrom = DateTime.Today.AddDays(-6); _dateTo = DateTime.Today;
        ClearAdvancedFilters();
        _page = 0;
        _filterVersion++;
        _take = InitialTake;
        await LoadCurrentAsync();
    }

    private void NormalizeLevels()
    {
        if (_levelMin is int min) _levelMin = Math.Clamp(min, 1, 200);
        if (_levelMax is int max) _levelMax = Math.Clamp(max, 1, 200);
        if (_levelMin > _levelMax) (_levelMin, _levelMax) = (_levelMax, _levelMin);
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

        await ApplyFiltersAsync();
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

        _page = 0;
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

}
