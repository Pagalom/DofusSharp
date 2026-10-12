using BestCrush.Components;
using FluentAssertions;

namespace Tests.BestCrush;

public sealed class ModularWorkspaceNavigationTest
{
    [Fact]
    public void AllNineModulesHaveStableOrderAndUniqueSlugs()
    {
        WorkspaceModules.All.Select(module => module.Name)
            .Should().Equal(
                "Concassage",
                "Achat / Vente",
                "Pierres d'âme",
                "Craft",
                "Familiers / XP",
                "Élevage",
                "Farming",
                "Forgemagie",
                "Opportunités");

        WorkspaceModules.All.Select(module => module.Slug)
            .Should().OnlyHaveUniqueItems();
        WorkspaceModules.All.Should().HaveCount(9);
    }

    [Theory]
    [InlineData("achat-vente")]
    [InlineData("pierres-ame")]
    [InlineData("craft")]
    [InlineData("familiers-xp")]
    [InlineData("elevage")]
    [InlineData("farming")]
    [InlineData("forgemagie")]
    [InlineData("opportunites")]
    public void PendingModulesHaveStableServerIndependentUrls(string slug)
    {
        WorkspaceModule module = WorkspaceModules.Find(slug)!;

        module.Should().NotBeNull();
        WorkspaceModules.Href(module, "Kourial")
            .Should().Be($"/modules/{slug}");
        WorkspaceModules.Href(module, "Other")
            .Should().Be($"/modules/{slug}");
        KnownRoutes.Module(slug).Should().Be($"/modules/{slug}");
    }

    [Fact]
    public void ConcassageKeepsItsValidatedLegacyRouteDuringM1()
    {
        WorkspaceModule concassage = WorkspaceModules.Find("concassage")!;

        WorkspaceModules.Href(concassage, "Kourial")
            .Should().Be("/servers/Kourial");
        WorkspaceModules.Href(concassage, "Other")
            .Should().Be("/servers/Other");
    }

    [Theory]
    [InlineData("/servers/Kourial")]
    [InlineData("servers/Kourial/rune-prices")]
    [InlineData("servers/Kourial/history")]
    [InlineData("servers/Kourial/equipment-prices?lot=10")]
    public void LegacyConcassageSubpagesKeepConcassageTabActive(string route)
    {
        WorkspaceModules.ActiveSlug(route)
            .Should().Be("concassage");
    }

    [Theory]
    [InlineData("achat-vente")]
    [InlineData("pierres-ame")]
    [InlineData("craft")]
    [InlineData("familiers-xp")]
    [InlineData("elevage")]
    [InlineData("farming")]
    [InlineData("forgemagie")]
    [InlineData("opportunites")]
    public void ModuleRouteActivatesItsOwnTabOnly(string slug)
    {
        WorkspaceModules.ActiveSlug($"/modules/{slug}")
            .Should().Be(slug);
        WorkspaceModules.ActiveSlug($"/modules/{slug}?source=menu")
            .Should().Be(slug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/servers")]
    [InlineData("/profile")]
    [InlineData("/modules/unknown")]
    [InlineData("/other-page")]
    public void NonWorkspaceRoutesDoNotHighlightAnUnrelatedTab(string route)
    {
        WorkspaceModules.ActiveSlug(route).Should().BeNull();
    }

    [Fact]
    public void CharacterAndGlobalSettingsHaveDistinctRoutes()
    {
        KnownRoutes.Profile().Should().Be("/profile");
        KnownRoutes.Settings("Kourial")
            .Should().Be("/servers/Kourial?settings=1");
    }

    [Fact]
    public void UnknownModuleIsNeverPresentedAsAValidWorkspaceTab()
    {
        WorkspaceModules.Find(null).Should().BeNull();
        WorkspaceModules.Find("unknown").Should().BeNull();
        WorkspaceModules.Find("Achat / Vente").Should().BeNull();
    }
}
