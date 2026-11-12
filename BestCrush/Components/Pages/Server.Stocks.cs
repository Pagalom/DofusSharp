using System.Numerics;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BestCrush.Components.Pages;

public partial class Server
{
    [SupplyParameterFromQuery(Name = "stocks")] public bool UseStocksFromQuery { get; set; }
    [SupplyParameterFromQuery(Name = "item")] public long? StockItemId { get; set; }
    private bool _takeStocks, _preparingStocks, _stockDisposed;
    private int _stockPreparationVersion;
    private LiveStockSource? _preparedStockSource, _pendingStockSource;
    private long? _lastStockItemId;
    private bool _lastStocksQuery;
    private IReadOnlyDictionary<long, BigInteger> _stockQuantities = new Dictionary<long, BigInteger>();
    private Dictionary<long, EquipmentProfitabilityResult> _baseCalculations = new();
    private Dictionary<long, StockCraftPlan> _stockPlans = new();
    private Dictionary<long, List<ItemCrushResult>> _stockResults = new();
    private bool UseStockCalculation => _takeStocks && !_preparingStocks &&
        StockOpportunityService.CanUse(_preparedStockSource, ServerName) &&
        ReferenceEquals(_preparedStockSource, Stocks.Selected);

    private EquipmentProfitabilityContext CachedMarket() => new() {
        ResourcePrices = _resourcePrices, EquipmentPrices = _equipmentPrices,
        RunePrices = _runePrices, Coefficients = _coefficients
    };

    private static IReadOnlyDictionary<long, BigInteger> Quantities(LiveStockSource? source, string server)
    {
        if (!StockOpportunityService.CanUse(source, server)) return new Dictionary<long, BigInteger>();
        return source!.Containers.Where(x => x.Complete && !x.Stale && x.Container is "inventory" or "bank")
            .SelectMany(x => x.Items).Where(x => x.ItemId <= (ulong)long.MaxValue)
            .GroupBy(x => (long)x.ItemId).ToDictionary(x => x.Key,
                x => x.Aggregate(BigInteger.Zero, (sum, item) => sum + item.Quantity));
    }

    private void SetStockInputs(LiveStockSource? source)
    {
        _stockPreparationVersion++;
        _preparingStocks = false;
        _preparedStockSource = source;
        _stockQuantities = Quantities(source, ServerName);
    }

    private void CacheCalculation(EquipmentProfitabilityResult calculation)
    {
        _baseCalculations[calculation.Equipment.DofusDbId] = calculation;
        if (StockOpportunityService.CanUse(_preparedStockSource, ServerName))
        {
            var plan = StockPlanner.Calculate(calculation.Equipment, 1, _stockQuantities, CachedMarket());
            _stockPlans[calculation.Equipment.DofusDbId] = plan;
            _stockResults[calculation.Equipment.DofusDbId] = calculation.Scenarios
                .Select(x => ToItemCrushResult(StockCrushingProjection.Apply(x, plan)) with { StockPlan = plan }).ToList();
        }
    }

    private void AddVisibleCalculation(EquipmentProfitabilityResult calculation)
    {
        long id = calculation.Equipment.DofusDbId;
        var plan = UseStockCalculation ? _stockPlans.GetValueOrDefault(id) : null;
        var all = plan is not null ? _stockResults[id] : calculation.Scenarios
            .Where(x => x.Cost is not null || x.CraftCost.TotalCost is not null).Select(ToItemCrushResult).ToList();
        var warnings = calculation.MissingData.ToHashSet();
        if (plan is not null)
        {
            warnings.RemoveWhere(x => x.StartsWith("Craft incomplet :", StringComparison.Ordinal));
            var remaining = StockCrushingProjection.RemainingCost(plan);
            if (!remaining.IsComplete)
                warnings.Add($"Craft incomplet : {remaining.MissingIngredientCount} prix d'ingrédient manquant(s)");
            // A known zero-purchase craft cannot be beaten on cash cost by an unpriced purchase.
            if (plan.HasRecipe && remaining.TotalCost == 0)
                warnings.Remove("Prix local de l'équipement manquant");
        }
        _missingDataByItem[id] = warnings;
        var visible = all.Where(MatchesExactRuneFilters).Where(PassesEconomicFilters).ToList();
        if (Model?.CompleteDataOnly == true && _missingDataByItem[id].Count > 0) visible.Clear();
        if (all.Count == 0) _unrankedItems!.Add(calculation.Equipment);
        else _rankedItems!.AddRange(visible);
    }

    private void RebuildVisibleCalculations()
    {
        if (_rankedItems is null || _unrankedItems is null) return;
        var failures = _unrankedItems.Where(x => !_baseCalculations.ContainsKey(x.DofusDbId)).ToArray();
        _rankedItems = new();
        _unrankedItems = new(failures);
        foreach (var calculation in _baseCalculations.Values) AddVisibleCalculation(calculation);
    }

    // This handler only switches prepared data and reapplies the local filters.
    // No database read, API request, recipe planning or crush calculation occurs here.
    private void ToggleStocks(ChangeEventArgs args)
    {
        _takeStocks = args.Value is true;
        RebuildVisibleCalculations();
        SaveSessionState();
    }

    private void OnCrushingStockChanged() => _ = InvokeAsync(() => PrepareStocksAsync());

    private async Task PrepareStocksAsync(bool force = false)
    {
        if (_stockDisposed) return;
        var source = Stocks.Selected;
        // Foreground/title events do not invalidate an unchanged stock snapshot.
        if (!force && ((!_preparingStocks && ReferenceEquals(source, _preparedStockSource)) ||
            (_preparingStocks && ReferenceEquals(source, _pendingStockSource)))) return;
        _pendingStockSource = source;
        int version = ++_stockPreparationVersion;
        _preparingStocks = true;
        RebuildVisibleCalculations();
        var quantities = Quantities(source, ServerName);
        var market = CachedMarket();
        var calculations = _baseCalculations.Values.ToArray();
        var originalCalculations = calculations.ToDictionary(x => x.Equipment.DofusDbId);
        bool canUse = StockOpportunityService.CanUse(source, ServerName);
        try
        {
            var prepared = await Task.Run(() => {
                Dictionary<long, StockCraftPlan> plans = new();
                Dictionary<long, List<ItemCrushResult>> results = new();
                if (canUse) foreach (var calculation in calculations)
                {
                    var plan = StockPlanner.Calculate(calculation.Equipment, 1, quantities, market);
                    plans[calculation.Equipment.DofusDbId] = plan;
                    results[calculation.Equipment.DofusDbId] = calculation.Scenarios
                        .Select(x => ToItemCrushResult(StockCrushingProjection.Apply(x, plan)) with { StockPlan = plan }).ToList();
                }
                return (plans, results);
            });
            if (_stockDisposed || version != _stockPreparationVersion) return;
            if (!ReferenceEquals(market.ResourcePrices, _resourcePrices) ||
                !ReferenceEquals(market.EquipmentPrices, _equipmentPrices))
            {
                await PrepareStocksAsync(force: true);
                return;
            }
            // Search/refresh can have appended or replaced items while preparing.
            // Reconcile those items against the current local inputs too.
            _preparedStockSource = source;
            _stockQuantities = quantities;
            _stockPlans = prepared.plans;
            _stockResults = prepared.results;
            foreach (var calculation in _baseCalculations.Values.Where(x => !originalCalculations.TryGetValue(x.Equipment.DofusDbId, out var original) || !ReferenceEquals(x, original)).ToArray())
                CacheCalculation(calculation);
            _preparingStocks = false;
                RebuildVisibleCalculations();
            StateHasChanged();
        }
        catch (Exception ex)
        {
            if (version != _stockPreparationVersion || _stockDisposed) return;
            _preparedStockSource = null;
            _preparingStocks = false;
            Logger.LogError(ex, "Unable to prepare crushing stock calculations");
            RebuildVisibleCalculations();
            StateHasChanged();
        }
    }
}
