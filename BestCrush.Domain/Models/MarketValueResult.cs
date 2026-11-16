namespace BestCrush.Domain.Models;

public readonly record struct MarketResaleLot(long Count, long Price, MarketPriceObservation Observation);

public readonly record struct MarketValueResult(
    double Value,
    bool IsEstimated,
    IReadOnlyCollection<MarketPriceObservation> UsedObservations)
{
    public MarketValueResult(
        double value,
        bool isEstimated)
        : this(
            value,
            isEstimated,
            Array.Empty<MarketPriceObservation>())
    {
    }

    public IReadOnlyList<MarketResaleLot> Lots { get; init; } = [];

    public DataFreshness? Freshness =>
        UsedObservations.Count == 0
            ? null
            : DataFreshnessEvaluator.Worst(
                UsedObservations
            );
}