using System.Numerics;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

internal static class BreedingExperienceCost
{
    internal static Dictionary<int, long> Estimate(IReadOnlyCollection<int> targets,
        IReadOnlyList<BreedingFuelItem> fuels, IReadOnlyList<ProductCraftRecipe> recipes, int? breeder,
        EquipmentProfitabilityContext market, StockCraftPlanner planner, CancellationToken token)
    {
        var byId = fuels.Where(fuel => fuel.Points > 0).ToDictionary(fuel => fuel.Id);
        if (targets.Count == 0 || byId.Count == 0) return [];
        int step = byId.Values.Select(fuel => fuel.Points).Aggregate(Gcd);
        int max = targets.Max();
        int steps = Units(max, step);
        var offers = market.ResourcePrices.Values.Where(observation =>
                !observation.IsCleared && observation.Price > 0 && observation.Quantity > 0 &&
                byId.ContainsKey(observation.DofusDbId))
            .Select(observation => (
                Steps: (int)Math.Min(steps, (long)(byId[observation.DofusDbId].Points / step) * observation.Quantity),
                Cost: observation.Price))
            .GroupBy(offer => offer.Steps).Select(group => (Steps: group.Key, Cost: group.Min(offer => offer.Cost)))
            .ToArray();

        var purchases = new long?[steps + 1];
        purchases[0] = 0;
        for (int need = 1; need <= steps; need++)
        {
            token.ThrowIfCancellationRequested();
            foreach (var offer in offers)
            {
                if (purchases[Math.Max(0, need - offer.Steps)] is { } remainder && remainder <= long.MaxValue - offer.Cost)
                    SelectCheaper(ref purchases[need], remainder + offer.Cost);
            }
        }

        var cheapest = (long?[])purchases.Clone();
        // One production batch can be complemented by whole lots. The cost of
        // the ingredients comes from the same buying calculation as the refill panel.
        IReadOnlyDictionary<long, BigInteger> emptyStock = new Dictionary<long, BigInteger>();
        foreach (var recipe in recipes)
        {
            if (recipe.Product.Job != "Éleveur" || recipe.Product.JobLevel is not { } required ||
                breeder is not { } level || level < required || !byId.TryGetValue(recipe.Product.DofusDbId, out var fuel))
                continue;
            int stepsPerUnit = fuel.Points / step;
            for (int quantity = 1; quantity <= Units(steps, stepsPerUnit); quantity++)
            {
                token.ThrowIfCancellationRequested();
                var craft = planner.Calculate(recipe, quantity, emptyStock, market);
                if (!craft.HasRecipe || craft.Ingredients.Any(ingredient => ingredient.Purchase is null)) break;
                long cost = craft.Ingredients.Sum(ingredient => ingredient.Purchase!.TotalCost);
                int capacity = quantity * stepsPerUnit;
                for (int need = Math.Min(steps, capacity - stepsPerUnit + 1); need <= steps; need++)
                {
                    if (purchases[Math.Max(0, need - capacity)] is { } remainder && remainder <= long.MaxValue - cost)
                        SelectCheaper(ref cheapest[need], remainder + cost);
                }
            }
        }
        return targets.Distinct().Where(target => cheapest[Units(target, step)].HasValue)
            .ToDictionary(target => target, target => cheapest[Units(target, step)]!.Value);
    }

    private static void SelectCheaper(ref long? current, long proposed)
    {
        if (!current.HasValue || proposed < current.Value) current = proposed;
    }

    private static int Units(int value, int step) => value == 0 ? 0 : 1 + (value - 1) / step;

    private static int Gcd(int left, int right)
    {
        while (right != 0) (left, right) = (right, left % right);
        return left;
    }
}
