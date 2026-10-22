using System.Net;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class EtwTcpConnectionOwnerIndexTest
{
    private static readonly DateTime T0 =
        new(2026, 10, 22, 21, 0, 0, DateTimeKind.Utc);

    private static TcpSocketTuple Tuple(int suffix = 0) =>
        new(IPAddress.Parse("192.0.2.7"), (ushort)(49000 + suffix),
            IPAddress.Parse("198.51.100.10"), 5555);

    [Fact]
    public void ExactTupleWithinTimeWindowResolvesPid()
    {
        EtwTcpConnectionOwnerIndex index = new();
        index.Observe(Tuple(), 501, T0);
        index.Resolve(Tuple(), T0.AddMilliseconds(500))
            .Should().Be(501);
        index.Count.Should().Be(1);
    }

    [Fact]
    public void OtherLocalPortNeverMatchesEvenIfDestAndTimeIdentical()
    {
        EtwTcpConnectionOwnerIndex index = new();
        index.Observe(Tuple(1), 501, T0);
        index.Resolve(Tuple(2), T0).Should().BeNull();
    }

    [Fact]
    public void WrongDestinationPortNeverMatches()
    {
        EtwTcpConnectionOwnerIndex index = new();
        index.Observe(Tuple(), 501, T0);
        TcpSocketTuple altered = Tuple() with { RemotePort = 7777 };
        index.Resolve(altered, T0).Should().BeNull();
    }

    [Fact]
    public void OldEtwEventNeverClaimsReusedTcpTuple()
    {
        EtwTcpConnectionOwnerIndex index = new();
        index.Observe(Tuple(), 501, T0);
        index.Resolve(Tuple(), T0.AddSeconds(20)).Should().BeNull();
    }

    [Fact]
    public void EventArrivingAfterPacketCanBeResolvedUsingOriginalTimestamp()
    {
        EtwTcpConnectionOwnerIndex index = new();
        index.Resolve(Tuple(), T0.AddMilliseconds(300)).Should().BeNull();
        index.Observe(Tuple(), 501, T0);
        index.Resolve(Tuple(), T0.AddMilliseconds(300))
            .Should().Be(501);
    }

    [Fact]
    public void DuplicateIdenticalOwnerRemainsUnique()
    {
        EtwTcpConnectionOwnerIndex index = new();
        index.Observe(Tuple(), 501, T0);
        index.Observe(Tuple(), 501, T0.AddMilliseconds(100));
        index.IsConflicted(Tuple()).Should().BeFalse();
        index.Resolve(Tuple(), T0).Should().Be(501);
    }

    [Fact]
    public void ContradictoryEtwPidFailsClosed()
    {
        EtwTcpConnectionOwnerIndex index = new();
        index.Observe(Tuple(), 501, T0);
        index.Observe(Tuple(), 502, T0.AddMilliseconds(100));
        index.IsConflicted(Tuple()).Should().BeTrue();
        index.Resolve(Tuple(), T0).Should().BeNull();
    }

    [Fact]
    public void SecondTcpGenerationUsingSameTupleFailsClosed()
    {
        EtwTcpConnectionOwnerIndex index = new();
        index.Observe(Tuple(), 501, T0);
        index.Observe(Tuple(), 501, T0.AddMinutes(1));
        index.IsConflicted(Tuple()).Should().BeTrue();
        index.Resolve(Tuple(), T0).Should().BeNull();
    }

    [Fact]
    public void InvalidEtwPidAndNonUtcEventAreRejected()
    {
        EtwTcpConnectionOwnerIndex index = new();
        index.Observe(Tuple(), -1, T0);
        index.Observe(Tuple(), 501, DateTime.SpecifyKind(T0, DateTimeKind.Local));
        index.Count.Should().Be(0);
        index.Resolve(Tuple(), T0).Should().BeNull();
    }

    [Fact]
    public void EtwEvidenceCanLinkAnOtherwiseMissingOwnerToUniqueWindow()
    {
        EtwTcpConnectionOwnerIndex index = new();
        WindowServerConnectionLinkStudy study = new();
        var selector = Tuple(1);
        var game = Tuple(2);
        index.Observe(selector, 501, T0);
        int? pid = index.Resolve(selector, T0.AddMilliseconds(10));
        pid.Should().Be(501);
        study.ObserveOwner(selector,
            [new TcpSocketOwner(selector, pid!.Value)], [(501, 1)]);
        study.ObserveSelectedId(1, 295);
        study.ObserveEvents(selector, [
            new(T0, 1, CharacterCaptureDirection.ClientToServer,
                CharacterCaptureEventKind.Opened),
            new(T0, 1, CharacterCaptureDirection.ClientToServer,
                CharacterCaptureEventKind.Closed)]);
        study.ObserveOwner(game,
            [new TcpSocketOwner(game, 501)], [(501, 1)]);
        study.ObserveEvents(game, [
            new(T0, 2, CharacterCaptureDirection.ServerToClient,
                CharacterCaptureEventKind.Opened),
            new(T0, 2, CharacterCaptureDirection.ServerToClient,
                CharacterCaptureEventKind.Message, "ksc"),
            new(T0, 2, CharacterCaptureDirection.ServerToClient,
                CharacterCaptureEventKind.Message, "ksb")
        ]);

        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("connection-session=1 game-session=2 reference=Draconiros")
            .And.Contain("owner-missing=0")
            .And.Contain("status=linked-candidate");
    }

    [Fact]
    public void ContradictoryOwnerQuarantinesPreviouslyLinkedFlow()
    {
        WindowServerConnectionLinkStudy study = new();
        TcpSocketTuple tuple = Tuple();
        study.ObserveOwner(tuple,
            [new TcpSocketOwner(tuple, 501)], [(501, 1), (502, 2)]);
        study.ObserveSelectedId(1, 295);
        study.ObserveEvents(tuple, [
            new(T0, 1, CharacterCaptureDirection.ClientToServer,
                CharacterCaptureEventKind.Opened)]);
        study.InvalidateOwner(tuple);

        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("reason=owner-conflict")
            .And.Contain("conflicts=1")
            .And.Contain("status=not-confirmed");
    }

    [Fact]
    public void CapacityOverflowPreventsUsingPartialEvidence()
    {
        EtwTcpConnectionOwnerIndex index = new();
        for (int i = 0; i < 129; i++)
            index.Observe(Tuple(i), 501, T0);
        index.Overflow.Should().BeTrue();
        index.Count.Should().Be(128);
        index.Resolve(Tuple(), T0).Should().BeNull();
    }
}
