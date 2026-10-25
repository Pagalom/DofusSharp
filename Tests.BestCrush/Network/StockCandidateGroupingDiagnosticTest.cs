using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class StockCandidateGroupingDiagnosticTest
{
    private static byte[] Item(ulong quantity, ulong itemId, ulong uid,
        params byte[][] effects) =>
        Bytes(2, Bytes(1, Message(
            Unsigned(3, quantity),
            Unsigned(4, itemId),
            Unsigned(5, uid),
            Message(effects.Select(e => Bytes(7, e)).ToArray()))));

    private static byte[] Bank(params byte[][] rows) =>
        Message(Unsigned(1, 1), Message(rows));

    [Fact]
    public void CompleteTripletsCanStillRejectUnreadableEffectShapes()
    {
        byte[] payload = Bank(
            Item(3, 842612, 881199,
                Message(Unsigned(5, 1233))),
            Item(7, 842613, 881200,
                Message(Unsigned(10, 5812))));

        StockSnapshotWireCandidate candidate =
            InventoryBankSnapshotDecoder.Decode("irp", payload)!;
        candidate.Status.Should().Be(
            StockSnapshotCandidateStatus.CompleteCandidate);
        StockCandidateGrouping.TryGroup(candidate).Should().BeNull();

        string summary = StockCandidateGrouping.DescribeBlockers(candidate);
        summary.Should().Contain("bad-scalars:0");
        summary.Should().Contain("quantity-overflow:0");
        summary.Should().Contain("undecodable-effects:2");
        summary.Should().Contain("effect-shapes:[missing-code:1;missing-value:1]");

        StockSnapshotDeltaStudy study = new();
        string first = study.Observe(1, "irp", payload)!;
        string again = study.Observe(1, "irp", payload)!;
        first.Should().Contain("grouping=not-ready");
        again.Should().Contain("snapshot-change=not-comparable");
        again.Should().Contain("undecodable-effects:2");
        foreach (string report in new[] { first, again })
        foreach (string forbidden in new[] { "842612", "842613",
            "881199", "881200", "1233", "5812" })
            report.Should().NotContain(forbidden);
    }

    [Fact]
    public void MalformedEffectDoesNotTurnIntoAResourceWithoutEffects()
    {
        byte[] partial = Bank(
            Item(3, 842612, 881199,
                Message(Unsigned(5, 1233))),
            Item(7, 842612, 881200));
        StockSnapshotWireCandidate snapshot =
            InventoryBankSnapshotDecoder.Decode("irp", partial)!;

        StockCandidateGrouping.TryGroup(snapshot).Should().BeNull(
            "an unreadable jet must not merge with a known empty jet");
        StockCandidateGrouping.DescribeBlockers(snapshot)
            .Should().Contain("effect-shapes:[missing-code:1]");
    }

    [Fact]
    public void FullyDecodedCandidatesHaveNoExtraRejectionDetail()
    {
        byte[] payload = Bank(
            Item(3, 842612, 881199,
                Message(Unsigned(5, 24), Unsigned(10, 1001))),
            Item(7, 842612, 881200,
                Message(Unsigned(10, 1001), Unsigned(5, 24))));

        StockSnapshotDeltaStudy study = new();
        string first = study.Observe(1, "irp", payload)!;
        string again = study.Observe(1, "irp", payload)!;
        first.Should().Contain("grouping=candidate groups=1 source-rows=2");
        first.Should().NotContain("grouping-blockers");
        again.Should().Contain("snapshot-change=compared");
        again.Should().Contain("unchanged-groups=1");
    }

    [Fact]
    public void BlockerDiagnosticsRejectUnsignedOverflowWithoutExposingValues()
    {
        StockSnapshotWireCandidate candidate = new("bank",
            StockSnapshotCandidateStatus.CompleteCandidate,
            [new StockItemWireCandidate(
                ulong.MaxValue, 842612, 881199, 0, [])]);
        StockCandidateGrouping.TryGroup(candidate).Should().BeNull();
        string report = StockCandidateGrouping.DescribeBlockers(candidate);
        report.Should().Contain("quantity-overflow:1");
        report.Should().NotContain(ulong.MaxValue.ToString());

        var untrustedIssue = new StockItemWireCandidate(3, 842612, 881199,
            1, null) { EffectDecodeIssue = "secret-network-value" };
        var custom = new StockSnapshotWireCandidate("bank",
            StockSnapshotCandidateStatus.CompleteCandidate, [untrustedIssue]);
        StockCandidateGrouping.DescribeBlockers(custom)
            .Should().Contain("effect-shapes:[other:1]");
        StockCandidateGrouping.DescribeBlockers(custom)
            .Should().NotContain("secret-network-value");
    }
}
