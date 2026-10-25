using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class StockBlockedItemIdentificationTest
{
    private static byte[] Row(ulong itemId, ulong uid, byte[] effect) =>
        Bytes(2, Bytes(1, Message(
            Unsigned(3, 1),
            Unsigned(4, itemId),
            Unsigned(5, uid),
            Bytes(7, effect))));

    private static byte[] Bank(params byte[][] rows) =>
        Message(Unsigned(1, 1), Message(rows));

    private static byte[] MissingValue() =>
        Message(Unsigned(10, 981122), Unsigned(2, 229999));

    [Fact]
    public void TypeIdsAreOnlyEmittedWithExplicitOptIn()
    {
        byte[] payload = Bank(
            Row(842612, 881199, MissingValue()),
            Row(842612, 881200, MissingValue()),
            Row(842613, 881201, MissingValue()));

        string usual = new StockSnapshotDeltaStudy()
            .Observe(1, "irp", payload)!;
        usual.Should().Contain("undecodable-effects:3");
        usual.Should().NotContain("blocked-item-types");
        usual.Should().NotContain("842612");
        usual.Should().NotContain("842613");

        string identified = new StockSnapshotDeltaStudy(true)
            .Observe(1, "irp", payload)!;
        identified.Should().Contain(
            "blocked-item-types=[item-id-candidate:842612,affected-rows:2|" +
            "item-id-candidate:842613,affected-rows:1] distinct-types=2");
        identified.Should().Contain(
            "blocked-type-shapes=[item-id-candidate:842612," +
            "first-missing-effect-shape:f2:v;f10:v|" +
            "item-id-candidate:842613," +
            "first-missing-effect-shape:f2:v;f10:v]");
        foreach (string sensitive in new[]
        {
            "881199", "881200", "881201", "981122", "229999"
        })
            identified.Should().NotContain(sensitive);
    }

    [Fact]
    public void OnlyUnrecognizedEffectsAreIdentifiedNotNormalInventory()
    {
        byte[] decoded = Message(Unsigned(10, 981122), Unsigned(5, 24));
        byte[] payload = Bank(
            Row(842612, 881199, MissingValue()),
            Row(842614, 881200, decoded),
            Row(842615, 881201, MissingValue()));
        string report = new StockSnapshotDeltaStudy(true)
            .Observe(1, "irp", payload)!;

        report.Should().Contain("item-id-candidate:842612");
        report.Should().Contain("item-id-candidate:842615");
        report.Should().NotContain("item-id-candidate:842614");
        report.Should().Contain("blocked-type-shapes=");
        report.Should().Contain("distinct-types=2");
    }

    [Fact]
    public void ReportIsCappedForLargeDistinctTypeLists()
    {
        byte[][] rows = Enumerable.Range(0, 50)
            .Select(i => Row((ulong)(842612 + i),
                (ulong)(881199 + i), MissingValue()))
            .ToArray();
        string report = new StockSnapshotDeltaStudy(true)
            .Observe(1, "irp", Bank(rows))!;

        report.Should().Contain("distinct-types=50");
        report.Should().Contain("additional-types-hidden=34");
        report.Should().Contain("item-id-candidate:842612");
        report.Should().NotContain("item-id-candidate:842628");
        report.Should().NotContain("881199");
        report.Length.Should().BeLessThan(3600,
            "the opt-in report is bounded to 16 item types and wire shapes");
    }

    [Fact]
    public void IdentificationRequiresStockSnapshotOptIn()
    {
        Action invalid = () =>
            new CharacterCaptureFlowTracker(stockIdentifyBlockers: true);
        invalid.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackerStaysAnonymousByDefaultAndIdentifiesOnlyWhenExplicit()
    {
        byte[] payload = Bank(Row(842612, 881199, MissingValue()));
        byte[] frame = Frame(Any("irp", payload));
        CharacterCaptureFlowKey key =
            new("127.0.0.1", 40001, "10.0.0.10", 5555);
        DateTime now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        CharacterCaptureFlowTracker anonymous = new(
            stockSnapshotStudy: true);
        CharacterCaptureFlowTracker identified = new(
            stockSnapshotStudy: true, stockIdentifyBlockers: true);

        CharacterCaptureEvent normal = anonymous.Observe(
            key, CharacterCaptureDirection.ServerToClient, 100, frame, now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        CharacterCaptureEvent opted = identified.Observe(
            key, CharacterCaptureDirection.ServerToClient, 100, frame, now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);

        normal.StockSnapshotSummary.Should().NotContain("842612");
        opted.StockSnapshotSummary.Should().Contain(
            "item-id-candidate:842612");
        opted.StockSnapshotSummary.Should().NotContain("881199");
        string serialized = System.Text.Json.JsonSerializer.Serialize(opted);
        serialized.Should().NotContain("881199");
        serialized.Should().NotContain("981122");
    }
}
