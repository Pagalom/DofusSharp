using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class StockSnapshotGroupChangeStudyTest
{
    private static byte[] Effect(ulong code, long value) =>
        Message(Unsigned(10, code), Unsigned(5, unchecked((ulong)value)));

    private static byte[] Row(ulong quantity, ulong itemId, ulong uid,
        params byte[][] effects) => Bytes(2, Bytes(1, Message(
            Unsigned(3, quantity), Unsigned(4, itemId), Unsigned(5, uid),
            Message(effects.Select(effect => Bytes(7, effect)).ToArray()))));

    private static byte[] Snapshot(string opcode, params byte[][] rows) =>
        Message(Unsigned(opcode == "irp" ? 1 : 4, 1), Message(rows));

    [Fact]
    public void SameLogicalGroupsAreComparedAcrossSnapshotsIgnoringInstanceUids()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] jetA = Effect(1001, 24);
        byte[] jetB = Effect(1001, 25);
        string? initial = study.Observe(1, "irp", Snapshot("irp",
            Row(10, 2418, 111, jetA), Row(5, 2418, 112, jetA),
            Row(1, 2418, 222, jetB), Row(4, 7777, 333)));
        initial.Should().Contain("snapshot-change=no-prior-snapshot");

        string? changed = study.Observe(1, "irp", Snapshot("irp",
            Row(7, 2418, 991, jetA), Row(5, 2418, 992, jetA),
            Row(1, 2418, 993, jetB), Row(9, 8888, 994)));
        changed.Should().Contain(
            "snapshot-change=compared increased-groups=0 " +
            "decreased-groups=1 new-groups=1 removed-groups=1 unchanged-groups=1");

        string? identical = study.Observe(1, "irp", Snapshot("irp",
            Row(12, 2418, 555, jetA), Row(1, 2418, 556, jetB),
            Row(9, 8888, 557)));
        identical.Should().Contain(
            "snapshot-change=compared increased-groups=0 " +
            "decreased-groups=0 new-groups=0 removed-groups=0 unchanged-groups=3");

        foreach (string report in new[] { initial!, changed!, identical! })
        foreach (string identifier in new[] { "2418", "7777", "8888",
            "1001", "991", "992", "993", "994" })
            report.Should().NotContain(identifier);
    }

    [Fact]
    public void DifferentEffectValuesNeverBecomeAQuantityChange()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irp", Snapshot("irp",
            Row(1, 2418, 111, Effect(1001, -2))));
        string? updated = study.Observe(1, "irp", Snapshot("irp",
            Row(1, 2418, 222, Effect(1001, -3))));
        updated.Should().Contain(
            "decreased-groups=0 new-groups=1 removed-groups=1 unchanged-groups=0");
    }

    [Fact]
    public void BankInventoryAndSeparateTcpSessionsCannotShareBaselines()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] bank = Snapshot("irp", Row(5, 2418, 111));
        byte[] inventory = Snapshot("irl", Row(5, 2418, 111));
        study.Observe(1, "irp", bank)
            .Should().Contain("snapshot-change=no-prior-snapshot");
        study.Observe(1, "irl", inventory)
            .Should().Contain("snapshot-change=no-prior-snapshot");
        study.Observe(2, "irp", bank)
            .Should().Contain("snapshot-change=no-prior-snapshot");
        study.DropSession(1);
        study.Observe(1, "irp", bank)
            .Should().Contain("snapshot-change=no-prior-snapshot");
        study.Observe(2, "irp", bank)
            .Should().Contain("snapshot-change=compared");
    }

    [Fact]
    public void CorruptedOrUnreadableSnapshotsRevokePreviousGroupReference()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] valid = Snapshot("irp", Row(5, 2418, 111,
            Effect(1001, 24)));
        byte[] unknownEffect = Snapshot("irp", Row(5, 2418, 222,
            Message(Unsigned(5, 24))));

        study.Observe(1, "irp", valid)
            .Should().Contain("snapshot-change=no-prior-snapshot");
        study.Observe(1, "irp", unknownEffect)
            .Should().Contain("snapshot-change=not-comparable");
        study.Observe(1, "irp", valid)
            .Should().Contain("snapshot-change=no-prior-snapshot");
        study.Observe(1, "irp", [0x00])
            .Should().Contain("snapshot-change=not-comparable");
        study.Observe(1, "irp", valid)
            .Should().Contain("snapshot-change=no-prior-snapshot");
    }

    [Fact]
    public void ComparisonRejectsDifferentContainersAndInvalidAmounts()
    {
        var a = new StockGroupingCandidate("bank", 1,
            [new StockGroupedItemCandidate(2418, 1, [])]);
        var b = new StockGroupingCandidate("inventory", 1,
            [new StockGroupedItemCandidate(2418, 1, [])]);
        StockSnapshotGroupChangeStudy.Describe(a, b)
            .Should().Be("snapshot-change=different-container");
        StockSnapshotGroupChangeStudy.Describe(a, null)
            .Should().Be("snapshot-change=not-comparable");
        StockSnapshotGroupChangeStudy.Describe(null, a)
            .Should().Be("snapshot-change=no-prior-snapshot");

        var invalid = new StockGroupingCandidate("bank", 1,
            [new StockGroupedItemCandidate(2418, 0, [])]);
        StockSnapshotGroupChangeStudy.Describe(a, invalid)
            .Should().Be("snapshot-change=not-comparable");

        var overflow = new StockGroupingCandidate("bank", 2,
        [
            new StockGroupedItemCandidate(2418, long.MaxValue, []),
            new StockGroupedItemCandidate(2418, 1, [])
        ]);
        StockSnapshotGroupChangeStudy.Describe(a, overflow)
            .Should().Be("snapshot-change=not-comparable");
    }
}
