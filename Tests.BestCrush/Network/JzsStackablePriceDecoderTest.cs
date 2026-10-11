using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class JzsStackablePriceDecoderTest
{
    [Theory]
    [InlineData(7448UL, 78UL, 13084UL, 139UL, 1399UL, 14284UL, 151014UL)]
    [InlineData(1457UL, 104UL, 2411UL, 483UL, 897UL, 9179UL, 85000UL)]
    public void CapturedRuneAndResourceLotsMatchTheHdv(
        ulong itemId, ulong category, ulong offerId,
        ulong one, ulong ten, ulong hundred, ulong thousand)
    {
        byte[] body = JzsMarket(itemId, category,
            JzsOffer(itemId, offerId, category, one, ten,
                     hundred, thousand));

        MarketObservation? observation =
            SemanticDecoders.TryDecodeJzsStackablePrices(body);

        observation.Should().NotBeNull();
        observation!.ItemId.Should().Be(itemId);
        observation.Offers.Should().ContainSingle();
        observation.Offers.Single().OfferId.Should().Be(offerId);
        observation.Offers.Single().Ladder
            .Should().Equal(one, ten, hundred, thousand);

        // Equipment prices must still refuse any unverified non-x1 slot.
        SemanticDecoders.TryDecodeJzsEquipmentPrices(body)
            .Should().BeNull();
    }

    [Fact]
    public void IndependentMinimaRemainInTheirOriginalLotPositions()
    {
        MarketObservation? observation =
            SemanticDecoders.TryDecodeJzsStackablePrices(
                JzsMarket(1457, 104,
                    JzsOffer(1457, 100, 104, 500, 1200, 9179, 100000),
                    JzsOffer(1457, 101, 104, 483, 897, 9300, 85000)));

        observation.Should().NotBeNull();
        observation!.Offers.Should().HaveCount(2);

        ulong[] positiveMinima =
            Enumerable.Range(0, 4).Select(index =>
                observation.Offers.Where(offer => offer.Ladder[index] > 0)
                    .Min(offer => offer.Ladder[index])).ToArray();

        positiveMinima.Should().Equal(483UL, 897UL, 9179UL, 85000UL);
    }

    [Fact]
    public void ZeroIsAnAbsentLotAndNeverBecomesAPrice()
    {
        MarketObservation? observation =
            SemanticDecoders.TryDecodeJzsStackablePrices(
                JzsMarket(7035, 40,
                    JzsOffer(7035, 11299, 40,
                             29975, 349995, 3399999, 0)));

        observation.Should().NotBeNull();
        observation!.Offers.Single().Ladder
            .Should().Equal(29975UL, 349995UL, 3399999UL, 0UL);
    }

    [Fact]
    public void PositiveLotCanExistEvenIfX1IsMissing()
    {
        MarketObservation? observation =
            SemanticDecoders.TryDecodeJzsStackablePrices(
                JzsMarket(100, 78,
                    JzsOffer(100, 1, 78, 0, 1399, 14284, 0)));

        observation.Should().NotBeNull();
        observation!.Offers.Single().Ladder
            .Should().Equal(0UL, 1399UL, 14284UL, 0UL);
        SemanticDecoders.TryDecodeJzsEquipmentPrices(
            JzsMarket(100, 78,
                JzsOffer(100, 1, 78, 0, 1399, 14284, 0)))
            .Should().BeNull();
    }

    [Fact]
    public void EmptySelectionIsNotAPriceObservation()
    {
        SemanticDecoders.TryDecodeJzsStackablePrices(
            JzsMarket(1457, 104)).Should().BeNull();
    }

    [Theory]
    [InlineData(0UL, 104UL)]
    [InlineData(1457UL, 0UL)]
    [InlineData(ulong.MaxValue, 104UL)]
    public void RejectsInvalidRootItemAndGrouping(ulong itemId, ulong category)
    {
        SemanticDecoders.TryDecodeJzsStackablePrices(
            JzsMarket(itemId, category,
                JzsOffer(itemId, 1, category, 483, 897, 9179, 85000)))
            .Should().BeNull();
    }

    [Fact]
    public void RefusesRowsWithMixedIdsOrGroups()
    {
        SemanticDecoders.TryDecodeJzsStackablePrices(
            JzsMarket(1457, 104,
                JzsOffer(1457, 1, 104, 483, 897, 9179, 85000),
                JzsOffer(1458, 2, 104, 1, 2, 3, 4)))
            .Should().BeNull();

        SemanticDecoders.TryDecodeJzsStackablePrices(
            JzsMarket(1457, 104,
                JzsOffer(1457, 1, 78, 1, 2, 3, 4)))
            .Should().BeNull();
    }

    [Theory]
    [InlineData(0UL, 0UL, 0UL, 0UL)]
    [InlineData(ulong.MaxValue, 0UL, 0UL, 0UL)]
    [InlineData(1UL, ulong.MaxValue, 0UL, 0UL)]
    [InlineData(1UL, 0UL, ulong.MaxValue, 0UL)]
    [InlineData(1UL, 0UL, 0UL, ulong.MaxValue)]
    public void RejectsEmptyOrOverflowedPrices(
        ulong one, ulong ten, ulong hundred, ulong thousand)
    {
        SemanticDecoders.TryDecodeJzsStackablePrices(
            JzsMarket(1457, 104,
                JzsOffer(1457, 1, 104, one, ten, hundred, thousand)))
            .Should().BeNull();
    }

    [Theory]
    [InlineData(new ulong[] { 483UL })]
    [InlineData(new ulong[] { 483UL, 897UL, 9179UL })]
    [InlineData(new ulong[] { 483UL, 897UL, 9179UL, 85000UL, 999UL })]
    public void RejectsNoncanonicalSlotCounts(ulong[] prices)
    {
        SemanticDecoders.TryDecodeJzsStackablePrices(
            JzsMarket(1457, 104, JzsOffer(1457, 1, 104, prices)))
            .Should().BeNull();
    }

    [Fact]
    public void InvalidSecondOfferRejectsAllPrices()
    {
        byte[] truncated = Message(
            Unsigned(1, 1457),
            Bytes(2, [0x80]),
            Unsigned(3, 2),
            Unsigned(4, 104));

        SemanticDecoders.TryDecodeJzsStackablePrices(
            JzsMarket(1457, 104,
                JzsOffer(1457, 1, 104, 483, 897, 9179, 85000),
                truncated)).Should().BeNull();
    }

    [Fact]
    public void RejectsDuplicatePricePayloadAndWrongWireType()
    {
        byte[] duplicate = Message(
            Unsigned(1, 1457),
            Bytes(2, Packed(483, 897, 9179, 85000)),
            Bytes(2, Packed(1, 2, 3, 4)),
            Unsigned(3, 1),
            Unsigned(4, 104));

        byte[] wrongType = Message(
            Unsigned(1, 1457),
            Unsigned(2, 483),
            Unsigned(3, 1),
            Unsigned(4, 104));

        SemanticDecoders.TryDecodeJzsStackablePrices(
            JzsMarket(1457, 104, duplicate)).Should().BeNull();

        SemanticDecoders.TryDecodeJzsStackablePrices(
            JzsMarket(1457, 104, wrongType)).Should().BeNull();
    }

    [Fact]
    public void MalformedRootCannotCreatePrices()
    {
        SemanticDecoders.TryDecodeJzsStackablePrices(
            [0x1a, 0x05, 0x01]).Should().BeNull();
    }
}
