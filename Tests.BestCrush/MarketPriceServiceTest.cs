using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class MarketPriceServiceTest : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly BestCrushDbContext _context;
    private readonly TestDataPriorityProvider _priority = new();
    private readonly MarketPriceService _service;

    public MarketPriceServiceTest()
    {
        _context = _database.CreateContext();
        _service = new MarketPriceService(_context, _priority);
    }

    public void Dispose() => _database.Dispose();

    [Theory]
    [InlineData(DataPriority.Manual, 300)]
    [InlineData(DataPriority.InGameAutomatic, 400)]
    public async Task SelectsLatestPriceWithinPreferredSource_NotCheapestOrNewestAcrossSources(DataPriority priority, long expectedPrice)
    {
        _priority.Priority = priority;
        await Store(
            Price(1, 1, 100, MarketPriceSource.Manual),
            Price(1, 1, 300, MarketPriceSource.Manual, at: ObservedAt.AddMinutes(1)),
            Price(1, 1, 50, at: ObservedAt.AddMinutes(2)),
            Price(1, 1, 400, at: ObservedAt.AddMinutes(3)));

        MarketPriceObservation? selected = await _service.GetLatestObservationAsync(MarketObjectType.Resource, 1, Server, 1);
        selected.Should().NotBeNull();
        selected!.Price.Should().Be(expectedPrice);
        var all = await _service.GetLatestObservationsForServerAsync(MarketObjectType.Resource, Server);
        all.Should().HaveCount(1);
        all[(1, 1)].Id.Should().Be(selected.Id);
        var sources = await _service.GetLatestLocalSourceObservationsForServerAsync(MarketObjectType.Resource, Server);
        sources[(1, 1, MarketPriceSource.Manual)].Price.Should().Be(300);
        sources[(1, 1, MarketPriceSource.InGameAutomatic)].Price.Should().Be(400);
    }

    [Fact]
    public async Task KeepsServerObjectTypeItemAndLotIsolated()
    {
        MarketPriceObservation target = Price(1, 1, 100);
        MarketPriceObservation otherLot = Price(1, 10, 800);
        MarketPriceObservation otherItem = Price(2, 1, 200);
        await Store(target, otherLot, otherItem,
            Price(1, 1, 1, server: "OTHER_SERVER", at: ObservedAt.AddHours(1)),
            Price(1, 1, 2, type: MarketObjectType.Rune, at: ObservedAt.AddHours(1)));

        (await _service.GetLatestObservationAsync(MarketObjectType.Resource, 1, Server, 1))!.Id.Should().Be(target.Id);
        var all = await _service.GetLatestObservationsForServerAsync(MarketObjectType.Resource, Server);
        all.Keys.Should().BeEquivalentTo(new[] { (1L, 1), (1L, 10), (2L, 1) });
        all[(1, 10)].Id.Should().Be(otherLot.Id);
        all[(2, 1)].Id.Should().Be(otherItem.Id);
    }

    [Theory]
    [InlineData(DataPriority.Manual, MarketPriceSource.Manual, 200)]
    [InlineData(DataPriority.InGameAutomatic, MarketPriceSource.InGameAutomatic, 100)]
    public async Task LatestClearSuppressesOlderValuesFromThatSource(DataPriority priority, MarketPriceSource clearedSource, long expectedPrice)
    {
        _priority.Priority = priority;
        await Store(Price(1, 1, 100, MarketPriceSource.Manual), Price(1, 1, 200),
            Price(1, 1, 0, clearedSource, at: ObservedAt.AddHours(1), cleared: true));

        (await _service.GetLatestObservationAsync(MarketObjectType.Resource, 1, Server, 1))!.Price.Should().Be(expectedPrice);
        var sources = await _service.GetLatestLocalSourceObservationsForServerAsync(MarketObjectType.Resource, Server);
        sources.Should().HaveCount(1);
        sources.Keys.Should().NotContain((1L, 1, clearedSource));
    }

    [Fact]
    public async Task ClearLocalClearsBothSourcesForOneLotAndKeepsHistory()
    {
        await Store(Price(1, 1, 100, MarketPriceSource.Manual), Price(1, 1, 200),
            Price(1, 10, 800), Price(1, 1, 300, server: "OTHER_SERVER"));

        await _service.ClearLocalAsync(MarketObjectType.Resource, 1, Server, 1);

        (await _service.GetLatestObservationAsync(MarketObjectType.Resource, 1, Server, 1)).Should().BeNull();
        (await _service.GetLatestObservationAsync(MarketObjectType.Resource, 1, Server, 10))!.Price.Should().Be(800);
        (await _service.GetLatestObservationAsync(MarketObjectType.Resource, 1, "OTHER_SERVER", 1))!.Price.Should().Be(300);
        var all = await _service.GetLatestObservationsForServerAsync(MarketObjectType.Resource, Server);
        all.Keys.Should().BeEquivalentTo(new[] { (1L, 10) });
        var history = await _service.GetHistoryAsync(MarketObjectType.Resource, 1, Server, quantity: 1);
        history.Select(price => price.Price).Should().BeEquivalentTo(new long[] { 100, 200 });
        MarketPriceObservation[] clears = await _context.MarketPriceObservations.Where(price => price.IsCleared).ToArrayAsync();
        clears.Should().HaveCount(2);
        clears.Should().OnlyContain(price => price.Price == 0 && price.Quantity == 1 && price.ServerName == Server && price.DofusDbId == 1 && price.ObjectType == MarketObjectType.Resource);
        clears.Select(price => price.Source).Should().BeEquivalentTo(new[] { MarketPriceSource.Manual, MarketPriceSource.InGameAutomatic });
        clears.Select(price => price.ObservedAtUtc).Distinct().Should().ContainSingle();
    }

    [Theory]
    [InlineData(359, true)]
    [InlineData(360, false)]
    [InlineData(361, false)]
    public async Task IdenticalPriceConfirmationUsesSixHourInterval(int ageInMinutes, bool deduplicated)
    {
        MarketPriceObservation previous = Price(1, 1, 100, at: DateTime.UtcNow.AddMinutes(-ageInMinutes));
        await Store(previous);
        DateTime before = DateTime.UtcNow;

        MarketPriceObservation result = await _service.AddObservationAsync(MarketObjectType.Resource, 1, Server, 100, 1, MarketPriceSource.InGameAutomatic);

        (result.Id == previous.Id).Should().Be(deduplicated);
        (await _context.MarketPriceObservations.CountAsync()).Should().Be(deduplicated ? 1 : 2);
        if (deduplicated)
            result.ObservedAtUtc.Should().Be(previous.ObservedAtUtc);
        else
            result.ObservedAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Theory]
    [InlineData(MarketObjectType.Rune, 1L, Server, 1, MarketPriceSource.InGameAutomatic)]
    [InlineData(MarketObjectType.Resource, 2L, Server, 1, MarketPriceSource.InGameAutomatic)]
    [InlineData(MarketObjectType.Resource, 1L, "OTHER_SERVER", 1, MarketPriceSource.InGameAutomatic)]
    [InlineData(MarketObjectType.Resource, 1L, Server, 10, MarketPriceSource.InGameAutomatic)]
    [InlineData(MarketObjectType.Resource, 1L, Server, 1, MarketPriceSource.Manual)]
    public async Task DeduplicationDoesNotCrossObservationKeys(MarketObjectType type, long id, string server, int quantity, MarketPriceSource source)
    {
        MarketPriceObservation previous = Price(1, 1, 100, at: DateTime.UtcNow.AddHours(-1));
        await Store(previous);

        MarketPriceObservation result = await _service.AddObservationAsync(type, id, server, 100, quantity, source);

        result.Id.Should().NotBe(previous.Id);
        result.ObjectType.Should().Be(type);
        result.DofusDbId.Should().Be(id);
        result.ServerName.Should().Be(server);
        result.Quantity.Should().Be(quantity);
        result.Source.Should().Be(source);
        (await _context.MarketPriceObservations.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(false, 101)]
    [InlineData(true, 100)]
    public async Task ChangedPriceOrLatestClearAllowsANewObservation(bool cleared, long newPrice)
    {
        await Store(Price(1, 1, 100, at: DateTime.UtcNow.AddHours(-2)),
            Price(1, 1, cleared ? 0 : 100, at: DateTime.UtcNow.AddHours(-1), cleared: cleared));

        MarketPriceObservation result = await _service.AddObservationAsync(MarketObjectType.Resource, 1, Server, newPrice, 1, MarketPriceSource.InGameAutomatic);

        result.IsCleared.Should().BeFalse();
        result.Price.Should().Be(newPrice);
        (await _context.MarketPriceObservations.CountAsync()).Should().Be(3);
        (await _service.GetLatestObservationAsync(MarketObjectType.Resource, 1, Server, 1))!.Id.Should().Be(result.Id);
    }

    [Theory]
    [InlineData(0, 1, "price")]
    [InlineData(-1, 1, "price")]
    [InlineData(100, 0, "quantity")]
    [InlineData(100, -1, "quantity")]
    public async Task RejectsNonPositivePriceOrLotWithoutWriting(long price, int quantity, string parameter)
    {
        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.AddObservationAsync(MarketObjectType.Resource, 1, Server, price, quantity, MarketPriceSource.Manual));

        exception.ParamName.Should().Be(parameter);
        (await _context.MarketPriceObservations.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task HistoryKeepsPastPricesUsesInclusiveBoundsAndExcludesClearMarkers()
    {
        MarketPriceObservation manual = Price(1, 1, 100, MarketPriceSource.Manual);
        MarketPriceObservation game = Price(1, 1, 150);
        MarketPriceObservation lot = Price(1, 10, 800);
        MarketPriceObservation later = Price(1, 1, 200, at: ObservedAt.AddHours(2));
        await Store(manual, game, lot, later,
            Price(1, 1, 0, at: ObservedAt.AddHours(1), cleared: true),
            Price(1, 1, 1, server: "OTHER_SERVER"), Price(2, 1, 1), Price(1, 1, 1, type: MarketObjectType.Rune));

        var atBoundary = await _service.GetHistoryAsync(MarketObjectType.Resource, 1, Server, ObservedAt, ObservedAt);
        atBoundary.Select(price => price.Id).Should().Equal(manual.Id, game.Id, lot.Id);
        var filtered = await _service.GetHistoryAsync(MarketObjectType.Resource, 1, Server,
            ObservedAt, ObservedAt.AddHours(2), MarketPriceSource.InGameAutomatic, 1);
        filtered.Select(price => price.Id).Should().Equal(game.Id, later.Id);
    }

    [Fact]
    public void SaleUsesLargestAvailableLotsAndReportsTheirObservations()
    {
        var prices = Prices(Price(1, 1, 10), Price(1, 10, 150), Price(1, 100, 900), Price(1, 1000, 7000));

        MarketValueResult result = _service.CalculateValue(1, 1111, prices)!.Value;

        result.Value.Should().Be(8060);
        result.IsEstimated.Should().BeFalse();
        result.UsedObservations.Should().BeEquivalentTo(prices.Values);
    }

    [Fact]
    public void SaleAndMinimumPurchaseHaveDifferentLotRules()
    {
        MarketPriceObservation single = Price(1, 1, 5);
        MarketPriceObservation ten = Price(1, 10, 80);
        var prices = Prices(single, ten);

        MarketValueResult sale = _service.CalculateValue(1, 10, prices)!.Value;
        MarketPurchaseResult purchase = _service.CalculateMinimumPurchaseCost(1, 10, prices)!;

        sale.Value.Should().Be(80);
        sale.UsedObservations.Should().ContainSingle().Which.Should().BeSameAs(ten);
        purchase.TotalCost.Should().Be(50);
        purchase.Lots.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 10 });
        purchase.UsedObservations.Should().ContainSingle().Which.Should().BeSameAs(single);
    }

    [Fact]
    public void SaleValuesFractionAtSinglePriceWithoutRoundingRuneQuantity()
    {
        var prices = Prices(Price(1, 1, 10), Price(1, 10, 80));

        MarketValueResult result = _service.CalculateValue(1, 12.5, prices)!.Value;

        result.Value.Should().Be(105);
        result.IsEstimated.Should().BeTrue();
        result.UsedObservations.Should().BeEquivalentTo(prices.Values);
    }

    [Fact]
    public void SaleEstimatesRemainderAndFractionFromSmallestLotWhenSingleIsMissing()
    {
        MarketPriceObservation ten = Price(1, 10, 80);
        var prices = Prices(ten, Price(1, 100, 650));

        MarketValueResult result = _service.CalculateValue(1, 12.5, prices)!.Value;

        result.Value.Should().Be(100);
        result.IsEstimated.Should().BeTrue();
        result.UsedObservations.Should().ContainSingle().Which.Should().BeSameAs(ten);
    }

    [Theory]
    [InlineData(1.0000005, 10, false)]
    [InlineData(1.000002, 10.00002, true)]
    public void SaleKeepsCurrentFractionalTolerance(double quantity, double expected, bool estimated)
    {
        MarketValueResult result = _service.CalculateValue(1, quantity, Prices(Price(1, 1, 10)))!.Value;

        result.Value.Should().BeApproximately(expected, 0.000000001);
        result.IsEstimated.Should().Be(estimated);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-2, 2)]
    public void NonPositiveQuantityReturnsZeroWithoutRequiringPrices(int quantity, int expectedSurplus)
    {
        MarketValueResult sale = _service.CalculateValue(1, quantity, Prices())!.Value;
        MarketPurchaseResult purchase = _service.CalculateMinimumPurchaseCost(1, quantity, Prices())!;

        sale.Value.Should().Be(0);
        sale.IsEstimated.Should().BeFalse();
        sale.UsedObservations.Should().BeEmpty();
        purchase.TotalCost.Should().Be(0);
        purchase.RequiredQuantity.Should().Be(quantity);
        purchase.PurchasedQuantity.Should().Be(0);
        purchase.SurplusQuantity.Should().Be(expectedSurplus);
        purchase.Lots.Should().BeEmpty();
        purchase.UsedObservations.Should().BeEmpty();
    }

    [Fact]
    public void MissingOrInvalidPricesAndOtherItemsCannotValueAPositiveQuantity()
    {
        var prices = Prices(Price(1, 0, 100), Price(1, 1, 0), Price(1, 10, -5), Price(2, 1, 100));

        _service.CalculateValue(1, 2, prices).Should().BeNull();
        _service.CalculateMinimumPurchaseCost(1, 2, prices).Should().BeNull();
    }

    [Fact]
    public void PurchaseCombinesLotsToMinimizeTotalPaid()
    {
        MarketPriceObservation single = Price(1, 1, 10);
        MarketPriceObservation ten = Price(1, 10, 80);

        MarketPurchaseResult result = _service.CalculateMinimumPurchaseCost(1, 12, Prices(single, ten, Price(1, 100, 1000)))!;

        result.TotalCost.Should().Be(100);
        result.RequiredQuantity.Should().Be(12);
        result.PurchasedQuantity.Should().Be(12);
        result.SurplusQuantity.Should().Be(0);
        result.Lots.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 2, [10] = 1 });
        result.UsedObservations.Should().BeEquivalentTo(new[] { single, ten });
    }

    [Fact]
    public void PurchasePaysForEntireCheaperLotIncludingSurplus()
    {
        MarketPriceObservation ten = Price(1, 10, 80);

        MarketPurchaseResult result = _service.CalculateMinimumPurchaseCost(1, 9, Prices(Price(1, 1, 10), ten))!;

        result.TotalCost.Should().Be(80);
        result.RequiredQuantity.Should().Be(9);
        result.PurchasedQuantity.Should().Be(10);
        result.SurplusQuantity.Should().Be(1);
        result.Lots.Should().BeEquivalentTo(new Dictionary<int, int> { [10] = 1 });
        result.UsedObservations.Should().ContainSingle().Which.Should().BeSameAs(ten);
    }

    [Fact]
    public void PurchasePrefersLessSurplusWhenTotalCostIsEqual()
    {
        MarketPurchaseResult result = _service.CalculateMinimumPurchaseCost(1, 9, Prices(Price(1, 1, 10), Price(1, 10, 90)))!;

        result.TotalCost.Should().Be(90);
        result.PurchasedQuantity.Should().Be(9);
        result.SurplusQuantity.Should().Be(0);
        result.Lots.Should().BeEquivalentTo(new Dictionary<int, int> { [1] = 9 });
    }

    [Fact]
    public void PurchaseCostOverflowIsNotSilentlyWrapped()
    {
        Assert.Throws<OverflowException>(() => _service.CalculateMinimumPurchaseCost(1, 2, Prices(Price(1, 1, long.MaxValue))));
    }

    private async Task Store(params MarketPriceObservation[] observations)
    {
        _context.MarketPriceObservations.AddRange(observations);
        await _context.SaveChangesAsync();
    }
}
