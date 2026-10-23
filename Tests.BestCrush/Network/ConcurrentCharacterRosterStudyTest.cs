using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3c.6: only synthetic messages. Neither roster IDs nor TCP sessions
/// are accepted as authenticated accounts, characters or game servers.
/// </summary>
public sealed class ConcurrentCharacterRosterStudyTest
{
    private const ulong A = 9000101;
    private const ulong B = 9000201;

    private static byte[] Row(ulong id, string name, int level) =>
        Message(Unsigned(3, id),
            Bytes(4, Message(Unsigned(3, (ulong)level),
                Bytes(6, Encoding.UTF8.GetBytes(name)))));

    private static byte[] Ksc(bool a) => a
        ? Message(Bytes(2, Row(A, "Sable-Ciel", 125)),
            Bytes(2, Row(9000102, "Roche-Lune", 2)))
        : Message(Bytes(2, Row(B, "Hiver-Rouge", 1)));

    private static byte[] Ksb(bool a) =>
        Bytes(1, Bytes(1, a
            ? Row(A, "Sable-Ciel", 125)
            : Row(B, "Hiver-Rouge", 1)));

    private static string Observe(
        ConcurrentCharacterRosterStudy study, int session,
        string opcode, byte[] body,
        CharacterCaptureDirection direction =
            CharacterCaptureDirection.ServerToClient) =>
        study.Observe(session, direction, opcode, body)!;

    private static void Connect(
        ConcurrentCharacterRosterStudy study, int session, bool a)
    {
        study.ObserveOpenedSession(session);
        Observe(study, session, "ksc", Ksc(a))
            .Should().Contain("status=pending");
        Observe(study, session, "ksb", Ksb(a))
            .Should().Contain("status=linked");
    }

    private static ConcurrentCharacterRosterStudy Complete()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0).Should().BeTrue();
        Connect(study, 2, a: true);
        study.StartStage(1).Should().BeTrue();
        Connect(study, 4, a: false);
        study.ObserveClosedSession(4);
        study.StartStage(2).Should().BeTrue();
        Connect(study, 6, a: false);
        return study;
    }

    [Fact]
    public void TwoSimultaneousClientsAndBReconnectYieldOnlyRosterCandidates()
    {
        string result = Complete().Summarize().Last();
        result.Should().Contain("status=concurrent-roster-group-candidate");
        result.Should().Contain("B-reconnect-roster-stable=yes");
        result.Should().Contain("A-B-disjoint=yes");
        result.Should().Contain("A-B-distinct=yes");
        result.Should().Contain("both-overlap-A=yes");
        result.Should().Contain(
            "account=unverified server=unverified semantic=unverified");
    }

    [Fact]
    public void OutputContainsNoNamesCandidateIdsOrTcpEndpoints()
    {
        string report = string.Join("\n", Complete().Summarize());
        report.Should().NotContain("Sable-Ciel");
        report.Should().NotContain("Hiver-Rouge");
        report.Should().NotContain("9000101");
        report.Should().NotContain("9000201");
        report.Should().NotContain("127.0.0.1");
        report.Should().NotContain("hash");
    }

    [Fact]
    public void BothAccountRostersAreKeptSeparateInLivePhases()
    {
        var study = Complete();
        study.Summarize()[0].Should().Contain("roster-rows=2");
        study.Summarize()[1].Should().Contain("roster-rows=1");
        study.Summarize()[2].Should().Contain("roster-rows=1");
        study.Summarize()[0].Should().Contain("game-closed=no");
        study.Summarize()[1].Should().Contain("game-closed=yes");
        study.Summarize()[2].Should().Contain("game-closed=no");
    }

    [Fact]
    public void CannotStartBWithoutLinkedActiveA()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(1).Should().BeFalse();
        study.StartStage(0).Should().BeTrue();
        study.ObserveOpenedSession(2);
        Observe(study, 2, "ksc", Ksc(true)).Should().Contain("pending");
        study.StartStage(1).Should().BeFalse();
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void CannotStartBReconnectUntilFirstBHasClosed()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0).Should().BeTrue();
        Connect(study, 2, true);
        study.StartStage(1).Should().BeTrue();
        Connect(study, 4, false);
        study.StartStage(2).Should().BeFalse();
        study.ObserveClosedSession(4);
        study.StartStage(2).Should().BeTrue();
    }

    [Fact]
    public void ClosingABeforeBReconnectFailsClosed()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0);
        Connect(study, 2, true);
        study.StartStage(1);
        Connect(study, 4, false);
        study.ObserveClosedSession(4);
        study.ObserveClosedSession(2);
        study.StartStage(2).Should().BeFalse();
        study.Summarize().Last().Should().Contain("primary-closed-early=yes");
    }

    [Fact]
    public void ClosingAAfterSecondBHasLinkedDoesNotInvalidateObservedOverlap()
    {
        var study = Complete();
        study.ObserveClosedSession(2);
        study.ObserveClosedSession(6);
        study.Summarize().Last()
            .Should().Contain("status=concurrent-roster-group-candidate");
    }

    [Fact]
    public void StaleBMessagesAfterClosureCannotReplaceFirstRoster()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0);
        Connect(study, 2, true);
        study.StartStage(1);
        Connect(study, 4, false);
        study.ObserveClosedSession(4);
        Observe(study, 4, "ksc", Ksc(true))
            .Should().Contain("status=closed");
        study.StartStage(2).Should().BeTrue();
        Connect(study, 6, false);
        study.Summarize().Last()
            .Should().Contain("status=concurrent-roster-group-candidate");
    }

    [Fact]
    public void OldFlowOpenedInPreviousStageCannotBeReassignedToB()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0);
        Connect(study, 2, true);
        study.StartStage(1);
        study.ObserveOpenedSession(2);
        Observe(study, 2, "ksc", Ksc(false))
            .Should().Contain("status=conflicted");
        study.Summarize()[0].Should().Contain("reason=roster-changed");
    }

    [Fact]
    public void AnotherGameSessionInOneStageFailsClosed()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0);
        Connect(study, 2, true);
        study.ObserveOpenedSession(3);
        Observe(study, 3, "ksc", Ksc(true))
            .Should().Contain("status=conflicted");
        study.Summarize()[0]
            .Should().Contain("reason=multiple-game-sessions");
    }

    [Fact]
    public void WrongSelectedCharacterNotPresentInRosterFailsClosed()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0);
        study.ObserveOpenedSession(2);
        Observe(study, 2, "ksc", Ksc(true))
            .Should().Contain("status=pending");
        Observe(study, 2, "ksb", Ksb(false))
            .Should().Contain("status=conflicted");
        study.Summarize()[0].Should().Contain("selection-not-in-roster");
    }

    [Fact]
    public void MalformedRosterCannotBeReplacedByOtherGameSession()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0);
        study.ObserveOpenedSession(2);
        Observe(study, 2, "ksc", [0x80])
            .Should().Contain("status=conflicted");
        study.ObserveOpenedSession(3);
        Observe(study, 3, "ksc", Ksc(true))
            .Should().Contain("status=conflicted");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void ClientToServerPacketsAndUnrelatedOpcodesAreIgnored()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0);
        study.ObserveOpenedSession(2);
        study.Observe(2, CharacterCaptureDirection.ClientToServer,
            "ksc", Ksc(true)).Should().BeNull();
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "knc", Ksc(true)).Should().BeNull();
        study.Summarize()[0].Should().Contain("status=incomplete");
    }

    [Fact]
    public void MixedCaptureDiagnosticsAreRejectedBeforeCapture()
    {
        var study = new ConcurrentCharacterRosterStudy();
        Action withPrevious = () => new CharacterCaptureFlowTracker(
            rosterStudy: new CharacterRosterContinuityStudy(),
            concurrentRosterStudy: study);
        withPrevious.Should().Throw<ArgumentException>();

        Action withProfession = () => new CharacterCaptureFlowTracker(
            studyProfessionContinuity: true, concurrentRosterStudy: study);
        withProfession.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ActualTrackerRelaysOpensAndClosesWithDistinctSessions()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0).Should().BeTrue();
        CharacterCaptureFlowTracker tracker = new(
            concurrentRosterStudy: study);

        var first = new CharacterCaptureFlowKey(
            "127.0.0.1", 40100, "10.0.0.1", 5555);
        DateTime time = new(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc);
        byte[] ksc = Frame(Any("ksc", Ksc(true)));
        byte[] ksb = Frame(Any("ksb", Ksb(true)));

        var select = tracker.Observe(first,
            CharacterCaptureDirection.ServerToClient, 100,
            ksc, time).Single(e => e.Kind == CharacterCaptureEventKind.Message);
        var loaded = tracker.Observe(first,
            CharacterCaptureDirection.ServerToClient,
            (uint)(100 + ksc.Length), ksb, time.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        select.ConcurrentRosterSummary.Should().Contain("status=pending");
        loaded.ConcurrentRosterSummary.Should().Contain("status=linked");
        tracker.Observe(first, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + ksc.Length + ksb.Length), [], time.AddSeconds(2),
            fin: true).Should().Contain(e => e.Kind == CharacterCaptureEventKind.Closed);
        study.StartStage(1).Should().BeFalse(); // A closed; overlap impossible.
    }

    [Fact]
    public void BReconnectMustRepeatSameSelectedCandidateAsWellAsRoster()
    {
        var study = new ConcurrentCharacterRosterStudy();
        study.StartStage(0);
        Connect(study, 2, true);
        study.StartStage(1);
        Connect(study, 4, false);
        study.ObserveClosedSession(4);
        study.StartStage(2);
        study.ObserveOpenedSession(6);
        Observe(study, 6, "ksc", Ksc(false))
            .Should().Contain("pending");

        // A different B candidate list row, but not the selected Z,
        // must fail the reconnect control.
        byte[] otherRoster = Message(
            Bytes(2, Row(B, "Hiver-Rouge", 1)),
            Bytes(2, Row(9000202, "Hiver-Neige", 2)));
        // Changing roster in-session is forbidden, irrespective of selection.
        Observe(study, 6, "ksc", otherRoster)
            .Should().Contain("conflicted");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }
}
