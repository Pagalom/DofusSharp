using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public sealed partial class MarketAnalyticsService
{
    /// <summary>Price captures and clears for the selected object.</summary>
    public async Task<MarketAnalyticsHistoryPage> GetObservationHistoryAsync(string serverName,
        MarketAnalyticsItemKey key, DateTime? fromUtc, DateTime toUtc, int page = 0,
        int pageSize = 50, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(serverName) || key.DofusDbId <= 0 || fromUtc > toUtc)
            throw new ArgumentException("Objet, serveur ou période invalide.");
        if (page < 0 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new ArgumentOutOfRangeException(nameof(page));
        var query = context.MarketPriceObservations.AsNoTracking().Where(o =>
            o.ServerName == serverName && o.ObjectType == key.ObjectType && o.DofusDbId == key.DofusDbId);
        if (key.ObjectType is MarketObjectType.Resource or MarketObjectType.Rune)
        {
            var rejected = await MarketPriceReliability.GetRejectedHistoryIdsAsync(query, fromUtc, toUtc, token);
            query = query.Where(o => !rejected.Contains(o.Id));
        }

        query = query.Where(o => o.ObservedAtUtc <= toUtc && (o.IsCleared || (o.Price > 0 && o.Quantity > 0)));
        if (fromUtc is { } from) query = query.Where(o => o.ObservedAtUtc >= from);
        var rows = await query.OrderByDescending(o => o.ObservedAtUtc).ThenByDescending(o => o.Id)
            .Skip(page * pageSize).Take(pageSize + 1)
            .Select(o => new MarketAnalyticsHistoryRow(o.Id, DateTime.SpecifyKind(o.ObservedAtUtc, DateTimeKind.Utc),
                o.Price, o.Quantity, o.Source, o.IsCleared)).ToArrayAsync(token);
        return new(rows.Take(pageSize).ToArray(), rows.Length > pageSize);
    }
}
