namespace BestCrush.Domain.Models;

public sealed record MarketPurchaseResult(
    long TotalCost,
    int RequiredQuantity,
    int PurchasedQuantity,
    IReadOnlyDictionary<int, int> Lots,
    IReadOnlyCollection<MarketPriceObservation> UsedObservations)
{
    public MarketPurchaseResult(
        long totalCost,
        int requiredQuantity,
        int purchasedQuantity,
        IReadOnlyDictionary<int, int> lots)
        : this(
            totalCost,
            requiredQuantity,
            purchasedQuantity,
            lots,
            Array.Empty<MarketPriceObservation>())
    {
    }

    public int SurplusQuantity =>
        PurchasedQuantity - RequiredQuantity;

    public DataFreshness? Freshness =>
        UsedObservations.Count == 0
            ? null
            : DataFreshnessEvaluator.Worst(
                UsedObservations
            );
}