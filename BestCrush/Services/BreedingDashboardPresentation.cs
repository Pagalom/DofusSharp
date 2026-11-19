using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed record BreedingGaugeDisplay(PaddockGauge Gauge, DateTime? Deadline, string? Status, bool Conditional = false);
public sealed record BreedingMountDisplay(Guid Id, int Number, PaddockMountReading? Reading,
    BreedingSpecies? Species, long? Value, IReadOnlyList<BreedingGaugeDisplay> Forecasts, int? TargetLevel = null);

internal static class BreedingDashboardPresentation
{
    internal static bool NeedsBreedingTraining(PaddockMountReading? mount) =>
        new[] { mount?.Endurance, mount?.Maturity, mount?.Love }.Any(value => value is < BreedingFuel.TrainingBarMaximum);

    internal static string MountTooltipHtml(BreedingMountDisplay mount)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        string serenityIcon = mount.Reading?.Serenity switch
        { < 0 => "emoji-frown-fill", > 0 => "emoji-smile-fill", 0 => "emoji-neutral-fill", _ => "question-circle-fill" };
        string serenityValue = mount.Reading?.Serenity.ToString("+0;-0;0") ?? "Non relevée";
        string serenity = $"<span class=\"breeding-need need-serenity\" role=\"img\" aria-label=\"Sérénité : {Encode(serenityValue)}\"><i class=\"bi bi-{serenityIcon}\" aria-hidden=\"true\"></i></span>";
        static string Need(string name, string tone, string icon, int? value)
        {
            string state = value switch { null => "is-unknown", >= BreedingFuel.TrainingBarMaximum => "is-full", <= 0 => "is-empty", _ => "is-partial" };
            int quarter = value is { } points ? Math.Clamp((int)(4L * points / BreedingFuel.TrainingBarMaximum), 0, 4) : 0;
            string text = value is { } current ? $"{current:N0} / {BreedingFuel.TrainingBarMaximum:N0}" : "Non relevée";
            string glyph = value is null ? "question" : icon;
            return $"<span class=\"breeding-need need-{tone} {state} quarter-{quarter}\" role=\"img\" aria-label=\"{name} : {Encode(text)}\"><i class=\"bi bi-{glyph}\" aria-hidden=\"true\"></i></span>";
        }
        return $"<strong class=\"breeding-tooltip-name\">{Encode(mount.Species?.Name ?? "Monture")}</strong><span class=\"breeding-tooltip-needs\">" + serenity +
            Need("Endurance", "endurance", "lightning-charge-fill", mount.Reading?.Endurance) +
            Need("Maturité", "maturity", "droplet-fill", mount.Reading?.Maturity) +
            Need("Amour", "love", "heart-fill", mount.Reading?.Love) + "</span>";
    }

    internal static bool NeedsSynchronization(DateTime? observed, bool complete, DateTime now) =>
        !complete || observed is null || now - observed.Value > TimeSpan.FromHours(2);

    internal static BreedingGaugeDisplay Forecast(BreedingTrainingPlan plan, Guid id, DateTime observed)
    {
        if (!plan.Mounts.TryGetValue(id, out var mount)) return new(plan.Gauge, null, null);
        if (plan.ToProtect.Contains(id) && mount.Exit is { } exit)
            return new(plan.Gauge, observed + exit.HappensAfter, "Sortie de plage");
        if (mount.Full is { } full)
            return new(plan.Gauge, observed + full, "Objectif", plan.Action == TrainingAction.PauseSerenity);
        if (mount.Entry is { } entry) return new(plan.Gauge, observed + entry, "Entrée dans la plage");
        if (mount.CanProgress && plan.ExhaustedAt is { } empty)
            return new(plan.Gauge, observed + empty, "Recharge");
        return new(plan.Gauge, null, mount.CanProgress ? "En cours" : "Hors plage");
    }

    internal static string Head(long? speciesId, long? family = null) => speciesId is > 0 and <= int.MaxValue &&
        BreedingLineage.Rides.TryGetValue((int)speciesId.Value, out var ride)
        ? $"img/dofus/mounts/{ride.ItemId}.png" : FallbackHead(family);

    internal static string FallbackHead(long? family) => family switch
    {
        1 => "img/breeding/dragodinde.svg", 2 => "img/breeding/muldo.svg", 3 => "img/breeding/volkorne.svg",
        _ => "img/mount.svg"
    };

    internal static string Icon(PaddockGauge gauge) => gauge switch
    {
        PaddockGauge.Endurance => "shield", PaddockGauge.Maturity => "flower1", PaddockGauge.Love => "heart",
        PaddockGauge.Experience => "stars", PaddockGauge.SerenityGain => "arrow-up", _ => "arrow-down"
    };

    internal static string Duration(DateTime deadline, DateTime now)
    {
        var remaining = deadline - now;
        if (remaining <= TimeSpan.Zero) return "À vérifier";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays} j {remaining.Hours} h";
        return remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours} h {remaining.Minutes:00}"
            : $"{remaining.Minutes} min {remaining.Seconds:00}";
    }
}
