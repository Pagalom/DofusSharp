using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using FluentAssertions;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush.Network;

public sealed class CrushValuationCharacterizationTest
{
    private const long RuneId = 1523;

    private static IReadOnlyList<CrushSessionRuneLotLine> Breakdown(
        int quantity,
        IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> prices)
    {
        return CrushSessionService.BuildRuneLotBreakdown(Value(quantity, prices));
    }

    private static MarketValueResult? Value(
        int quantity,
        IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> prices)
    {
        // CalculateValue is stateless; no SQLite writing or Maui window.
        using TestDatabase database = new();
        MarketPriceService service = new(
            database.CreateContext(),
            new TestDataPriorityProvider());
        return service.CalculateValue(RuneId, quantity, prices);
    }

    [Fact]
    public void SixteenViRunesUseTenLotPlusSixSinglesWithoutEstimation()
    {
        var prices = Prices(
            Price(RuneId, 10, 1595, type: MarketObjectType.Rune),
            Price(RuneId, 1, 130, type: MarketObjectType.Rune));

        MarketValueResult? value = Value(16, prices);
        value.Should().NotBeNull();
        value!.Value.Value.Should().Be(2368);
        value.Value.IsEstimated.Should().BeFalse();

        IReadOnlyList<CrushSessionRuneLotLine> lots =
            Breakdown(16, prices);
        lots.Should().HaveCount(2);
        lots[0].Count.Should().Be(1);
        lots[0].LotQuantity.Should().Be(10);
        lots[0].LotPrice.Should().Be(1594);
        lots[0].IsEstimated.Should().BeFalse();
        lots[1].Count.Should().Be(6);
        lots[1].LotQuantity.Should().Be(1);
        lots[1].LotPrice.Should().Be(129);
        lots[1].IsEstimated.Should().BeFalse();

        lots.Sum(lot => (double)lot.Count * lot.LotPrice /
                          (lot.IsEstimated ? lot.LotQuantity : 1))
            .Should().Be(value.Value.Value);
    }

    [Fact]
    public void SmallerQuantityCannotUseThePriceOfALargerLot()
    {
        var prices = Prices(Price(RuneId, 10, 1000, type: MarketObjectType.Rune));

        Value(3, prices).Should().BeNull();
        Breakdown(3, prices).Should().BeEmpty();
        Value(16, prices).Should().BeNull();
        Breakdown(16, prices).Should().BeEmpty();
    }

    [Fact]
    public void PricesRetainSourceAndTimestampAcrossRealBreakdown()
    {
        var timestamp = ObservedAt.AddHours(4);
        var prices = Prices(
            Price(RuneId, 10, 1000, MarketPriceSource.Manual,
                type: MarketObjectType.Rune, at: timestamp));

        CrushSessionRuneLotLine lot = Breakdown(10, prices)
            .Should().ContainSingle().Which;
        lot.IsEstimated.Should().BeFalse();
        lot.PriceSource.Should().Be(MarketPriceSource.Manual);
        lot.PriceObservedAtUtc.Should().Be(timestamp);
    }

    [Fact]
    public void MissingPricesLeaveValueUnknownAndNoBreakdown()
    {
        var prices = Prices();
        Value(10, prices).Should().BeNull();
        Breakdown(10, prices).Should().BeEmpty();
    }

    [Fact]
    public void UnrelatedRunePricesDoNotPopulateTargetBreakdown()
    {
        var prices = Prices(
            Price(1111, 1, 99, type: MarketObjectType.Rune),
            Price(2222, 10, 100, type: MarketObjectType.Rune));
        Value(16, prices).Should().BeNull();
        Breakdown(16, prices).Should().BeEmpty();
    }

    [Fact]
    public void ZeroAndNonPositivePricesDoNotCreateSaleLots()
    {
        var prices = Prices(
            Price(RuneId, 1, 0, type: MarketObjectType.Rune),
            Price(RuneId, 10, -100, type: MarketObjectType.Rune));

        Value(16, prices).Should().BeNull();
        Breakdown(16, prices).Should().BeEmpty();
    }
}
