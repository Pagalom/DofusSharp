using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public sealed record CrushingPreparationStep(ForgemagingRune Rune, ForgemagingOdds Odds,
    ForgemagingCrushValue OnCriticalSuccess, double EstimatedNetGain, bool CoveredBySink);

public sealed record CrushingPreparationPlan(ForgemagingCrushValue? Current, CrushingPreparationStep? Next,
    string? StopReason, IReadOnlyList<string> Notices);

public static class CrushingPreparationPlanner
{
    public static CrushingPreparationPlan Plan(ForgemagingSession session, IReadOnlyList<ForgemagingRune> runes,
        ForgemagingCrushContext? context)
    {
        if (ForgemagingPlanner.ValidateObservedRoll(session) is { } error) return new(null, null, error, []);
        var current = ForgemagingCrushAssessment.EstimateRoll(session.Level, session.Lines, runes, context);
        if (!FullyPriced(current))
        {
            var notices = current.Missing.Count > 0 ? current.Missing
                : ["Certains rendements avec focus ne peuvent pas être valorisés aux prix relevés."];
            return new(current.Best, null, "Il manque des données pour conseiller une rune.", notices);
        }

        var baseline = current.Best!;
        double? remaining = session.Budget - session.KnownSpent;
        if (remaining is <= 0) return new(baseline, null, "Budget atteint. Conservez le jet actuel.", []);
        if (remaining.HasValue && session.UnpricedAttempts > 0)
            return new(baseline, null, "Le coût de certaines tentatives est inconnu. Le budget restant ne peut pas être vérifié.", []);

        var stats = session.Lines.Select(line => line.Characteristic).ToHashSet();
        bool existingOverOrExo = ForgemagingPlanner.ExcessWeight(session.Lines) > 0;
        bool skippedQuotes = false;
        List<CrushingPreparationStep> candidates = [];
        foreach (var rune in runes.Where(rune => rune.Gain > 0 &&
                     (rune.Characteristic != Characteristic.Hunting || stats.Contains(rune.Characteristic))))
        {
            var odds = ForgemagingPlanner.Evaluate(session.Level, session.Lines, rune);
            if (odds.Blocked is not null) continue;
            if (rune.UnitPrice is not > 0 || !double.IsFinite(rune.UnitPrice.Value))
            {
                skippedQuotes = true;
                continue;
            }
            double price = rune.UnitPrice.Value;
            if (price > remaining) continue;

            var roll = session.Lines.Select(line => line.Copy()).ToList();
            var target = roll.Find(line => line.Characteristic == rune.Characteristic);
            if (target is null)
            {
                target = new() { Characteristic = rune.Characteristic, Current = 0 };
                roll.Add(target);
            }
            target.Current += rune.Gain;

            var projected = ForgemagingCrushAssessment.EstimateRoll(session.Level, roll, runes, context);
            if (!FullyPriced(projected))
            {
                skippedQuotes = true;
                continue;
            }

            var better = projected.Best!;
            double increase = better.Value - baseline.Value;
            if (increase <= 0) continue;

            double consumedWeight = rune.Weight * (target.Current < 0 ? .5 : 1);
            bool coveredBySink = !existingOverOrExo && session.Sink is { } sink && sink + 1e-9 >= consumedWeight;
            double critical = odds.Critical / 100;
            // Without coverage the model cannot price collateral damage reliably. Stress-test
            // every non-critical result against losing the existing crushing value.
            double net = coveredBySink
                ? odds.Success * increase - price
                : critical * increase - (1 - critical) * baseline.Value - price;
            if (net > 0) candidates.Add(new(rune, odds, better, net, coveredBySink));
        }

        var next = candidates.OrderByDescending(candidate => candidate.EstimatedNetGain)
            .ThenByDescending(candidate => candidate.Odds.Critical).ThenBy(candidate => candidate.Rune.Id).FirstOrDefault();
        return new(baseline, next, next is null
            ? "Conservez le jet actuel. Aucune tentative chiffrable ne présente une marge positive avec cette estimation prudente."
            : null, skippedQuotes ? ["Certaines runes ont été écartées faute de prix suffisants."] : []);
    }

    private static bool FullyPriced(CrushingRollEstimate estimate) =>
        estimate.Best is not null && estimate.Missing.Count == 0 &&
        estimate.Lines.All(line => line.FocusQuantity is not > 0 || line.FocusValue is not null);
}
