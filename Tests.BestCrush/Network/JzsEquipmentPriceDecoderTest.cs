using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class JzsEquipmentPriceDecoderTest
{
    [Fact]
    public void TonguesWabbitsOfferPricesMatchVisibleX1Minimum()
    {
        // Independent synthetic encoding of the 34 values observed for
        // item 6493. Prices 4899, 4900 and 5000 match the HDV screenshot.
        ulong[] observedPrices =
        [
            5198, 4900, 5000, 120000, 6400, 6500, 5249, 7999, 5800,
            6998, 5498, 8000, 5390, 6998, 6998, 5392, 5197, 7900,
            5197, 4899, 6999, 8500, 5499, 1350007, 1399177,
            5391, 5999, 5999, 1550178, 5199, 5494, 7500, 5250, 6000
        ];

        byte[][] rows = observedPrices.Select((price, index) =>
            JzsOffer(6493, checked((ulong)(7529 + index)), 11,
                     price, 0, 0, 0)).ToArray();

        MarketObservation? result =
            SemanticDecoders.TryDecodeJzsEquipmentPrices(
                JzsMarket(6493, 11, rows));

        result.Should().NotBeNull();
        result!.ItemId.Should().Be(6493);
        result.Offers.Should().HaveCount(34);
        result.Offers.Min(offer => offer.Ladder[0])
            .Should().Be(4899UL);
        result.Offers.Select(offer => offer.Ladder[0])
            .Should().Contain(4899UL);
        result.Offers.Select(offer => offer.Ladder[0])
            .Should().Contain(4900UL);
        result.Offers.Select(offer => offer.Ladder[0])
            .Should().Contain(5000UL);
        result.Offers.Should().OnlyContain(offer =>
            offer.Ladder.Count == 4 &&
            offer.Ladder.Skip(1).All(value => value == 0));
    }

    [Fact]
    public void EmptySelectionHasNoPriceObservation()
    {
        SemanticDecoders.TryDecodeJzsEquipmentPrices(
            JzsMarket(6493, 11)).Should().BeNull();
    }

    [Theory]
    [InlineData(0UL, 11UL)]
    [InlineData(42UL, 0UL)]
    [InlineData(ulong.MaxValue, 11UL)]
    public void RejectsInvalidRootIdOrCategory(ulong itemId, ulong category)
    {
        SemanticDecoders.TryDecodeJzsEquipmentPrices(
            JzsMarket(itemId, category,
                JzsOffer(itemId, 1, category, 4899, 0, 0, 0)))
            .Should().BeNull();
    }

    [Fact]
    public void RejectsWrongOfferItemOrCategoryRatherThanMixingPrices()
    {
        SemanticDecoders.TryDecodeJzsEquipmentPrices(
            JzsMarket(42, 11,
                JzsOffer(42, 100, 11, 4899, 0, 0, 0),
                JzsOffer(43, 101, 11, 1, 0, 0, 0)))
            .Should().BeNull();

        SemanticDecoders.TryDecodeJzsEquipmentPrices(
            JzsMarket(42, 11,
                JzsOffer(42, 100, 9, 1, 0, 0, 0)))
            .Should().BeNull();
    }

    [Theory]
    [InlineData(0UL, 0UL, 0UL, 0UL)]
    [InlineData(4899UL, 10UL, 0UL, 0UL)]
    [InlineData(4899UL, 0UL, 10UL, 0UL)]
    [InlineData(4899UL, 0UL, 0UL, 10UL)]
    [InlineData(ulong.MaxValue, 0UL, 0UL, 0UL)]
    public void RejectsUnverifiedOrOverflowLadderSlots(
        ulong a, ulong b, ulong c, ulong d)
    {
        SemanticDecoders.TryDecodeJzsEquipmentPrices(
            JzsMarket(42, 11,
                JzsOffer(42, 100, 11, a, b, c, d)))
            .Should().BeNull();
    }

    [Theory]
    [InlineData(new ulong[] { 4899UL })]
    [InlineData(new ulong[] { 4899UL, 0UL })]
    [InlineData(new ulong[] { 4899UL, 0UL, 0UL, 0UL, 0UL })]
    public void RejectsNoncanonicalLadderLength(ulong[] prices)
    {
        SemanticDecoders.TryDecodeJzsEquipmentPrices(
            JzsMarket(42, 11, JzsOffer(42, 100, 11, prices)))
            .Should().BeNull();
    }

    [Fact]
    public void InvalidSecondOfferRejectsWholeObservation()
    {
        byte[] malformed =
            Message(Unsigned(1, 42), Bytes(2, [0x80]),
                    Unsigned(3, 101), Unsigned(4, 11));

        SemanticDecoders.TryDecodeJzsEquipmentPrices(
            JzsMarket(42, 11,
                JzsOffer(42, 100, 11, 4899, 0, 0, 0),
                malformed)).Should().BeNull();
    }

    [Fact]
    public void MalformedRootAndNonPriceSelectionAreRejected()
    {
        SemanticDecoders.TryDecodeJzsEquipmentPrices(
            [0x1a, 0x05, 0x01]).Should().BeNull();

        SemanticDecoders.TryDecodeJzsEquipmentPrices(
            JzsMarket(42, 11,
                Message(Unsigned(1, 42), Unsigned(3, 100),
                        Unsigned(4, 11)))).Should().BeNull();
    }
}
