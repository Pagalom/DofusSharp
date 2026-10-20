using System.Net;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class LiveDofusClientNetworkBindingsTest
{
    private static readonly TcpSocketTuple A = new(
        IPAddress.Parse("192.0.2.10"), 48001,
        IPAddress.Parse("198.51.100.20"), 5555);
    private static readonly TcpSocketTuple B = A with { LocalPort = 48002 };
    private static readonly DofusClientWindowKey WindowA = new(101, 1201);
    private static readonly DofusClientWindowKey WindowB = new(102, 1202);

    [Fact]
    public void TwoSeparateSocketsBelongToTwoSeparateWindows()
    {
        LiveDofusClientNetworkBindings state = new();
        TcpSocketOwner[] sockets = [new(A, 1201), new(B, 1202)];
        DofusClientWindowKey[] windows = [WindowA, WindowB];

        state.Observe(A, 1, sockets, windows).Should().BeTrue();
        state.Observe(B, 1, sockets, windows).Should().BeTrue();

        state.LinkedClients.Should().BeEquivalentTo(windows);
        state.IsLinked(WindowA).Should().BeTrue();
        state.IsLinked(WindowB).Should().BeTrue();
    }

    [Fact]
    public void MerelySeeingWindowOrMatchingPortDoesNotLinkClient()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 1, [new(B, 1201)], [WindowA])
            .Should().BeFalse();
        state.LinkedClients.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateSocketOwnerOrTwoWindowsOnOnePidMustFailClosed()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 1,
            [new(A, 1201), new(A, 1202)],
            [WindowA, WindowB]).Should().BeFalse();

        DofusClientWindowKey anotherWindowSamePid = new(103, 1201);
        state.Observe(A, 1,
            [new(A, 1201)],
            [WindowA, anotherWindowSamePid]).Should().BeFalse();
        state.LinkedClients.Should().BeEmpty();
    }

    [Fact]
    public void ClosingWindowOrDisconnectingSocketRevokesLink()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 1, [new(A, 1201)], [WindowA]).Should().BeTrue();

        state.Reconcile(1, [new(A, 1201)], []);
        state.IsLinked(WindowA).Should().BeFalse();
        // Reappearing under the same socket while the old capture lives
        // cannot silently recover the revoked window.
        state.Observe(A, 1, [new(A, 1201)], [WindowA]).Should().BeFalse();

        state.Reset(2);
        state.Observe(A, 2, [new(A, 1201)], [WindowA]).Should().BeTrue();
        state.Reconcile(2, [], [WindowA]);
        state.IsLinked(WindowA).Should().BeFalse();
    }

    [Fact]
    public void SameTupleChangingOwnerCannotMoveToAnotherWindowInSameEpoch()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 1, [new(A, 1201)], [WindowA]).Should().BeTrue();

        state.Observe(A, 1, [new(A, 1202)], [WindowA, WindowB])
            .Should().BeFalse();
        state.IsLinked(WindowA).Should().BeFalse();
        state.Observe(A, 1, [new(A, 1202)], [WindowA, WindowB])
            .Should().BeFalse();
        state.IsLinked(WindowB).Should().BeFalse();

        state.Reset(2);
        state.Observe(A, 2, [new(A, 1202)], [WindowA, WindowB])
            .Should().BeTrue();
        state.IsLinked(WindowB).Should().BeTrue();
    }

    [Fact]
    public void NewCaptureEpochErasesOldBindings()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 1, [new(A, 1201)], [WindowA]).Should().BeTrue();
        state.Reset(2);
        state.LinkedClients.Should().BeEmpty();
        state.IsLinked(WindowA).Should().BeFalse();
        new LiveDofusClientNetworkBindings().LinkedClients.Should().BeEmpty();
    }

    [Fact]
    public void SocketOwnerChangeDuringInventoryRefreshStaysQuarantined()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 1, [new(A, 1201)], [WindowA]).Should().BeTrue();

        state.Reconcile(1, [new(A, 1202)], [WindowA, WindowB]);
        state.IsLinked(WindowA).Should().BeFalse();
        state.Observe(A, 1, [new(A, 1202)], [WindowA, WindowB])
            .Should().BeFalse();
    }

    [Fact]
    public void ClosedWindowIsRevokedEvenIfNoNewNetworkPacketArrives()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 1, [new(A, 1201)], [WindowA]).Should().BeTrue();
        state.RemoveMissingWindows([WindowB]);
        state.LinkedClients.Should().BeEmpty();
        state.Observe(A, 1, [new(A, 1201)], [WindowA])
            .Should().BeFalse();
    }

    [Fact]
    public void MissingOwnerDoesNotRevivePreviousBinding()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 1, [new(A, 1201)], [WindowA]).Should().BeTrue();
        state.Observe(A, 1, [], [WindowA]).Should().BeFalse();
        state.IsLinked(WindowA).Should().BeFalse();
    }
    [Fact]
    public void TwoVerifiedTcpConnectionsGetDifferentLocalWindowContexts()
    {
        LiveDofusClientNetworkBindings state = new();
        TcpSocketOwner[] sockets = [new(A, 1201), new(B, 1202)];
        DofusClientWindowKey[] windows = [WindowA, WindowB];
        state.Observe(A, 31, sockets, windows).Should().BeTrue();
        state.Observe(B, 31, sockets, windows).Should().BeTrue();

        state.ResolveConnection(A, 31, 11).Should().Be(WindowA);
        state.ResolveConnection(B, 31, 12).Should().Be(WindowB);
        state.IsCurrentConnection(A, 31, 11, WindowA).Should().BeTrue();
        state.IsCurrentConnection(31, 11, WindowA).Should().BeTrue();
        state.IsCurrentConnection(A, 31, 11, WindowB).Should().BeFalse();
        state.IsCurrentConnection(B, 31, 12, WindowB).Should().BeTrue();
    }

    [Fact]
    public void StaleLogicalConnectionCannotReclaimSameReusedTcpSocket()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 31, [new(A, 1201)], [WindowA]).Should().BeTrue();

        state.ResolveConnection(A, 31, 11).Should().Be(WindowA);
        state.ResolveConnection(A, 31, 12).Should().Be(WindowA);
        state.IsCurrentConnection(A, 31, 11, WindowA).Should().BeFalse();
        state.IsCurrentConnection(31, 11, WindowA).Should().BeFalse();
        state.ResolveConnection(A, 31, 11).Should().BeNull();
        state.IsCurrentConnection(A, 31, 12, WindowA).Should().BeTrue();
    }

    [Fact]
    public void MissingOrUnlinkedSocketNeverGetsClientContext()
    {
        LiveDofusClientNetworkBindings state = new();
        state.ResolveConnection(A, 31, 11).Should().BeNull();
        state.Observe(A, 31, [new(B, 1201)], [WindowA]).Should().BeFalse();
        state.ResolveConnection(A, 31, 11).Should().BeNull();
        state.ResolveConnection(B, 31, 0).Should().BeNull();
        state.IsCurrentConnection(A, 31, 11, WindowA).Should().BeFalse();
    }

    [Fact]
    public void WindowClosureOrNewCaptureRevokesAlreadyAssociatedMessageContext()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 31, [new(A, 1201)], [WindowA]).Should().BeTrue();
        state.ResolveConnection(A, 31, 11).Should().Be(WindowA);

        state.RemoveMissingWindows([]);
        state.IsCurrentConnection(A, 31, 11, WindowA).Should().BeFalse();
        state.IsCurrentConnection(31, 11, WindowA).Should().BeFalse();
        state.ResolveConnection(A, 31, 11).Should().BeNull();

        state.Reset(32);
        state.Observe(A, 32, [new(A, 1201)], [WindowA]).Should().BeTrue();
        state.ResolveConnection(A, 32, 12).Should().Be(WindowA);
        state.IsCurrentConnection(A, 31, 11, WindowA).Should().BeFalse();
        state.IsCurrentConnection(31, 11, WindowA).Should().BeFalse();
        state.IsCurrentConnection(A, 32, 12, WindowA).Should().BeTrue();
        state.IsCurrentConnection(32, 12, WindowA).Should().BeTrue();
    }

    [Fact]
    public void CandidateWindowOnWireMessageIsNotCharacterIdentity()
    {
        var message = new DofusWireMessage(
            "S→C", "ipz", [], DateTime.UtcNow,
            new NetworkCaptureLease("ExampleServer", 1),
            CaptureEpoch: 31,
            ConnectionId: 11,
            CandidateWindow: WindowA);

        message.CandidateWindow.Should().Be(WindowA);
        message.ConnectionId.Should().Be(11);
        new CurrentCharacterState().Characters.Should().BeEmpty();
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
    }

}
