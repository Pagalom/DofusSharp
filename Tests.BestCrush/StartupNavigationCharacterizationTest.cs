using BestCrush.Components;
using BestCrush.Domain.Services;
using DofusSharp.Dofocus.ApiClients.Models.Servers;
using FluentAssertions;

namespace Tests.BestCrush;

/// <summary>
/// M0 baseline only: protect the exact current startup routing and the
/// in-memory-only server selection before moving to the modular workspace.
/// The legacy routing expectations must change explicitly in M1/M2.
/// </summary>
public sealed class StartupNavigationCharacterizationTest
{
    [Fact]
    public void WithoutCurrentServerSplashCurrentlyOpensLegacyServerPicker()
    {
        KnownRoutes.Splash().Should().Be("/");
        KnownRoutes.AfterInitialization(null)
            .Should().Be(KnownRoutes.Servers());
        KnownRoutes.Servers().Should().Be("/servers");
    }

    [Fact]
    public void WithCurrentServerSplashCurrentlyOpensThatServersConcassagePage()
    {
        DofocusServer current = new() { Name = "Kourial" };

        KnownRoutes.AfterInitialization(current)
            .Should().Be("/servers/Kourial");
    }

    [Fact]
    public void HistoryRouteCurrentlyRemainsScopedToItsServer()
    {
        KnownRoutes.History("Kourial")
            .Should().Be("/servers/Kourial/history");
        KnownRoutes.History("Other")
            .Should().Be("/servers/Other/history");
    }

    [Fact]
    public async Task ServersServiceCurrentlyKeepsChoiceOnlyWithinItsInstance()
    {
        // These two methods do not use the catalog/image dependencies.
        // No network calls or MAUI preferences are involved in this test.
        ServersService firstInstance = new(null!, null!);
        ServersService recreatedInstance = new(null!, null!);

        (await firstInstance.GetCurrentServer()).Should().BeNull();

        DofocusServer choice = new() { Name = "Kourial" };
        await firstInstance.SetCurrentServerAsync(choice);

        (await firstInstance.GetCurrentServer()).Should().BeSameAs(choice);
        (await recreatedInstance.GetCurrentServer()).Should().BeNull(
            "persisting the last chosen server is a future M2 requirement, not existing behavior");
    }
}
