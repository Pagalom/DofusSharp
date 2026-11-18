using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed record BreedingResaleOption(int Level, long SalePrice, long ExperienceCost, DateTimeOffset ObservedAtUtc,
    int OfferCount = 0, bool? Fertile = null)
{
    internal long Net => SalePrice - ExperienceCost;
}

public sealed record BreedingResaleAssessment(BreedingResaleOption? Current, BreedingResaleOption? Best,
    int QuotedLevels, int ComparedLevels, DateTimeOffset? LatestSpeciesQuoteAt)
{
    internal IReadOnlyList<BreedingResaleOption> Options { get; init; } = [];
    internal int? RecommendedLevel => Current is not null && Best is { } best && best.Level > Current.Level
        ? best.Level : null;

    internal long? AdditionalNet => RecommendedLevel.HasValue ? Best!.Net - Current!.Net : null;
}

internal static class BreedingResaleAdvisor
{
    internal static readonly TimeSpan PriceLifetime = TimeSpan.FromHours(72);

    internal static bool IsFresh(BreedingLevelPrice price, DateTimeOffset now) =>
        now - price.ObservedAtUtc <= PriceLifetime;

    internal static IEnumerable<BreedingLevelPrice> LatestSnapshot(IEnumerable<BreedingLevelPrice> prices)
    {
        var latest = prices.Max(price => (DateTimeOffset?)price.ObservedAtUtc);
        return prices.Where(price => price.ObservedAtUtc == latest);
    }

    internal static int Need(PaddockMountReading mount, int level) =>
        BreedingTrainingRules.FuelNeeded(mount, PaddockGauge.Experience,
            BreedingTrainingRules.PointsNeeded(mount, PaddockGauge.Experience, level) ?? 0);

    internal static IEnumerable<BreedingLevelPrice> ComparableQuotes(PaddockMountReading mount,
        IEnumerable<BreedingLevelPrice> quotes)
    {
        bool? fertile = mount.Fertility is { } status ? status != BreedingMountFertility.Sterile : null;
        return quotes.Where(quote => quote.Level >= mount.Level && (fertile is null || quote.Fertile == fertile));
    }

    internal static BreedingResaleAssessment Compare(PaddockMountReading mount, IReadOnlyList<BreedingLevelPrice> quotes,
        IReadOnlyDictionary<int, long> costs)
    {
        var applicable = ComparableQuotes(mount, quotes)
            .GroupBy(quote => quote.Level).Select(group => group.MinBy(quote => quote.SuggestedPrice)!).ToArray();
        var options = new List<BreedingResaleOption>(applicable.Length);
        foreach (var quote in applicable)
        {
            int fuel = Need(mount, quote.Level);
            if (fuel > 0 && !costs.ContainsKey(fuel)) continue;
            options.Add(new(quote.Level, quote.SuggestedPrice, costs.GetValueOrDefault(fuel), quote.ObservedAtUtc, quote.OfferCount, quote.Fertile));
        }
        var best = options.OrderByDescending(option => option.Net).ThenBy(option => option.Level).FirstOrDefault();
        return new(options.FirstOrDefault(option => option.Level == mount.Level), best, applicable.Length, options.Count,
            quotes.Max(quote => (DateTimeOffset?)quote.ObservedAtUtc)) { Options = options };
    }
}
