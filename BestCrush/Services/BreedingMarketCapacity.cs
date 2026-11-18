using BestCrush.Domain.Models;

namespace BestCrush.Services;

public sealed record BreedingMarketCapacity(long WeeklySales, long SalesAtPrice, long WeeklyLimit,
    long DailyRemaining, long WeeklyRemaining, DateTimeOffset ObservedAtUtc)
{
    internal long Remaining => Math.Min(DailyRemaining, WeeklyRemaining);
}

internal sealed record BreedingMarketDemand(GlobalSalesHistory History, long ListedToday, long ListedThisWeek)
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(2);

    internal BreedingMarketCapacity AtPrice(long price)
    {
        var points = History.Points(7).Where(point => point.RecordedAtUtc <= History.ObservedAtUtc && point.UnitPrice > 0).ToArray();
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
