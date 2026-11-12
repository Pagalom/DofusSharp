using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using FluentAssertions;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class DataWorkspaceHistoryTest
{
    [Theory]
    [InlineData(MarketObjectType.Resource)]
    [InlineData(MarketObjectType.Rune)]
    [InlineData(MarketObjectType.Equipment)]
    public async Task ObjectHistoryUsesTheExactIdAndServerBeforePagination(MarketObjectType type)
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        // Identical names make a text-only deep link ambiguous.
        foreach (long id in new long[] { 42, 43 })
        {
            if (type == MarketObjectType.Resource) db.Resources.Add(new Resource(id) { Name = "Même nom" });
            else if (type == MarketObjectType.Rune) db.Runes.Add(new Rune(id) { Name = "Même nom" });
            else db.Equipments.Add(new Equipment(id) { Name = "Même nom" });
        }
        DateTime now = DateTime.UtcNow;
        foreach (var entry in new[] { (42L, "A", now), (43L, "A", now.AddHours(1)), (42L, "B", now.AddHours(2)) })
            db.MarketPriceObservations.Add(new MarketPriceObservation
            {
                DofusDbId = entry.Item1, ObjectType = type, ServerName = entry.Item2,
                Quantity = 1, Price = 100, Source = MarketPriceSource.Manual, ObservedAtUtc = entry.Item3
            });
        await db.SaveChangesAsync();
        var service = new HistoryService(db);

        if (type == MarketObjectType.Equipment)
        {
            var exact = await service.GetItemHistoryAsync("A", null, null, null, null, null,
                HistorySortColumn.Date, HistorySortDirection.Descending, 1, itemId: 42);
            exact.Rows.Should().ContainSingle().Which.DofusDbId.Should().Be(42);
            exact.Rows.Single().ObservedAtUtc.Should().Be(now);
            exact.HasMore.Should().BeFalse();
            var all = await service.GetItemHistoryAsync("A", null, null, null, null, null,
                HistorySortColumn.Date, HistorySortDirection.Descending, 10);
            all.Rows.Select(row => row.DofusDbId).Should().BeEquivalentTo([42L, 43L]);
        }
        else
        {
            var exact = await service.GetMarketHistoryAsync(type, "A", null, null, null,
                HistorySortColumn.Date, HistorySortDirection.Descending, 1, itemId: 42);
            exact.Rows.Should().ContainSingle().Which.DofusDbId.Should().Be(42);
            exact.Rows.Single().ObservedAtUtc.Should().Be(now);
            exact.HasMore.Should().BeFalse();
            var all = await service.GetMarketHistoryAsync(type, "A", null, null, null,
                HistorySortColumn.Date, HistorySortDirection.Descending, 10);
            all.Rows.Select(row => row.DofusDbId).Should().BeEquivalentTo([42L, 43L]);
        }
    }
}
