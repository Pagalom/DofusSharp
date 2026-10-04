using BestCrush.NetworkProbe.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class ItemDecoderTest
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    public void KdbAndIsaShareTheItemEnvelopeAndQuantityDefault(int? quantity, int expectedQuantity)
    {
        byte[] body = Bytes(2, Bytes(5, Item(8001, 42, quantity.HasValue ? (ulong)quantity.Value : null,
            Stat(118, 10), Stat(119, -2), Stat(118, 20), Unsigned(1, 120), Stat(0, 1), [0])));

        foreach (ItemDetailObservation? item in new[]
                 { SemanticDecoders.TryDecodeItemDetail(body), SemanticDecoders.TryDecodeInventoryAdd(body) })
        {
            item.Should().NotBeNull();
            item!.ItemUid.Should().Be(8001);
            item.ItemId.Should().Be(42);
            item.Quantity.Should().Be((ulong)expectedQuantity);
            item.Stats.Should().Equal(new ItemStatObservation(118, 10),
                new ItemStatObservation(119, -2), new ItemStatObservation(118, 20));
        }
    }

    [Theory]
    [InlineData(0, 42)]
    [InlineData(8001, 0)]
    public void ItemObjectRequiresBothUidAndItemId(int uid, int itemId)
    {
        byte[] item = Item((ulong)uid, (ulong)itemId);
        SemanticDecoders.TryDecodeItemDetail(Bytes(2, Bytes(5, item))).Should().BeNull();
        SemanticDecoders.TryDecodeInventoryAdd(Bytes(2, Bytes(5, item))).Should().BeNull();
        SemanticDecoders.TryDecodeCraftOutput(Bytes(1, Bytes(5, item))).Should().BeNull();
        SemanticDecoders.TryDecodeSmithmagicResult(Bytes(3, Bytes(2, item))).Should().BeNull();
    }

    [Fact]
    public void IuqUsesFieldOneEnvelopeInsteadOfItemDetailFieldTwo()
    {
        byte[] envelope = Bytes(5, Item(8001, 42, 2));
        var item = SemanticDecoders.TryDecodeCraftOutput(Bytes(1, envelope))!;
        item.ItemUid.Should().Be(8001);
        item.ItemId.Should().Be(42);
        item.Quantity.Should().Be(2);
        SemanticDecoders.TryDecodeCraftOutput(Bytes(2, envelope)).Should().BeNull();
        SemanticDecoders.TryDecodeItemDetail(Bytes(1, envelope)).Should().BeNull();
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(2, 2)]
    public void KbuKeepsResultCodeAndNestedItem(int? code, int expected)
    {
        byte[] body = Message(code.HasValue ? Unsigned(2, (ulong)code.Value) : [],
            Bytes(3, Bytes(2, Item(8001, 42, 1, Stat(118, 15)))));
        var result = SemanticDecoders.TryDecodeSmithmagicResult(body)!;
        result.ResultCode.Should().Be(expected);
        result.Item.ItemUid.Should().Be(8001);
        result.Item.ItemId.Should().Be(42);
        result.Item.Quantity.Should().Be(1);
        result.Item.Stats.Should().Equal(new ItemStatObservation(118, 15));
    }

    [Fact]
    public void KbuResultCodeConversionRemainsChecked()
    {
        Action decode = () => SemanticDecoders.TryDecodeSmithmagicResult(
            Message(Unsigned(2, (ulong)int.MaxValue + 1), Bytes(3, Bytes(2, Item(8001, 42)))));
        decode.Should().Throw<OverflowException>();
    }

    [Fact]
    public void InventoryQuantityAllowsZeroAndRemovalOnlyNeedsUid()
    {
        var quantity = SemanticDecoders.TryDecodeInventoryQuantity(Bytes(3, Unsigned(3, 8001)));
        quantity.Should().Be(new InventoryQuantityObservation(8001, 0));
        SemanticDecoders.TryDecodeInventoryRemove(Unsigned(1, 8001))
            .Should().Be(new InventoryRemoveObservation(8001));
        SemanticDecoders.TryDecodeInventoryQuantity(Bytes(3, Unsigned(2, 10))).Should().BeNull();
        SemanticDecoders.TryDecodeInventoryRemove(Unsigned(1, 0)).Should().BeNull();
    }

    [Fact]
    public void WorkshopDeltaDefaultsToOneAndPreservesSignedNegativeValues()
    {
        SemanticDecoders.TryDecodeWorkshopSlot(Unsigned(2, 8001))
            .Should().Be(new WorkshopSlotObservation(1, 8001));
        SemanticDecoders.TryDecodeWorkshopSlot(Message(Unsigned(1, ulong.MaxValue), Unsigned(2, 8001)))
            .Should().Be(new WorkshopSlotObservation(-1, 8001));
    }

    [Fact]
    public void MalformedItemEnvelopesAreRejected()
    {
        SemanticDecoders.TryDecodeItemDetail(Bytes(2, [0])).Should().BeNull();
        SemanticDecoders.TryDecodeInventoryAdd([0]).Should().BeNull();
        SemanticDecoders.TryDecodeCraftOutput(Bytes(1, Bytes(5, [0]))).Should().BeNull();
        SemanticDecoders.TryDecodeSmithmagicResult(Bytes(3, [0])).Should().BeNull();
    }

    [Fact]
    public void SmithmagicRequestsKeepTheirDifferentQuantityDefaultsAndZeroRules()
    {
        SemanticDecoders.TryDecodeSmithmagicRequest(Unsigned(1, 8001))
            .Should().Be(new SmithmagicRequestObservation(8001, 1));
        SemanticDecoders.TryDecodeSmithmagicRequest(Message(Unsigned(1, 8001), Unsigned(3, 0)))
            .Should().Be(new SmithmagicRequestObservation(8001, 0));
        SemanticDecoders.TryDecodeSmithmagicRequest([]).Should().BeNull();
        SemanticDecoders.TryDecodeSmithmagicBatchRequest([])
            .Should().Be(new SmithmagicBatchRequestObservation(1, 0));
        SemanticDecoders.TryDecodeSmithmagicBatchRequest(Message(Unsigned(2, 10), Unsigned(3, 7)))
            .Should().Be(new SmithmagicBatchRequestObservation(10, 7));
        SemanticDecoders.TryDecodeSmithmagicBatchRequest(Unsigned(2, 0)).Should().BeNull();
    }

    [Fact]
    public void SmithmagicAuxiliaryStackUsesFieldThreeThenFive()
    {
        var result = SemanticDecoders.TryDecodeSmithmagicStack(Bytes(3, Bytes(5, Item(8002, 100, 10))))!;
        result.Stack.ItemUid.Should().Be(8002);
        result.Stack.ItemId.Should().Be(100);
        result.Stack.Quantity.Should().Be(10);
        SemanticDecoders.TryDecodeSmithmagicStack(Bytes(2, Bytes(5, Item(8002, 100, 10)))).Should().BeNull();
    }

    [Fact]
    public void ListingCreatedKeepsMarketUidSeparateFromItemIdAndReadsPriceAtRoot()
    {
        byte[] listing = Message(Unsigned(1, 9001), Unsigned(2, 42), Unsigned(3, 10),
            Bytes(5, Stat(118, 10)));
        var result = SemanticDecoders.TryDecodeMarketListingCreated(
            Message(Bytes(3, listing), Unsigned(5, 800)))!;
        result.MarketListingUid.Should().Be(9001);
        result.ItemId.Should().Be(42);
        result.Quantity.Should().Be(10);
        result.Price.Should().Be(800);
        result.Stats.Should().Equal(new ItemStatObservation(118, 10));
        SemanticDecoders.TryDecodeMarketListingCreated(Bytes(3, listing)).Should().BeNull();
        SemanticDecoders.TryDecodeMarketListingRequest(
            Message(Unsigned(1, 10), Unsigned(2, 800), Unsigned(3, 8001)))
            .Should().Be(new MarketListingRequestObservation(8001, 10, 800));
    }
}
