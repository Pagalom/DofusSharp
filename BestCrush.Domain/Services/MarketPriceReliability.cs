using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

internal static class MarketPriceReliability
{
    // Automatic captures can omit unchanged prices for six hours.
    internal static readonly TimeSpan ComparisonWindow = TimeSpan.FromHours(6);
    private const decimal OutlierFactor = 10;
    private const decimal ConsensusFactor = 3;
    private static readonly IComparer<MarketPriceObservation> ByDate =
        Comparer<MarketPriceObservation>.Create((left, right) => left.ObservedAtUtc.CompareTo(right.ObservedAtUtc));

    internal static IQueryable<MarketPriceObservation> IncludeReferenceWindow(
        IQueryable<MarketPriceObservation> query, DateTime? from, DateTime? to)
    {
        if (from is { } first && first >= DateTime.MinValue + ComparisonWindow)
            query = query.Where(quote => quote.ObservedAtUtc >= first - ComparisonWindow);
        if (to is { } last && last <= DateTime.MaxValue - ComparisonWindow)
            query = query.Where(quote => quote.ObservedAtUtc <= last + ComparisonWindow);
        return query;
    }

    internal static async Task<Guid[]> GetRejectedHistoryIdsAsync(
        IQueryable<MarketPriceObservation> query, DateTime? from, DateTime? to, CancellationToken token)
    {
        var observations = await IncludeReferenceWindow(query.Where(quote =>
            quote.Source == MarketPriceSource.InGameAutomatic && quote.ObjectType != MarketObjectType.Equipment),
            from, to).ToArrayAsync(token);
        return GetRejectedHistoryIds(observations).ToArray();
    }

    internal static HashSet<Guid> GetRejectedHistoryIds(IEnumerable<MarketPriceObservation> observations)
    {
        HashSet<Guid> rejected = [];
        foreach (var item in observations.Where(IsComparable)
                     .GroupBy(quote => (quote.ServerName, quote.ObjectType, quote.DofusDbId)))
        {
            var byLot = item.GroupBy(quote => quote.Quantity)
                .Select(lot => lot.OrderBy(quote => quote.ObservedAtUtc).ThenBy(quote => quote.Id).ToArray()).ToArray();
            if (byLot.Length < 2)
                continue;

            var unitPrices = new List<decimal>(byLot.Length);
            foreach (var quote in item)
            {
                if (quote.IsCleared)
                    continue;

                unitPrices.Clear();
                var unitPrice = UnitPrice(quote);
                unitPrices.Add(unitPrice);
                foreach (var lot in byLot)
                {
                    if (lot[0].Quantity == quote.Quantity)
                        continue;

                    // Compare with the closest capture for each other lot, even when
                    // its unchanged price was recorded before this capture.
                    var neighbor = FindNearest(lot, quote);
                    if (!neighbor.IsCleared && (neighbor.ObservedAtUtc - quote.ObservedAtUtc).Duration() <= ComparisonWindow)
                        unitPrices.Add(UnitPrice(neighbor));
                }

                if (unitPrice > MaximumAcceptedUnitPrice(unitPrices))
                    rejected.Add(quote.Id);
            }
        }

        return rejected;
    }

    internal static IEnumerable<MarketPriceObservation> FilterCurrent(
        IEnumerable<MarketPriceObservation> latest, HashSet<Guid> rejected)
    {
        // Once a lot is missing, it must not distort the reference for the remaining offers.
        foreach (var item in latest.Where(quote => !rejected.Contains(quote.Id)).GroupBy(quote => quote.DofusDbId))
        {
            var unitPrices = item.Where(quote => !quote.IsCleared && IsComparable(quote)).Select(UnitPrice).ToList();
            decimal maximum = MaximumAcceptedUnitPrice(unitPrices);
            foreach (var quote in item)
            {
                if (quote.IsCleared || !IsComparable(quote) || UnitPrice(quote) <= maximum)
                    yield return quote;
            }
        }
    }

    private static bool IsComparable(MarketPriceObservation quote) =>
        quote.Source == MarketPriceSource.InGameAutomatic &&
        quote.ObjectType is MarketObjectType.Resource or MarketObjectType.Rune &&
        quote.Quantity > 0 && (quote.IsCleared || quote.Price > 0);

    private static decimal UnitPrice(MarketPriceObservation quote) => (decimal)quote.Price / quote.Quantity;

    private static MarketPriceObservation FindNearest(MarketPriceObservation[] lot, MarketPriceObservation quote)
    {
        int index = Array.BinarySearch(lot, quote, ByDate);
        if (index >= 0)
            return lot[index];

        index = ~index;
        if (index == 0)
            return lot[0];
        if (index == lot.Length)
            return lot[^1];

        var before = lot[index - 1];
        var after = lot[index];
        return quote.ObservedAtUtc - before.ObservedAtUtc <= after.ObservedAtUtc - quote.ObservedAtUtc
            ? before : after;
    }

    private static decimal MaximumAcceptedUnitPrice(List<decimal> unitPrices)
    {
        if (unitPrices.Count < 2)
            return decimal.MaxValue;

        unitPrices.Sort();
        if (unitPrices[^1] <= unitPrices[0] * OutlierFactor)
            return decimal.MaxValue;

        decimal median = (unitPrices[(unitPrices.Count - 1) / 2] + unitPrices[unitPrices.Count / 2]) / 2;
        int supportingLots = unitPrices.Count(price => price >= median / ConsensusFactor && price <= median * ConsensusFactor);
        // Without a consensus, a lone bargain cannot establish a ceiling for the other lots.
        return supportingLots > unitPrices.Count / 2
            ? median * OutlierFactor
            : decimal.MaxValue;
    }
}
