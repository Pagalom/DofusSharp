using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

internal static class BreedingDecisions
{
    internal static IReadOnlyList<int> EnclosureLevels { get; } = [1, 40, 80, 120, 160, 200];

    internal static bool CanPair(PaddockMountReading first, PaddockMountReading second) =>
        first.Id != second.Id && first.Fertility == BreedingMountFertility.Ready &&
        second.Fertility == BreedingMountFertility.Ready &&
        first.Sex.HasValue && second.Sex.HasValue && first.Sex != second.Sex;

    internal static int? MissingBreedingPoints(PaddockMountReading mount)
    {
        if (mount.Endurance is not { } endurance || mount.Maturity is not { } maturity || mount.Love is not { } love)
            return null;
        return 3 * BreedingFuel.TrainingBarMaximum - endurance - maturity - love;
    }

    internal static int? TransferDestination(PaddockMountReading mount, PaddockGauge gauge, int? source,
        IReadOnlyList<LivePaddockReading> enclosures, int? breederLevel)
    {
        if (!BreedingTrainingRules.CanProgress(gauge, mount.Serenity))
            return null;
        return enclosures.Where(enclosure => enclosure.Enclosure is >= 1 and <= 6 && enclosure.Enclosure != source &&
                enclosure.Reading.Mounts.Count < 10 &&
                (breederLevel is null || breederLevel >= EnclosureLevels[enclosure.Enclosure!.Value - 1]) &&
                enclosure.Reading.Gauges.Any(value => value.Gauge == gauge && value.Active && value.Fuel > 0) &&
                !enclosure.Reading.Gauges.Any(value => value.Active && value.Fuel > 0 &&
                    value.Gauge is PaddockGauge.SerenityGain or PaddockGauge.SerenityLoss))
            .OrderByDescending(enclosure => enclosure.ObservedAtUtc).FirstOrDefault()?.Enclosure;
    }

    internal static (decimal Base, decimal WithOptimakina) TargetGenerationChance(int firstLevel, int secondLevel)
    {
        decimal chance = Math.Clamp(30m + (firstLevel + secondLevel) * 0.15m, 0m, 100m);
        return (chance, Math.Min(100m, chance + 20m));
    }

    internal static decimal MaximumOptimakinaPrice(int firstLevel, int secondLevel, long targetPrice, long alternativePrice)
    {
        var chance = TargetGenerationChance(firstLevel, secondLevel);
        return Math.Max(0m, targetPrice - (decimal)alternativePrice) * (chance.WithOptimakina - chance.Base) / 100m;
    }

}
