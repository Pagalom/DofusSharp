using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

/// <summary>
/// All data is synthetic or copied from user-confirmed anonymized
/// profession (level, cumulative XP) examples; no network needed.
/// </summary>
public sealed class ProfessionLevelExperienceReferenceTest
{
    [Theory]
    [InlineData(1, 0UL, 0UL, 20UL)]
    [InlineData(2, 30UL, 20UL, 60UL)]
    [InlineData(3, 76UL, 60UL, 120UL)]
    [InlineData(4, 144UL, 120UL, 200UL)]
    [InlineData(8, 708UL, 560UL, 720UL)]
    [InlineData(69, 47043UL, 46920UL, 48300UL)]
    [InlineData(195, 379581UL, 378300UL, 382200UL)]
    [InlineData(199, 394020UL, 394020UL, 398000UL)]
    public void KnownLevelsProduceRealCumulativeBoundaries(
        int level, ulong current, ulong expectedStart, ulong expectedNext)
    {
        ProfessionLevelExperienceReference.TryGetBoundaries(
            level, current, out ulong start, out ulong next).Should().BeTrue();
        start.Should().Be(expectedStart);
        next.Should().Be(expectedNext);
    }

    [Theory]
    [InlineData(0, 0UL)]
    [InlineData(1, 20UL)]
    [InlineData(8, 559UL)]
    [InlineData(8, 720UL)]
    [InlineData(69, 48300UL)]
    [InlineData(195, 382200UL)]
    [InlineData(200, 398000UL)]
    [InlineData(201, 399000UL)]
    [InlineData(199, ulong.MaxValue)]
    public void InvalidOrStaleLevelXpPairCannotProduceBar(int level, ulong xp)
    {
        ProfessionLevelExperienceReference.TryGetBoundaries(
            level, xp, out ulong start, out ulong next).Should().BeFalse();
        start.Should().Be(0);
        next.Should().Be(0);
    }

    [Fact]
    public void All199NonTerminalLevelsAreContiguousAndHaveIncreasingThresholds()
    {
        for (int level = 1; level < 200; level++)
        {
            ulong start = 10UL * (ulong)level * (ulong)(level - 1);
            ulong end = 10UL * (ulong)level * (ulong)(level + 1);

            ProfessionLevelExperienceReference.TryGetBoundaries(
                level, start, out ulong actualStart, out ulong actualEnd)
                .Should().BeTrue();

            actualStart.Should().Be(start);
            actualEnd.Should().Be(end);
            (actualEnd - actualStart).Should().Be((ulong)level * 20UL);
        }
    }

    [Theory]
    [InlineData("Alchimiste", 195, 379581UL, 1281UL, 3900UL, "32.85")]
    [InlineData("Bûcheron", 69, 47043UL, 123UL, 1380UL, "8.91")]
    [InlineData("Bijoutier", 8, 708UL, 148UL, 160UL, "92.50")]
    [InlineData("Bijoutier", 3, 76UL, 16UL, 60UL, "26.67")]
    public void LevelProgressPresentationMatchesObservedNumbers(
        string name, int level, ulong total, ulong earned,
        ulong required, string decimalPercent)
    {
        ProfessionLevelExperienceReference.TryGetBoundaries(
            level, total, out ulong start, out ulong next).Should().BeTrue();

        var progress = ProfessionProgressPresentation.For(
            new AccountProfessionProgress(name, level, total, start, next));

        progress.Should().NotBeNull();
        progress!.Value.EarnedInLevel.Should().Be(earned);
        progress.Value.RequiredInLevel.Should().Be(required);
        progress.Value.Percent.ToString("0.00",
            System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(decimalPercent);
    }
}
