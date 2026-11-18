using System.Numerics;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

internal sealed record FuelUse(BreedingFuelItem Item, int Quantity);
internal sealed record FuelExchange(BreedingFuelItem Item, int Quantity, int Dust);
internal sealed record FuelPurchase(BreedingFuelItem Item, int LotSize, long Price);
internal sealed record FuelRefillPlan(IReadOnlyList<FuelUse> Stock, StockCraftPlan? Craft,
    IReadOnlyList<FuelPurchase> Purchases, long Cost, int ReserveAfter)
{
    internal IEnumerable<FuelUse> FuelingOrder => Stock
        .Concat(Exchanges.Select(exchange => new FuelUse(exchange.Item, exchange.Quantity)))
        .Concat(Craft is null || CraftedFuel is null ? [] : [new FuelUse(CraftedFuel, Craft.Quantity)])
        .Concat(Purchases.Select(purchase => new FuelUse(purchase.Item, purchase.LotSize)))
        .OrderBy(use => use.Item.Capacity);

    internal BreedingFuelItem? CraftedFuel { get; init; }
    internal IReadOnlyList<FuelExchange> Exchanges { get; init; } = [];
    internal bool RefillAfterEmpty { get; init; }

    internal TimeSpan? EstimateDuration(int initialReserve, int target)
    {
        if (!RefillAfterEmpty)
            return BreedingFuel.TimeToSpend(ReserveAfter, target);

        TimeSpan duration = TimeSpan.Zero;
        Consume(initialReserve);
        foreach (var tier in FuelingOrder.GroupBy(use => use.Item.Capacity))
        {
            long available = tier.Sum(use => (long)use.Quantity * use.Item.Points);
            while (target > 0 && available > 0)
            {
                int dose = (int)Math.Min(available, tier.Key);
                Consume(dose);
                available -= dose;
            }
        }
        return target == 0 ? duration : null;

        void Consume(int fuel)
        {
            int needed = Math.Min(target, fuel);
            duration += BreedingFuel.TimeToSpend(fuel, needed)!.Value;
            target -= needed;
        }
    }
}

internal static class BreedingRefillPlanner
{
    internal const long DustItemId = 33_511;

    internal static FuelRefillPlan? Calculate(int reserve, int target, IReadOnlyList<BreedingFuelItem> catalog,
        IReadOnlyDictionary<long, BigInteger> stock, IReadOnlyList<ProductCraftRecipe> recipes,
        int? breederLevel, EquipmentProfitabilityContext prices, StockCraftPlanner craftPlanner,
        bool allowRefills = false, CancellationToken token = default)
    {
        if (!allowRefills)
            target = Math.Min(target, BreedingFuel.Capacity);
        var fuels = catalog.Where(item => (allowRefills || item.Capacity > reserve) && item.Points > 0).ToArray();
        var stockUsed = new List<FuelUse>();
        int stockReserve = reserve;
        foreach (var item in fuels.OrderBy(item => item.Capacity).ThenByDescending(item => item.Points))
        {
            int cap = allowRefills ? target : item.Capacity;
            int needed = Units(Math.Min(target, cap) - stockReserve, item.Points);
            int quantity = (int)BigInteger.Min(needed, stock.GetValueOrDefault(item.Id));
            if (quantity <= 0)
                continue;
            stockUsed.Add(new(item, quantity));
            stockReserve = After(stockReserve, target, item, quantity, allowRefills);
        }
        var exchanges = new List<FuelExchange>();
        BigInteger dust = stock.GetValueOrDefault(DustItemId);
        foreach (var item in fuels.Where(item => item.Points == 10_000).OrderBy(item => item.Capacity))
        {
            int dustPerUnit = item.Capacity switch { 80_000 => 50, 140_000 => 200, 180_000 => 800, 200_000 => 3_200, _ => 0 };
            if (dustPerUnit == 0 || dust < dustPerUnit || stockReserve >= target) continue;
            int cap = allowRefills ? target : item.Capacity;
            int quantity = (int)BigInteger.Min(Units(Math.Min(target, cap) - stockReserve, item.Points), dust / dustPerUnit);
            if (quantity == 0) continue;
            int dustUsed = quantity * dustPerUnit;
            exchanges.Add(new(item, quantity, dustUsed));
            dust -= dustUsed;
            stockReserve = After(stockReserve, target, item, quantity, allowRefills);
        }
        if (stockReserve >= target)
            return new(stockUsed, null, [], 0, stockReserve) { RefillAfterEmpty = allowRefills, Exchanges = exchanges };

        var remainingStock = stock.ToDictionary();
        foreach (var used in stockUsed)
            remainingStock[used.Item.Id] -= used.Quantity;
        if (exchanges.Count > 0)
            remainingStock[DustItemId] = dust;
        var fuelsById = fuels.ToDictionary(item => item.Id);
        var offers = prices.ResourcePrices.Values.Where(quote => !quote.IsCleared &&
                quote.Price > 0 && quote.Quantity > 0 && fuelsById.ContainsKey(quote.DofusDbId))
            .Select(quote => new FuelPurchase(fuelsById[quote.DofusDbId], quote.Quantity, quote.Price)).ToArray();
        var purchaseOnly = Walk(stockReserve, target, offers, null, null, allowRefills, token);
        long? bestCost = purchaseOnly.Cost;
        var bestPurchases = purchaseOnly.Purchases;
        int finalReserve = purchaseOnly.Reserve;
        StockCraftPlan? bestCraft = null;
        BreedingFuelItem? craftedFuel = null;
        foreach (var recipe in recipes)
        {
            if (breederLevel is null || recipe.Product.JobLevel is not { } requiredLevel ||
                breederLevel < requiredLevel || recipe.Product.Job != "Éleveur" ||
                !fuelsById.TryGetValue(recipe.Product.DofusDbId, out var item))
                continue;

            int cap = allowRefills ? target : Math.Min(item.Capacity, target);
            int maxQuantity = Units(cap - stockReserve, item.Points);
            if (maxQuantity == 0) continue;
            var options = new Dictionary<int, (StockCraftPlan Plan, long Cost)>();
            for (int quantity = 1; quantity <= maxQuantity; quantity++)
            {
                token.ThrowIfCancellationRequested();
                var craft = craftPlanner.Calculate(recipe, quantity, remainingStock, prices);
                if (!craft.HasRecipe || craft.Ingredients.Any(ingredient => ingredient.Purchase is null))
                    break;
                long cash = craft.Ingredients.Sum(ingredient => ingredient.Purchase!.TotalCost);
                if (bestCost is { } best && cash >= best)
                    break;
                options.Add(quantity, (craft, cash));
            }
            if (options.Count == 0) continue;
            var combined = Walk(stockReserve, target, offers, (item, options), bestCost, allowRefills, token);
            if (combined.Cost is null || (bestCost is { } current && combined.Cost >= current))
                continue;
            bestCost = combined.Cost;
            bestCraft = combined.Craft;
            bestPurchases = combined.Purchases;
            finalReserve = combined.Reserve;
            craftedFuel = bestCraft is null ? null : item;
        }

        if (bestCost is null)
            return null;
        return new(stockUsed, bestCraft, bestPurchases, bestCost.Value, finalReserve)
        {
            CraftedFuel = craftedFuel,
            RefillAfterEmpty = allowRefills,
            Exchanges = exchanges
        };
    }

    private sealed record Step(long Cost, int Before, FuelPurchase? Purchase, StockCraftPlan? Craft);

    private static (long? Cost, List<FuelPurchase> Purchases, StockCraftPlan? Craft, int Reserve) Walk(
        int start, int target, FuelPurchase[] offers,
        (BreedingFuelItem Fuel, Dictionary<int, (StockCraftPlan Plan, long Cost)> Options)? crafts, long? ceiling,
        bool allowRefills, CancellationToken token)
    {
        // The graph is acyclic: each action raises the reserve. A second layer
        // records paths that have already scheduled the selected recipe.
        var withoutCraft = new Step?[target + 1];
        var withCraft = crafts.HasValue ? new Step?[target + 1] : null;
        withoutCraft[start] = new(0, start, null, null);
        for (int reserve = start; reserve < target; reserve++)
        {
            if (reserve % 2048 == 0) token.ThrowIfCancellationRequested();
            Buy(withoutCraft, reserve);
            if (withCraft is null || crafts is not { } recipe) continue;
            Buy(withCraft, reserve);
            if (withoutCraft[reserve] is not { } from || (!allowRefills && reserve >= recipe.Fuel.Capacity)) continue;
            foreach (var (_, option) in recipe.Options)
                Update(withCraft, reserve, from.Cost, option.Cost, recipe.Fuel,
                    option.Plan.Quantity, null, option.Plan);
        }
        var path = withCraft ?? withoutCraft;
        if (path[target] is not { } best) return (null, [], null, start);
        var purchases = new List<FuelPurchase>();
        StockCraftPlan? craft = null;
        int end = target;
        int reserveAfter = target;
        while (end > start)
        {
            Step step = path[end]!;
            if (step.Purchase is { } purchase)
            {
                purchases.Add(purchase);
                if (end == target)
                    reserveAfter = After(step.Before, target, purchase.Item, purchase.LotSize, allowRefills);
            }
            if (step.Craft is { } crafted)
            {
                craft = crafted;
                if (end == target)
                    reserveAfter = After(step.Before, target, crafts!.Value.Fuel, crafted.Quantity, allowRefills);
                path = withoutCraft;
            }
            end = step.Before;
        }
        purchases.Reverse();
        return (best.Cost, purchases, craft, reserveAfter);

        void Buy(Step?[] layer, int reserve)
        {
            if (layer[reserve] is not { } from) return;
            foreach (var offer in offers)
                if (allowRefills || offer.Item.Capacity > reserve)
                    Update(layer, reserve, from.Cost, offer.Price, offer.Item, offer.LotSize, offer, null);
        }

        void Update(Step?[] layer, int reserve, long baseCost, long cash, BreedingFuelItem fuel,
            int count, FuelPurchase? purchase, StockCraftPlan? craft)
        {
            if (baseCost > long.MaxValue - cash) return;
            long cost = baseCost + cash;
            if (ceiling is { } limit && cost >= limit) return;
            int after = Math.Min(target, After(reserve, target, fuel, count, allowRefills));
            if (layer[after] is not { } existing || cost < existing.Cost)
                layer[after] = new(cost, reserve, purchase, craft);
        }
    }

    private static int After(int before, int target, BreedingFuelItem fuel, int quantity, bool allowRefills) =>
        (int)Math.Min(allowRefills ? int.MaxValue : fuel.Capacity,
            before + (long)fuel.Points * Math.Min(quantity, Units(target - before, fuel.Points)));

    private static int Units(int points, int amount) => points <= 0 ? 0 : (points - 1) / amount + 1;
}
