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

        state.Observe(A, 1, [new(A, 1201)], [WindowA]).Should().BeTrue();
        state.Reconcile(1, [], [WindowA]);
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
    public void MissingOwnerDoesNotRevivePreviousBinding()
    {
        LiveDofusClientNetworkBindings state = new();
        state.Observe(A, 1, [new(A, 1201)], [WindowA]).Should().BeTrue();
        state.Observe(A, 1, [], [WindowA]).Should().BeFalse();
        state.IsLinked(WindowA).Should().BeFalse();
    }
}
