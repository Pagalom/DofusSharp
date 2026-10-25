using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class StockSnapshotDeltaStudyTest
{
    private static byte[] StockRow(ulong quantity, ulong itemId,
        ulong uid, params byte[][] effects) =>
        Bytes(2, Bytes(1, Message(
            Unsigned(3, quantity), Unsigned(4, itemId), Unsigned(5, uid),
            Message(effects.Select(x => Bytes(7, x)).ToArray()))));

    private static byte[] Snapshot(string opcode, params byte[][] rows) =>
        Message(Unsigned(opcode == "irl" ? 4 : 1, 1), Message(rows));

    private static byte[] Delta(string opcode, ulong quantity, ulong itemId,
        ulong uid, params byte[][] effects) =>
        Bytes(opcode == "iqx" ? 2 : 1,
            Bytes(1, Message(
                Unsigned(3, quantity), Unsigned(4, itemId),
                Unsigned(5, uid),
                Message(effects.Select(x => Bytes(7, x)).ToArray()))));

    [Fact]
    public void MatchesKnownBankCandidateWithoutExposingObservedIdentifiers()
    {
        StockSnapshotDeltaStudy study = new();
        string? start = study.Observe(1, "irp",
            Snapshot("irp", StockRow(1, 2418, 818181),
                StockRow(50, 1523, 919191)));
        start.Should().Contain("container=bank status=complete-candidate");
        start.Should().Contain("baseline=stored");

        string? delta = study.Observe(1, "iqx",
            Delta("iqx", 1, 2418, 818181));
        delta.Should().Be("delta=fields345-candidate " +
            "bank=[uid-candidate-found itemId-candidate=same quantity-candidate=same] " +
            "inventory=[baseline-unobserved]");

        string? quantityChanged = study.Observe(1, "iqz",
            Delta("iqz", 49, 1523, 919191));
        quantityChanged.Should().Contain(
            "bank=[uid-candidate-found itemId-candidate=same quantity-candidate=different]");

        foreach (string report in new[] { start!, delta!, quantityChanged! })
        foreach (string sensitive in new[] { "2418", "1523", "818181", "919191" })
            report.Should().NotContain(sensitive);
    }

    [Fact]
    public void SameUidCandidateAndDifferentItemIdMustNotPassAsMatch()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irp", Snapshot("irp", StockRow(1, 2418, 818181)));
        study.Observe(1, "iqx", Delta("iqx", 1, 2427, 818181))
            .Should().Contain(
                "bank=[uid-candidate-found itemId-candidate=different quantity-candidate=same]");
    }

    [Fact]
    public void InventoryAndBankCandidateBaselinesAreSeparate()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irl", Snapshot("irl", StockRow(2, 2418, 111)));
        study.Observe(1, "irp", Snapshot("irp", StockRow(3, 2427, 222)));

        study.Observe(1, "iqz", Delta("iqz", 2, 2418, 111))
            .Should().Contain(
                "bank=[uid-candidate-not-found] " +
                "inventory=[uid-candidate-found itemId-candidate=same quantity-candidate=same]");

        study.Observe(1, "iqx", Delta("iqx", 3, 2427, 222))
            .Should().Contain(
                "bank=[uid-candidate-found itemId-candidate=same quantity-candidate=same] " +
                "inventory=[uid-candidate-not-found]");
    }

    [Fact]
    public void SessionsAndReconnectionNeverShareCandidateBaselines()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irp", Snapshot("irp", StockRow(1, 2418, 123)));
        study.Observe(2, "iqx", Delta("iqx", 1, 2418, 123))
            .Should().Contain("bank=[baseline-unobserved]");

        study.DropSession(1);
        study.Observe(1, "iqx", Delta("iqx", 1, 2418, 123))
            .Should().Contain("bank=[baseline-unobserved]");
    }

    [Fact]
    public void IncompleteSnapshotRevokesAnEarlierCandidateBaseline()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irp", Snapshot("irp", StockRow(1, 2418, 123)));
        study.Observe(1, "irp", [0x00])
            .Should().Contain("status=malformed");
        study.Observe(1, "iqz", Delta("iqz", 1, 2418, 123))
            .Should().Contain("bank=[baseline-unobserved]");
    }

    [Fact]
    public void UnknownAndMalformedDeltasCannotBeMisreadAsTransfers()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irx", Delta("iqx", 1, 2418, 123))
            .Should().BeNull();
        study.Observe(1, "iqz", [0x00])
            .Should().Be("delta=unrecognized");
        study.Observe(1, "iqx", Delta("iqx", 0, 2418, 123))
            .Should().Be("delta=unrecognized");
        study.Observe(1, "iqx", Delta("iqx", 1, 2418, 123))
            .Should().Contain("baseline-unobserved");
    }

    [Fact]
    public void EffectsDoNotAffectIdentityCandidateComparison()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] changedEffect = Message(Unsigned(5, 26), Unsigned(10, 119));
        study.Observe(1, "irp", Snapshot("irp",
            StockRow(1, 2427, 777)));
        study.Observe(1, "iqx", Delta("iqx", 1, 2427, 777,
            changedEffect))
            .Should().Contain(
                "bank=[uid-candidate-found itemId-candidate=same quantity-candidate=same]");
    }

    [Fact]
    public void TcpTrackerPublishesOnlyAnonymousMatchingSummaryWhenEnabled()
    {
        byte[] bank = Frame(Any("irp",
            Snapshot("irp", StockRow(1, 2418, 881188))));
        byte[] transfer = Frame(Any("iqx", Delta("iqx", 1, 2418, 881188)));
        CharacterCaptureFlowKey key =
            new("127.0.0.1", 40001, "10.0.0.10", 5555);
        DateTime now = new(2026, 10, 25, 1, 40, 0, DateTimeKind.Utc);
        CharacterCaptureFlowTracker tracker = new(stockSnapshotStudy: true);

        tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            100, bank, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message)
            .StockSnapshotSummary.Should().Contain("baseline=stored");

        CharacterCaptureEvent delta = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + bank.Length), transfer, now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        delta.StockSnapshotSummary.Should().Contain(
            "uid-candidate-found itemId-candidate=same");
        string serialized = System.Text.Json.JsonSerializer.Serialize(delta);
        serialized.Should().NotContain("2418");
        serialized.Should().NotContain("881188");
    }
}
