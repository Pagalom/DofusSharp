using BestCrush.Network.Protocol;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class BreedingGaugeNoticesTest
{
    private readonly DateTime _now = DateTime.UtcNow;
    private static PaddockMountReading Mount(int serenity, int? love = 0) => new(Guid.NewGuid(), 1, 0, serenity, 0)
        { Endurance = 0, Love = love, Fertility = BreedingMountFertility.Fertile };

    [Fact]
    public void FullGaugeIsUselessButUnknownGaugeDoesNotClaimCompletion()
    {
        var reading = new PaddockReading([new(PaddockGauge.Love, true, 10_000)], [Mount(100, 20_000)]);
        var notices = BreedingGaugeNotices.Build("Enclos 1", reading, [], new Dictionary<Guid, int>(), _now, _now);
        Assert.Equal("Objectifs atteints", Assert.Single(notices).Reason);
        Assert.Empty(BreedingGaugeNotices.Build("Enclos 1", reading with { Mounts = [Mount(100, null)] }, [], new Dictionary<Guid, int>(), _now, _now));
    }

    [Fact]
    public void OutsideRangeWarnsUnlessSerenityIsBringingMountIntoRange()
    {
        var reading = new PaddockReading([new(PaddockGauge.Love, true, 100_000)], [Mount(-500)]);
        Assert.Equal("Hors plage de sérénité", Assert.Single(BreedingGaugeNotices.Build("Enclos 1", reading, [], new Dictionary<Guid, int>(), _now, _now)).Reason);
        reading = reading with { Gauges = [.. reading.Gauges, new(PaddockGauge.SerenityGain, true, 100_000)] };
        var plan = BreedingTrainingPlan.From(reading, PaddockGauge.Love);
        Assert.Empty(BreedingGaugeNotices.Build("Enclos 1", reading, [plan], new Dictionary<Guid, int>(), _now, _now));
    }

    [Fact]
    public void OldReadingsAndInactiveGaugesNeverTriggerInstructions()
    {
        var reading = new PaddockReading([new(PaddockGauge.Endurance, true, 1)], []);
        Assert.Empty(BreedingGaugeNotices.Build("Enclos 1", reading, [], new Dictionary<Guid, int>(), _now.AddSeconds(-31), _now));
        Assert.Empty(BreedingGaugeNotices.Build("Enclos 1", reading with { Gauges = [new(PaddockGauge.Endurance, false, 1)] }, [], new Dictionary<Guid, int>(), _now, _now));
        Assert.Equal("Enclos vide", Assert.Single(BreedingGaugeNotices.Build("Enclos 1", reading, [], new Dictionary<Guid, int>(), _now, _now)).Reason);
    }
}
