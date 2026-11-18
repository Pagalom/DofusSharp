using BestCrush.Network.Protocol;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class CurrentMarketRefreshTest
{
    [Fact]
    public void DecodesPublicFourLotMinimaIndependentlyOfPurchaseTracking()
    {
        var body = Message(Bytes(1, Packed(346, 3970, 44882, 349980)),
            Unsigned(2, 200), Unsigned(3, 46), Unsigned(4, 5824));
        var market = Assert.IsType<MarketObservation>(CurrentPurchaseDecoder.DecodeStackableMarket(body));
        Assert.Equal(200UL, market.ItemId);
        var offer = Assert.Single(market.Offers);
        Assert.Equal(5824UL, offer.OfferId);
        Assert.Equal(new ulong[] { 346, 3970, 44882, 349980 }, offer.Ladder);
        Assert.Equal(200UL, CurrentPurchaseDecoder.DecodeOffer(body)!.ItemId);
    }

    [Fact]
    public void RejectsIncompleteAmbiguousMalformedOrOverflowedRefreshes()
    {
        var identifiers = Message(Unsigned(2, 200), Unsigned(3, 46), Unsigned(4, 5824));
        byte[][] invalid = [[], new byte[129], identifiers,
            Message(Bytes(1, Packed(1, 2, 3)), identifiers),
            Message(Bytes(1, Packed(0, 0, 0, 0)), identifiers),
            Message(Bytes(1, Packed(1, 2, 3, ulong.MaxValue)), identifiers),
            Message(Bytes(1, [0x80]), identifiers),
            Message(Bytes(1, Packed(1, 2, 3, 4)), identifiers, Unsigned(2, 201)),
            Message(Bytes(1, Packed(1, 2, 3, 4)), Unsigned(2, 200), Unsigned(4, 5824)),
            Message(Bytes(1, Packed(1, 2, 3, 4)), Unsigned(2, 0), Unsigned(3, 46), Unsigned(4, 5824))];
        foreach (var body in invalid) Assert.Null(CurrentPurchaseDecoder.DecodeStackableMarket(body));
    }
}
