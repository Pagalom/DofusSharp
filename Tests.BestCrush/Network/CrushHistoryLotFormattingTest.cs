using BestCrush.Components.Pages;
using BestCrush.Domain.Models;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class CrushHistoryLotFormattingTest
{
    private static CrushHistoryRuneLot Lot(
        long count,
        int lotQuantity,
        long lotPrice,
        bool estimated) =>
        new(
            new CrushHistoryRune(),
            count,
            lotQuantity,
            lotPrice,
            estimated,
            MarketPriceSource.InGameAutomatic,
            null);

    [Fact]
    public void EstimatedPartialTenLotUsesTheUnitPriceInsteadOfWholeLotPrice()
    {
        string text = History.FormatCrushHistoryLot(Lot(3, 10, 1000, true));

        text.Should().Be(
            $"3×({1000L:N0}/10) = {300d:N0} K");
        text.Should().NotContain($"= {3000d:N0} K");
    }

    [Fact]
    public void NormalTenLotKeepsTheExistingPresentationAndFullLotValue()
    {
        string text = History.FormatCrushHistoryLot(Lot(1, 10, 1595, false));

        text.Should().Be($"1×10 = {1595d:N0} K");
    }

    [Fact]
    public void SixSingleLotsKeepTheirExistingPresentation()
    {
        string text = History.FormatCrushHistoryLot(Lot(6, 1, 130, false));

        text.Should().Be($"6×1 = {780d:N0} K");
    }

    [Fact]
    public void EstimatedOneUnitLotStillCountsIndividualRunes()
    {
        string text = History.FormatCrushHistoryLot(Lot(3, 1, 100, true));

        text.Should().Be($"3×1 = {300d:N0} K");
    }

    [Fact]
    public void EstimatedMixedTenLotRemainderContributesOnlySixUnitPrices()
    {
        string text = History.FormatCrushHistoryLot(Lot(6, 10, 1000, true));

        text.Should().Be($"6×({1000L:N0}/10) = {600d:N0} K");
    }
}
