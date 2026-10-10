using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class NetworkCaptureGateTest
{
    [Fact]
    public void SelectionNeedsExplicitConfirmationAndDoesNotRenewSilently()
    {
        CurrentServerState state = new();
        state.SelectServer("A");
        state.GetCaptureLease().Should().BeNull();

        state.ConfirmCapture("A");
        NetworkCaptureLease lease = state.GetCaptureLease()!.Value;

        state.SelectServer("A");
        state.GetCaptureLease().Should().Be(lease);

        state.SelectServer("B");
        state.GetCaptureLease().Should().BeNull();
        state.IsCaptureLeaseActive(lease).Should().BeFalse();
        state.GetCaptureStatus().IsAmbiguous.Should().BeFalse();

        Action confirmOld = () => state.ConfirmCapture("A");
        confirmOld.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OneConnectionIsAllowedAndAnotherConnectionSuspends()
    {
        CurrentServerState state = new();
        state.SelectServer("A");
        state.ConfirmCapture("A");
        NetworkCaptureLease lease = state.GetCaptureLease()!.Value;

        state.TryAcceptConnection(lease, "client:123-server:5555").Should().BeTrue();
        state.TryAcceptConnection(lease, "client:123-server:5555").Should().BeTrue();
        state.TryAcceptConnection(lease, "client:124-server:5555").Should().BeFalse();

        state.GetCaptureStatus().IsAmbiguous.Should().BeTrue();
        state.GetCaptureLease().Should().BeNull();
        state.IsCaptureLeaseActive(lease).Should().BeFalse();

        state.ConfirmCapture("A");
        NetworkCaptureLease newLease = state.GetCaptureLease()!.Value;
        newLease.Should().NotBe(lease);
        state.TryAcceptConnection(lease, "client:123-server:5555").Should().BeFalse();
        state.TryAcceptConnection(newLease, "client:124-server:5555").Should().BeTrue();
    }

    [Fact]
    public void SuspensionAndClearInvalidateEarlierGeneration()
    {
        CurrentServerState state = new();
        state.SelectServer("A");
        state.ConfirmCapture("A");
        NetworkCaptureLease first = state.GetCaptureLease()!.Value;

        state.SuspendCapture();
        state.IsCaptureLeaseActive(first).Should().BeFalse();

        state.ConfirmCapture("A");
        NetworkCaptureLease second = state.GetCaptureLease()!.Value;
        second.Should().NotBe(first);

        state.Clear();
        state.GetCaptureLease().Should().BeNull();
        state.ServerName.Should().BeNull();
        state.IsCaptureLeaseActive(second).Should().BeFalse();
    }
}
