using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public sealed record ForgemagingCoachingStep(ForgemagingAdvice Advice, string Stage, string Reason);
public sealed record ForgemagingCoachingPlan(IReadOnlyList<ForgemagingCoachingStep> Steps,
    IReadOnlyList<string> Notices, string? StopReason)
{
    public ForgemagingCoachingStep? Next => Steps.FirstOrDefault();
}

/// <summary>Bounded local heuristics, recalculated from the actual roll. No simulated losses or global cost forecast.</summary>
public static class ForgemagingCoach
{
    private static bool Heavy(Characteristic stat) => stat is Characteristic.Ap or Characteristic.Mp or Characteristic.Range;
    private static int NaturalMaximum(ForgemagingLine line) => Math.Max(line.Minimum!.Value, line.Maximum!.Value);

    public static ForgemagingCoachingPlan Plan(ForgemagingSession session, IReadOnlyList<ForgemagingRune> runes)
    {
        if (ForgemagingPlanner.Validate(session) is { } error) return new([], [], error);
        if (session.Lines.All(l => l.Current >= l.Target)) return new([], [], "Le jet actuel atteint tous vos objectifs. Vous pouvez arrêter la FM.");
        var notices = new List<string>();
        if (session.Sink is null) notices.Add("Puits inconnu : le conseil ne suppose aucune réserve disponible.");
        if (session.UnpricedAttempts > 0) notices.Add("Des essais passés n’ont pas de prix : le budget restant exact est inconnu.");
        double? remaining = session.Budget is { } cap ? Math.Max(0, cap - session.KnownSpent) : null;
        if (remaining == 0) return new([], notices, "Budget atteint : arrêtez les tentatives ou ajustez votre budget.");
        var candidates = ForgemagingPlanner.Recommend(session, runes).ToArray();
        if (candidates.Any(a => a.Rune.UnitPrice is not > 0))
            notices.Add("Prix incomplets : les variantes sans prix restent visibles, mais leur intérêt économique ne peut pas être comparé.");
        bool missingNatural = session.Lines.Any(l => l.IsNative && l.Current < Math.Min(l.Target, NaturalMaximum(l)) && !Heavy(l.Characteristic));
        if (missingNatural && ForgemagingPlanner.ExcessWeight(session.Lines) > 0)
            notices.Add("Un over ou exo est déjà présent : les prochains remontages peuvent le dégrader, même avec du puits.");
        var steps = new List<(ForgemagingCoachingStep Step, int Phase, double Weight)>();
        foreach (var group in candidates.GroupBy(a => a.Rune.Characteristic))
        {
            var line = session.Lines.Single(l => l.Characteristic == group.Key);
            var available = group.Where(a => remaining is null || a.Rune.UnitPrice is > 0 && a.Rune.UnitPrice <= remaining).ToArray();
            if (available.Length == 0) continue;
            // Prefer native gains before creating an unnecessary over; retain all variants in the comparison table.
            int Over(ForgemagingAdvice a) => line.IsNative && line.Current + a.Rune.Gain <= NaturalMaximum(line) ? 0 : 1;
            int bestOver = available.Min(Over);
            available = available.Where(a => Over(a) == bestOver).ToArray();
            bool natural = bestOver == 0;
            if (natural && session.Sink > 0 && available.Any(a => a.SinkCovers is true))
                available = available.Where(a => a.SinkCovers is true).ToArray();
            var known = available.Where(a => a.Rune.UnitPrice is > 0).ToArray();
            var chosen = known.Length > 0
                ? known.OrderBy(a => a.CostPerUsefulPoint).ThenBy(a => Math.Max(0, a.Rune.Gain - a.UsefulGain)).ThenBy(a => a.Rune.Weight).ThenBy(a => a.Rune.Id).First()
                : available.OrderByDescending(a => a.Odds.Success).ThenBy(a => Math.Max(0, a.Rune.Gain - a.UsefulGain)).ThenBy(a => a.Rune.Weight).ThenBy(a => a.Rune.Id).First();
            bool delayHeavy = natural && Heavy(group.Key) && missingNatural && session.Sink > 0;
            int phase = !natural ? 3 : delayHeavy ? 2 : Heavy(group.Key) && session.Sink is not > 0 ? 0 : 1;
            string stage = phase switch { 0 => "Stabiliser la ligne lourde", 1 => "Remonter les lignes naturelles", 2 => "Repasser la ligne lourde", _ => "Terminer les overs et exos" };
            string reason = phase switch
            {
                0 => "Remontez cette ligne lourde avant de terminer les lignes plus légères : un repassage tardif pourrait dégrader leur finition.",
                1 when chosen.SinkCovers is true => "Utilisez d’abord le puits disponible pour ce remontage naturel. Couvrir le poids de la rune ne garantit pas sa réussite.",
                1 => "Remontez cette ligne naturelle avant les overs et exos ; les lignes les plus lourdes passent en priorité.",
                2 => "D’autres lignes naturelles restent à remonter : utilisez le puits avant de tenter ce repassage lourd.",
                _ => "Gardez cette finition pour après les remontages naturels afin de limiter les retouches qui pourraient la faire sauter."
            };
            if (known.Length > 0)
                reason += " Cette variante présente le plus faible coût comparatif parmi les variantes chiffrées retenues pour cette étape.";
            else reason += " Prix absent : le choix repose sur les chances estimées et le dépassement du jet visé, pas sur un coût supposé nul.";
            if (chosen.Rune.Gain > chosen.UsefulGain)
                reason += $" Son gain nominal dépasse votre objectif de {chosen.Rune.Gain - chosen.UsefulGain} point(s).";
            if (chosen.Rune.UnitPrice is { } price && remaining is { } budget && price > budget) continue;
            steps.Add((new(chosen, stage, reason), phase, group.Key.GetWeight()));
        }
        var ordered = steps.OrderBy(s => s.Phase).ThenByDescending(s => s.Weight)
            .ThenBy(s => s.Step.Advice.CostPerUsefulPoint ?? double.MaxValue).ThenBy(s => s.Step.Advice.Rune.Id).Select(s => s.Step).ToArray();
        if (ordered.Length == 0)
            return new([], notices, remaining is not null && candidates.Length > 0
                ? "Aucune rune chiffrée ne tient dans le budget restant connu. Vérifiez les prix manquants ou arrêtez les tentatives."
                : "Aucune rune utilisable avec les données disponibles. Vérifiez le jet et les objectifs.");
        return new(ordered, notices, null);
    }
}
