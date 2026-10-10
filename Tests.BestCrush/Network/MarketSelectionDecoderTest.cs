using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class MarketSelectionDecoderTest
{
    // Observed jzs response: repeated f1 offers; f2 market/category;
    // f3 selected Dofus item ID. This path must NEVER persist price data.

    [Theory]
    [InlineData(12851UL, 9UL)]
    [InlineData(1356UL, 3UL)]
    public void JzsWithOfferRowsIdentifiesSelectedItem(
        ulong itemId, ulong category)
    {
        byte[] offer = Message(
            Unsigned(1, itemId),
            Bytes(2, [1, 2, 3, 4, 5]),
            Unsigned(3, 28116),
            Unsigned(4, category),
            Bytes(5, Packed(20, 21, 22)));

        byte[] response = Message(
            Bytes(1, offer),
            Unsigned(2, category),
            Unsigned(3, itemId));

        SemanticDecoders.TryDecodeMarketSelectionItemId(response)
            .Should().Be(itemId);
    }

    [Fact]
    public void JzsWithoutOffersStillIdentifiesSelectedItem()
    {
        byte[] response = Message(
            Unsigned(2, 9),
            Unsigned(3, 12851));

        SemanticDecoders.TryDecodeMarketSelectionItemId(response)
            .Should().Be(12851);
    }

    [Theory]
    [InlineData(false, true, 12851UL)]
    [InlineData(true, false, 12851UL)]
    [InlineData(true, true, 0UL)]
    public void IncompleteSelectionIsNotInterpretedAsEquipment(
        bool hasCategory, bool hasItemId, ulong itemId)
    {
        byte[] response = Message(
            hasCategory ? Unsigned(2, 9) : [],
            hasItemId ? Unsigned(3, itemId) : []);

        SemanticDecoders.TryDecodeMarketSelectionItemId(response)
            .Should().BeNull();
    }

    [Fact]
    public void MalformedSelectionIsNotInterpretedAsEquipment()
    {
        SemanticDecoders.TryDecodeMarketSelectionItemId([0x1a, 0x05, 0x01])
            .Should().BeNull();
    }
}
