namespace BestCrush.Domain.Models;

public enum DataFreshness
{
    Fresh,
    Aging,
    Stale
}

public static class DataFreshnessEvaluator
{
    public static DataFreshness Evaluate(
        DateTime observedAtUtc,
        DateTime? nowUtc = null)
    {
        DateTime now =
            nowUtc ?? DateTime.UtcNow;

        TimeSpan age =
            now - observedAtUtc;

        if (age < TimeSpan.FromMinutes(30))
        {
            return DataFreshness.Fresh;
        }

        if (age < TimeSpan.FromHours(12))
        {
            return DataFreshness.Aging;
        }

        return DataFreshness.Stale;
    }

    public static DataFreshness Worst(
        IEnumerable<MarketPriceObservation> observations,
        DateTime? nowUtc = null)
    {
        return observations
            .Select(observation =>
                Evaluate(
                    observation.ObservedAtUtc,
                    nowUtc
                ))
            .Max();
    }
}