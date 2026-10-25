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
            "bank=[uid-candidate-found itemId-candidate=same quantity-candidate=same exact-effects=not-applicable] " +
            "inventory=[baseline-unobserved] previous-delta=[none]");

        string? quantityChanged = study.Observe(1, "iqz",
            Delta("iqz", 49, 1523, 919191));
        quantityChanged.Should().Contain(
            "bank=[uid-candidate-found itemId-candidate=same quantity-candidate=different exact-effects=not-applicable]");

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
                "bank=[uid-candidate-found itemId-candidate=different quantity-candidate=same exact-effects=not-applicable]");
    }

    [Fact]
    public void InventoryAndBankCandidateBaselinesAreSeparate()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irl", Snapshot("irl", StockRow(2, 2418, 111)));
        study.Observe(1, "irp", Snapshot("irp", StockRow(3, 2427, 222)));

        string? fromInventory = study.Observe(1, "iqz",
            Delta("iqz", 2, 2418, 111));
        fromInventory.Should().Contain(
            "bank=[uid-candidate-not-found same-type-rows=0 " +
            "same-type-quantity-rows=0 same-type-effects-count-rows=0");
        fromInventory.Should().Contain(
            "inventory=[uid-candidate-found itemId-candidate=same quantity-candidate=same exact-effects=not-applicable]");

        string? fromBank = study.Observe(1, "iqx",
            Delta("iqx", 3, 2427, 222));
        fromBank.Should().Contain(
            "bank=[uid-candidate-found itemId-candidate=same quantity-candidate=same exact-effects=not-applicable]");
        fromBank.Should().Contain(
            "inventory=[uid-candidate-not-found same-type-rows=0 " +
            "same-type-quantity-rows=0 same-type-effects-count-rows=0");
        fromBank.Should().Contain("previous-delta=[opcode=iqz");
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
                "bank=[uid-candidate-found itemId-candidate=same quantity-candidate=same exact-effects=not-applicable]");
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

    [Fact]
    public void MissingUidInBankStillReportsAnonymousSameTypeCandidates()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] oneEffect = Message(Unsigned(5, 26), Unsigned(10, 119));
        study.Observe(1, "irp", Snapshot("irp",
            StockRow(1, 2427, 10001, oneEffect),
            StockRow(2, 2427, 10002, oneEffect),
            StockRow(1, 2427, 10003),
            StockRow(1, 2418, 10004)));

        string? report = study.Observe(1, "iqx",
            Delta("iqx", 1, 2427, 10005, oneEffect));
        report.Should().Contain(
            "bank=[uid-candidate-not-found same-type-rows=3 " +
            "same-type-quantity-rows=2 same-type-effects-count-rows=2");
        report.Should().Contain("previous-delta=[none]");
        foreach (string sensitive in new[] { "2427", "2418", "10001", "10005" })
            report.Should().NotContain(sensitive);
    }

    [Fact]
    public void ConsecutiveDeltasAreComparedWithoutTreatingThemAsOneTransfer()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "irp", Snapshot("irp",
            StockRow(1, 2427, 10001)));

        study.Observe(1, "iqx", Delta("iqx", 1, 2427, 10002))
            .Should().Contain("previous-delta=[none]");

        study.Observe(1, "iqz", Delta("iqz", 1, 2427, 10002))
            .Should().Contain(
                "previous-delta=[opcode=iqx f3=same f4=same f5=same " +
                "effects-count=same exact-effects=not-applicable]");

        study.Observe(1, "iqx", Delta("iqx", 2, 2427, 10003,
                Message(Unsigned(5, 20), Unsigned(10, 119))))
            .Should().Contain(
                "previous-delta=[opcode=iqz f3=different f4=same " +
                "f5=different effects-count=different exact-effects=not-applicable]");
    }

    [Fact]
    public void NewSnapshotAndClosedSessionResetDeltaCorrelation()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "iqx", Delta("iqx", 1, 2418, 10001));
        study.Observe(1, "irp", Snapshot("irp",
            StockRow(1, 2418, 10001)));
        study.Observe(1, "iqz", Delta("iqz", 1, 2418, 10001))
            .Should().Contain("previous-delta=[none]");

        study.DropSession(1);
        study.Observe(1, "iqx", Delta("iqx", 1, 2418, 10001))
            .Should().Contain("previous-delta=[none]");
    }

    [Fact]
    public void ExactEffectsLinkOneOfSameTypeCandidatesWithoutMatchingUid()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] vitality = Message(Unsigned(5, 99), Unsigned(10, 1001));
        byte[] intelligence = Message(Unsigned(5, 20), Unsigned(10, 1002));
        byte[] anotherVitality = Message(Unsigned(5, 95), Unsigned(10, 1001));

        study.Observe(1, "irp", Snapshot("irp",
            StockRow(1, 2418, 10001, vitality, intelligence),
            StockRow(1, 2418, 10002, anotherVitality, intelligence)));

        // An instance can have an f5 that differs between container and delta;
        // full effect equality is only corroboration, never an identity proof.
        string? report = study.Observe(1, "iqx",
            Delta("iqx", 1, 2418, 99999, intelligence, vitality));
        report.Should().Contain("uid-candidate-not-found same-type-rows=2");
        report.Should().Contain("same-type-quantity-rows=2");
        report.Should().Contain("same-type-effects-count-rows=2");
        report.Should().Contain("exact-effects-status=decoded " +
            "same-type-exact-effects-rows=1 " +
            "same-type-quantity-exact-effects-rows=1");
        foreach (string sensitive in new[] { "2418", "10001", "99999",
            "1001", "1002", "95", "99" })
            report.Should().NotContain(sensitive);
    }

    [Fact]
    public void ConsecutiveDeltasCanHaveDifferentUidButIdenticalExactEffects()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] firstEffect = Message(Unsigned(5, 26), Unsigned(10, 1002));
        byte[] secondEffect = Message(Unsigned(5, 24), Unsigned(10, 1001));

        study.Observe(1, "iqx", Delta("iqx", 1, 2427, 111,
            firstEffect, secondEffect));
        study.Observe(1, "iqz", Delta("iqz", 1, 2427, 222,
            secondEffect, firstEffect))
            .Should().Contain("f3=same f4=same f5=different " +
                "effects-count=same exact-effects=same");

        byte[] changed = Message(Unsigned(5, 25), Unsigned(10, 1001));
        study.Observe(1, "iqx", Delta("iqx", 1, 2427, 333,
            firstEffect, changed))
            .Should().Contain("f3=same f4=same f5=different " +
                "effects-count=same exact-effects=different");
    }

    [Fact]
    public void NegativeEffectsAndDuplicateTypesAreComparedAsMultisets()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] negative = Message(
            Unsigned(5, unchecked((ulong)(long)-2)), Unsigned(10, 1900));
        byte[] positive = Message(Unsigned(5, 5), Unsigned(10, 1900));
        study.Observe(1, "irp", Snapshot("irp",
            StockRow(1, 2418, 111, negative, positive)));
        study.Observe(1, "iqx", Delta("iqx", 1, 2418, 222,
            positive, negative))
            .Should().Contain("same-type-exact-effects-rows=1");
        study.Observe(1, "iqz", Delta("iqz", 1, 2418, 333,
            positive, negative, positive))
            .Should().Contain("previous-delta=[opcode=iqx " +
                "f3=same f4=same f5=different " +
                "effects-count=different exact-effects=different]");
    }

    [Fact]
    public void AbsentAndUnrecognizedEffectsNeverCountAsExactMatches()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] legitimate = Message(Unsigned(5, 24), Unsigned(10, 1001));
        byte[] malformed = Message(Unsigned(5, 24)); // no effect-type code
        study.Observe(1, "irp", Snapshot("irp",
            StockRow(1, 2418, 123, legitimate)));

        study.Observe(1, "iqx", Delta("iqx", 1, 2418, 456))
            .Should().Contain(
                "exact-effects-status=no-effects same-type-exact-effects-rows=0");

        study.Observe(1, "iqz", Delta("iqz", 1, 2418, 789, malformed))
            .Should().Contain(
                "exact-effects-status=unrecognized same-type-exact-effects-rows=0");

        study.Observe(1, "iqx", Delta("iqx", 1, 2418, 987, legitimate))
            .Should().Contain(
                "exact-effects-status=decoded same-type-exact-effects-rows=1");

        // An unreadable effect line can never be treated as equal.
        study.Observe(1, "iqz", Delta("iqz", 1, 2418, 1000, malformed))
            .Should().Contain("effects-count=same exact-effects=unrecognized");
    }

    [Fact]
    public void SameUidAndDifferentRealEffectsAreReportedSeparately()
    {
        StockSnapshotDeltaStudy study = new();
        byte[] before = Message(Unsigned(5, 24), Unsigned(10, 1001));
        byte[] after = Message(Unsigned(5, 25), Unsigned(10, 1001));
        study.Observe(1, "irp", Snapshot("irp",
            StockRow(1, 2427, 777, before)));
        study.Observe(1, "iqx", Delta("iqx", 1, 2427, 777, after))
            .Should().Contain(
                "uid-candidate-found itemId-candidate=same " +
                "quantity-candidate=same exact-effects=different");
    }
}
