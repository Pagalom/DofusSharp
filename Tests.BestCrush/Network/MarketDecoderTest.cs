using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class MarketDecoderTest
{
    [Fact]
    public void CurrentJznKeepsOfferOrderZerosAndInlineIdsWithoutSelectingMinimums()
    {
        byte[] body = Market(42,
            MarketOffer(501, 42, 12, 0, 850, 8000),
            MarketOffer(502, 0, 15, 110, 800, 0),
            MarketOffer(503, 43, 9));

        var market = SemanticDecoders.TryDecodeMarket(body)!;

        market.ItemId.Should().Be(42);
        market.Offers.Select(offer => offer.OfferId).Should().Equal(501UL, 502UL, 503UL);
        market.Offers.Select(offer => offer.ItemId).Should().Equal(42UL, 42UL, 43UL);
        market.Offers[0].Ladder.Should().Equal(12UL, 0UL, 850UL, 8000UL);
        market.Offers[1].Ladder.Should().Equal(15UL, 110UL, 800UL, 0UL);
        market.Offers[2].Ladder.Should().Equal(9UL);
        // Minimum positive prices per lot are selected later by PersistMarketAsync,
        // not by this decoder. That routing/persistence boundary is not exercised here.
    }

    [Fact]
    public void ItemWithoutOffersIsAValidObservationAndCurrentLayoutWinsBeforeFallbacks()
    {
        var empty = SemanticDecoders.TryDecodeMarket(Message(Unsigned(1, 42), Unsigned(3, 99)))!;
        empty.ItemId.Should().Be(42);
        empty.Offers.Should().BeEmpty();

        byte[] structuredOffer = Message(Unsigned(1, 501), Bytes(6, Packed(12, 100)));
        var preferred = SemanticDecoders.TryDecodeMarket(
            Message(Unsigned(1, 42), Unsigned(2, 99), Bytes(3, structuredOffer)))!;
        preferred.ItemId.Should().Be(42);
        preferred.Offers.Should().BeEmpty();
    }

    [Fact]
    public void SkipsMalformedOffersAndEmptyOrIncompletePackedLadders()
    {
        byte[] body = Market(42, [0], Unsigned(5, 501),
            Bytes(6, []), Bytes(6, [0x80]), MarketOffer(505, 42, 12));

        var market = SemanticDecoders.TryDecodeMarket(body)!;

        market.Offers.Should().ContainSingle().Which.OfferId.Should().Be(505);
        SemanticDecoders.TryDecodeMarket([0]).Should().BeNull();
        SemanticDecoders.TryDecodeMarket([]).Should().BeNull();
    }

    [Theory]
    [InlineData("4,12,120,1100,10000", "12,120,1100,10000")]
    [InlineData("17,12,120,1100,10000", "12,120,1100,10000")]
    [InlineData("10000,1100,120,12", "12,120,1100,10000")]
    [InlineData("1000,0,10,0", "0,10,0,1000")]
    [InlineData("1,100", "100")]
    [InlineData("0,0,0,0", "0,0,0,0")]
    public void KeepsExistingCountPrefixAndReversalHeuristics(string raw, string expected)
    {
        ulong[] ladder = raw.Split(',').Select(ulong.Parse).ToArray();
        var result = SemanticDecoders.TryDecodeMarket(Market(42, MarketOffer(501, 42, ladder)))!;
        result.Offers.Single().Ladder.Should().Equal(expected.Split(',').Select(ulong.Parse));
    }

    [Fact]
    public void DecoderDoesNotClampUnsignedPricesOrTrimExtraLotSlots()
    {
        var wide = SemanticDecoders.TryDecodeMarket(Market(42, MarketOffer(501, 42, ulong.MaxValue)))!;
        wide.Offers.Single().Ladder.Should().Equal(ulong.MaxValue);
        var extra = SemanticDecoders.TryDecodeMarket(Market(42, MarketOffer(501, 42, 100, 200, 300, 400, 500)))!;
        extra.Offers.Single().Ladder.Should().Equal(100UL, 200UL, 300UL, 400UL, 500UL);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StructuredFallbackUsesRootIdOrFirstInlineId(bool hasRootId)
    {
        byte[] offer = Message(Unsigned(1, 501), Unsigned(5, 42), Bytes(6, Packed(12, 100)));
        var result = SemanticDecoders.TryDecodeMarket(
            Message(hasRootId ? Unsigned(2, 99) : [], Bytes(3, offer)))!;

        result.ItemId.Should().Be(hasRootId ? 99UL : 42UL);
        result.Offers.Single().ItemId.Should().Be(42);
        result.Offers.Single().OfferId.Should().Be(501);
        result.Offers.Single().Ladder.Should().Equal(12UL, 100UL);
    }

    [Fact]
    public void CompactFallbackFindsShallowItemIdAndAssignsSyntheticOfferIndexes()
    {
        byte[] body = Bytes(9, Message(Unsigned(2, 42), Bytes(8, Packed(12, 120)), Bytes(8, Packed(13, 130))));
        var result = SemanticDecoders.TryDecodeMarket(body)!;
        result.ItemId.Should().Be(42);
        result.Offers.Select(offer => offer.OfferId).Should().Equal(0UL, 1UL);
        result.Offers[0].Ladder.Should().Equal(12UL, 120UL);
        result.Offers[1].Ladder.Should().Equal(13UL, 130UL);
    }
}
