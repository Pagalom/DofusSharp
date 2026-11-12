using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public sealed record ForgemagingCrushContext(double? CoefficientPercent, DateTime? ObservedAtUtc, double ValueMultiplier = 1);
public sealed record ForgemagingCrushValue(Characteristic? Focus, double Value)
{
    public IReadOnlyDictionary<long, double>? RuneQuantities { get; init; }
}
public sealed record ForgemagingCrushComparison(ForgemagingCrushValue? Current, ForgemagingCrushValue? Target,
    double? PreparationCeiling, IReadOnlyList<string> Missing, bool IsComplete);

/// <summary>Values two explicit rolls. Does not predict a path, a rune budget, or the probability of reaching the target.</summary>
public static class ForgemagingCrushAssessment
{
    public static ForgemagingCrushComparison Compare(ForgemagingSession session, IReadOnlyList<ForgemagingRune> runes,
        ForgemagingCrushContext? context)
    {
        if (ForgemagingPlanner.Validate(session) is { } error) return new(null, null, null, [error], false);
        return CompareRolls(session.Level, session.Lines,
            session.Lines.Select(line => new ForgemagingLine { Characteristic = line.Characteristic, Minimum = line.Minimum, Maximum = line.Maximum, Current = line.Target }).ToArray(), runes, context);
    }

    public static ForgemagingCrushComparison CompareRolls(int level, IReadOnlyList<ForgemagingLine>? before,
        IReadOnlyList<ForgemagingLine>? after, IReadOnlyList<ForgemagingRune> runes, ForgemagingCrushContext? context,
        bool includeRuneQuantities = false)
    {
        if (level is < 1 or > 200) return new(null, null, null, ["Niveau d’objet manquant ou invalide."], false);
        if (context?.CoefficientPercent is not > 0 || !double.IsFinite(context.CoefficientPercent.Value))
            return new(null, null, null, ["Coefficient de concassage non relevé : consultez l’objet au concasseur puis actualisez les données."], false);
        if (!double.IsFinite(context.ValueMultiplier) || context.ValueMultiplier is < 0 or > 1)
            return new(null, null, null, ["Paramètre de valorisation du concassage invalide."], false);
        var basicRunes = runes.Where(r => !r.Name.StartsWith("Rune Pa ", StringComparison.OrdinalIgnoreCase) &&
                !r.Name.StartsWith("Rune Ra ", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.Characteristic).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id).First());
        var missing = new HashSet<string>();
        ForgemagingCrushValue? Value(IReadOnlyList<ForgemagingLine>? lines)
        {
            if (lines is null || lines.Any(line => line.Current is null))
            {
                missing.Add("Jet incomplet.");
                return null;
            }
            var weights = new Dictionary<Characteristic, double>();
            foreach (var line in lines)
            {
                int value = line.Current!.Value;
                if (value <= 0) continue;
                weights[line.Characteristic] = CrushService.GetCrushWeight(line.Characteristic, value, level);
                // Match CrushService's unfocused rune eligibility and native focus support.
                if (!basicRunes.TryGetValue(line.Characteristic, out var rune))
                { missing.Add($"Rune de base absente du catalogue : {line.Characteristic}."); continue; }
                if (rune.UnitPrice is not > 0 || !double.IsFinite(rune.UnitPrice.Value)) missing.Add($"Prix de rune manquant : {rune.Name}.");
            }
            double multiplier = context.CoefficientPercent.Value / 100 * context.ValueMultiplier;
            double coefficient = context.CoefficientPercent.Value / 100;
            var scenarios = new List<ForgemagingCrushValue>();
            if (weights.Count == 0) return new(null, 0);
            if (weights.Keys.All(stat => basicRunes.TryGetValue(stat, out var rune) && rune.UnitPrice is > 0 && double.IsFinite(rune.UnitPrice.Value)))
                scenarios.Add(new(null, weights.Sum(p => p.Value / p.Key.GetBasicRuneWeight() * basicRunes[p.Key].UnitPrice!.Value) * multiplier)
                {
                    RuneQuantities = includeRuneQuantities
                        ? weights.ToDictionary(p => basicRunes[p.Key].Id, p => p.Value / p.Key.GetBasicRuneWeight() * coefficient)
                        : null
                });
            foreach (var pair in weights)
            {
                // Do not recommend an unverified exotic focus.
                if (!lines.Any(l => l.Characteristic == pair.Key && l.IsNative)) continue;
                if (!basicRunes.TryGetValue(pair.Key, out var focusedRune) || focusedRune.UnitPrice is not > 0 || !double.IsFinite(focusedRune.UnitPrice!.Value)) continue;
                double weight = pair.Value + weights.Where(p => p.Key != pair.Key).Sum(p => p.Value) / 2;
                scenarios.Add(new(pair.Key, weight / pair.Key.GetBasicRuneWeight() * basicRunes[pair.Key].UnitPrice!.Value * multiplier)
                {
                    RuneQuantities = includeRuneQuantities
                        ? new Dictionary<long, double> { [focusedRune.Id] = weight / pair.Key.GetBasicRuneWeight() * coefficient }
                        : null
                });
            }
            return scenarios.OrderByDescending(v => v.Value).ThenBy(v => v.Focus).FirstOrDefault();
        }
        var current = Value(before); var target = Value(after);
        bool complete = missing.Count == 0 && current is not null && target is not null;
        return new(current, target, complete ? target!.Value - current!.Value : null, missing.Order().ToArray(), complete);
    }
}
