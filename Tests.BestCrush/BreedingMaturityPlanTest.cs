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

        Assert.Equal(MaturityAction.PauseSerenity, plan.Action);
        Assert.True(plan.IsDue(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMinutes(61.5), plan.Mounts[currentMount.Id].Exit!.HappensAfter);
        Assert.Empty(plan.ToProtect);
    }

    [Fact]
    public void KeepsChangingSerenityWhileAnotherMountCanEnterSafely()
    {
        var plan = Plan(Mount(1_680, 8_350), Mount(2_200, 8_020));

        Assert.Equal(MaturityAction.KeepSerenity, plan.Action);
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

        Assert.Equal(MaturityAction.ProtectMounts, plan.Action);
        Assert.Equal(atRisk.Id, Assert.Single(plan.ToProtect));
        Assert.Equal(TimeSpan.FromSeconds(110), plan.FirstRisk!.HappensAfter);
        Assert.True(plan.IsDue(TimeSpan.Zero));
    }

    [Fact]
    public void AShortMaturityBarCanFinishBeforeAnotherMountArrives()
    {
        var plan = Plan(Mount(-1_900, 19_950), Mount(2_600, 7_000));

        Assert.Equal(MaturityAction.KeepSerenity, plan.Action);
        Assert.Empty(plan.ToProtect);
    }

    [Fact]
    public void UnknownMaturityPreventsAnEnclosureWideStopRecommendation()
    {
        var plan = Plan(Mount(1_680, 8_350), Mount(2_600, null));

        Assert.Equal(MaturityAction.ConfirmReadings, plan.Action);
        Assert.False(plan.IsDue(TimeSpan.Zero));
    }

    [Fact]
    public void StopsIfTheRemainingSerenityFuelCannotBringTheOtherMountIntoRange()
    {
        var plan = BreedingMaturityPlan.From(new(Gauges(100), [Mount(1_100, 8_350), Mount(2_600, 7_000)]));

        Assert.Equal(MaturityAction.PauseSerenity, plan.Action);
        Assert.Equal(0, plan.WaitingCount);
    }

    private static PaddockMountReading Mount(int serenity, int? maturity) =>
        new(Guid.NewGuid(), 96, 160_000, serenity, maturity);

    private static BreedingMaturityPlan Plan(params PaddockMountReading[] mounts) =>
        BreedingMaturityPlan.From(new(Gauges(66_440), mounts));

    private static PaddockGaugeReading[] Gauges(int serenityFuel) =>
        [new(PaddockGauge.SerenityLoss, true, serenityFuel), new(PaddockGauge.Maturity, true, 69_410)];
}
