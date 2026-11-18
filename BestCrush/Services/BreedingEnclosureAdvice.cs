using BestCrush.Network.Protocol;

namespace BestCrush.Services;

internal static class BreedingEnclosureAdvice
{
    internal static string? Describe(PaddockReading reading, IReadOnlyDictionary<Guid, int> resaleGoals)
    {
        var fertility = BreedingTrainingRules.Gauges.Where(gauge => gauge != PaddockGauge.Experience)
            .Select(gauge => new Demand(gauge, reading.Mounts.Where(mount =>
                BreedingTrainingRules.PointsNeeded(mount, gauge, 100) > 0).ToArray()))
            .Where(demand => demand.Mounts.Length > 0).ToArray();
        bool needsXp = reading.Mounts.Any(mount =>
            BreedingTrainingRules.PointsNeeded(mount, PaddockGauge.Experience, 100, resaleGoals) > 0);
        var usable = fertility.Select(demand => (demand.Gauge,
                Count: demand.Mounts.Count(mount => BreedingTrainingRules.CanProgress(demand.Gauge, mount.Serenity))))
            .Where(value => value.Count > 0).OrderByDescending(value => value.Count)
            .Select(value => value.Gauge).Take(2).ToList();

        var movement = reading.Gauges.FirstOrDefault(gauge => gauge.Active && gauge.Fuel > 0 &&
            gauge.Gauge is PaddockGauge.SerenityGain or PaddockGauge.SerenityLoss);
        if (movement is not null && reading.Gauges.Any(gauge =>
                gauge.Active && fertility.Any(demand => demand.Gauge == gauge.Gauge)))
            return null; // The forecast for that active phase takes precedence.

        if (usable.Count > 0)
        {
            if (usable.Count < 2 && needsXp)
                usable.Add(PaddockGauge.Experience);
            return $"Étape conseillée · {string.Join(" + ", usable.Select(BreedingTrainingRules.Name))}.";
        }
        if (fertility.Length == 0)
            return needsXp ? "Étape conseillée · Expérience." : null;

        var objective = fertility.Select(demand => (demand, Target: demand.Gauge == PaddockGauge.Endurance ? -1_000 : 1_000))
            .OrderBy(candidate => candidate.demand.Mounts.Sum(mount => Math.Abs(mount.Serenity - candidate.Target)))
            .First();
        string target = objective.Target > 0 ? "+1 000" : "−1 000";
        return $"Étape conseillée · Amener les montures concernées vers {target} de sérénité pour {BreedingTrainingRules.Name(objective.demand.Gauge).ToLowerInvariant()}."
            + (needsXp ? " L’expérience peut progresser en parallèle." : "");
    }

    private sealed record Demand(PaddockGauge Gauge, PaddockMountReading[] Mounts);
}
