using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class StockOpaqueWireSnapshotStudyTest
{
    private static byte[] Row(ulong qty, ulong item, ulong uid,
        params byte[][] effects) => Bytes(2, Bytes(1, Message(
            Unsigned(3, qty), Unsigned(4, item), Unsigned(5, uid),
            Message(effects.Select(e => Bytes(7, e)).ToArray()))));

    private static byte[] Snapshot(string opcode, params byte[][] rows) =>
        Message(Unsigned(opcode == "irp" ? 1 : 4, 1), Message(rows));

    private static byte[] Numeric(ulong code, ulong val) =>
        Message(Unsigned(5, val), Unsigned(10, code));
    private static byte[] Nested(int field, ulong code, byte[] bytes) =>
        Message(Bytes(field, bytes), Unsigned(10, code));

    [Fact]
    public void AllFiveObservedWireVariantShapesCanFormResearchOnlyGroups()
    {
        byte[] body = Snapshot("irp",
            Row(1, 2415, 90001, Numeric(111, 29), Nested(8, 112, [8, 16])),
            Row(1, 7113, 90002, Nested(7, 221, [8, 18])),
            Row(1, 7711, 90003, Nested(9, 331, [8, 44])),
            Row(1, 7423, 90004, Nested(6, 441, [8, 1])),
            Row(1, 7424, 90005, Message(Unsigned(10, 551))));

        string report = new StockSnapshotDeltaStudy()
            .Observe(1, "irp", body)!;
        report.Should().Contain("grouping=not-ready");
        report.Should().Contain(
            "wire-grouping=candidate groups=5 source-rows=5 rows-with-structured-effects=5");
        report.Should().Contain("wire-snapshot-change=no-prior-snapshot");
        foreach (string value in new[] { "2415", "7113", "7711",
            "90001", "90002", "90003", "90004", "90005" })
            report.Should().NotContain(value);
    }

    [Fact]
    public void FullWireContentCanMeasureQuantityWithoutMatchingUid()
    {
        byte[] nested = Nested(9, 331, [8, 61]);
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irp", Snapshot("irp",
            Row(10, 1523, 701), Row(1, 7711, 702, nested)))
            .Should().Contain("wire-snapshot-change=no-prior-snapshot");
        string next = study.Observe(1, "irp", Snapshot("irp",
            Row(7, 1523, 901), Row(1, 7711, 902, nested)))!;
        next.Should().Contain(
            "wire-snapshot-change=compared increased-groups=0 decreased-groups=1 " +
            "new-groups=0 removed-groups=0 unchanged-groups=1");
        next.Should().Contain("grouping=not-ready");
    }

    [Fact]
    public void NestedEffectsWithDifferentBytesRemainDistinct()
    {
        byte[] a = Nested(8, 112, [8, 16]);
        byte[] b = Nested(8, 112, [8, 17]);
        string report = new StockSnapshotDeltaStudy().Observe(1, "irp",
            Snapshot("irp", Row(1, 2415, 9981, a),
                Row(1, 2415, 9982, b)))!;
        report.Should().Contain("wire-grouping=candidate groups=2");
    }

    [Fact]
    public void InvalidNestedEffectRevokesThePriorReference()
    {
        byte[] valid = Snapshot("irp",
            Row(1, 2415, 1234, Nested(8, 112, [8, 16])));
        byte[] broken = Snapshot("irp", Row(1, 2415, 1235, [0]));
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irp", valid)
            .Should().Contain("wire-snapshot-change=no-prior-snapshot");
        study.Observe(1, "irp", broken)
            .Should().Contain("wire-grouping=not-ready");
        study.Observe(1, "irp", valid)
            .Should().Contain("wire-snapshot-change=no-prior-snapshot");
    }

    [Fact]
    public void DifferentUidsAndReorderedOuterFieldsDoNotCreateFalseChanges()
    {
        byte[] firstEffect = Nested(8, 112, [8, 16]);
        byte[] reordered = Message(Unsigned(10, 112), Bytes(8, [8, 16]));
        StockSnapshotDeltaStudy study = new();
        string initial = study.Observe(1, "irp", Snapshot("irp",
            Row(2, 2415, 111, Numeric(222, 24), firstEffect),
            Row(3, 2415, 112, reordered, Numeric(222, 24))))!;
        initial.Should().Contain("wire-grouping=candidate groups=1");

        string later = study.Observe(1, "irp", Snapshot("irp",
            Row(5, 2415, 998, firstEffect, Numeric(222, 24))))!;
        later.Should().Contain(
            "wire-snapshot-change=compared increased-groups=0 decreased-groups=0 " +
            "new-groups=0 removed-groups=0 unchanged-groups=1");
    }

    [Fact]
    public void DifferentSessionsAndContainersDoNotShareStructuralBaselines()
    {
        byte[] bank = Snapshot("irp", Row(2, 2415, 1234,
            Nested(8, 112, [8, 16])));
        byte[] inventory = Snapshot("irl", Row(2, 2415, 1234,
            Nested(8, 112, [8, 16])));
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irp", bank)
            .Should().Contain("wire-snapshot-change=no-prior-snapshot");
        study.Observe(1, "irl", inventory)
            .Should().Contain("wire-snapshot-change=no-prior-snapshot");
        study.Observe(2, "irp", bank)
            .Should().Contain("wire-snapshot-change=no-prior-snapshot");

        study.DropSession(1);
        study.Observe(1, "irp", bank)
            .Should().Contain("wire-snapshot-change=no-prior-snapshot");
        study.Observe(2, "irp", bank)
            .Should().Contain("wire-snapshot-change=compared");
    }

}
