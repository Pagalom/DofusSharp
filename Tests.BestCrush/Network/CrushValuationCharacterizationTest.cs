using System.Reflection;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using FluentAssertions;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush.Network;

/// <summary>
/// P4.0: tests of the existing, real valuation and lot-breakdown paths.
/// These tests do not assert the incorrect History.razor estimated-lot
/// expression as a desired behavior.
/// </summary>
public sealed class CrushValuationCharacterizationTest
{
    private const long RuneId = 1523;

    private static IReadOnlyList<CrushSessionRuneLotLine> Breakdown(
        int quantity,
        IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> prices)
    {
        MethodInfo? method = typeof(CrushSessionService).GetMethod(
            "BuildRuneLotBreakdown",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("characterize the actual existing service method");
        object? value = method!.Invoke(null, [RuneId, quantity, prices]);
        return (IReadOnlyList<CrushSessionRuneLotLine>)value!;
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
        value!.Value.Value.Should().Be(2375);
        value.Value.IsEstimated.Should().BeFalse();

        IReadOnlyList<CrushSessionRuneLotLine> lots =
            Breakdown(16, prices);
        lots.Should().HaveCount(2);
        lots[0].Count.Should().Be(1);
        lots[0].LotQuantity.Should().Be(10);
        lots[0].LotPrice.Should().Be(1595);
        lots[0].IsEstimated.Should().BeFalse();
        lots[1].Count.Should().Be(6);
        lots[1].LotQuantity.Should().Be(1);
        lots[1].LotPrice.Should().Be(130);
        lots[1].IsEstimated.Should().BeFalse();

        lots.Sum(lot => (double)lot.Count * lot.LotPrice /
                          (lot.IsEstimated ? lot.LotQuantity : 1))
            .Should().Be(value.Value.Value);
    }

    [Fact]
    public void ThreeUnitsWithOnlyTenLotCostThreeTenthsNotThreeLots()
    {
        var prices = Prices(
            Price(RuneId, 10, 1000, type: MarketObjectType.Rune));

        MarketValueResult? value = Value(3, prices);
        value.Should().NotBeNull();
        value!.Value.Value.Should().Be(300);
        value.Value.IsEstimated.Should().BeTrue();

        CrushSessionRuneLotLine lot = Breakdown(3, prices)
            .Should().ContainSingle().Which;
        lot.Count.Should().Be(3);
        lot.LotQuantity.Should().Be(10);
        lot.LotPrice.Should().Be(1000);
        lot.IsEstimated.Should().BeTrue();

        // Expected History display contract for P4.2:
        // 3×(1000/10) = 300 K. The CURRENT Razor markup incorrectly
        // renders Count*LotPrice = 3000 K (not asserted here).
        ((double)lot.Count * lot.LotPrice / lot.LotQuantity)
            .Should().Be(value.Value.Value);
    }

    [Fact]
    public void MixedFullAndEstimatedLotsKeepTheirEconomicTotals()
    {
        var prices = Prices(
            Price(RuneId, 10, 1000, type: MarketObjectType.Rune));
        MarketValueResult? value = Value(16, prices);

        value.Should().NotBeNull();
        value!.Value.Value.Should().Be(1600);
        value.Value.IsEstimated.Should().BeTrue();
        IReadOnlyList<CrushSessionRuneLotLine> lots = Breakdown(16, prices);

        lots.Should().HaveCount(2);
        lots[0].Count.Should().Be(1);
        lots[0].IsEstimated.Should().BeFalse();
        lots[1].Count.Should().Be(6);
        lots[1].LotQuantity.Should().Be(10);
        lots[1].IsEstimated.Should().BeTrue();
        lots.Sum(lot => (double)lot.Count * lot.LotPrice /
                          (lot.IsEstimated ? lot.LotQuantity : 1))
            .Should().Be(value.Value.Value);
    }

    [Fact]
    public void PricesRetainSourceAndTimestampAcrossRealBreakdown()
    {
        var timestamp = ObservedAt.AddHours(4);
        var prices = Prices(
            Price(RuneId, 10, 1000, MarketPriceSource.Manual,
                type: MarketObjectType.Rune, at: timestamp));

        CrushSessionRuneLotLine lot = Breakdown(3, prices)
            .Should().ContainSingle().Which;
        lot.IsEstimated.Should().BeTrue();
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
