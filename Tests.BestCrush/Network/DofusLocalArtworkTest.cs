using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class DofusLocalArtworkTest
{
    [Theory]
    [InlineData("Mineur", "img/dofus/jobs/24.png")]
    [InlineData("Bûcheron", "img/dofus/jobs/2.png")]
    [InlineData("Forgeron", "img/dofus/jobs/11.png")]
    [InlineData("Costumage", "img/dofus/jobs/64.png")]
    [InlineData("Pêcheur", "img/dofus/jobs/36.png")]
    public void KnownJobNamesHaveOnlyLocalArtworkPaths(string name, string expected) =>
        DofusLocalArtwork.ProfessionIcon(name).Should().Be(expected);

    [Theory]
    [InlineData("Éniripsa", "img/dofus/classes/7.png")]
    [InlineData("Eniripsa", "img/dofus/classes/7.png")]
    [InlineData("Écaflip", "img/dofus/classes/6.png")]
    [InlineData("Ecaflip", "img/dofus/classes/6.png")]
    [InlineData("Féca", "img/dofus/classes/1.png")]
    [InlineData("Forgelance", "img/dofus/classes/20.png")]
    public void KnownClassNamesResolveToLocalHead(string name, string expected) =>
        DofusLocalArtwork.ClassPortrait(name).Should().Be(expected);

    [Fact]
    public void MissingAndUnknownArtworkNeverProduceRemoteUrl()
    {
        DofusLocalArtwork.ProfessionIcon(null).Should().BeNull();
        DofusLocalArtwork.ProfessionIcon("Métier inconnu").Should().BeNull();
        DofusLocalArtwork.ClassPortrait(null).Should().BeNull();
        DofusLocalArtwork.ClassPortrait("Compte non vérifié").Should().BeNull();

        foreach (var classId in Enumerable.Range(1, 19))
        {
            string? path = DofusLocalArtwork.ClassPortrait(
                DofusClassDisplayNames.ForId(classId));
            path.Should().StartWith("img/dofus/classes/");
            path.Should().NotContain("http");
        }
    }
}
