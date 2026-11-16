using BestCrush.Domain.Services;

namespace Tests.BestCrush;

public sealed class BreedingGaugeForecastTest
{
    [Fact]
    public void MaturityWarnsBeforeSerenityLeavesItsRange()
    {
        var alerts = BreedingGaugeForecast.Estimate(
            BreedingGauge.Maturity, 720, -1_710, -10, 10, TimeSpan.FromMinutes(2));

        var alert = Assert.Single(alerts);
        Assert.Equal(BreedingGaugeMilestone.StopSerenityChange, alert.Milestone);
        Assert.Equal(TimeSpan.FromMinutes(3), alert.NotifyAfter);
        Assert.Equal(TimeSpan.FromMinutes(5), alert.HappensAfter);
    }

    [Fact]
    public void MaturityCanFinishAfterEnteringTheRange()
    {
        var alerts = BreedingGaugeForecast.Estimate(
            BreedingGauge.Maturity, 30, 2_050, -10, 10, TimeSpan.FromMinutes(2));

        Assert.Collection(alerts,
            entry => Assert.Equal(new(BreedingGaugeMilestone.EnterSerenityRange,
                TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(50)), entry),
            full => Assert.Equal(new(BreedingGaugeMilestone.GaugeFull,
                TimeSpan.FromSeconds(70), TimeSpan.FromSeconds(70)), full));
    }

    [Theory]
    [InlineData(BreedingGauge.Endurance, -1, 10)]
    [InlineData(BreedingGauge.Maturity, 2_000, 10)]
    [InlineData(BreedingGauge.Maturity, -2_000, -10)]
    [InlineData(BreedingGauge.Love, 0, -10)]
    public void AFullGaugeOnTheExitTickIsNotAssumed(
        BreedingGauge gauge, int serenity, int change)
    {
        var alert = Assert.Single(BreedingGaugeForecast.Estimate(
            gauge, 1, serenity, change, 10, TimeSpan.FromMinutes(2)));

        Assert.Equal(new(BreedingGaugeMilestone.StopSerenityChange,
            TimeSpan.Zero, TimeSpan.FromSeconds(10)), alert);
    }

    [Theory]
    [InlineData(BreedingGauge.Love, -100, -10)]
    [InlineData(BreedingGauge.Endurance, 0, 0)]
    [InlineData(BreedingGauge.Maturity, 2_100, -5_000)]
    public void NoDeadlineIfTheMountNeverEntersTheRange(
        BreedingGauge gauge, int serenity, int change)
    {
        Assert.Empty(BreedingGaugeForecast.Estimate(
            gauge, 100, serenity, change, 10, TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void ExperienceContinuesWhenSerenityReachesItsLimit()
    {
        var alert = Assert.Single(BreedingGaugeForecast.Estimate(
            BreedingGauge.Experience, 17, 5_000, 10, 10, TimeSpan.FromMinutes(2)));

        Assert.Equal(new(BreedingGaugeMilestone.GaugeFull,
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20)), alert);
    }
}
