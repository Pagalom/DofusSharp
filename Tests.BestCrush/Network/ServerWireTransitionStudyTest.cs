using BestCrush.Network.Capture;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>All data are synthetic. No real server/account values.</summary>
public sealed class ServerWireTransitionStudyTest
{
    private static readonly DateTime T0 =
        new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
    private static CharacterCaptureEvent E(int session,
        CharacterCaptureEventKind kind, string? opcode = null) =>
        new(T0, session, CharacterCaptureDirection.ServerToClient, kind, opcode);

    private static void Phase(ServerWireTransitionStudy study,
        int stage, int session, ulong candidate)
    {
        study.StartStage(stage).Should().BeTrue();
        study.ObserveEvents([E(session, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(session, CharacterCaptureDirection.ClientToServer,
            "sel", Message(Unsigned(3, candidate)));
        study.ObserveMessage(session, CharacterCaptureDirection.ServerToClient,
            "inf", Message(Bytes(2, Message(Unsigned(1, candidate)))));
        study.ObserveMessage(session, CharacterCaptureDirection.ServerToClient,
            "ksc", Message(Unsigned(3, 111)));
        study.ObserveMessage(session, CharacterCaptureDirection.ServerToClient,
            "ksb", Message(Unsigned(3, 111)));
        study.ObserveEvents([E(session, CharacterCaptureEventKind.Closed)]);
    }

    [Fact]
    public void AbAStableNumericFieldIsReportedOnlyAsAnUnverifiedPath()
    {
        ServerWireTransitionStudy study = new();
        Phase(study, 0, 2, 12045);
        Phase(study, 1, 4, 67890);
        Phase(study, 2, 6, 12045);

        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("p3m6=summary comparable=yes candidate-count=2")
            .And.Contain("C-S:sel:f3")
            .And.Contain("S-C:inf:f2.f1")
            .And.Contain("semantic=unverified server=unverified")
            .And.NotContain("12045")
            .And.NotContain("67890");
    }

    [Fact]
    public void SharedValueAcrossAllStagesIsNotDiscriminating()
    {
        ServerWireTransitionStudy study = new();
        Phase(study, 0, 2, 42);
        Phase(study, 1, 4, 42);
        Phase(study, 2, 6, 42);
        study.Summarize().Last().Should()
            .Contain("candidate-count=0 candidate-paths=[none]");
    }

    [Fact]
    public void ReturnToAWithDifferentValueDoesNotProduceFalseCandidate()
    {
        ServerWireTransitionStudy study = new();
        Phase(study, 0, 2, 10);
        Phase(study, 1, 4, 99);
        Phase(study, 2, 6, 20);
        study.Summarize().Last().Should().Contain("candidate-count=0");
    }

    [Fact]
    public void IncompleteStageFailsClosed()
    {
        ServerWireTransitionStudy study = new();
        Phase(study, 0, 2, 10);
        study.StartStage(1);
        study.ObserveEvents([E(4, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(4, CharacterCaptureDirection.ClientToServer,
            "sel", Message(Unsigned(3, 20)));
        study.ObserveMessage(4, CharacterCaptureDirection.ServerToClient,
            "ksc", Message(Unsigned(3, 20)));
        study.ObserveEvents([E(4, CharacterCaptureEventKind.Closed)]);
        Phase(study, 2, 6, 10);

        study.Summarize().Last().Should()
            .Contain("comparable=no candidate-paths=[none]")
            .And.Contain("reason=incomplete-or-ambiguous-stage");
    }

    [Fact]
    public void KnownCharacterAndProfessionOpcodesAreNotNumericCandidates()
    {
        ServerWireTransitionStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Message(Unsigned(3, 111)));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ipz", Message(Unsigned(5, 222)));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Message(Unsigned(3, 333)));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "sel", Message(Unsigned(3, 444))); // after ksb ignored
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Closed)]);
        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("early-messages=0 numeric-paths=0")
            .And.NotContain("ksc:f")
            .And.NotContain("ksb:f")
            .And.NotContain("ipz:f");
    }

    [Fact]
    public void ConflictingValuesInSameStageAreExcluded()
    {
        ServerWireTransitionStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "sel", Message(Unsigned(3, 120)));
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "sel", Message(Unsigned(3, 999)));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", []);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", []);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 340);
        Phase(study, 2, 6, 120);

        study.Summarize().Last().Should().NotContain("C-S:sel:f3");
    }

    [Fact]
    public void OversizedWireIsSkippedAndExplicitlyMarkedAsPartialCoverage()
    {
        ServerWireTransitionStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "sel", new byte[8193]);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", []);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", []);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 340);
        Phase(study, 2, 6, 120);
        study.Summarize().Last().Should().Contain("comparable=yes")
            .And.Contain("coverage=partial")
            .And.Contain("semantic=unverified");
    }

    [Fact]
    public void SessionStagesNeverChangeWhenLaterEventsArrive()
    {
        ServerWireTransitionStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Opened)]);
        study.StartStage(1);
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "sel", Message(Unsigned(3, 123)));
        study.ObserveEvents([E(4, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(4, CharacterCaptureDirection.ClientToServer,
            "sel", Message(Unsigned(3, 456)));
        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("p3m6=stage-A1 tcp-sessions=1")
            .And.Contain("p3m6=stage-B tcp-sessions=1");
    }

    [Fact]
    public void EvictedOrMultipleCompletedGameSessionsMakeStageUnusable()
    {
        ServerWireTransitionStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient, "ksc", []);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient, "ksb", []);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Evicted)]);
        Phase(study, 1, 4, 500);
        Phase(study, 2, 6, 100);
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    [Fact]
    public void SourceDirectionIsPartOfPathKey()
    {
        ServerWireTransitionStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "sel", Message(Unsigned(3, 50)));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "sel", Message(Unsigned(3, 70)));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient, "ksc", []);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient, "ksb", []);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 80);
        Phase(study, 2, 6, 50);
        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("C-S:sel:f3")
            .And.NotContain("S-C:sel:f3");
    }

    [Fact]
    public void SharedTcpTrackerFeedsResearchCollectorWithoutChangingNormalMode()
    {
        ServerWireTransitionStudy study = new();
        study.StartStage(0);
        CharacterCaptureFlowTracker tracker = new(serverWireStudy: study);
        CharacterCaptureFlowKey key = new("192.0.2.1", 45678,
            "198.51.100.2", 5555);
        byte[] a = Frame(Any("sel", Message(Unsigned(3, 456))));
        byte[] b = Frame(Any("ksc", []));
        byte[] c = Frame(Any("ksb", []));
        uint seq = 100;

        foreach (byte[] frame in new[] { a, b, c })
        {
            var ev = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
                seq, frame, T0);
            study.ObserveEvents(ev);
            seq += (uint)frame.Length;
        }
        study.ObserveEvents(tracker.Observe(key,
            CharacterCaptureDirection.ClientToServer,
            200, [], T0.AddSeconds(1), fin: true));

        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("stage-A1 tcp-sessions=1")
            .And.Contain("early-messages=1 numeric-paths=1");
    }

    [Fact]
    public void SessionCapacityOverflowIsFailClosed()
    {
        ServerWireTransitionStudy study = new();
        study.StartStage(0);
        for (int i = 1; i <= 25; i++)
            study.ObserveEvents([E(i, CharacterCaptureEventKind.Opened)]);
        study.StartStage(1);
        study.StartStage(2);
        study.Summarize().Last().Should().Contain("comparable=no")
            .And.Contain("reason=session-capacity-exceeded");
    }

    [Fact]
    public void CannotCombineWithOtherTrackerStudy()
    {
        ServerWireTransitionStudy study = new();
        Action action = () => new CharacterCaptureFlowTracker(
            serverWireStudy: study, studyProfessionFlow: true);
        action.Should().Throw<ArgumentException>();
    }
}
