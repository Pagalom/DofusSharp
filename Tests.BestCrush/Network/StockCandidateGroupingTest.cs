using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class StockCandidateGroupingTest
{
    private static byte[] Row(ulong quantity, ulong itemId, ulong uid,
        params byte[][] effects) => Bytes(2, Bytes(1, Message(
            Unsigned(3, quantity),
            Unsigned(4, itemId),
            Unsigned(5, uid),
            Message(effects.Select(e => Bytes(7, e)).ToArray()))));

    private static byte[] Snapshot(string opcode, params byte[][] rows) =>
        Message(Unsigned(opcode == "irp" ? 1 : 4, 1), Message(rows));

    private static byte[] Effect(ulong code, long value) =>
        Message(Unsigned(10, code), Unsigned(5, unchecked((ulong)value)));

    [Fact]
    public void SameTypeAndExactEffectMultisetMergesQuantityNotUid()
    {
        byte[] vi = Effect(1001, 24);
        byte[] intel = Effect(1002, 26);
        byte[] snapshot = Snapshot("irp",
            Row(2, 2427, 111, vi, intel),
            Row(3, 2427, 222, intel, vi),
            Row(1, 2427, 333, Effect(1001, 25), intel),
            Row(4, 2418, 444, vi, intel),
            Row(7, 1523, 555),
            Row(2, 1523, 666));

        StockSnapshotWireCandidate? decoded =
            InventoryBankSnapshotDecoder.Decode("irp", snapshot);
        decoded!.Status.Should().Be(StockSnapshotCandidateStatus.CompleteCandidate);
        StockGroupingCandidate? grouped = StockCandidateGrouping.TryGroup(decoded);

        grouped.Should().NotBeNull();
        grouped!.Container.Should().Be("bank");
        grouped.SourceRows.Should().Be(6);
        grouped.Groups.Should().HaveCount(4);
        grouped.Groups.Where(x => x.ItemIdCandidate == 2427)
            .Select(x => x.QuantityCandidate)
            .Should().BeEquivalentTo(new long[] { 5, 1 });
        grouped.Groups.Single(x => x.ItemIdCandidate == 1523)
            .QuantityCandidate.Should().Be(9);
        grouped.Groups.Single(x => x.ItemIdCandidate == 2418)
            .QuantityCandidate.Should().Be(4);
        grouped.Groups.SelectMany(x => x.Effects)
            .Should().NotBeEmpty();

        string report = grouped.Describe();
        report.Should().Be("grouping=candidate groups=4 source-rows=6 groups-with-effects=3");
        foreach (string sensitive in new[] { "2427", "2418", "1523",
            "111", "222", "333", "444", "555", "666", "1001", "1002" })
            report.Should().NotContain(sensitive);
    }

    [Fact]
    public void SameTypeDifferentRealEffectsRemainSeparateIncludingNegativeValues()
    {
        byte[] before = Effect(1001, -2);
        byte[] after = Effect(1001, -3);
        StockSnapshotWireCandidate snapshot =
            InventoryBankSnapshotDecoder.Decode("irl", Snapshot("irl",
                Row(1, 2418, 1234, before),
                Row(1, 2418, 5678, after)))!;

        StockGroupingCandidate grouping = StockCandidateGrouping.TryGroup(snapshot)!;
        grouping.Container.Should().Be("inventory");
        grouping.Groups.Should().HaveCount(2);
        grouping.Groups.Select(x => x.Effects[0].Value)
            .Should().BeEquivalentTo(new long[] { -2, -3 });
    }

    [Fact]
    public void IncompleteOrUnrecognizedEffectsMustNotBecomeGroupedStock()
    {
        byte[] valid = Row(1, 2418, 1234, Effect(1001, 24));
        byte[] withoutType = Row(1, 2418, 5678,
            Message(Unsigned(5, 24)));
        byte[] snapshot = Snapshot("irp", valid, withoutType);

        StockSnapshotWireCandidate decoded =
            InventoryBankSnapshotDecoder.Decode("irp", snapshot)!;
        decoded.Status.Should().Be(StockSnapshotCandidateStatus.CompleteCandidate,
            "stock triplets can be structurally intact while effects are unreadable");
        StockCandidateGrouping.TryGroup(decoded).Should().BeNull();

        StockCandidateGrouping.TryGroup(null).Should().BeNull();
        StockCandidateGrouping.TryGroup(
            InventoryBankSnapshotDecoder.Decode("irp", [0x00]))
            .Should().BeNull();
        StockCandidateGrouping.TryGroup(
            InventoryBankSnapshotDecoder.Decode("irp", Snapshot("irp")))
            .Should().BeNull("an empty candidate has not been confirmed semantically");
    }

    [Fact]
    public void InvalidCandidateOrOverflowFailsClosed()
    {
        var invalidQty = new StockSnapshotWireCandidate("bank",
            StockSnapshotCandidateStatus.CompleteCandidate,
            [new StockItemWireCandidate(ulong.MaxValue, 2418, 1234, 0, [])]);
        StockCandidateGrouping.TryGroup(invalidQty).Should().BeNull();

        var overflowQty = new StockSnapshotWireCandidate("bank",
            StockSnapshotCandidateStatus.CompleteCandidate,
            [
                new StockItemWireCandidate((ulong)long.MaxValue, 2418, 1234, 0, []),
                new StockItemWireCandidate(1, 2418, 1235, 0, [])
            ]);
        StockCandidateGrouping.TryGroup(overflowQty).Should().BeNull();

        var missingEffects = new StockSnapshotWireCandidate("bank",
            StockSnapshotCandidateStatus.CompleteCandidate,
            [new StockItemWireCandidate(1, 2418, 1234, 1, null)]);
        StockCandidateGrouping.TryGroup(missingEffects).Should().BeNull();
    }

    [Fact]
    public void SameLogicalItemInTwoContainersMustRemainSeparate()
    {
        byte[] row = Row(1, 2418, 1234, Effect(1001, 24));
        StockGroupingCandidate bank = StockCandidateGrouping.TryGroup(
            InventoryBankSnapshotDecoder.Decode("irp", Snapshot("irp", row)))!;
        StockGroupingCandidate inventory = StockCandidateGrouping.TryGroup(
            InventoryBankSnapshotDecoder.Decode("irl", Snapshot("irl", row)))!;

        bank.Container.Should().Be("bank");
        inventory.Container.Should().Be("inventory");
        bank.Groups.Should().HaveCount(1);
        inventory.Groups.Should().HaveCount(1);
        // Caller must never merge these candidates, because their owner
        // and account-scoped inventory semantics remain unverified.
    }

    [Fact]
    public void StockProbePublishesOnlyAggregateCountsNotRawValues()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] payload = Snapshot("irp",
            Row(12, 2427, 818181, Effect(1001, 24)),
            Row(2, 2427, 919191, Effect(1001, 24)));
        string? report = study.Observe(1, "irp", payload);
        report.Should().Contain(
            "grouping=candidate groups=1 source-rows=2 groups-with-effects=1");
        foreach (string sensitive in new[] { "2427", "818181",
            "919191", "1001" })
            report.Should().NotContain(sensitive);

        string? notReady = study.Observe(1, "irp", [0x00]);
        notReady.Should().Contain("grouping=not-ready");
    }
}
