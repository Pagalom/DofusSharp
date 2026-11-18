using System.Numerics;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

internal sealed record BreedingRefillResult(FuelRefillPlan? Plan, int? BreederLevel, bool StockComplete,
    bool RecipesAvailable, string? Server, IReadOnlyList<BreedingPriceCheck> PriceChecks);

public sealed record BreedingPriceCheck(long ItemId, string Name, DateTimeOffset? LastObserved, string Reason);
internal sealed record BreedingExperiencePricing(Dictionary<int, long> Costs, IReadOnlyList<BreedingPriceCheck> PriceChecks);
internal sealed record BreedingCaptureSupply(long? Cost, DateTimeOffset? LastObserved);

internal sealed class BreedingRefillService(IDbContextFactory<BestCrushDbContext> database,
    IDataPriorityProvider priority, ActivityCatalogService catalog, LiveStockService liveStock,
    CurrentServerState servers, CurrentWindowProfessionsState professions, ILogger<BreedingRefillService> logger)
{
    internal const long CaptureNetId = 32_521;
    internal const string CaptureNetName = "Filet de capture universel";

    internal async Task<BreedingCaptureSupply> EstimateCaptureSupplyAsync(DofusClientWindowKey? window, string server, CancellationToken token)
    {
        await using var db = await database.CreateDbContextAsync(token);
        var prices = new MarketPriceService(db, priority);
        var observations = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Resource, server,
            token, excludeUnavailableLots: true);
        var observed = observations.Values.Where(quote => quote.DofusDbId == CaptureNetId && quote.Price > 0 && !quote.IsCleared)
            .Max(quote => (DateTime?)quote.ObservedAtUtc);
        var cutoff = DateTime.UtcNow - BreedingResaleAdvisor.PriceLifetime;
        var resources = observations.Where(pair => pair.Value.ObservedAtUtc >= cutoff).ToDictionary();
        long? cost = prices.CalculateMinimumPurchaseCost(CaptureNetId, 1, resources)?.TotalCost;
        var skills = professions.ForWindow(window, server);
        if (skills is not null)
        {
            try
            {
                var recipe = (await catalog.GetProductCraftsAsync([CaptureNetId], token)).FirstOrDefault();
                if (recipe?.Product.Job is { } job && recipe.Product.JobLevel is { } required &&
                    skills.Any(skill => skill.Name == job && skill.Level >= required) &&
                    recipe.Ingredients.All(ingredient => ingredient.MarketType == MarketObjectType.Resource))
                {
                    var empty = new Dictionary<(long, int), MarketPriceObservation>();
                    var market = new EquipmentProfitabilityContext
                    {
                        ResourcePrices = resources, EquipmentPrices = empty, RunePrices = empty, Coefficients = []
                    };
                    var craft = new StockCraftPlanner(prices).Calculate(recipe, 1, new Dictionary<long, BigInteger>(), market);
                    if (craft.HasRecipe && craft.Ingredients.All(ingredient => ingredient.Purchase is not null))
                    {
                        long fabrication = craft.Ingredients.Sum(ingredient => ingredient.Purchase!.TotalCost);
                        cost = cost is { } purchase ? Math.Min(purchase, fabrication) : fabrication;
                    }
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Coût de fabrication du filet universel indisponible.");
            }
        }
        return new(cost, observed is { } date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null);
    }

    internal Task<BreedingRefillResult> CompareAsync(DofusClientWindowKey? window, string? viewServer,
        PaddockGauge gauge, int reserve, int target, CancellationToken token) =>
        WithPricingAsync(window, viewServer, gauge, token, (fuels, stock, recipes, breeder, market, planner, complete, available, server, checks) =>
            new BreedingRefillResult(BreedingRefillPlanner.Calculate(reserve, target, fuels, stock, recipes,
                breeder, market, planner, gauge == PaddockGauge.Experience, token), breeder, complete, available, server, checks));

    internal Task<BreedingExperiencePricing> EstimateTrainingCostsAsync(DofusClientWindowKey? window,
        string server, PaddockGauge gauge, IReadOnlyCollection<int> targets, CancellationToken token) =>
        targets.Count == 0 ? Task.FromResult(new BreedingExperiencePricing([], [])) :
        WithPricingAsync(window, server, gauge, token, (fuels, _, recipes, breeder, market, planner, _, _, _, checks) =>
            new BreedingExperiencePricing(BreedingExperienceCost.Estimate(targets, fuels, recipes, breeder, market, planner, token), checks));

    private async Task<T> WithPricingAsync<T>(DofusClientWindowKey? window, string? viewServer,
        PaddockGauge gauge, CancellationToken token,
        Func<BreedingFuelItem[], IReadOnlyDictionary<long, BigInteger>, ProductCraftRecipe[], int?,
            EquipmentProfitabilityContext, StockCraftPlanner, bool, bool, string?, IReadOnlyList<BreedingPriceCheck>, T> calculate)
    {
        var lease = window is { } key ? servers.GetWindowCaptureLease(key) : null;
        string? server = lease?.ServerName ?? viewServer;
        var source = window is { } stockWindow ? liveStock.ForWindow(stockWindow) : null;
        var containers = StockOpportunityService.CanUse(source, server)
            ? source!.Containers.Where(container => container.Complete && !container.Stale &&
                container.Container is "inventory" or "bank").ToArray()
            : [];
        var stock = containers.SelectMany(container => container.Items)
            .Where(item => item.ItemId > 0 && item.ItemId <= long.MaxValue)
            .GroupBy(item => (long)item.ItemId)
            .ToDictionary(group => group.Key, group => group.Aggregate(BigInteger.Zero, (sum, item) => sum + item.Quantity));
        bool stockComplete = containers.Any(container => container.Container == "inventory") &&
            containers.Any(container => container.Container == "bank");
        int? breeder = professions.ForWindow(window, server)?
            .FirstOrDefault(profession => string.Equals(profession.Name, "Éleveur", StringComparison.OrdinalIgnoreCase))?.Level;
        var fuels = (await catalog.GetBreedingFuelsAsync(token)).Where(fuel => fuel.Gauge == gauge).ToArray();
        ProductCraftRecipe[] recipes = [];
        bool recipesAvailable = true;
        if (breeder is > 0)
        {
            try
            {
                recipes = await catalog.GetProductCraftsAsync(fuels.Select(fuel => fuel.Id).ToArray(), token);
                recipes = recipes.Where(recipe => recipe.Product.Job == "Éleveur" && recipe.Product.JobLevel <= breeder).ToArray();
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Impossible de charger les recettes de carburant pour {Gauge}.", gauge);
                recipesAvailable = false;
            }
        }

        await using var db = await database.CreateDbContextAsync(token);
        var prices = new MarketPriceService(db, priority);
        IReadOnlyDictionary<(long, int), MarketPriceObservation> empty = new Dictionary<(long, int), MarketPriceObservation>();
        async Task<IReadOnlyDictionary<(long, int), MarketPriceObservation>> Quotes(MarketObjectType type) =>
            server is null ? empty : await prices.GetLatestObservationsForServerAsync(type, server, token, excludeUnavailableLots: true);

        var observations = new EquipmentProfitabilityContext
        {
            ResourcePrices = await Quotes(MarketObjectType.Resource),
            EquipmentPrices = recipes.Any(recipe => recipe.Ingredients.Any(i => i.MarketType == MarketObjectType.Equipment))
                ? await Quotes(MarketObjectType.Equipment) : empty,
            RunePrices = recipes.Any(recipe => recipe.Ingredients.Any(i => i.MarketType == MarketObjectType.Rune))
                ? await Quotes(MarketObjectType.Rune) : empty,
            Coefficients = []
        };
        var cutoff = DateTime.UtcNow - BreedingResaleAdvisor.PriceLifetime;
        var market = new EquipmentProfitabilityContext
        {
            ResourcePrices = Fresh(observations.ResourcePrices),
            EquipmentPrices = Fresh(observations.EquipmentPrices),
            RunePrices = Fresh(observations.RunePrices),
            Coefficients = []
        };
        var checks = fuels.Select(fuel => (Id: fuel.Id, fuel.Name, Type: MarketObjectType.Resource, Fuel: true))
            .Concat(recipes.SelectMany(recipe => recipe.Ingredients).Where(ingredient => stock.GetValueOrDefault(ingredient.ItemId) < ingredient.Count)
                .Select(ingredient => (Id: ingredient.ItemId, ingredient.Name, Type: ingredient.MarketType, Fuel: false)))
            .DistinctBy(entry => (entry.Id, entry.Type))
            .Select(entry => (Entry: entry, Last: StockCraftPlanner.PricesFor(observations, entry.Type).Values
                .Where(q => q.DofusDbId == entry.Id && q.Price > 0 && !q.IsCleared).Max(q => (DateTime?)q.ObservedAtUtc)))
            .Where(entry => entry.Last is null || entry.Last < cutoff)
            .OrderByDescending(entry => entry.Entry.Fuel).ThenByDescending(entry => entry.Last.HasValue)
            .ThenBy(entry => entry.Entry.Name)
            .Select(entry => new BreedingPriceCheck(entry.Entry.Id, entry.Entry.Name,
                entry.Last is { } seen ? new DateTimeOffset(DateTime.SpecifyKind(seen, DateTimeKind.Utc)) : null,
                entry.Entry.Fuel ? BreedingTrainingRules.Name(gauge) : "Ingrédient de carburant"))
            .ToArray();
        if (lease is { } activeLease && !servers.IsCaptureLeaseActive(activeLease))
            throw new InvalidOperationException("La session Dofus a changé pendant le calcul.");
        return await Task.Run(() => calculate(fuels, stock, recipes, breeder, market, new(prices),
            stockComplete, recipesAvailable, server, checks), token);

        Dictionary<(long, int), MarketPriceObservation> Fresh(IReadOnlyDictionary<(long, int), MarketPriceObservation> quotes) =>
            quotes.Where(entry => entry.Value.ObservedAtUtc >= cutoff).ToDictionary();
    }
}
