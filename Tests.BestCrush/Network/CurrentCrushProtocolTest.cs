using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class CurrentCrushProtocolTest
{
    private static ProtocolMap Map() => ProtocolMap.Load(
        Path.Combine(AppContext.BaseDirectory,
            "NetworkFixtures", "protocol-map.json"));

    [Fact]
    public void CurrentAndLegacyCrushRoutesCoexistInSharedMap()
    {
        ProtocolMap map = Map();
        map.ItemDetail.Should().Be("kdb");
        map.CurrentItemDetail.Should().Be("kch");
        map.CrushResult.Should().Be("kci");
        map.CurrentCrushResult.Should().Be("kag");
    }

    [Fact]
    public void CurrentKchItemDetailNormalizesUidIdAndQuantity()
    {
        ProtocolMap map = Map();
        byte[] wire = CurrentItemDetail(9022491, 6924, 1);
        ItemDetailObservation? item = SemanticDecoders.TryDecodeItemDetail(
            map.NormalizeBody("kch", wire));

        item.Should().NotBeNull();
        item!.ItemUid.Should().Be(9022491UL);
        item.ItemId.Should().Be(6924UL);
        item.Quantity.Should().Be(1UL);

        // Wire fields are not the old kdb wire layout.
        SemanticDecoders.TryDecodeItemDetail(wire).Should().BeNull();
    }

    [Theory]
    [InlineData(11387412UL, 0.5976145f, 59.76145f)]
    [InlineData(9022491UL, 1.615723f, 161.5723f)]
    public void CurrentKagCoefficientKeepsUnroundedPercent(
        ulong uid, float fraction, float expectedPercent)
    {
        ProtocolMap map = Map();
        byte[] wire = CurrentCrush(CurrentCrushRow(uid, fraction));
        CrushObservation? crush = SemanticDecoders.TryDecodeCrush(
            map.NormalizeBody("kag", wire));

        crush.Should().NotBeNull();
        crush!.Lines.Should().ContainSingle();
        crush.Lines[0].ItemUid.Should().Be(uid);
        crush.Lines[0].CoefficientPercent
            .Should().BeApproximately(expectedPercent, 0.0001f);
        crush.Lines[0].Runes.Should().BeEmpty();
    }

    [Fact]
    public void CurrentKagSixViRunesYieldRuneId1523AndQuantitySix()
    {
        ProtocolMap map = Map();
        byte[] wire = CurrentCrush(
            CurrentCrushRow(9022491, 1.615723f,
                CurrentCrushRune(1523, 6)));

        CrushObservation? crush = SemanticDecoders.TryDecodeCrush(
            map.NormalizeBody("kag", wire));

        crush.Should().NotBeNull();
        crush!.Lines.Should().ContainSingle();
        crush.Lines[0].ItemUid.Should().Be(9022491UL);
        crush.Lines[0].CoefficientPercent
            .Should().BeApproximately(161.5723f, 0.0001f);
        crush.Lines[0].Runes.Should().ContainSingle()
            .Which.Should().Be(new RuneDrop(1523, 6));
    }

    [Fact]
    public void CurrentKagMultipleRuneRowsRetainOrder()
    {
        ProtocolMap map = Map();
        byte[] wire = CurrentCrush(
            CurrentCrushRow(2001, 1.25f,
                CurrentCrushRune(100, 2),
                CurrentCrushRune(101, 3)),
            CurrentCrushRow(2002, 0.75f,
                CurrentCrushRune(102, 4)));

        CrushObservation? result = SemanticDecoders.TryDecodeCrush(
            map.NormalizeBody("kag", wire));

        result.Should().NotBeNull();
        result!.Lines.Select(line => line.ItemUid)
            .Should().Equal(2001UL, 2002UL);
        result.Lines[0].Runes
            .Should().Equal(new RuneDrop(100, 2), new RuneDrop(101, 3));
        result.Lines[1].Runes
            .Should().Equal(new RuneDrop(102, 4));
    }

    [Fact]
    public void MalformedCurrentCrushCannotProduceCoefficient()
    {
        ProtocolMap map = Map();
        // Float present, but the equipment UID is absent.
        byte[] wire = CurrentCrush(
            Message(Float(1, 1.615723f)));

        SemanticDecoders.TryDecodeCrush(
            map.NormalizeBody("kag", wire)).Should().BeNull();

        // Incorrect nested shape is rejected by field normalization.
        map.NormalizeBody("kag",
            CurrentCrush(
                Message(Unsigned(4, 3), Float(1, 1f), Unsigned(3, 900))))
            .Should().BeEmpty();
    }

    [Fact]
    public void OldKciAndKdbMessagesRemainUnchanged()
    {
        ProtocolMap map = Map();
        byte[] legacyItem = Bytes(2, Bytes(5, Item(8001, 42)));
        SemanticDecoders.TryDecodeItemDetail(
            map.NormalizeBody("kdb", legacyItem))!.ItemUid
            .Should().Be(8001UL);

        byte[] legacyCrush = Bytes(1,
            CrushRow(8001, 1.5f, Rune(100, 2)));

        CrushObservation? result = SemanticDecoders.TryDecodeCrush(
            map.NormalizeBody("kci", legacyCrush));

        result.Should().NotBeNull();
        result!.Lines.Should().ContainSingle();
        result.Lines[0].CoefficientPercent
            .Should().BeApproximately(150f, 0.0001f);
        result.Lines[0].Runes.Should().ContainSingle()
            .Which.Should().Be(new RuneDrop(100, 2));
    }
}
