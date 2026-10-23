using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

/// <summary>
/// Character sheet progress must reflect real cumulative XP boundaries,
/// never a fabricated linear percentage or a guess from a profession level.
/// </summary>
public sealed class ProfessionProgressPresentationTest
{
    [Fact]
    public void ProperPerLevelXpProgressUsesBothKnownCumulativeThresholds()
    {
        var job = new AccountProfessionProgress(
            "Forgeron", 20, CumulativeExperience: 250,
            LevelStartCumulativeExperience: 200,
            NextLevelCumulativeExperience: 400);

        ProfessionProgressPresentation.For(job).Should().Be(
            new ProfessionProgressPresentation.LevelProgress(50, 200, 25m));
    }

    [Fact]
    public void ExactLevelStartIsGenuineZeroPercentWithKnownThresholds()
    {
        var job = new AccountProfessionProgress(
            "Sculpteur", 6, 301, 301, 501);
        ProfessionProgressPresentation.For(job).Should().Be(
            new ProfessionProgressPresentation.LevelProgress(0, 200, 0m));
    }

    [Fact]
    public void ExactNextBoundaryIsGenuineFullBar()
    {
        var job = new AccountProfessionProgress(
            "Mineur", 7, 424, 224, 424);
        ProfessionProgressPresentation.For(job).Should().Be(
            new ProfessionProgressPresentation.LevelProgress(200, 200, 100m));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(50, 232)]
    [InlineData(200, 123456789)]
    public void NoThresholdsMeansNoProgressBarEvenIfLevelAndTotalXpAreKnown(
        int level, ulong cumulative)
    {
        var job = new AccountProfessionProgress(
            "Façonneur", level, cumulative);

        ProfessionProgressPresentation.For(job).Should().BeNull();
    }

    [Fact]
    public void AOneSidedThresholdCannotSynthesizeTheOtherOne()
    {
        ProfessionProgressPresentation.For(
            new AccountProfessionProgress("Pêcheur", 4, 141,
                LevelStartCumulativeExperience: 110))
            .Should().BeNull();
        ProfessionProgressPresentation.For(
            new AccountProfessionProgress("Pêcheur", 4, 141,
                NextLevelCumulativeExperience: 190))
            .Should().BeNull();
    }

    [Theory]
    [InlineData(300UL, 300UL, 300UL)]
    [InlineData(300UL, 400UL, 300UL)]
    [InlineData(250UL, 300UL, 400UL)]
    [InlineData(450UL, 300UL, 400UL)]
    public void InconsistentXpEvidenceDoesNotProduceAProgressBar(
        ulong cumulative, ulong start, ulong next)
    {
        ProfessionProgressPresentation.For(
            new AccountProfessionProgress("Bricoleur", 10,
                cumulative, start, next)).Should().BeNull();
    }

    [Fact]
    public void LargeCumulativeExperienceDoesNotOverflowPercentCalculation()
    {
        ulong start = ulong.MaxValue - 1000;
        ulong next = ulong.MaxValue;
        ulong current = ulong.MaxValue - 250;

        ProfessionProgressPresentation.For(new AccountProfessionProgress(
            "Costumage", 199, current, start, next)).Should().Be(
            new ProfessionProgressPresentation.LevelProgress(750, 1000, 75m));
    }

    [Fact]
    public void InvalidProfessionLevelCannotProduceProgress()
    {
        ProfessionProgressPresentation.For(
            new AccountProfessionProgress("Forgeron", 0, 50, 0, 100))
            .Should().BeNull();
        ProfessionProgressPresentation.For(
            new AccountProfessionProgress("Forgeron", 201, 50, 0, 100))
            .Should().BeNull();
    }

    [Fact]
    public void ProgressCalculationsNeverWriteToLiveAccountWorkspace()
    {
        var job = new AccountProfessionProgress("Cordomage", 6, 301, 100, 501);
        ProfessionProgressPresentation.For(job).Should().NotBeNull();

        var current = new CurrentAccountProfessionsState();
        current.ActiveSnapshot.Should().BeNull();
        new CurrentCharacterState().Characters.Should().BeEmpty();
    }
}
