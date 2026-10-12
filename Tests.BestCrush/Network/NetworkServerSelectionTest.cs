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
    public void ReturningToAPreviouslyVisitedServerNeverReactivatesItsOldLease()
    {
        CurrentServerState state = new();
        state.SelectServer("Kourial");
        NetworkCaptureLease first = state.GetCaptureLease()!.Value;

        state.SelectServer("Another");
        NetworkCaptureLease middle = state.GetCaptureLease()!.Value;

        state.SelectServer("Kourial");
        NetworkCaptureLease restored = state.GetCaptureLease()!.Value;

        restored.ServerName.Should().Be("Kourial");
        restored.Should().NotBe(first);
        state.IsCaptureLeaseActive(first).Should().BeFalse();
        state.IsCaptureLeaseActive(middle).Should().BeFalse();
        state.IsCaptureLeaseActive(restored).Should().BeTrue();
    }

    [Fact]
    public void ReSelectingSameServerAfterNavigationDoesNotInvalidateCapturedMessages()
    {
        CurrentServerState state = new();
        int changeEvents = 0;
        state.ServerSelectionChanged += () => changeEvents++;

        state.SelectServer("Kourial");
        NetworkCaptureLease beforeNavigation = state.GetCaptureLease()!.Value;
        state.SelectServer("Kourial");

        state.GetCaptureLease().Should().Be(beforeNavigation);
        state.IsCaptureLeaseActive(beforeNavigation).Should().BeTrue();
        changeEvents.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void InvalidServerChoiceCannotReplaceTheActiveCaptureLease(
        string? invalidServerName)
    {
        CurrentServerState state = new();
        state.SelectServer("Kourial");
        NetworkCaptureLease validLease = state.GetCaptureLease()!.Value;

        Action select = () => state.SelectServer(invalidServerName!);

        select.Should().Throw<ArgumentException>();
        state.GetCaptureLease().Should().Be(validLease);
        state.IsCaptureLeaseActive(validLease).Should().BeTrue();
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
