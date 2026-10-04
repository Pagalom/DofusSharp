using BestCrush.NetworkProbe.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class CrushDecoderTest
{
    [Fact]
    public void KeepsRowOrderRepeatedUidsAndRuneOrderWithoutRequiringAnItemCache()
    {
        byte[] body = Message(
            Bytes(1, CrushRow(8001, 3.71f, Rune(100, 2), Rune(200, 1), Rune(100, 3))),
            Bytes(1, CrushRow(9999, 1.25f)),
            Bytes(1, CrushRow(8001, 2f, Rune(100, 7))));

        var result = SemanticDecoders.TryDecodeCrush(body)!;

        result.Lines.Select(line => line.ItemUid).Should().Equal(8001UL, 9999UL, 8001UL);
        result.Lines.Select(line => line.CoefficientPercent).Should().Equal(371f, 125f, 200f);
        result.Lines[0].Runes.Should().Equal(new RuneDrop(100, 2), new RuneDrop(200, 1), new RuneDrop(100, 3));
        result.Lines[1].Runes.Should().BeEmpty();
        result.Lines[2].Runes.Should().Equal(new RuneDrop(100, 7));
        result.Lines.Should().OnlyContain(line => line.SecondaryPercent == null);
    }

    [Theory]
    [InlineData(0f, true, 0f)]
    [InlineData(10f, true, 1000f)]
    [InlineData(-0.01f, false, 0f)]
    [InlineData(10.001f, false, 0f)]
    [InlineData(float.NaN, false, 0f)]
    [InlineData(float.PositiveInfinity, false, 0f)]
    [InlineData(float.NegativeInfinity, false, 0f)]
    public void PrimaryFractionMustBeFiniteAndBetweenZeroAndTen(float fraction, bool accepted, float percent)
    {
        var result = SemanticDecoders.TryDecodeCrush(Bytes(1, CrushRow(8001, fraction)));
        (result is not null).Should().Be(accepted);
        if (accepted)
            result!.Lines.Single().CoefficientPercent.Should().Be(percent);
    }

    [Fact]
    public void SecondaryFractionIsConvertedSeparatelyAndIsNotValidatedLikePrimary()
    {
        var finite = SemanticDecoders.TryDecodeCrush(
            Bytes(1, Message(CrushRow(8001, 1.25f), Float(4, 1.5f))))!.Lines.Single();
        finite.CoefficientPercent.Should().Be(125f);
        finite.SecondaryPercent.Should().Be(150f);
        var nonFinite = SemanticDecoders.TryDecodeCrush(
            Bytes(1, Message(CrushRow(8001, 1.25f), Float(4, float.NaN))))!.Lines.Single();
        float.IsNaN(nonFinite.SecondaryPercent!.Value).Should().BeTrue();
    }

    [Fact]
    public void SkipsInvalidRowsButKeepsFollowingValidRows()
    {
        byte[] body = Message(Bytes(1, [0]), Bytes(1, CrushRow(0, 1f)),
            Bytes(1, Unsigned(3, 8001)),
            Bytes(1, Message(Unsigned(3, 8001), Unsigned(2, 1))),
            Bytes(1, CrushRow(8002, 1f, Rune(100, 1))));
        SemanticDecoders.TryDecodeCrush(body)!.Lines.Should().ContainSingle().Which.ItemUid.Should().Be(8002);
    }

    [Fact]
    public void RuneDecoderKeepsZeroMissingAndWideQuantitiesButDropsZeroIdsAndMalformedRunes()
    {
        byte[] row = CrushRow(8001, 1f,
            Rune(0, 10), [0], Rune(100, 0), Unsigned(1, 200), Rune(300, (ulong)int.MaxValue + 1));
        var runes = SemanticDecoders.TryDecodeCrush(Bytes(1, row))!.Lines.Single().Runes;
        runes.Should().Equal(new RuneDrop(100, 0), new RuneDrop(200, 0), new RuneDrop(300, 2147483648UL));
        // Filtering positive quantities and checked int conversion belong to PersistCrushAsync.
    }

    [Fact]
    public void EmptyMalformedOrUnrecognizedCrushMessagesReturnNull()
    {
        SemanticDecoders.TryDecodeCrush([]).Should().BeNull();
        SemanticDecoders.TryDecodeCrush([0]).Should().BeNull();
        SemanticDecoders.TryDecodeCrush(Bytes(2, CrushRow(8001, 1f))).Should().BeNull();
        SemanticDecoders.TryDecodeCrush(Bytes(1, CrushRow(0, 1f))).Should().BeNull();
    }
}
