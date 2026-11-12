using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class MarketAnalyticsHistoryTest
{
    [Fact]
    public async Task RawHistoryIsPagedStableScopedAndNeverAggregatesCaptures()
    {
        using var database = new TestDatabase(); await using var db = database.CreateContext();
        var at = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        MarketPriceObservation Row(string server, long id, long price, DateTime date,
            MarketObjectType type = MarketObjectType.Rune, bool clear = false) => new()
        { ServerName = server, DofusDbId = id, Price = price, Quantity = 10, ObjectType = type,
            Source = price % 2 == 0 ? MarketPriceSource.Manual : MarketPriceSource.InGameAutomatic,
            ObservedAtUtc = date, IsCleared = clear };
        for (int i = 0; i < 55; i++) db.MarketPriceObservations.Add(Row("A", 1, 101 + i, at.AddMinutes(i / 2)));
        db.MarketPriceObservations.AddRange(Row("B", 1, 999, at), Row("A", 2, 999, at),
            Row("A", 1, 999, at, MarketObjectType.Resource), Row("A", 1, 999, at.AddDays(1)),
            Row("A", 1, 999, at.AddDays(-1)), Row("A", 1, 0, at.AddMinutes(28), clear: true));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new MarketAnalyticsService(db); var key = new MarketAnalyticsItemKey(MarketObjectType.Rune, 1);
        var first = await service.GetObservationHistoryAsync("A", key, at, at.AddHours(1));
        var next = await service.GetObservationHistoryAsync("A", key, at, at.AddHours(1), 1);
        Assert.Equal(50, first.Rows.Count); Assert.True(first.HasMore);
        Assert.Equal(6, next.Rows.Count); Assert.False(next.HasMore);
        var all = first.Rows.Concat(next.Rows).ToArray(); Assert.Equal(56, all.Select(r => r.Id).Distinct().Count());
        Assert.True(first.Rows[0].IsCleared); Assert.Null(first.Rows[0].UnitPrice);
        Assert.All(all, r => Assert.Equal(DateTimeKind.Utc, r.AtUtc.Kind));
        Assert.Contains(all, r => r.UnitPrice == 10.1); Assert.Contains(all, r => r.UnitPrice == 15.5);
        Assert.Equal(57, (await service.GetObservationHistoryAsync("A", key, null, at.AddHours(1), pageSize: 100)).Rows.Count);
        Assert.Empty(db.ChangeTracker.Entries());
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetObservationHistoryAsync("A", key, null, at, token: stop.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetObservationHistoryAsync("A", key, null, at, page: -1));
    }
}
