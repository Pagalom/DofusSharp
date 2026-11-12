using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class MarketPriceReliabilityTest
{
    [Theory]
    [InlineData(MarketObjectType.Resource, true)]
    [InlineData(MarketObjectType.Rune, true)]
    [InlineData(MarketObjectType.Resource, false)]
    public async Task VeryLowQuoteRemainsAvailableForPurchaseAndInHistory(MarketObjectType type, bool hasConsensus)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        const long itemId = 2656;
        context.MarketPriceObservations.AddRange(
            Price(itemId, 1, 100, type: type),
            Price(itemId, 100, 100, type: type));
        if (hasConsensus)
            context.MarketPriceObservations.AddRange(
                Price(itemId, 10, 997, type: type),
                Price(itemId, 1000, 67_999, type: type));
        await context.SaveChangesAsync();

        var prices = new MarketPriceService(context, new TestDataPriorityProvider());
        var current = await prices.GetLatestObservationsForServerAsync(type, Server);
        Assert.Equal(100, current[(itemId, 1)].Price);
        Assert.Equal(100, current[(itemId, 100)].Price);
        Assert.Equal(100L, prices.CalculateMinimumPurchaseCost(itemId, 100, current)?.TotalCost);
        Assert.Equal(100, Assert.Single(await prices.GetHistoryAsync(type, itemId, Server, quantity: 100)).Price);
    }

    [Theory]
    [InlineData(MarketObjectType.Resource)]
    [InlineData(MarketObjectType.Rune)]
    public async Task RejectedQuoteIsMissingAcrossCurrentPricesAndHistoricalAnalysis(MarketObjectType type)
    {
        using var database = new TestDatabase();
        await using var context = database.CreateContext();
        const long itemId = 2656;
        if (type == MarketObjectType.Resource)
            context.Resources.Add(new Resource(itemId) { Name = "Os ronge de Gobichon" });
        else
            context.Runes.Add(new Rune(itemId) { Name = "Rune test" });

        DateTime outlierAt = ObservedAt.AddHours(1);
        context.MarketPriceObservations.AddRange(
            Price(itemId, 100, 9_800, type: type),
            Price(itemId, 1, 100, type: type, at: outlierAt),
            Price(itemId, 10, 997, type: type, at: outlierAt.AddMilliseconds(4)),
            Price(itemId, 100, 5_000_000, type: type, at: outlierAt.AddMilliseconds(8)),
            Price(itemId, 1000, 67_999, type: type, at: outlierAt.AddMilliseconds(12)));
        await context.SaveChangesAsync();

        var prices = new MarketPriceService(context, new TestDataPriorityProvider());
        Assert.Null(await prices.GetLatestObservationAsync(type, itemId, Server, 100));
        var current = await prices.GetLatestObservationsForServerAsync(type, Server);
        Assert.Equal(new[] { 1, 10, 1000 }, current.Keys.Select(key => key.Quantity).Order());
        Assert.Equal(9_970d, prices.CalculateValue(itemId, 100, current)?.Value);
        Assert.Equal(9_970L, prices.CalculateMinimumPurchaseCost(itemId, 100, current)?.TotalCost);
        var sources = await prices.GetLatestLocalSourceObservationsForServerAsync(type, Server);
        Assert.False(sources.ContainsKey((itemId, 100, MarketPriceSource.InGameAutomatic)));
        var history = await prices.GetHistoryAsync(type, itemId, Server, quantity: 100);
        Assert.Equal(9_800, Assert.Single(history).Price);
        var page = await new HistoryService(context).GetMarketHistoryAsync(type, Server, null, null, null,
            HistorySortColumn.Value, HistorySortDirection.Descending, 1, itemId: itemId,
            options: new HistoryQueryOptions { FromUtc = outlierAt.AddMilliseconds(7), LotQuantity = 100 });
        Assert.Empty(page.Rows);

        var analytics = new MarketAnalyticsService(context);
        var key = new MarketAnalyticsItemKey(type, itemId);
        var analyticsHistory = await analytics.GetObservationHistoryAsync(Server, key, null, outlierAt.AddHours(1));
        Assert.Equal(4, analyticsHistory.Rows.Count);
        Assert.DoesNotContain(analyticsHistory.Rows, quote => quote.Price == 5_000_000);

        var lot = new MarketAnalyticsSeriesDefinition { ObjectType = type, DofusDbId = itemId, LotQuantity = 100 };
        var request = new MarketAnalyticsRequest(Server, [lot], MarketAnalyticsPeriod.Hours24,
            ObservedAt, outlierAt.AddHours(1));
        var series = await analytics.BuildComparisonAsync(request, []);
        var points = Assert.Single(series.Groups).FusedSamples;
        Assert.Equal(98d, points[0].Price);
        Assert.Null(points[^1].Price);
        Assert.Equal(0, points[^1].ObservationCount);

        context.MarketPriceObservations.Add(Price(itemId, 100, 9_500, type: type, at: outlierAt.AddMinutes(30)));
        await context.SaveChangesAsync();
        Assert.Equal(9_500, (await prices.GetLatestObservationAsync(type, itemId, Server, 100))?.Price);
        var recovered = await analytics.BuildComparisonAsync(request, []);
        Assert.Equal(95d, Assert.Single(recovered.Groups).FusedSamples[^1].Price);
        Assert.Equal(6, await context.MarketPriceObservations.CountAsync());
    }
}
