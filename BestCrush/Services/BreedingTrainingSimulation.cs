using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

internal sealed record BreedingTrainingSimulation(
    IReadOnlyDictionary<Guid, MountTrainingPlan> Mounts,
    TimeSpan? ExhaustedAt)
{
    private static readonly TimeSpan LeadTime = TimeSpan.FromMinutes(2);

    internal static BreedingTrainingSimulation Run(PaddockReading reading, PaddockGauge gauge, int fuel,
        PaddockGaugeReading? serenity, Func<PaddockMountReading, int?> neededPoints)
    {
        var mounts = reading.Mounts.Select(mount => new Mount(mount, gauge,
            neededPoints(mount) ?? 0))
            .Where(mount => mount.NeedsTraining).ToArray();
        if (serenity is null)
        {
            int needed = mounts.Where(mount => mount.Eligible).Select(mount => mount.FuelNeeded).DefaultIfEmpty().Max();
            return new(mounts.ToDictionary(mount => mount.Id, mount => new MountTrainingPlan(mount.Eligible, null,
                mount.Eligible ? BreedingFuel.TimeToSpend(fuel, mount.FuelNeeded) : null, null)),
                needed >= fuel ? BreedingFuel.TimeToSpend(fuel, fuel) : null);
        }
        int serenityFuel = serenity?.Fuel ?? 0;
        int direction = serenity?.Gauge switch
        {
            PaddockGauge.SerenityLoss => -1,
            PaddockGauge.SerenityGain => 1,
            _ => 0
        };
        TimeSpan? exhaustedAt = fuel == 0 ? TimeSpan.Zero : null;
        for (int cycle = 1; mounts.Any(mount => mount.NeedsTraining); cycle++)
        {
            int shift = direction != 0 && mounts.Any(mount => mount.Serenity != direction * 5_000)
                ? BreedingFuel.PointsPerCycle(serenityFuel) * direction : 0;
            int growth = BreedingFuel.PointsPerCycle(fuel);
            if (shift == 0 && (growth == 0 || !mounts.Any(mount => mount.NeedsTraining && mount.Eligible)))
                break;

            var time = TimeSpan.FromSeconds(cycle * BreedingGaugeForecast.SecondsPerTick);
            serenityFuel -= Math.Abs(shift);
            foreach (var mount in mounts)
                mount.Shift(shift, time);

            if (growth == 0 || !mounts.Any(mount => mount.NeedsTraining && mount.Eligible))
                continue;

            fuel -= growth;
            foreach (var mount in mounts)
            {
                if (mount.NeedsTraining && mount.Eligible)
                    mount.Grow(growth, time);
            }
            if (fuel == 0)
                exhaustedAt = time;
        }

        return new(mounts.ToDictionary(mount => mount.Id, mount => new MountTrainingPlan(mount.InitiallyEligible,
                mount.Entry, mount.Full, mount.Exit)), exhaustedAt);
    }

    private sealed class Mount(PaddockMountReading reading, PaddockGauge gauge, int missing)
    {
        internal Guid Id => reading.Id;
        internal int Serenity { get; private set; } = reading.Serenity;
        private int Missing { get; set; } = missing;
        internal bool NeedsTraining => Missing > 0;
        internal int FuelNeeded => BreedingTrainingRules.FuelNeeded(reading, gauge, Missing);
        internal bool Eligible => BreedingTrainingRules.CanProgress(gauge, Serenity);
        internal bool InitiallyEligible => BreedingTrainingRules.CanProgress(gauge, reading.Serenity);
        internal TimeSpan? Entry { get; private set; }
        internal TimeSpan? Full { get; set; }
        internal BreedingGaugeAlert? Exit { get; private set; }

        internal void Shift(int points, TimeSpan time)
        {
            if (points == 0)
                return;
            bool wasEligible = Eligible;
            Serenity = Math.Clamp(Serenity + points, -5_000, 5_000);
            if (!NeedsTraining || wasEligible == Eligible)
                return;
            if (Eligible)
                Entry ??= time;
            else
                Exit ??= new(BreedingGaugeMilestone.StopSerenityChange,
                    time > LeadTime ? time - LeadTime : TimeSpan.Zero, time);
        }

        internal void Grow(int points, TimeSpan time)
        {
            Missing -= points * BreedingTrainingRules.GainMultiplier(reading, gauge);
            if (!NeedsTraining)
                Full = time;
        }
    }
}
