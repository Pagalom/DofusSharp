using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

internal enum MaturityAction
{
    None,
    PauseSerenity,
    KeepSerenity,
    ProtectMounts,
    ConfirmReadings
}

internal sealed record MountMaturityPlan(
    bool CanProgress, TimeSpan? Entry, TimeSpan? Full, BreedingGaugeAlert? Exit);

internal sealed record BreedingMaturityPlan(
    MaturityAction Action,
    PaddockGauge? SerenityGauge,
    TimeSpan? WaitingUntil,
    BreedingGaugeAlert? FirstRisk,
    IReadOnlyDictionary<Guid, MountMaturityPlan> Mounts,
    IReadOnlySet<Guid> ToProtect,
    int WaitingCount)
{
    private const int PointsPerCycle = 10;
    private const int FirstFuelTier = 80_000;
    private const int FullMaturity = 20_000;
    private static readonly TimeSpan LeadTime = TimeSpan.FromMinutes(2);

    internal static BreedingMaturityPlan From(PaddockReading reading)
    {
        var maturity = reading.Gauges.Single(gauge => gauge.Gauge == PaddockGauge.Maturity);
        var serenity = reading.Gauges.FirstOrDefault(gauge => gauge.Active && gauge.Fuel > 0 &&
            gauge.Gauge is PaddockGauge.SerenityGain or PaddockGauge.SerenityLoss);
        var mounts = new Dictionary<Guid, MountMaturityPlan>();
        var toProtect = new HashSet<Guid>();
        var plan = new BreedingMaturityPlan(MaturityAction.None, serenity?.Gauge, null, null, mounts, toProtect, 0);
        if (!maturity.Active || maturity.Fuel <= 0)
            return plan;

        int serenityDelta = serenity?.Gauge switch
        {
            PaddockGauge.SerenityGain => PointsPerCycle,
            PaddockGauge.SerenityLoss => -PointsPerCycle,
            _ => 0
        };
        bool canForecast = maturity.Fuel <= FirstFuelTier && (serenity?.Fuel ?? 0) <= FirstFuelTier;
        foreach (var mount in reading.Mounts)
        {
            if (mount.Maturity is not { } progress || progress >= FullMaturity)
                continue;

            bool canProgress = BreedingGaugeForecast.CanProgress(BreedingGauge.Maturity, mount.Serenity);
            if (!canForecast)
            {
                mounts.Add(mount.Id, new(canProgress, null, null, null));
                continue;
            }

            var forecast = BreedingGaugeForecast.Estimate(BreedingGauge.Maturity, FullMaturity - progress,
                mount.Serenity, serenityDelta, PointsPerCycle, LeadTime);
            var entry = forecast.SingleOrDefault(alert => alert.Milestone == BreedingGaugeMilestone.EnterSerenityRange);
            var exit = forecast.SingleOrDefault(alert => alert.Milestone == BreedingGaugeMilestone.StopSerenityChange);
            var full = forecast.SingleOrDefault(alert => alert.Milestone == BreedingGaugeMilestone.GaugeFull);
            if (entry is not null && !HasFuel(serenity, entry.HappensAfter))
                entry = null;
            if (exit is not null && (!HasFuel(serenity, exit.HappensAfter) || (!canProgress && entry is null)))
                exit = null;
            if (full is not null && (!HasFuel(maturity, full.HappensAfter) || (!canProgress && entry is null)))
                full = null;

            mounts.Add(mount.Id, new(canProgress, entry?.HappensAfter, full?.HappensAfter, exit));
        }

        if (serenity is null || mounts.Count == 0)
            return plan;
        // When every unfinished mount is already in range, the decision holds
        // regardless of the per-cycle rate for either gauge.
        if (reading.Mounts.Any(mount => mount.Maturity is null))
            return plan with { Action = MaturityAction.ConfirmReadings };
        if (mounts.Values.All(mount => mount.CanProgress))
            return plan with { Action = MaturityAction.PauseSerenity };
        if (!canForecast)
            return plan;

        var arrivals = mounts.Values.Where(mount => mount.Entry.HasValue)
            .Select(mount => mount.Entry!.Value).ToArray();
        if (arrivals.Length == 0)
            return plan with { Action = MaturityAction.PauseSerenity };

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
            Action = firstRisk is null ? MaturityAction.KeepSerenity : MaturityAction.ProtectMounts,
            FirstRisk = firstRisk,
            WaitingUntil = lastEntry,
            WaitingCount = arrivals.Length
        };
    }

    internal bool IsDue(TimeSpan elapsed) => Action == MaturityAction.PauseSerenity ||
        (FirstRisk is { } risk && elapsed >= risk.NotifyAfter);

    internal string? Describe(DateTime observedAtUtc)
    {
        string change = SerenityGauge == PaddockGauge.SerenityLoss ? "la perte" : "le gain";
        string waiting = WaitingCount == 1 ? "1 monture attend" : $"{WaitingCount} montures attendent";
        return Action switch
        {
            MaturityAction.PauseSerenity when Mounts.Values.All(mount => mount.CanProgress) =>
                $"Pour la maturité, couper {change} de sérénité maintenant. Toutes les montures qui en ont besoin sont dans la plage.",
            MaturityAction.PauseSerenity =>
                $"Couper {change} de sérénité. Avec le carburant actuel, ce réglage ne fait entrer aucune autre monture dans la plage de maturité.",
            MaturityAction.KeepSerenity =>
                $"Maintenir {change} de sérénité pour l’instant. {waiting} d’entrer dans la plage. Dernière entrée estimée vers {At(observedAtUtc, WaitingUntil!.Value)}.",
            MaturityAction.ProtectMounts =>
                $"{waiting} d’entrer dans la plage. Si tu maintiens {change} de sérénité, prévoir un autre enclos pour les montures signalées. Première sortie de plage vers {At(observedAtUtc, FirstRisk!.HappensAfter)}.",
            MaturityAction.ConfirmReadings =>
                "Vérifier la maturité des montures sans valeur avant de couper la sérénité pour tout l’enclos.",
            _ => null
        };
    }

    internal string DescribeMount(Guid id, DateTime observedAtUtc)
    {
        if (!Mounts.TryGetValue(id, out var mount))
            return "—";
        if (mount.Entry is { } entry)
            return $"Entrée dans la plage vers {At(observedAtUtc, entry)}";
        if (ToProtect.Contains(id) && mount.Exit is { } exit)
            return $"À déplacer avant la sortie de plage vers {At(observedAtUtc, exit.HappensAfter)}";
        if (mount.Full is { } full && Action != MaturityAction.PauseSerenity)
            return $"Maturité pleine vers {At(observedAtUtc, full)}";
        return mount.CanProgress ? "Maturité en progression" : "Hors de la plage de maturité";
    }

    private static string At(DateTime observedAtUtc, TimeSpan delay) =>
        observedAtUtc.Add(delay).ToLocalTime().ToString("HH:mm");

    private static bool HasFuel(PaddockGaugeReading? gauge, TimeSpan time) =>
        gauge is not null && time.TotalSeconds / BreedingGaugeForecast.SecondsPerTick * PointsPerCycle <= gauge.Fuel;
}
