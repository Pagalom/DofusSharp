using BestCrush.Domain.Models;

namespace BestCrush.Services;

public enum BreedingDemandEvidence { Observed, Missing, HistoryUnavailable }

public sealed record BreedingMarketCapacity(long WeeklySales, long SalesAtPrice, long WeeklyLimit,
    long DailyRemaining, long WeeklyRemaining, DateTimeOffset ObservedAtUtc)
{
    public BreedingDemandEvidence Evidence { get; init; } = BreedingDemandEvidence.Observed;
    internal long Remaining => Evidence == BreedingDemandEvidence.Observed ? Math.Min(DailyRemaining, WeeklyRemaining) : 0;
}

internal sealed record BreedingMarketDemand(GlobalSalesHistory? History, long ListedToday, long ListedThisWeek)
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(2);
    internal BreedingDemandEvidence Evidence { get; init; } = BreedingDemandEvidence.Observed;
    internal DateTimeOffset EvidenceAt { get; init; }
    internal bool HasUsableHistory => Evidence == BreedingDemandEvidence.Observed && History is not null;

    internal static BreedingMarketDemand FromEvidence(long itemId, GlobalSalesHistory? history,
        MarketHistoryAvailability? availability, long today, long week, DateTimeOffset now)
    {
        bool unavailable = availability?.IsUnavailable(itemId, history?.ObservedAtUtc, now) == true;
        bool fresh = history is not null && history.ObservedAtUtc <= now && now - history.ObservedAtUtc <= Lifetime;
        return new(history, today, week)
        {
            Evidence = unavailable ? BreedingDemandEvidence.HistoryUnavailable : fresh ? BreedingDemandEvidence.Observed : BreedingDemandEvidence.Missing,
            EvidenceAt = availability?.ObservedAtUtc ?? history?.ObservedAtUtc ?? DateTimeOffset.MinValue
        };
    }

    internal BreedingMarketCapacity AtPrice(long price)
    {
        // These zeros are conservative production quotas, not invented observations of zero sales.
        if (!HasUsableHistory)
            return new(0, 0, 0, 0, 0, EvidenceAt) { Evidence = Evidence == BreedingDemandEvidence.Observed ? BreedingDemandEvidence.Missing : Evidence };
        var points = History!.Points(7).Where(point => point.RecordedAtUtc <= History.ObservedAtUtc && point.UnitPrice > 0).ToArray();
        long weekly = points.Sum(point => point.UnitsSold);
        long soldAtPrice = points.Where(point => point.UnitPrice >= price).Sum(point => point.UnitsSold);
        var quota = new ResaleQuota(soldAtPrice, History.ObservedAtUtc + Lifetime);
        long weeklyLimit = soldAtPrice / 2 + soldAtPrice % 2;
        return new(weekly, soldAtPrice, weeklyLimit, quota.Remaining(ListedToday),
            Math.Max(0, weeklyLimit - ListedThisWeek), History.ObservedAtUtc);
    }

    internal IReadOnlyDictionary<Guid, BreedingResaleOption> Allocate(
        IEnumerable<(Guid Id, IReadOnlyList<BreedingResaleOption> Options)> mounts)
    {
        var candidates = mounts.Select(mount => (mount.Id, Offers: mount.Options.Where(option => option.Net > 0)
                .Select(option => (Option: option, Capacity: AtPrice(option.SalePrice).Remaining))
                .Where(offer => offer.Capacity > 0).OrderByDescending(offer => offer.Option.Net)
                .ThenBy(offer => offer.Option.Level).ToArray()))
            .Where(mount => mount.Offers.Length > 0).OrderByDescending(mount => mount.Offers[0].Option.Net);
        var selected = new Dictionary<Guid, BreedingResaleOption>();
        foreach (var mount in candidates)
            if (mount.Offers.FirstOrDefault(offer => offer.Capacity > selected.Count).Option is { } option)
                selected.Add(mount.Id, option);
        return selected;
    }
}
