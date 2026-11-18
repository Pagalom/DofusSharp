using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class BreedingMaturityPlanTest
{
    [Fact]
    public void AStopDeadlineAnHourAwayDoesNotPreventPausingNow()
    {
        var currentMount = Mount(1_680, 8_350);
        var plan = Plan(currentMount, Mount(1_450, 8_840), Mount(2_800, 20_000));

        Assert.Equal(TrainingAction.PauseSerenity, plan.Action);
        Assert.True(plan.IsDue(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMinutes(61.5), plan.Mounts[currentMount.Id].Exit!.HappensAfter);
        Assert.Empty(plan.ToProtect);
    }

    [Fact]
    public void KeepsChangingSerenityWhileAnotherMountCanEnterSafely()
    {
        var plan = Plan(Mount(1_680, 8_350), Mount(2_200, 8_020));

        Assert.Equal(TrainingAction.KeepSerenity, plan.Action);
        Assert.False(plan.IsDue(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(200), plan.WaitingUntil);
        Assert.Equal(1, plan.WaitingCount);
    }

    [Fact]
    public void WarnsBeforeTheFirstMountExitsWhenAnotherStillNeedsSerenityChange()
    {
        var atRisk = Mount(-1_900, 8_350);
        var other = Mount(2_600, 7_000);
        var plan = Plan(atRisk, other);

        Assert.Equal(TrainingAction.ProtectMounts, plan.Action);
        Assert.Equal(atRisk.Id, Assert.Single(plan.ToProtect));
        Assert.Equal(TimeSpan.FromSeconds(110), plan.FirstRisk!.HappensAfter);
        Assert.True(plan.IsDue(TimeSpan.Zero));
    }

    [Fact]
    public void AShortMaturityBarCanFinishBeforeAnotherMountArrives()
    {
        var plan = Plan(Mount(-1_900, 19_950), Mount(2_600, 7_000));

        Assert.Equal(TrainingAction.KeepSerenity, plan.Action);
        Assert.Empty(plan.ToProtect);
    }

    [Fact]
    public void UnknownMaturityPreventsAnEnclosureWideStopRecommendation()
    {
        var plan = Plan(Mount(1_680, 8_350), Mount(2_600, null));

        Assert.Equal(TrainingAction.ConfirmReadings, plan.Action);
        Assert.False(plan.IsDue(TimeSpan.Zero));
    }

    [Fact]
    public void StopsIfTheRemainingSerenityFuelCannotBringTheOtherMountIntoRange()
    {
        var plan = BreedingTrainingPlan.From(new(Gauges(100), [Mount(1_100, 8_350), Mount(2_600, 7_000)]), PaddockGauge.Maturity);

        Assert.Equal(TrainingAction.PauseSerenity, plan.Action);
        Assert.Equal(0, plan.WaitingCount);
    }

    [Theory]
    [InlineData(80_000, 10)]
    [InlineData(80_001, 20)]
    [InlineData(140_001, 30)]
    [InlineData(180_001, 40)]
    public void FuelReserveDeterminesPointsPerCycle(int reserve, int points) =>
        Assert.Equal(points, BreedingFuel.PointsPerCycle(reserve));

    [Fact]
    public void ForecastSlowsDownWhenFuelCrossesATier()
    {
        var mount = Mount(0, 19_960);
        var reading = new PaddockReading([new(PaddockGauge.Maturity, true, 80_010)], [mount]);
        var plan = BreedingTrainingPlan.From(reading, PaddockGauge.Maturity);
        Assert.Equal(TimeSpan.FromSeconds(30), plan.Mounts[mount.Id].Full);
        Assert.Equal(TimeSpan.FromSeconds(30), BreedingFuel.TimeToSpend(80_010, 40));
    }

    [Fact]
    public void MountsShareTheFuelAndRechargeOnlyCoversThoseInRange()
    {
        var first = Mount(0, 19_980);
        var second = Mount(0, 19_970);
        var outside = Mount(2_001, 0);
        var reading = new PaddockReading([new(PaddockGauge.Maturity, true, 20)], [first, second, outside]);
        var plan = BreedingTrainingPlan.From(reading, PaddockGauge.Maturity);
        Assert.Equal(30, BreedingTrainingPlan.RefillTarget(reading, PaddockGauge.Maturity));
        Assert.Equal(TimeSpan.FromSeconds(20), plan.Mounts[first.Id].Full);
        Assert.Null(plan.Mounts[second.Id].Full);
        Assert.Equal(TimeSpan.FromSeconds(20), plan.ExhaustedAt);
    }

    [Fact]
    public void LoveAndEnduranceUseTheirOwnSerenityWindowsAndBars()
    {
        var negative = Mount(-1, 20_000) with { Endurance = 19_970, Love = 18_000 };
        var positive = Mount(0, 20_000) with { Endurance = 18_000, Love = 19_960 };
        var reading = new PaddockReading(
            [new(PaddockGauge.Endurance, true, 100), new(PaddockGauge.Love, true, 100)], [negative, positive]);

        var endurance = BreedingTrainingPlan.From(reading, PaddockGauge.Endurance);
        var love = BreedingTrainingPlan.From(reading, PaddockGauge.Love);
        Assert.Equal(30, BreedingTrainingPlan.RefillTarget(reading, PaddockGauge.Endurance));
        Assert.Equal(40, BreedingTrainingPlan.RefillTarget(reading, PaddockGauge.Love));
        Assert.Equal(TimeSpan.FromSeconds(30), endurance.Mounts[negative.Id].Full);
        Assert.Equal(TimeSpan.FromSeconds(40), love.Mounts[positive.Id].Full);
        Assert.Null(endurance.Mounts[positive.Id].Full);
        Assert.Null(love.Mounts[negative.Id].Full);
    }

    [Fact]
    public void ExperienceForecastIgnoresSerenityAndUsesTheSelectedLevel()
    {
        var mount = Mount(-4_000, null) with { Experience = 172_640 };
        var reading = new PaddockReading([new(PaddockGauge.Experience, true, 65)], [mount]);
        var plan = BreedingTrainingPlan.From(reading, PaddockGauge.Experience, 100);
        Assert.Equal(TimeSpan.FromSeconds(30), plan.Mounts[mount.Id].Full);
        Assert.Equal(28, BreedingTrainingPlan.RefillTarget(reading, PaddockGauge.Experience, 100));
        Assert.Equal(867_582 - 172_640, BreedingTrainingPlan.RefillTarget(reading, PaddockGauge.Experience, 200));
    }

    private static PaddockMountReading Mount(int serenity, int? maturity) =>
        new(Guid.NewGuid(), 96, 160_000, serenity, maturity);

    private static BreedingTrainingPlan Plan(params PaddockMountReading[] mounts) =>
        BreedingTrainingPlan.From(new(Gauges(66_440), mounts), PaddockGauge.Maturity);

    private static PaddockGaugeReading[] Gauges(int serenityFuel) =>
        [new(PaddockGauge.SerenityLoss, true, serenityFuel), new(PaddockGauge.Maturity, true, 69_410)];
}
