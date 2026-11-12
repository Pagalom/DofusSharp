using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Components.Pages;

public partial class Server
{
    private MarketDataChangeNotifier? _marketChanges;
    private readonly CancellationTokenSource _marketStop = new();
    private readonly HashSet<(MarketObjectType Type, long Id)> _pendingMarketChanges = [];
    private Task _marketRefreshTask = Task.CompletedTask;
    private bool _marketUpdating;
    private int _marketGeneration;
    private long _marketRevision;
    private string? _marketRefreshError;

    private void InitializeMarketTracking()
    {
        _marketChanges = ProfessionServices.GetService<MarketDataChangeNotifier>();
        if (_marketChanges is not null) _marketChanges.Changed += OnCrushingMarketChanged;
    }
    private void DisposeMarketTracking()
    {
        if (_marketChanges is not null) _marketChanges.Changed -= OnCrushingMarketChanged;
        _marketGeneration++;
        _marketStop.Cancel();
        _marketStop.Dispose();
    }
    private void InvalidateMarketRefresh()
    {
        _marketGeneration++;
        _pendingMarketChanges.Clear();
        _marketRefreshError = null;
    }
    private void OnCrushingMarketChanged(object? sender, MarketDataChangedEventArgs args)
    {
        if (_stockDisposed) return;
        _ = InvokeAsync(() => {
            if (_stockDisposed || !string.Equals(ServerName, args.ServerName, StringComparison.OrdinalIgnoreCase)) return;
            var change = (args.ObjectType, args.DofusDbId);
            if (!_searching && !(_allItems?.Any(e => DependsOnMarket(e, change)) ?? false)) return;
            _pendingMarketChanges.Add(change);
            ScheduleMarketRefresh();
        });
    }
    private bool DependsOnMarket(Equipment equipment, (MarketObjectType Type, long Id) change) => change.Type switch
    {
        MarketObjectType.Equipment => equipment.DofusDbId == change.Id || equipment.EquipmentRecipe.Any(x => x.IngredientEquipment.DofusDbId == change.Id),
        MarketObjectType.Resource => equipment.Recipe.Any(x => x.Resource.DofusDbId == change.Id),
        MarketObjectType.Rune => _baseCalculations.TryGetValue(equipment.DofusDbId, out var calculation) &&
            (calculation.PotentialRunes.Any(x => x.DofusDbId == change.Id) || calculation.Scenarios.Any(s => s.Runes.Keys.Any(r => r.DofusDbId == change.Id))) ||
            _availableRunes.Any(r => r.DofusDbId == change.Id && equipment.Characteristics.Any(c => c.Characteristic == r.Characteristic)),
        _ => false
    };
    private void CatchUpMarketChanges()
    {
        if (_stockDisposed || _marketChanges is null) return;
        var snapshot = _marketChanges.ChangesSince(ServerName, _marketRevision);
        foreach (var change in snapshot.Changes)
            if (_allItems?.Any(e => DependsOnMarket(e, (change.ObjectType, change.DofusDbId))) == true)
                _pendingMarketChanges.Add((change.ObjectType, change.DofusDbId));
        if (_pendingMarketChanges.Count == 0) _marketRevision = snapshot.Revision;
        ScheduleMarketRefresh();
    }
    private void ScheduleMarketRefresh()
    {
        if (_stockDisposed || _searching || _marketUpdating || _pendingMarketChanges.Count == 0) return;
        _marketRefreshTask = RefreshMarketAsync(_marketStop.Token);
        StateHasChanged();
    }
    private sealed record RefreshedCrush(EquipmentProfitabilityResult Calculation, StockCraftPlan? Plan, List<ItemCrushResult>? StockRows);

    private async Task RefreshMarketAsync(CancellationToken token)
    {
        _marketUpdating = true;
        _marketRefreshError = null;
        try
        {
            // Group price-lot notifications from the same capture into one background read.
            await Task.Delay(180, token);
            while (!_stockDisposed && !_searching && _pendingMarketChanges.Count > 0)
            {
                var changes = _pendingMarketChanges.ToArray();
                _pendingMarketChanges.Clear();
                var equipments = (_allItems ?? []).Where(e => changes.Any(c => DependsOnMarket(e, c))).ToArray();
                if (equipments.Length == 0) continue;
                string server = ServerName;
                int generation = _marketGeneration;
                long revision = _marketChanges?.Revision ?? _marketRevision;
                var source = Stocks.Selected;
                var quantities = Quantities(source, server);
                bool canUseStocks = StockOpportunityService.CanUse(source, server);
                using var scope = AnalysisScopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<EquipmentProfitabilityService>();
                var planner = scope.ServiceProvider.GetRequiredService<StockCraftPlanner>();
                var refreshed = await Task.Run(async () => {
                    var market = await service.LoadContextAsync(server, token);
                    List<RefreshedCrush> rows = [];
                    foreach (var equipment in equipments)
                    {
                        token.ThrowIfCancellationRequested();
                        var result = await service.CalculateAsync(equipment, server, market,
                            includeUnpricedScenarios: true, allowRemoteCoefficient: false);
                        var plan = canUseStocks ? planner.Calculate(equipment, 1, quantities, market) : null;
                        var stockRows = plan is null ? null : result.Scenarios.Select(s => ToItemCrushResult(StockCrushingProjection.Apply(s, plan)) with { StockPlan = plan }).ToList();
                        rows.Add(new(result, plan, stockRows));
                    }
                    return (market, rows);
                }, token);
                if (_stockDisposed || generation != _marketGeneration || !string.Equals(server, ServerName, StringComparison.OrdinalIgnoreCase)) continue;
                _runePrices = refreshed.market.RunePrices;
                _resourcePrices = refreshed.market.ResourcePrices;
                _equipmentPrices = refreshed.market.EquipmentPrices;
                _coefficients = refreshed.market.Coefficients;
                bool sameStock = !_preparingStocks && ReferenceEquals(source, _preparedStockSource) && ReferenceEquals(source, Stocks.Selected);
                foreach (var row in refreshed.rows)
                {
                    long id = row.Calculation.Equipment.DofusDbId;
                    _baseCalculations[id] = row.Calculation;
                    _craftCostsByItem[id] = row.Calculation.CraftCost;
                    if (sameStock)
                    {
                        if (row.Plan is not null && row.StockRows is not null) { _stockPlans[id] = row.Plan; _stockResults[id] = row.StockRows; }
                        else { _stockPlans.Remove(id); _stockResults.Remove(id); }
                    }
                }
                if (!sameStock) await PrepareStocksAsync(force: true);
                RebuildVisibleCalculations();
                _marketRevision = Math.Max(_marketRevision, revision);
                SaveSessionState();
                StateHasChanged();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_stockDisposed)
            {
                _marketRefreshError = "Les prix n’ont pas pu être actualisés. Relancez l’analyse.";
                Logger.LogError(error, "Unable to refresh affected crushing results");
            }
        }
        finally
        {
            _marketUpdating = false;
            if (!_stockDisposed) { StateHasChanged(); ScheduleMarketRefresh(); }
        }
    }
}
