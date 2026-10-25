using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class InventoryBankSnapshotDecoderTest
{
    // Synthetic fixtures only; these paths remain protocol candidates.
    private static byte[] Row(
        ulong quantity, ulong itemId, ulong uid, params byte[][] effects) =>
        Bytes(2, Bytes(1, Message(
            Unsigned(3, quantity),
            Unsigned(4, itemId),
            Unsigned(5, uid),
            Message(effects.Select(x => Bytes(7, x)).ToArray()))));

    private static byte[] Snapshot(string opcode, params byte[][] rows) =>
        Message(Unsigned(opcode == "irl" ? 4 : 1, 1), Message(rows));

    [Fact]
    public void InventoryAndBankUseSeparateContainersAndPreserveEveryCandidateRow()
    {
        byte[] effect = Message(Unsigned(5, 26), Unsigned(10, 119));
        byte[] inventory = Snapshot("irl",
            Row(1, 2418, 818181, effect),
            Row(500, 1523, 919191));
        byte[] bank = Snapshot("irp",
            Row(1, 2427, 717171, effect));

        StockSnapshotWireCandidate? decodedInventory =
            InventoryBankSnapshotDecoder.Decode("irl", inventory);
        StockSnapshotWireCandidate? decodedBank =
            InventoryBankSnapshotDecoder.Decode("irp", bank);

        decodedInventory.Should().NotBeNull();
        decodedBank.Should().NotBeNull();
        decodedInventory!.Container.Should().Be("inventory");
        decodedBank!.Container.Should().Be("bank");
        decodedInventory.Status.Should().Be(
            StockSnapshotCandidateStatus.CompleteCandidate);
        decodedBank.Status.Should().Be(
            StockSnapshotCandidateStatus.CompleteCandidate);

        decodedInventory.Rows.Should().HaveCount(2);
        decodedInventory.Rows[0].Field3QuantityCandidate.Should().Be(1);
        decodedInventory.Rows[0].Field4ItemIdCandidate.Should().Be(2418);
        decodedInventory.Rows[0].Field5InstanceUidCandidate.Should().Be(818181);
        decodedInventory.Rows[0].EffectFields.Should().Be(1);
        decodedInventory.Rows[1].Field3QuantityCandidate.Should().Be(500);
        decodedInventory.Rows[1].EffectFields.Should().Be(0);
        decodedBank.Rows.Should().HaveCount(1);
        decodedBank.Rows[0].Field5InstanceUidCandidate.Should().Be(717171);

        string invReport = decodedInventory.Describe();
        invReport.Should().Be("container=inventory status=complete-candidate " +
            "rows=2 fields345=2 incomplete-rows=0 effects-present=1 duplicate-f5=0");
        string bankReport = decodedBank.Describe();
        bankReport.Should().Contain("container=bank status=complete-candidate rows=1");

        foreach (string privateValue in new[] { "818181", "919191", "717171",
            "2418", "2427", "1523", "500" })
        {
            invReport.Should().NotContain(privateValue);
            bankReport.Should().NotContain(privateValue);
        }
    }

    [Fact]
    public void NoSnapshotIsNotAnEmptySnapshot()
    {
        InventoryBankSnapshotDecoder.Decode("iqz", Snapshot("irl"))
            .Should().BeNull();
        InventoryBankSnapshotDecoder.Decode("irl", [])!
            .Status.Should().Be(StockSnapshotCandidateStatus.Malformed);

        InventoryBankSnapshotDecoder.Decode("irl", Snapshot("irl"))!
            .Status.Should().Be(StockSnapshotCandidateStatus.EmptyCandidate);
        InventoryBankSnapshotDecoder.Decode("irp", Snapshot("irp"))!
            .Status.Should().Be(StockSnapshotCandidateStatus.EmptyCandidate);

        InventoryBankSnapshotDecoder.Decode("irl", Unsigned(99, 10))!
            .Status.Should().Be(StockSnapshotCandidateStatus.Incomplete);
        InventoryBankSnapshotDecoder.Decode("irp", [0x00])!
            .Status.Should().Be(StockSnapshotCandidateStatus.Malformed);
    }

    [Fact]
    public void InvalidOrMissingCandidateFieldsCannotBeComplete()
    {
        byte[] missingUid = Bytes(2, Bytes(1, Message(
            Unsigned(3, 2), Unsigned(4, 2418))));
        byte[] duplicateUid = Snapshot("irl", Row(1, 2418, 888),
            Row(1, 2427, 888));
        byte[] wrongWireType = Bytes(2, Bytes(1, Message(
            Unsigned(3, 2), Bytes(4, [0x01]), Unsigned(5, 888))));
        byte[] repeatedField = Bytes(2, Bytes(1, Message(
            Unsigned(3, 1), Unsigned(3, 2),
            Unsigned(4, 2418), Unsigned(5, 888))));

        InventoryBankSnapshotDecoder.Decode("irl",
            Message(Unsigned(4, 1), missingUid))!
            .Status.Should().Be(StockSnapshotCandidateStatus.Incomplete);
        InventoryBankSnapshotDecoder.Decode("irl", duplicateUid)!
            .Status.Should().Be(StockSnapshotCandidateStatus.Incomplete);
        InventoryBankSnapshotDecoder.Describe("irl", duplicateUid)
            .Should().Contain("duplicate-f5=1");
        InventoryBankSnapshotDecoder.Decode("irl",
            Message(Unsigned(4, 1), wrongWireType))!
            .Status.Should().Be(StockSnapshotCandidateStatus.Incomplete);
        InventoryBankSnapshotDecoder.Decode("irp",
            Message(Unsigned(1, 1), repeatedField))!
            .Status.Should().Be(StockSnapshotCandidateStatus.Incomplete);
        InventoryBankSnapshotDecoder.Decode("irl",
            Snapshot("irl", Row(0, 2418, 888)))!
            .Status.Should().Be(StockSnapshotCandidateStatus.Incomplete);
    }

    [Fact]
    public void CorruptionAndResourceLimitsFailClosed()
    {
        InventoryBankSnapshotDecoder.Decode("irp", new byte[256 * 1024 + 1])!
            .Status.Should().Be(StockSnapshotCandidateStatus.Malformed);
        InventoryBankSnapshotDecoder.Decode("irp", Message(
            Unsigned(1, 1), Bytes(2, [0x00])))!
            .Status.Should().Be(StockSnapshotCandidateStatus.Malformed);

        byte[] oversized = Snapshot("irl",
            Enumerable.Range(0, 4097).Select(x =>
                Row(1, 2418, (ulong)x + 1)).ToArray());
        InventoryBankSnapshotDecoder.Decode("irl", oversized)!
            .Status.Should().Be(StockSnapshotCandidateStatus.Malformed);
    }

    [Fact]
    public void EffectChangesDoNotChangeInstanceIdentityCandidates()
    {
        byte[] older = Snapshot("irl", Row(1, 2427, 170001,
            Message(Unsigned(5, 5), Unsigned(10, 1003))));
        byte[] newer = Snapshot("irl", Row(1, 2427, 170001,
            Message(Unsigned(5, 6), Unsigned(10, 1003)),
            Message(Unsigned(5, 3), Unsigned(10, 1004))));

        StockSnapshotWireCandidate a =
            InventoryBankSnapshotDecoder.Decode("irl", older)!;
        StockSnapshotWireCandidate b =
            InventoryBankSnapshotDecoder.Decode("irl", newer)!;
        a.Rows[0].Field5InstanceUidCandidate.Should()
            .Be(b.Rows[0].Field5InstanceUidCandidate);
        a.Rows[0].Field4ItemIdCandidate.Should()
            .Be(b.Rows[0].Field4ItemIdCandidate);
        a.Rows[0].EffectFields.Should().Be(1);
        b.Rows[0].EffectFields.Should().Be(2);
    }

    [Fact]
    public void TcpTrackerOnlyReportsOptedInServerToClientSnapshots()
    {
        byte[] payload = Snapshot("irl", Row(1, 2418, 888444));
        byte[] frame = Frame(Any("irl", payload));
        CharacterCaptureFlowKey key =
            new("127.0.0.1", 40001, "10.0.0.10", 5555);
        DateTime now = new(2026, 10, 25, 1, 10, 0, DateTimeKind.Utc);

        CharacterCaptureFlowTracker tracker = new(
            stockSnapshotStudy: true);
        CharacterCaptureEvent incoming = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient,
            100, frame, now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);

        incoming.StockSnapshotSummary.Should().Contain(
            "container=inventory status=complete-candidate");
        incoming.ItemEffectsShape.Should().BeNull();
        string serialized = System.Text.Json.JsonSerializer.Serialize(incoming);
        serialized.Should().NotContain("888444");
        serialized.Should().NotContain("2418");

        CharacterCaptureFlowTracker off = new();
        CharacterCaptureEvent absent = off.Observe(
            key, CharacterCaptureDirection.ServerToClient,
            100, frame, now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        absent.StockSnapshotSummary.Should().BeNull();

        CharacterCaptureFlowTracker outbound = new(stockSnapshotStudy: true);
        CharacterCaptureEvent direction = outbound.Observe(
            key, CharacterCaptureDirection.ClientToServer,
            100, frame, now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        direction.StockSnapshotSummary.Should().BeNull();

        Action invalid = () => new CharacterCaptureFlowTracker(
            stockSnapshotStudy: true, itemEffectsShape: true);
        invalid.Should().Throw<ArgumentException>();
    }
}
