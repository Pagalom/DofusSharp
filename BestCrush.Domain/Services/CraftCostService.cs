using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public sealed class CraftCostService(
    MarketPriceService marketPriceService)
{
    public async Task<CraftCostResult>
        CalculateAsync(
            Equipment equipment,
            string serverName,
            CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation>
            observations =
                await marketPriceService
                    .GetLatestObservationsForServerAsync(
                        MarketObjectType.Resource,
                        serverName,
                        cancellationToken
                    );

        return Calculate(
            equipment,
            observations
        );
    }

    public CraftCostResult Calculate(
        Equipment equipment,
        IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation> observations)
    {
        List<CraftResourceCostLine> lines = [];

        foreach (IGrouping<long, RecipeEntry> group
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
                        observations
                    );

            lines.Add(
                new CraftResourceCostLine(
                    resource.DofusDbId,
                    resource.Name,
                    requiredQuantity,
                    purchase
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
}