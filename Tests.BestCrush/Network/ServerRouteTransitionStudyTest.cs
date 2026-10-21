using BestCrush.Network.Capture;
using FluentAssertions;

namespace Tests.BestCrush.Network;

/// <summary>All endpoints are TEST-NET synthetic IPv4 addresses.</summary>
public sealed class ServerRouteTransitionStudyTest
{
    private static readonly DateTime T0 =
        new(2026, 10, 21, 20, 0, 0, DateTimeKind.Utc);

    private static CharacterCaptureFlowKey Route(string remote, int localPort) =>
        new("192.0.2.2", localPort, remote, 5555);

    private static CharacterCaptureEvent Ev(int session,
        CharacterCaptureEventKind kind, string? opcode = null) =>
        new(T0, session, CharacterCaptureDirection.ServerToClient, kind, opcode);

    private static void Complete(ServerRouteTransitionStudy study,
        string remote, int session, bool close = true)
    {
        CharacterCaptureFlowKey route = Route(remote, 49000 + session);
        study.Observe(route, [
            Ev(session, CharacterCaptureEventKind.Opened),
            Ev(session, CharacterCaptureEventKind.Message, "ksc"),
            Ev(session, CharacterCaptureEventKind.Message, "ksb")
        ]);
        if (close)
            study.Observe(route, [Ev(session, CharacterCaptureEventKind.Closed)]);
    }

    [Fact]
    public void AThenBThenAWithDistinctRoutesIsOnlyAHeuristic()
    {
        ServerRouteTransitionStudy study = new();
        study.StartStage(0).Should().BeTrue();
        Complete(study, "198.51.100.12", 2);
        study.StartStage(1).Should().BeTrue();
        Complete(study, "198.51.100.34", 4);
        study.StartStage(2).Should().BeTrue();
        Complete(study, "198.51.100.12", 6);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("destination-pattern=A-B-A")
            .And.Contain("A1-vs-A2=equal")
            .And.Contain("A1-vs-B=different")
            .And.Contain("meaning=route-only")
            .And.Contain("server=unverified");
        report.Should().NotContain("198.51.100");
        report.Should().NotContain("49002");
    }

    [Fact]
    public void OneSharedGatewayCannotBeMistakenForAConfirmedServer()
    {
        ServerRouteTransitionStudy study = new();
        study.StartStage(0);
        Complete(study, "198.51.100.12", 2);
        study.StartStage(1);
        Complete(study, "198.51.100.12", 4);
        study.StartStage(2);
        Complete(study, "198.51.100.12", 6);
        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("destination-pattern=same-all")
            .And.Contain("server=unverified");
    }

    [Fact]
    public void MissingKsbIsIncompleteAndMustNotDetermineARoute()
    {
        ServerRouteTransitionStudy study = new();
        study.StartStage(0);
        var r = Route("198.51.100.12", 48000);
        study.Observe(r, [
            Ev(2, CharacterCaptureEventKind.Opened),
            Ev(2, CharacterCaptureEventKind.Message, "ksc"),
            Ev(2, CharacterCaptureEventKind.Closed)
        ]);
        study.StartStage(1);
        Complete(study, "198.51.100.34", 4);
        study.StartStage(2);
        Complete(study, "198.51.100.12", 6);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("p3m5=stage-A1 tcp-sessions=1 ksc-ksb-sessions=0")
            .And.Contain("destination-pattern=unavailable");
    }

    [Fact]
    public void ClientNotClosedIsNotComparable()
    {
        ServerRouteTransitionStudy study = new();
        study.StartStage(0);
        Complete(study, "198.51.100.12", 2, close: false);
        study.StartStage(1);
        Complete(study, "198.51.100.34", 4);
        study.StartStage(2);
        Complete(study, "198.51.100.12", 6);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("p3m5=stage-A1 tcp-sessions=1 ksc-ksb-sessions=1 closed-complete=0 comparable=no")
            .And.Contain("destination-pattern=unavailable");
    }

    [Fact]
    public void MultipleCompleteGameSessionsInStageAreAmbiguous()
    {
        ServerRouteTransitionStudy study = new();
        study.StartStage(0);
        Complete(study, "198.51.100.12", 2);
        Complete(study, "198.51.100.13", 3);
        study.StartStage(1);
        Complete(study, "198.51.100.34", 4);
        study.StartStage(2);
        Complete(study, "198.51.100.12", 6);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("p3m5=stage-A1 tcp-sessions=2 ksc-ksb-sessions=2")
            .And.Contain("destination-pattern=unavailable");
    }

    [Fact]
    public void LatePreviousStageCloseDoesNotReassignOldSessionToNextStage()
    {
        ServerRouteTransitionStudy study = new();
        study.StartStage(0);
        var a = Route("198.51.100.12", 48000);
        study.Observe(a, [
            Ev(2, CharacterCaptureEventKind.Opened),
            Ev(2, CharacterCaptureEventKind.Message, "ksc"),
            Ev(2, CharacterCaptureEventKind.Message, "ksb")
        ]);
        study.StartStage(1);
        study.Observe(a, [Ev(2, CharacterCaptureEventKind.Closed)]);
        Complete(study, "198.51.100.34", 4);
        study.StartStage(2);
        Complete(study, "198.51.100.12", 6);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("p3m5=stage-A1 tcp-sessions=1")
            .And.Contain("p3m5=stage-B tcp-sessions=1")
            .And.Contain("destination-pattern=A-B-A");
    }

    [Fact]
    public void RequiresSequentialManualStageTransitions()
    {
        ServerRouteTransitionStudy study = new();
        study.StartStage(2).Should().BeFalse();
        study.StartStage(-1).Should().BeFalse();
        study.StartStage(0).Should().BeTrue();
        study.StartStage(2).Should().BeFalse();
        study.StartStage(0).Should().BeFalse();
        study.StartStage(1).Should().BeTrue();
        study.StartStage(2).Should().BeTrue();
        study.StartStage(3).Should().BeFalse();
    }

    [Fact]
    public void EvictionIsNotAcceptedAsCleanlyClosedGameSession()
    {
        ServerRouteTransitionStudy study = new();
        study.StartStage(0);
        CharacterCaptureFlowKey a = Route("198.51.100.12", 49002);
        study.Observe(a, [
            Ev(2, CharacterCaptureEventKind.Opened),
            Ev(2, CharacterCaptureEventKind.Message, "ksc"),
            Ev(2, CharacterCaptureEventKind.Message, "ksb"),
            Ev(2, CharacterCaptureEventKind.Evicted)
        ]);
        study.StartStage(1);
        Complete(study, "198.51.100.34", 4);
        study.StartStage(2);
        Complete(study, "198.51.100.12", 6);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("stage-A1 tcp-sessions=1 ksc-ksb-sessions=1 closed-complete=0")
            .And.Contain("destination-pattern=unavailable");
    }

    [Fact]
    public void NoManualStageMeansNoAttribution()
    {
        ServerRouteTransitionStudy study = new();
        Complete(study, "198.51.100.12", 2);
        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("destination-pattern=unavailable")
            .And.Contain("stage-A1 tcp-sessions=0");
    }
}
