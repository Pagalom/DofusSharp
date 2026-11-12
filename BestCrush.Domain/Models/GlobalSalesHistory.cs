namespace BestCrush.Domain.Models;

public sealed record GlobalSalePoint(long UnitPrice, long UnitsSold, DateTimeOffset RecordedAtUtc);

public sealed record GlobalSalesHistory(SalesMarketKey Market, DateTimeOffset ObservedAtUtc,
    IReadOnlyList<GlobalSalePoint> Last24Hours, IReadOnlyList<GlobalSalePoint> Last30Days)
{
    public IReadOnlyList<GlobalSalePoint> Points(int days) => days switch
    {
        1 => Last24Hours.Where(x => x.RecordedAtUtc >= ObservedAtUtc.AddDays(-1)).ToArray(),
        7 or 30 => Last30Days.Where(x => x.RecordedAtUtc >= ObservedAtUtc.AddDays(-days)).ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(days))
    };

    public GlobalSalesObservation Period(int days)
    {
        long units = Points(days).Aggregate(0L, (sum, row) => checked(sum + row.UnitsSold));
        return new(Market, ObservedAtUtc.AddDays(-days), ObservedAtUtc, units, ObservedAtUtc);
    }

    public (double UnitPrice, DateTimeOffset? LastAtOrAboveUtc)? SevenDayMedian()
    {
        bool IsSale(GlobalSalePoint point) =>
            point.UnitPrice > 0 && point.UnitsSold > 0 && point.RecordedAtUtc <= ObservedAtUtc;

        var week = Points(7).Where(IsSale).OrderBy(point => point.UnitPrice).ToArray();
        if (week.Length == 0)
            return null;

        long units = week.Sum(point => point.UnitsSold);
        long lowerRank = (units - 1) / 2;
        long upperRank = units / 2;
        long cumulative = 0;
        long? lowerPrice = null;
        double median = 0;
        foreach (var point in week)
        {
            cumulative += point.UnitsSold;
            if (cumulative > lowerRank)
                lowerPrice ??= point.UnitPrice;
            if (cumulative > upperRank)
            {
                median = (point.UnitPrice + (double)lowerPrice!.Value) / 2;
                break;
            }
        }

        var recent = Points(1).Where(IsSale).ToArray();
        var older = Points(30).Where(point => IsSale(point) &&
            (recent.Length == 0 || point.RecordedAtUtc < ObservedAtUtc.AddDays(-1)));
        var lastAtOrAbove = recent.Concat(older).Where(point => point.UnitPrice >= median)
            .Max(point => (DateTimeOffset?)point.RecordedAtUtc);
        return (median, lastAtOrAbove);
    }

    public double? WeightedUnitPrice(int days)
    {
        var points = Points(days).Where(x => x.UnitPrice > 0 && x.UnitsSold > 0).ToArray();
        if (points.Length == 0) return null;
        double total = points.Sum(x => (double)x.UnitsSold);
        return points.Sum(x => x.UnitPrice * (x.UnitsSold / total));
    }
}
