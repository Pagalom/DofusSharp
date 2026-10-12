using BestCrush.Components;
using BestCrush.Domain.Services;
using DofusSharp.Dofocus.ApiClients.Models.Servers;
using FluentAssertions;

namespace Tests.BestCrush;

/// <summary>
/// M0 baseline adapted for M2: startup now opens the workspace.
/// The legacy Concassage and history URLs remain server-scoped.
/// </summary>
public sealed class StartupNavigationCharacterizationTest
{
    [Fact]
    public void WithoutCurrentServerSplashOpensModularConcassageLanding()
    {
        KnownRoutes.Splash().Should().Be("/");
        KnownRoutes.AfterInitialization(null)
            .Should().Be(KnownRoutes.Module("concassage"));
        KnownRoutes.Servers().Should().Be("/servers");
    }

    [Fact]
    public void WithRestoredServerSplashOpensThatServersConcassagePage()
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
    public async Task ServersServiceWithoutPreferenceAdapterRemainsInMemoryOnly()
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
            "without an injected persistence adapter these instances must not share state");
    }
}
