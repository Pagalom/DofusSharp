using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

/// <summary>
/// SQL browsing is a temporary read-only view. It never supplies evidence
/// for the capture engine and never changes the character/session roster.
/// </summary>
public sealed class ServerViewFilterStateTest
{
    private static readonly DofusClientWindowKey TyleziaWindow = new(101, 1001);
    private static readonly DofusClientWindowKey KourialWindow = new(102, 1002);

    [Fact]
    public void DefaultViewFollowsProvenWindowServerWithoutManualChoice()
    {
        ServerViewFilterState view = new();

        view.ServerName.Should().BeNull();
        view.IsBrowsingManually.Should().BeFalse();

        view.ReconcileTarget(TyleziaWindow, "Tylezia").Should().BeTrue();
        view.ServerName.Should().Be("Tylezia");
        view.TargetWindow.Should().Be(TyleziaWindow);
        view.IsBrowsingManually.Should().BeFalse();
    }

    [Fact]
    public void ManualSqlServerBrowseDoesNotChangeCharacterOrNetworkContext()
    {
        ServerViewFilterState view = new();
        CurrentCharacterState characters = new();
        WindowServerAttributionState attribution = new();
        CurrentServerState economic = new(attribution, automaticRoutingEnabled: true);
        characters.SelectServer("Tylezia");
        view.FollowWindow(TyleziaWindow, "Tylezia");

        view.BrowseServer("Kourial").Should().BeTrue();
        view.IsBrowsingManually.Should().BeTrue();
        view.ServerName.Should().Be("Kourial");
        view.TargetWindow.Should().Be(TyleziaWindow);
        characters.ServerName.Should().Be("Tylezia");
        economic.GetCaptureLease().Should().BeNull();
        economic.GetWindowCaptureLease(TyleziaWindow).Should().BeNull();
    }

    [Fact]
    public void RoutineNetworkUpdatesCannotOverwriteManualBrowsingFilter()
    {
        ServerViewFilterState view = new();
        view.FollowWindow(TyleziaWindow, "Tylezia");
        view.BrowseServer("Kourial");

        view.ReconcileTarget(TyleziaWindow, "Tylezia").Should().BeFalse();
        view.ReconcileTarget(KourialWindow, "Draconiros").Should().BeFalse();
        view.ReconcileTarget(null, null).Should().BeFalse();
        view.ServerName.Should().Be("Kourial");
        view.IsBrowsingManually.Should().BeTrue();
    }

    [Fact]
    public void ClickingSameAlreadyTargetedCharacterRestoresItsNetworkServer()
    {
        ServerViewFilterState view = new();
        view.FollowWindow(TyleziaWindow, "Tylezia");
        view.BrowseServer("Kourial");

        view.FollowWindow(TyleziaWindow, "Tylezia").Should().BeTrue();
        view.ServerName.Should().Be("Tylezia");
        view.IsBrowsingManually.Should().BeFalse();
        view.TargetWindow.Should().Be(TyleziaWindow);
    }

    [Fact]
    public void GameActivationOfOtherWindowReturnsToItsOwnServer()
    {
        ServerViewFilterState view = new();
        view.FollowWindow(TyleziaWindow, "Tylezia");
        view.BrowseServer("Hell Mina");

        view.FollowWindow(KourialWindow, "Kourial").Should().BeTrue();
        view.ServerName.Should().Be("Kourial");
        view.TargetWindow.Should().Be(KourialWindow);
        view.IsBrowsingManually.Should().BeFalse();
    }

    [Fact]
    public void UnknownGameServerDoesNotBorrowBrowsingOrPriorNetworkServer()
    {
        ServerViewFilterState view = new();
        view.FollowWindow(TyleziaWindow, "Tylezia");
        view.BrowseServer("Kourial");

        view.FollowWindow(KourialWindow, null).Should().BeTrue();
        view.ServerName.Should().BeNull();
        view.IsBrowsingManually.Should().BeFalse();
        view.TargetWindow.Should().Be(KourialWindow);
    }

    [Fact]
    public void TwoWindowsOnSameServerUseSameSqlFilterWithoutSharingIdentity()
    {
        ServerViewFilterState view = new();
        view.FollowWindow(TyleziaWindow, "Tylezia");

        view.FollowWindow(KourialWindow, "Tylezia").Should().BeTrue();
        view.ServerName.Should().Be("Tylezia");
        view.TargetWindow.Should().Be(KourialWindow);
    }

    [Fact]
    public void ManualBrowsingCanInspectServerWithoutAnyClient()
    {
        ServerViewFilterState view = new();
        view.BrowseServer("Draconiros").Should().BeTrue();
        view.ServerName.Should().Be("Draconiros");
        view.TargetWindow.Should().BeNull();

        view.FollowWindow(null, null).Should().BeTrue();
        view.ServerName.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void InvalidManualServerFilterIsRejected(string name)
    {
        ServerViewFilterState view = new();
        Action action = () => view.BrowseServer(name);
        action.Should().Throw<ArgumentException>();
        view.IsBrowsingManually.Should().BeFalse();
    }
}
