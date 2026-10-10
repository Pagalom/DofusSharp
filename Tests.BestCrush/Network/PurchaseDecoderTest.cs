using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class PurchaseDecoderTest
{
    [Fact]
    public void KeiReadsPriceQuantityAndOfferIdFromTheirCurrentFields()
    {
        // price=150, quantity=10, offer=501; unrelated f3 must not become OfferId.
        var result = SemanticDecoders.TryDecodePurchaseRequest(
            Convert.FromHexString("089601100A18E70728F503"))!;
        result.Should().Be(new PurchaseRequestObservation(150, 10, 501));
    }

    [Theory]
    [InlineData(0, 10, 501)]
    [InlineData(150, 0, 501)]
    [InlineData(150, 10, 0)]
    public void KeiRequiresStrictlyPositivePriceQuantityAndOfferId(int price, int quantity, int offerId)
    {
        SemanticDecoders.TryDecodePurchaseRequest(Message(
            Unsigned(1, (ulong)price), Unsigned(2, (ulong)quantity), Unsigned(5, (ulong)offerId)))
            .Should().BeNull();
    }

    [Fact]
    public void KefKeepsItemOfferLadderAndSignedStatValuesWithoutPurchaseContext()
    {
        byte[] body = Message(Unsigned(1, 42), Unsigned(2, 501), Bytes(4, Packed(4, 12, 100, 900, 8000)),
            Bytes(5, Stat(118, 10)), Bytes(5, Stat(119, -2)));
        var offer = SemanticDecoders.TryDecodePurchaseOffer(body)!;
        offer.ItemId.Should().Be(42);
        offer.OfferId.Should().Be(501);
        offer.Ladder.Should().Equal(12UL, 100UL, 900UL, 8000UL);
        offer.Stats.Should().Equal(new ItemStatObservation(118, 10), new ItemStatObservation(119, -2));
        // No clock or pending purchase is consulted by the decoder.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("80")]
    public void KefWithoutAUsableLadderIsStillDecoded(string? ladderHex)
    {
        byte[] body = Message(Unsigned(1, 42), Unsigned(2, 501),
            ladderHex is null ? [] : Bytes(4, Convert.FromHexString(ladderHex)));
        SemanticDecoders.TryDecodePurchaseOffer(body)!.Ladder.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 501)]
    [InlineData(42, 0)]
    public void KefRequiresItemAndOfferIds(int itemId, int offerId)
    {
        SemanticDecoders.TryDecodePurchaseOffer(Message(
            Unsigned(1, (ulong)itemId), Unsigned(2, (ulong)offerId), Bytes(4, Packed(12))))
            .Should().BeNull();
    }

    [Fact]
    public void KbdUsesFieldsTwoAndThreeAndDoesNotNarrowUnsignedQuantity()
    {
        var receipt = SemanticDecoders.TryDecodePurchaseReceipt(
            Message(Unsigned(1, 999), Unsigned(2, ulong.MaxValue), Unsigned(3, 501)))!;
        receipt.Should().Be(new PurchaseReceiptObservation(ulong.MaxValue, 501));
    }

    [Theory]
    [InlineData(0, 501)]
    [InlineData(10, 0)]
    public void KbdRequiresPositiveQuantityAndOfferId(int quantity, int offerId)
    {
        SemanticDecoders.TryDecodePurchaseReceipt(
            Message(Unsigned(2, (ulong)quantity), Unsigned(3, (ulong)offerId))).Should().BeNull();
    }

    [Fact]
    public void PurchaseDecodersRejectMalformedMessages()
    {
        SemanticDecoders.TryDecodePurchaseRequest([0]).Should().BeNull();
        SemanticDecoders.TryDecodePurchaseOffer([0]).Should().BeNull();
        SemanticDecoders.TryDecodePurchaseReceipt([0]).Should().BeNull();
    }
}
