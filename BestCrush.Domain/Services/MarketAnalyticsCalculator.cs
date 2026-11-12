using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

/// <summary>Pure comparison math: common time grid, bounded carry, fixed cohorts and equal member weights.</summary>
public static class MarketAnalyticsCalculator
{
    public static MarketAnalyticsComparison Calculate(MarketAnalyticsRequest request,
        IReadOnlyList<IReadOnlyList<MarketAnalyticsMember>> selections,
        IReadOnlyList<MarketAnalyticsObservation> observations, CancellationToken token = default)
    {
        if (request.Series.Count != selections.Count) throw new ArgumentException("Sélections incohérentes.");
        if (request.MaxPoints is < 2 or > 512) throw new ArgumentOutOfRangeException(nameof(request.MaxPoints));
        if (request.MaxQuoteAgeHours is < 1 or > 8760) throw new ArgumentOutOfRangeException(nameof(request.MaxQuoteAgeHours));
        DateTime to = request.ToUtc ?? DateTime.UtcNow;
        DateTime from = request.FromUtc ?? request.Period switch
        {
            MarketAnalyticsPeriod.Hours24 => to.AddHours(-24),
            MarketAnalyticsPeriod.Days7 => to.AddDays(-7),
            MarketAnalyticsPeriod.Days30 => to.AddDays(-30),
            _ => observations.Where(o => o.AtUtc <= to && o.Price > 0 && !o.IsCleared)
                .Select(o => o.AtUtc).DefaultIfEmpty(to.AddDays(-7)).Min()
        };
        if (from >= to) throw new ArgumentException("La fin de période doit suivre son début.");
        var grid = Grid(from, to, request.Period, request.MaxPoints);
        var age = TimeSpan.FromHours(request.MaxQuoteAgeHours);
        var byItem = observations.Where(o => o.AtUtc <= to && o.AtUtc >= from - age && o.Quantity > 0)
            .GroupBy(o => o.Key).ToDictionary(g => g.Key, g => g.OrderBy(o => o.AtUtc).ToArray());
        var cache = new Dictionary<TimelineKey, MarketAnalyticsSample[]>();
        var emptyTimeline = grid.Select(t => new MarketAnalyticsSample(t,null,null,0,null,false)).ToArray();
        var timelines = new List<(MarketAnalyticsMember Member, MarketAnalyticsSample[] Samples)>[selections.Count];
        for (int group = 0; group < selections.Count; group++)
        {
            token.ThrowIfCancellationRequested();
            var definition = request.Series[group];
            timelines[group] = [];
            foreach (var member in selections[group].DistinctBy(m => m.Key))
            {
                var key = new TimelineKey(member.Key, definition.LotQuantity, definition.SourceMode, definition.Aggregation, definition.ValueMode);
                if (!byItem.TryGetValue(member.Key,out var itemObservations))
                {
                    timelines[group].Add((member,emptyTimeline)); continue;
                }
                if (!cache.TryGetValue(key, out var samples))
                {
                    samples = Timeline(itemObservations, definition, grid, age, token);
                    cache[key] = samples;
                }
                timelines[group].Add((member, samples));
            }
        }

        // An entirely unobserved group is reported as empty; it cannot fabricate a reference.
        var observedGroups = timelines.Select(g => g.Where(m => m.Samples.Any(s => s.Price is > 0)).ToArray())
            .Where(g => g.Length > 0).ToArray();
        int? reference = null;
        double bestMinimum = -1,bestAverage = -1;
        for (int i = 0; i < grid.Length && observedGroups.Length > 0; i++)
        {
            var coverage = observedGroups.Select(g => g.Count(m => m.Samples[i].Price is > 0)/(double)g.Length).ToArray();
            double minimum = coverage.Min(),average = coverage.Average();
            if (minimum <= 0) continue;
            // Prefer the most representative common date; ties keep the earliest date.
            if (minimum > bestMinimum+1e-9 || (Math.Abs(minimum-bestMinimum)<1e-9 && average > bestAverage+1e-9))
            { reference = i; bestMinimum = minimum; bestAverage = average; }
        }
        List<MarketAnalyticsGroupResult> results = [];
        for (int g = 0; g < timelines.Length; g++)
        {
            token.ThrowIfCancellationRequested();
            var definition = request.Series[g];
            var members = timelines[g].Select(m =>
            {
                double? baseline = reference is int at ? m.Samples[at].Price : null;
                return new MarketAnalyticsMemberSeries(m.Member,
                    baseline is > 0 ? m.Samples.Select(s => s with { Index = s.Price.HasValue ? s.Price.Value / baseline.Value * 100 : null }).ToArray() : m.Samples, baseline);
            }).ToArray();
            // No entrants later in the period: their missing baseline is visible in the member table.
            var cohort = members.Where(m => m.ReferencePrice is > 0).ToArray();
            var rawCohort = reference.HasValue ? cohort : members.Where(m => m.Samples.Any(s => s.Price.HasValue)).ToArray();
            var fused = grid.Select((time,i) =>
            {
                var parts = rawCohort.Select(m => m.Samples[i]).ToArray();
                bool complete = parts.Length > 0 && parts.All(s => s.Price.HasValue);
                double? price = complete ? GroupValue(parts.Select(s => s.Price!.Value),definition.GroupAggregation) : null;
                double? index = complete && reference.HasValue ? GroupValue(parts.Select(s => s.Index!.Value),definition.GroupAggregation) : null;
                return new MarketAnalyticsSample(time,price,index,parts.Sum(s => s.ObservationCount),
                    complete ? parts.Min(s => s.LastObservedAtUtc) : null,complete && parts.Any(s => s.IsCarried));
            }).ToArray();
            results.Add(new(definition.Id,definition.ItemName,members,fused,cohort.Length));
        }
        return new(request.ServerName,from,to,reference is int r ? grid[r] : null,results,observations.Count);
    }

    private readonly record struct TimelineKey(MarketAnalyticsItemKey Item, int? Lot,
        MarketAnalyticsSourceMode Source, MarketAnalyticsAggregation Aggregation, MarketAnalyticsValueMode ValueMode);
    private readonly record struct Slot(int Quantity, MarketPriceSource Source);
    private sealed record Quote(double? Value, DateTime At);

    private static MarketAnalyticsSample[] Timeline(IReadOnlyList<MarketAnalyticsObservation> all,
        MarketAnalyticsSeriesDefinition definition, DateTime[] grid, TimeSpan age, CancellationToken token)
    {
        var observations = all.Where(o => (definition.LotQuantity is null || o.Quantity == definition.LotQuantity) &&
            (definition.SourceMode == MarketAnalyticsSourceMode.All || o.Source ==
                (definition.SourceMode == MarketAnalyticsSourceMode.Manual ? MarketPriceSource.Manual : MarketPriceSource.InGameAutomatic))).ToArray();
        var quotes = new Dictionary<Slot, Quote>();
        var points = new MarketAnalyticsSample[grid.Length];
        int cursor = 0;
        for (int i = 0; i < grid.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var batches = new Dictionary<Slot,List<double>>();
            while (cursor < observations.Length && observations[cursor].AtUtc <= grid[i])
            {
                var o = observations[cursor++];
                var slot = new Slot(o.Quantity,o.Source);
                if (o.IsCleared)
                {
                    quotes[slot] = new(null,o.AtUtc); batches.Remove(slot); continue;
                }
                if (o.Price <= 0) continue;
                double price = definition.LotQuantity is null || definition.ValueMode == MarketAnalyticsValueMode.UnitPrice
                    ? (double)o.Price / o.Quantity : o.Price;
                quotes[slot] = new(price,o.AtUtc);
                if (i > 0)
                {
                    if (!batches.TryGetValue(slot,out var values)) batches[slot] = values = [];
                    values.Add(price);
                }
            }
            // Carry the interval's computed value for each lot/source, not its final
            // raw observation. Other members may refresh while this value is reused.
            foreach (var (slot,batch) in batches)
                quotes[slot] = quotes[slot] with { Value = Aggregate(batch,definition.Aggregation) };
            var valid = quotes.Where(pair => pair.Value.Value.HasValue && grid[i] - pair.Value.At <= age).ToArray();
            double? value = valid.Length == 0 ? null : Aggregate(valid.Select(pair => pair.Value.Value!.Value),definition.Aggregation);
            int count = i == 0
                ? valid.Count(pair => pair.Value.At == grid[i])
                : valid.Sum(pair => batches.TryGetValue(pair.Key,out var batch) ? batch.Count : 0);
            points[i] = new(grid[i],value,null,count,valid.Length == 0 ? null : valid.Max(pair => pair.Value.At),value.HasValue && count == 0);
        }
        return points;
    }

    private static DateTime[] Grid(DateTime from, DateTime to, MarketAnalyticsPeriod period, int maxPoints)
    {
        long preferred = period switch
        {
            MarketAnalyticsPeriod.Hours24 => TimeSpan.TicksPerHour,
            MarketAnalyticsPeriod.Days7 => TimeSpan.TicksPerHour * 6,
            MarketAnalyticsPeriod.Days30 => TimeSpan.TicksPerDay,
            _ => TimeSpan.TicksPerHour
        };
        long span = (to - from).Ticks;
        long step = Math.Max(preferred,(long)Math.Ceiling((double)span / (maxPoints - 1)));
        List<DateTime> points = [from];
        for (long elapsed = step; elapsed < span; elapsed += step) points.Add(from.AddTicks(elapsed));
        points.Add(to);
        return points.ToArray();
    }

    private static double GroupValue(IEnumerable<double> values, MarketAnalyticsGroupAggregation aggregation) =>
        Aggregate(values,aggregation == MarketAnalyticsGroupAggregation.Median ? MarketAnalyticsAggregation.Median : MarketAnalyticsAggregation.Mean);

    public static double Aggregate(IEnumerable<double> values, MarketAnalyticsAggregation aggregation)
    {
        double[] sorted = values.Order().ToArray();
        if (sorted.Length == 0) throw new ArgumentException("Aucune valeur à agréger.");
        if (aggregation == MarketAnalyticsAggregation.Mean) return sorted.Average();
        double percentile = aggregation switch { MarketAnalyticsAggregation.FirstQuartile => .25, MarketAnalyticsAggregation.ThirdQuartile => .75, _ => .5 };
        double position = (sorted.Length - 1) * percentile;
        int lower = (int)Math.Floor(position), upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}
