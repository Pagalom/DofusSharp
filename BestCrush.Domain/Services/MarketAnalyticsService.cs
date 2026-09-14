using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public sealed class MarketAnalyticsService(
    MarketPriceService marketPriceService)
{
    public async Task<MarketAnalyticsSeriesResult>
        BuildSeriesAsync(
            MarketAnalyticsSeriesDefinition definition,
            string serverName,
            MarketAnalyticsPeriod period,
            CancellationToken cancellationToken = default)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DateTime? fromUtc = GetFromUtc(period, nowUtc);

        MarketPriceSource? source =
            definition.SourceMode switch
            {
                MarketAnalyticsSourceMode.InGameAutomatic =>
                    MarketPriceSource.InGameAutomatic,
                MarketAnalyticsSourceMode.Manual =>
                    MarketPriceSource.Manual,
                _ => null
            };

        IReadOnlyList<MarketPriceObservation> observations =
            await marketPriceService.GetHistoryAsync(
                definition.ObjectType,
                definition.DofusDbId,
                serverName,
                fromUtc,
                nowUtc,
                source,
                definition.LotQuantity,
                cancellationToken
            );

        if (observations.Count == 0)
        {
            return new MarketAnalyticsSeriesResult(
                definition.Id,
                BuildLabel(definition),
                definition.Transformation,
                []
            );
        }

        TimeSpan bucketSize =
            GetBucketSize(
                period,
                observations[0].ObservedAtUtc,
                observations[^1].ObservedAtUtc
            );

        List<MarketAnalyticsPoint> points =
            observations
                .GroupBy(observation =>
                    BucketStart(
                        observation.ObservedAtUtc,
                        bucketSize
                    ))
                .OrderBy(group => group.Key)
                .Select(group =>
                {
                    double[] values = group
                        .Select(observation =>
                            definition.LotQuantity is null ||
                            definition.ValueMode ==
                                MarketAnalyticsValueMode.UnitPrice
                                    ? (double)observation.Price /
                                        observation.Quantity
                                    : observation.Price)
                        .OrderBy(value => value)
                        .ToArray();

                    double rawValue = Aggregate(
                        values,
                        definition.Aggregation
                    );

                    return new MarketAnalyticsPoint(
                        group.Key +
                            TimeSpan.FromTicks(
                                bucketSize.Ticks / 2),
                        rawValue,
                        rawValue,
                        values.Length
                    );
                })
                .ToList();

        if (definition.Transformation ==
            MarketAnalyticsTransformation.Base100)
        {
            double? baseValue = points
                .Select(point => point.Value)
                .FirstOrDefault(value => value > 0);

            if (baseValue is > 0)
            {
                double divisor = baseValue.Value;

                points = points
                    .Select(point =>
                        point with
                        {
                            Value =
                                point.Value /
                                divisor *
                                100.0
                        })
                    .ToList();
            }
        }

        return new MarketAnalyticsSeriesResult(
            definition.Id,
            BuildLabel(definition),
            definition.Transformation,
            points
        );
    }

    private static DateTime? GetFromUtc(
        MarketAnalyticsPeriod period,
        DateTime nowUtc)
    {
        return period switch
        {
            MarketAnalyticsPeriod.Hours24 =>
                nowUtc.AddHours(-24),
            MarketAnalyticsPeriod.Days7 =>
                nowUtc.AddDays(-7),
            MarketAnalyticsPeriod.Days30 =>
                nowUtc.AddDays(-30),
            _ => null
        };
    }

    private static TimeSpan GetBucketSize(
        MarketAnalyticsPeriod period,
        DateTime firstUtc,
        DateTime lastUtc)
    {
        return period switch
        {
            MarketAnalyticsPeriod.Hours24 =>
                TimeSpan.FromHours(1),
            MarketAnalyticsPeriod.Days7 =>
                TimeSpan.FromHours(6),
            MarketAnalyticsPeriod.Days30 =>
                TimeSpan.FromDays(1),
            _ => AdaptiveBucketSize(
                lastUtc - firstUtc)
        };
    }

    private static TimeSpan AdaptiveBucketSize(
        TimeSpan span)
    {
        if (span <= TimeSpan.FromDays(3))
        {
            return TimeSpan.FromHours(1);
        }

        if (span <= TimeSpan.FromDays(14))
        {
            return TimeSpan.FromHours(3);
        }

        if (span <= TimeSpan.FromDays(60))
        {
            return TimeSpan.FromHours(12);
        }

        if (span <= TimeSpan.FromDays(365))
        {
            return TimeSpan.FromDays(1);
        }

        return TimeSpan.FromDays(7);
    }

    private static DateTime BucketStart(
        DateTime utc,
        TimeSpan bucketSize)
    {
        DateTime normalized =
            utc.Kind == DateTimeKind.Utc
                ? utc
                : utc.ToUniversalTime();

        long ticks =
            normalized.Ticks /
            bucketSize.Ticks *
            bucketSize.Ticks;

        return new DateTime(
            ticks,
            DateTimeKind.Utc
        );
    }

    private static double Aggregate(
        IReadOnlyList<double> sortedValues,
        MarketAnalyticsAggregation aggregation)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        return aggregation switch
        {
            MarketAnalyticsAggregation.Mean =>
                sortedValues.Average(),
            MarketAnalyticsAggregation.FirstQuartile =>
                Percentile(sortedValues, 0.25),
            MarketAnalyticsAggregation.ThirdQuartile =>
                Percentile(sortedValues, 0.75),
            _ =>
                Percentile(sortedValues, 0.50)
        };
    }

    private static double Percentile(
        IReadOnlyList<double> sortedValues,
        double percentile)
    {
        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        double position =
            (sortedValues.Count - 1) *
            percentile;

        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);

        if (lower == upper)
        {
            return sortedValues[lower];
        }

        double fraction =
            position - lower;

        return sortedValues[lower] +
            (sortedValues[upper] -
                sortedValues[lower]) *
            fraction;
    }

    private static string BuildLabel(
        MarketAnalyticsSeriesDefinition definition)
    {
        string aggregation =
            definition.Aggregation switch
            {
                MarketAnalyticsAggregation.Mean =>
                    "moyenne",
                MarketAnalyticsAggregation.FirstQuartile =>
                    "Q1",
                MarketAnalyticsAggregation.ThirdQuartile =>
                    "Q3",
                _ => "médiane"
            };

        string lot =
            definition.LotQuantity is int quantity
                ? $"x{quantity}"
                : "tous lots";

        string value =
            definition.LotQuantity is null ||
            definition.ValueMode ==
                MarketAnalyticsValueMode.UnitPrice
                    ? "prix unitaire"
                    : "prix du lot";

        string source =
            definition.SourceMode switch
            {
                MarketAnalyticsSourceMode.Manual =>
                    "manuel",
                MarketAnalyticsSourceMode.All =>
                    "toutes sources",
                _ => "jeu"
            };

        string transformation =
            definition.Transformation ==
                MarketAnalyticsTransformation.Base100
                    ? " · base 100"
                    : string.Empty;

        return
            $"{definition.ItemName} — " +
            $"{aggregation} · {lot} · " +
            $"{value} · {source}" +
            transformation;
    }
}
