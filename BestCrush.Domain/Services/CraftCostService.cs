using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public sealed class CraftCostService(
    MarketPriceService marketPriceService)
{
    public CraftCostResult Calculate(
        Equipment equipment,
        IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation> resourceObservations,
        IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation> equipmentObservations)
    {
        List<CraftResourceCostLine> lines = [];

        foreach (
            IGrouping<long, RecipeEntry> group
            in equipment.Recipe
                .GroupBy(entry =>
                    entry.Resource.DofusDbId))
        {
            RecipeEntry first =
                group.First();

            Resource resource =
                first.Resource;

            int requiredQuantity =
                group.Sum(entry =>
                    entry.Count);

            MarketPurchaseResult? purchase =
                marketPriceService
                    .CalculateMinimumPurchaseCost(
                        resource.DofusDbId,
                        requiredQuantity,
                        resourceObservations
                    );

            lines.Add(
                new CraftResourceCostLine(
                    resource.DofusDbId,
                    resource.Name,
                    requiredQuantity,
                    purchase,
                    MarketObjectType.Resource
                )
            );
        }

        foreach (
            IGrouping<long, EquipmentRecipeEntry> group
            in equipment.EquipmentRecipe
                .GroupBy(entry =>
                    entry.IngredientEquipment.DofusDbId))
        {
            EquipmentRecipeEntry first =
                group.First();

            Equipment ingredient =
                first.IngredientEquipment;

            int requiredQuantity =
                group.Sum(entry =>
                    entry.Count);

            MarketPurchaseResult? purchase =
                BuildEquipmentPurchase(
                    ingredient,
                    requiredQuantity,
                    equipmentObservations
                );

            lines.Add(
                new CraftResourceCostLine(
                    ingredient.DofusDbId,
                    ingredient.Name,
                    requiredQuantity,
                    purchase,
                    MarketObjectType.Equipment
                )
            );
        }

        return new CraftCostResult(
            lines
                .OrderBy(line =>
                    line.ResourceName)
                .ToList()
        );
    }

    private static MarketPurchaseResult?
        BuildEquipmentPurchase(
            Equipment ingredient,
            int requiredQuantity,
            IReadOnlyDictionary<
                (long DofusDbId, int Quantity),
                MarketPriceObservation> equipmentObservations)
    {
        if (!equipmentObservations.TryGetValue(
            (
                ingredient.DofusDbId,
                1
            ),
            out MarketPriceObservation? observation) ||
            observation.Price <= 0)
        {
            return null;
        }

        long totalCost =
            checked(
                observation.Price *
                (long)requiredQuantity
            );

        return new MarketPurchaseResult(
            totalCost,
            requiredQuantity,
            requiredQuantity,
            new Dictionary<int, int>
            {
                [1] = requiredQuantity
            },
            new[] { observation }
        );
    }
}
