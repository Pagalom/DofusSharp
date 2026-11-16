namespace BestCrush.Domain.Services;

public enum BreedingGauge
{
    Endurance,
    Maturity,
    Love,
    Experience
}

public enum BreedingGaugeMilestone
{
    EnterSerenityRange,
    StopSerenityChange,
    GaugeFull
}

public sealed record BreedingGaugeAlert(
    BreedingGaugeMilestone Milestone, TimeSpan NotifyAfter, TimeSpan HappensAfter);

public static class BreedingGaugeForecast
{
    private const int MinSerenity = -5_000;
    private const int MaxSerenity = 5_000;
    public const int SecondsPerTick = 10;

    public static bool CanProgress(BreedingGauge gauge, int serenity)
    {
        var (min, max) = SerenityRange(gauge);
        return serenity >= min && serenity <= max;
    }

    /// <summary>
    /// Estimates one gauge with the supplied gains for a single enclosure phase.
    /// Recalculate from the next observation if the gains or the active gauges change.
    /// </summary>
    public static IReadOnlyList<BreedingGaugeAlert> Estimate(
        BreedingGauge gauge, int pointsRemaining, int serenity, int serenityDelta,
        int gaugeGain, TimeSpan exitLeadTime)
    {
        if (serenity is < MinSerenity or > MaxSerenity)
            throw new ArgumentOutOfRangeException(nameof(serenity));
        if (serenityDelta is < -MaxSerenity or > MaxSerenity)
            throw new ArgumentOutOfRangeException(nameof(serenityDelta));
        if (exitLeadTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(exitLeadTime));
        if (pointsRemaining <= 0 || gaugeGain <= 0)
            return [];

        var (min, max) = SerenityRange(gauge);

        bool InRange(int value) => value >= min && value <= max;
        int tick = 0;
        bool startedInRange = InRange(serenity);
        while (!InRange(serenity))
        {
            int next = Math.Clamp(serenity + serenityDelta, MinSerenity, MaxSerenity);
            if (next == serenity || (serenity < min && next > max) || (serenity > max && next < min))
                return [];
            serenity = next;
            tick++;
        }

        List<BreedingGaugeAlert> alerts = [];
        if (!startedInRange)
        {
            var entryAt = AtTick(tick);
            alerts.Add(new(BreedingGaugeMilestone.EnterSerenityRange, entryAt, entryAt));
        }

        long ticksToFill = (pointsRemaining - 1) / gaugeGain + 1L;
        // The live maturity reading increased on the tick that entered the
        // window. The tick that takes the mount out does not count.
        if (!startedInRange)
            ticksToFill--;
        int? ticksToExit = serenityDelta switch
        {
            > 0 when max < MaxSerenity => (max - serenity) / serenityDelta + 1,
            < 0 when min > MinSerenity => (serenity - min) / -serenityDelta + 1,
            _ => null
        };
        if (ticksToExit is { } exit && exit <= ticksToFill)
        {
            TimeSpan exitsAt = AtTick(tick + exit);
            TimeSpan notifyAt = exitLeadTime >= exitsAt ? TimeSpan.Zero : exitsAt - exitLeadTime;
            alerts.Add(new(BreedingGaugeMilestone.StopSerenityChange, notifyAt, exitsAt));
        }
        else
        {
            var fullAt = AtTick(tick + ticksToFill);
            alerts.Add(new(BreedingGaugeMilestone.GaugeFull, fullAt, fullAt));
        }

        return alerts.OrderBy(alert => alert.NotifyAfter).ToArray();
    }

    private static TimeSpan AtTick(long tick) => TimeSpan.FromSeconds(tick * (double)SecondsPerTick);

    private static (int Min, int Max) SerenityRange(BreedingGauge gauge) => gauge switch
    {
        BreedingGauge.Endurance => (MinSerenity, -1),
        BreedingGauge.Maturity => (-2_000, 2_000),
        BreedingGauge.Love => (0, MaxSerenity),
        BreedingGauge.Experience => (MinSerenity, MaxSerenity),
        _ => throw new ArgumentOutOfRangeException(nameof(gauge))
    };
}
