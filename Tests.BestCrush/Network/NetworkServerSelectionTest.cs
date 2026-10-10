using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class NetworkServerSelectionTest
{
    [Fact]
    public void ServerSelectionImmediatelyProvidesCaptureContext()
    {
        CurrentServerState state = new();
        state.GetCaptureLease().Should().BeNull();

        state.SelectServer("A");
        NetworkCaptureLease first = state.GetCaptureLease()!.Value;
        first.ServerName.Should().Be("A");
        state.IsCaptureLeaseActive(first).Should().BeTrue();

        state.SelectServer("A");
        state.GetCaptureLease().Should().Be(first);

        state.SelectServer("B");
        NetworkCaptureLease second = state.GetCaptureLease()!.Value;
        second.ServerName.Should().Be("B");
        second.Should().NotBe(first);
        state.IsCaptureLeaseActive(first).Should().BeFalse();
        state.IsCaptureLeaseActive(second).Should().BeTrue();
    }

    [Fact]
    public void ClearingSelectionDiscardsThePreviousCaptureContext()
    {
        CurrentServerState state = new();
        state.SelectServer("A");
        NetworkCaptureLease old = state.GetCaptureLease()!.Value;

        state.Clear();
        state.HasSelectedServer.Should().BeFalse();
        state.GetCaptureLease().Should().BeNull();
        state.IsCaptureLeaseActive(old).Should().BeFalse();

        state.SelectServer("A");
        NetworkCaptureLease current = state.GetCaptureLease()!.Value;
        current.Should().NotBe(old);
        state.IsCaptureLeaseActive(current).Should().BeTrue();
    }

    [Fact]
    public void SelectionChangeEventOnlyFiresForRealChanges()
    {
        CurrentServerState state = new();
        int eventCount = 0;
        state.ServerSelectionChanged += () => eventCount++;

        state.SelectServer("A");
        state.SelectServer("A");
        state.SelectServer("B");
        state.Clear();

        eventCount.Should().Be(3);
    }
}
