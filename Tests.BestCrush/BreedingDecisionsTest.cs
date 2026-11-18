using BestCrush.Network.Protocol;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class BreedingDecisionsTest
{
    [Fact]
    public void OptimakinaIsOnlyWorthTheExtraChanceRemainingBeforeOneHundredPercent()
    {
        Assert.Equal((90m, 100m), BreedingDecisions.TargetGenerationChance(200, 200));
        Assert.Equal(1_000m, BreedingDecisions.MaximumOptimakinaPrice(200, 200, 13_000, 3_000));
    }

    [Fact]
    public void ATransferNeedsRoomAnUnlockedEnclosureAndTheRightActiveGauge()
    {
        var mount = new PaddockMountReading(Guid.NewGuid(), 1, 0, -500, 0);
        var now = DateTime.UtcNow;
        var second = new LivePaddockReading(new([new(PaddockGauge.Endurance, true, 5_000)], []), now, 1, 1, 2);
        Assert.Equal(2, BreedingDecisions.TransferDestination(mount, PaddockGauge.Endurance, 1, [second], 40));
        Assert.Null(BreedingDecisions.TransferDestination(mount, PaddockGauge.Endurance, 1, [second], 39));
        Assert.Null(BreedingDecisions.TransferDestination(mount with { Serenity = 500 }, PaddockGauge.Endurance, 1, [second], 40));
    }
}
