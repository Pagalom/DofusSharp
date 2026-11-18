using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

internal enum TrainingAction
{
    None,
    PauseSerenity,
    KeepSerenity,
    ProtectMounts,
    ConfirmReadings
}

internal sealed record MountTrainingPlan(
    bool CanProgress, TimeSpan? Entry, TimeSpan? Full, BreedingGaugeAlert? Exit);

internal sealed record BreedingTrainingPlan(
    PaddockGauge Gauge,
    TrainingAction Action,
    PaddockGauge? SerenityGauge,
    TimeSpan? WaitingUntil,
    BreedingGaugeAlert? FirstRisk,
    IReadOnlyDictionary<Guid, MountTrainingPlan> Mounts,
    IReadOnlySet<Guid> ToProtect,
    int WaitingCount)
{
    internal TimeSpan? ExhaustedAt { get; init; }

    internal static int RefillTarget(PaddockReading reading, PaddockGauge gauge, int experienceLevel = 100,
        IReadOnlyDictionary<Guid, int>? resaleGoals = null) => reading.Mounts
        .Where(mount => BreedingTrainingRules.CanProgress(gauge, mount.Serenity))
        .Select(mount => BreedingTrainingRules.FuelNeeded(mount, gauge,
            BreedingTrainingRules.PointsNeeded(mount, gauge, experienceLevel, resaleGoals) ?? 0))
        .DefaultIfEmpty().Max();

    internal static BreedingTrainingPlan From(PaddockReading reading, PaddockGauge gauge, int experienceLevel = 100,
        IReadOnlyDictionary<Guid, int>? resaleGoals = null)
    {
        var training = reading.Gauges.Single(value => value.Gauge == gauge);
        var serenity = gauge == PaddockGauge.Experience ? null : reading.Gauges.FirstOrDefault(value =>
            value.Active && value.Fuel > 0 && value.Gauge is PaddockGauge.SerenityGain or PaddockGauge.SerenityLoss);
        var toProtect = new HashSet<Guid>();
        var plan = new BreedingTrainingPlan(gauge, TrainingAction.None, serenity?.Gauge, null, null,
            new Dictionary<Guid, MountTrainingPlan>(), toProtect, 0);
        if (!training.Active)
            return plan;

        int? Needed(PaddockMountReading mount) => BreedingTrainingRules.PointsNeeded(mount, gauge, experienceLevel, resaleGoals);
        var simulation = BreedingTrainingSimulation.Run(reading, gauge, training.Fuel, serenity, Needed);
        var mounts = simulation.Mounts;
        plan = plan with { Mounts = mounts, ExhaustedAt = simulation.ExhaustedAt };

        if (serenity is null || mounts.Count == 0)
            return plan;
        if (reading.Mounts.Any(mount => Needed(mount) is null))
            return plan with { Action = TrainingAction.ConfirmReadings };
        if (mounts.Values.All(mount => mount.CanProgress))
        {
            var paused = BreedingTrainingSimulation.Run(reading, gauge, training.Fuel, null, Needed);
            return plan with
            {
                Action = TrainingAction.PauseSerenity,
                Mounts = mounts.ToDictionary(pair => pair.Key,
                    pair => pair.Value with { Full = paused.Mounts[pair.Key].Full }),
                ExhaustedAt = paused.ExhaustedAt
            };
        }

        var arrivals = mounts.Values.Where(mount => mount.Entry.HasValue)
            .Select(mount => mount.Entry!.Value).ToArray();
        if (arrivals.Length == 0)
            return plan with { Action = TrainingAction.PauseSerenity };

        TimeSpan lastEntry = arrivals.Max();
        BreedingGaugeAlert? firstRisk = null;
        foreach (var (id, mount) in mounts)
        {
            if (mount.Exit is not { } risk || risk.HappensAfter > lastEntry)
                continue;
            toProtect.Add(id);
            if (firstRisk is null || risk.HappensAfter < firstRisk.HappensAfter)
                firstRisk = risk;
        }
        return plan with
        {
            Action = firstRisk is null ? TrainingAction.KeepSerenity : TrainingAction.ProtectMounts,
            FirstRisk = firstRisk,
            WaitingUntil = lastEntry,
            WaitingCount = arrivals.Length
        };
    }

    internal bool IsDue(TimeSpan elapsed) => Action == TrainingAction.PauseSerenity ||
        (FirstRisk is { } risk && elapsed >= risk.NotifyAfter);

    internal string? Describe(DateTime observedAtUtc)
    {
        string change = SerenityGauge == PaddockGauge.SerenityLoss ? "la perte" : "le gain";
        string waiting = WaitingCount == 1 ? "1 monture attend" : $"{WaitingCount} montures attendent";
        string name = BreedingTrainingRules.Name(Gauge);
        return Action switch
        {
            TrainingAction.PauseSerenity when Mounts.Values.All(mount => mount.CanProgress) =>
                $"{name} · Couper {change} de sérénité maintenant. Toutes les montures concernées sont dans la plage.",
            TrainingAction.PauseSerenity =>
                $"{name} · Couper {change} de sérénité. Avec cette réserve, aucune autre monture n’entre dans la plage.",
            TrainingAction.KeepSerenity =>
                $"{name} · Maintenir {change} de sérénité. {waiting} d’entrer dans la plage, au plus tard vers {At(observedAtUtc, WaitingUntil!.Value)}.",
            TrainingAction.ProtectMounts =>
                $"{name} · {waiting} d’entrer dans la plage. Si tu maintiens {change} de sérénité, prévoir un autre enclos pour les montures signalées. Première sortie vers {At(observedAtUtc, FirstRisk!.HappensAfter)}.",
            TrainingAction.ConfirmReadings =>
                $"{name} · Vérifier les valeurs manquantes avant de couper la sérénité pour tout l’enclos.",
            _ => null
        };
    }

    internal string? DescribeMount(Guid id, DateTime observedAtUtc)
    {
        if (!Mounts.TryGetValue(id, out var mount))
            return null;
        if (ToProtect.Contains(id) && mount.Exit is { } exit)
            return $"À déplacer avant la sortie de plage vers {At(observedAtUtc, exit.HappensAfter)}";
        string entry = mount.Entry is { } arrival ? $"Entrée vers {At(observedAtUtc, arrival)}" : "";
        if (mount.Full is { } full)
            return (entry.Length > 0 ? entry + " · " : "") +
                $"{(Gauge == PaddockGauge.Experience ? "Niveau cible atteint" : "Jauge pleine")} vers {At(observedAtUtc, full)}" +
                (Action == TrainingAction.PauseSerenity ? " à sérénité stable" : "");
        if (entry.Length > 0) return entry;
        if (mount.CanProgress && ExhaustedAt is { } exhausted)
            return exhausted == TimeSpan.Zero ? "Recharge nécessaire" : $"Recharge vers {At(observedAtUtc, exhausted)}";
        return mount.CanProgress ? "En progression" : "Hors de la plage de sérénité";
    }

    internal static string At(DateTime observedAtUtc, TimeSpan delay)
    {
        var origin = observedAtUtc.ToLocalTime();
        var deadline = observedAtUtc.Add(delay).ToLocalTime();
        return deadline.ToString(deadline.Date == origin.Date ? "HH:mm" : "dd/MM HH:mm");
    }
}
