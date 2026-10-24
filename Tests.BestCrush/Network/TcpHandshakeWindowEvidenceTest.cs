using System.Net;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class TcpHandshakeWindowEvidenceTest
{
    private static readonly TcpSocketTuple Socket = new(
        IPAddress.Parse("127.0.0.1"), 40101,
        IPAddress.Parse("127.0.0.2"), 5555);
    private static readonly TcpSocketTuple OtherSocket = Socket with
    {
        LocalPort = 40102
    };
    private static readonly DofusClientWindowKey First = new(101, 1201);
    private static readonly DofusClientWindowKey Second = new(102, 1202);
    private static readonly DateTime At =
        new(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);

    private static TcpHandshakeWindowEvidence Start()
    {
        TcpHandshakeWindowEvidence state = new();
        state.Reset(1);
        return state;
    }

    [Fact]
    public void ExactValidatedSynOwnerSurvivesSocketClosureUntilFrameDecoded()
    {
        TcpHandshakeWindowEvidence state = Start();
        state.Remember(1, 11, Socket, First, At);

        // The TCP table no longer contains the short-lived Connection.
        // Only the same logical token, tuple and uniquely visible HWND/PID
        // authorize reusing the SYN-time window owner.
        state.Resolve(1, 11, Socket, At.AddSeconds(2), () => [First])
            .Should().Be(First);
    }

    [Fact]
    public void OtherWindowOrTcpTokenCannotBorrowHandshakeOwnership()
    {
        TcpHandshakeWindowEvidence state = Start();
        state.Remember(1, 11, Socket, First, At);

        state.Resolve(1, 12, Socket, At.AddSeconds(1), () => [First])
            .Should().BeNull();
        state.Resolve(1, 11, OtherSocket, At.AddSeconds(1), () => [First])
            .Should().BeNull();
        state.Resolve(1, 11, Socket, At.AddSeconds(1), () => [Second])
            .Should().BeNull();
    }

    [Fact]
    public void ClosedOrRecycledClientHandleCannotInheritPriorSynOwner()
    {
        TcpHandshakeWindowEvidence state = Start();
        state.Remember(1, 11, Socket, First, At);

        state.Resolve(1, 11, Socket, At.AddSeconds(1), () => [])
            .Should().BeNull();
        state.Resolve(1, 11, Socket, At.AddSeconds(1),
                () => [new DofusClientWindowKey(First.Handle, 9999)])
            .Should().BeNull();
        state.Resolve(1, 11, Socket, At.AddSeconds(1),
                () => [First, new DofusClientWindowKey(202, First.ProcessId)])
            .Should().BeNull();
    }

    [Fact]
    public void ExpiredOwnerIsNotValidAtServerSelectionFrame()
    {
        TcpHandshakeWindowEvidence state = Start();
        state.Remember(1, 11, Socket, First, At);

        state.Resolve(1, 11, Socket, At.AddSeconds(11), () => [First])
            .Should().BeNull();
        state.Resolve(1, 11, Socket, At.AddSeconds(-1), () => [First])
            .Should().BeNull();
    }

    [Fact]
    public void ConflictingOwnershipQuarantinesLogicalSocket()
    {
        TcpHandshakeWindowEvidence state = Start();
        state.Remember(1, 11, Socket, First, At);
        state.Remember(1, 11, Socket, Second, At.AddSeconds(1));

        state.Resolve(1, 11, Socket, At.AddSeconds(2), () => [First])
            .Should().BeNull();
        state.Remember(1, 11, Socket, First, At.AddSeconds(3));
        state.Resolve(1, 11, Socket, At.AddSeconds(4), () => [First])
            .Should().BeNull();
    }

    [Fact]
    public void NewCaptureInvalidatesEveryPreviousHandshakeHint()
    {
        TcpHandshakeWindowEvidence state = Start();
        state.Remember(1, 11, Socket, First, At);
        state.Reset(2);

        state.Resolve(1, 11, Socket, At.AddSeconds(1), () => [First])
            .Should().BeNull();
        state.Remember(1, 11, Socket, First, At);
        state.Resolve(1, 11, Socket, At.AddSeconds(1), () => [First])
            .Should().BeNull();

        state.Remember(2, 21, Socket, Second, At);
        state.Resolve(2, 21, Socket, At.AddSeconds(1), () => [Second])
            .Should().Be(Second);
    }

    [Fact]
    public void InvalidOrUnknownEvidenceCannotSupplyWindow()
    {
        TcpHandshakeWindowEvidence state = Start();
        state.Remember(1, 0, Socket, First, At);
        state.Remember(1, 11, Socket,
            new DofusClientWindowKey(0, 1201), At);

        state.Resolve(1, 11, Socket, At, () => [First]).Should().BeNull();
    }
}
