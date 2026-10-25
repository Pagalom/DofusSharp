using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class StockMissingEffectWireShapeTest
{
    private static byte[] Item(ulong itemId, ulong uid, params byte[][] effects) =>
        Bytes(2, Bytes(1, Message(
            Unsigned(3, 1), Unsigned(4, itemId), Unsigned(5, uid),
            Message(effects.Select(effect => Bytes(7, effect)).ToArray()))));

    private static byte[] Bank(params byte[][] rows) =>
        Message(Unsigned(1, 1), Message(rows));

    [Fact]
    public void MissingValueReportsOnlyFieldNumbersWireKindsAndVariantCounts()
    {
        byte[] shapeA = Message(
            Unsigned(10, 5812), Unsigned(2, 991777));
        byte[] shapeB = Message(
            Bytes(4, [0x66, 0x6f, 0x6f]), Unsigned(10, 5813));
        byte[] payload = Bank(
            Item(842612, 881199, shapeA),
            Item(842613, 881200, shapeA),
            Item(842614, 881201, shapeB));

        StockSnapshotDeltaStudy study = new();
        string report = study.Observe(1, "irp", payload)!;
        report.Should().Contain("grouping=not-ready");
        report.Should().Contain(
            "undecodable-effects:3");
        report.Should().Contain(
            "missing-value-wire-shapes:[(f2:v;f10:v)x2|(f4:len;f10:v)x1]");
        foreach (string value in new[] { "842612", "842613",
            "842614", "881199", "881200", "881201", "5812",
            "5813", "991777", "foo" })
            report.Should().NotContain(value);
    }

    [Fact]
    public void NoGuessIsMadeForEffectsWithoutCandidateValue()
    {
        StockSnapshotWireCandidate snapshot =
            InventoryBankSnapshotDecoder.Decode("irp", Bank(
                Item(842612, 881199, Message(
                    Unsigned(10, 5812),
                    Unsigned(2, 991777)))))!;

        snapshot.Status.Should().Be(StockSnapshotCandidateStatus.CompleteCandidate);
        snapshot.Rows[0].Effects.Should().BeNull();
        snapshot.Rows[0].EffectDecodeIssue.Should().Be("missing-value");
        snapshot.Rows[0].EffectDecodeShape.Should().Be("f2:v;f10:v");
        StockCandidateGrouping.TryGroup(snapshot).Should().BeNull(
            "an effect with unknown encoding is not a confirmed empty effect");
    }

    [Fact]
    public void FullyDecodedEffectsHaveNoMissingValueShape()
    {
        byte[] payload = Bank(Item(842612, 881199,
            Message(Unsigned(5, 24), Unsigned(10, 5812))));
        StockSnapshotWireCandidate snapshot =
            InventoryBankSnapshotDecoder.Decode("irp", payload)!;

        snapshot.Rows[0].EffectDecodeIssue.Should().BeNull();
        snapshot.Rows[0].EffectDecodeShape.Should().BeNull();
        StockCandidateGrouping.TryGroup(snapshot).Should().NotBeNull();
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irp", payload)
            .Should().NotContain("missing-value-wire-shapes");
    }

    [Fact]
    public void MissingValueShapeIsBoundedWithNonstandardFieldNumbers()
    {
        byte[] many = Message(
            Enumerable.Range(1, 22)
                .Where(n => n != 5)
                .Select(n => Unsigned(n, (ulong)(999000 + n)))
                .Append(Unsigned(200, 999999))
                .ToArray());
        byte[] payload = Bank(Item(842612, 881199, many));
        StockSnapshotDeltaStudy study = new();
        string report = study.Observe(1, "irp", payload)!;
        report.Should().Contain("more-fields");
        report.Should().Contain("other-fields");
        report.Should().NotContain("999999");
        report.Should().NotContain("999001");
        report.Length.Should().BeLessThan(1200);
    }

    [Fact]
    public void WireShapeIsNotReportedForOtherEffectFailures()
    {
        byte[] payload = Bank(Item(842612, 881199,
            Message(Unsigned(5, 24))));
        string report = new StockSnapshotDeltaStudy()
            .Observe(1, "irp", payload)!;
        report.Should().Contain("effect-shapes:[missing-code:1]");
        report.Should().NotContain("missing-value-wire-shapes");
    }
}
