using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed record BreedingGaugeNotice(string Location, PaddockGauge Gauge, string Title, string Reason, DateTime? Deadline = null);

internal static class BreedingGaugeNotices
{
    internal static IReadOnlyList<BreedingGaugeNotice> Build(string location, PaddockReading reading,
        IReadOnlyList<BreedingTrainingPlan> plans, IReadOnlyDictionary<Guid, int> goals, DateTime observed, DateTime now)
    {
        // Never turn an old snapshot into a current instruction.
        if (now - observed > TimeSpan.FromSeconds(30)) return [];
        var result = new List<BreedingGaugeNotice>();
        foreach (var gauge in reading.Gauges.Where(g => g.Active))
        {
            var name = BreedingTrainingRules.Name(gauge.Gauge);
            if (reading.Mounts.Count == 0)
            { result.Add(new(location, gauge.Gauge, $"Couper {name.ToLowerInvariant()}", "Enclos vide")); continue; }
            if (gauge.Gauge is PaddockGauge.SerenityGain or PaddockGauge.SerenityLoss)
            {
                if (reading.Mounts.All(m => !BreedingTrainingRules.CanProgress(gauge.Gauge, m.Serenity)))
                    result.Add(new(location, gauge.Gauge, $"Couper {name.ToLowerInvariant()}", "Limite de sérénité atteinte"));
                else if (plans.Any(p => p.Action == TrainingAction.PauseSerenity && p.SerenityGauge == gauge.Gauge))
                    result.Add(new(location, gauge.Gauge, $"Couper {name.ToLowerInvariant()}", "Plage utile atteinte"));
                continue;
            }
            var needs = reading.Mounts.Select(m => BreedingTrainingRules.PointsNeeded(m, gauge.Gauge, 100, goals)).ToArray();
            if (needs.All(n => n == 0))
                result.Add(new(location, gauge.Gauge, $"Couper {name.ToLowerInvariant()}", "Objectifs atteints"));
            else if (needs.All(n => n.HasValue) && !reading.Mounts.Any(m =>
                BreedingTrainingRules.PointsNeeded(m, gauge.Gauge, 100, goals) > 0 && BreedingTrainingRules.CanProgress(gauge.Gauge, m.Serenity)) &&
                !plans.Any(p => p.Gauge == gauge.Gauge && p.Mounts.Values.Any(m => m.Entry is not null)))
                result.Add(new(location, gauge.Gauge, $"Ajuster {name.ToLowerInvariant()}", "Hors plage de sérénité"));
            var plan = plans.FirstOrDefault(p => p.Gauge == gauge.Gauge);
            if (plan?.FirstRisk is { } risk)
                result.Add(new(location, gauge.Gauge, "Déplacer les montures à protéger", $"{name} · sortie de plage", observed + risk.NotifyAfter));
        }
        return result;
    }
}
