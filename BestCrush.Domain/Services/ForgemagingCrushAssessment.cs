using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public sealed record ForgemagingCrushContext(double? CoefficientPercent, DateTime? ObservedAtUtc, double ValueMultiplier = 1)
{
    public IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> ResalePrices { get; init; } =
        new Dictionary<(long, int), MarketPriceObservation>();
}
public sealed record ForgemagingCrushValue(Characteristic? Focus, double Value)
{
    public IReadOnlyDictionary<long, double>? RuneQuantities { get; init; }
    public DataFreshness? PriceFreshness { get; init; }
}
public sealed record ForgemagingCrushComparison(ForgemagingCrushValue? Current, ForgemagingCrushValue? Target,
    double? PreparationCeiling, IReadOnlyList<string> Missing, bool IsComplete);

public sealed record CrushingRuneLine(Characteristic Stat, ForgemagingRune? Rune, double? StandardQuantity,
    double? StandardValue, double? FocusQuantity, double? FocusValue, double? UnitPrice);

public sealed record CrushingRollEstimate(IReadOnlyList<CrushingRuneLine> Lines, double? StandardTotal,
    ForgemagingCrushValue? Best, IReadOnlyList<string> Missing);

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
        var current = EstimateRoll(level, before, runes, context, includeRuneQuantities);
        var target = EstimateRoll(level, after, runes, context, includeRuneQuantities);
        var missing = current.Missing.Union(target.Missing).Order().ToArray();
        bool complete = missing.Length == 0 && current.Best is not null && target.Best is not null;
        return new(current.Best, target.Best, complete ? target.Best!.Value - current.Best!.Value : null, missing, complete);
    }

    public static CrushingRollEstimate EstimateRoll(int level, IReadOnlyList<ForgemagingLine>? lines,
        IReadOnlyList<ForgemagingRune> runes, ForgemagingCrushContext? context, bool includeRuneQuantities = false)
    {
        if (level is < 1 or > 200) return new([], null, null, ["Niveau d’objet manquant ou invalide."]);
        if (lines is null || lines.Any(line => line.Current is null)) return new([], null, null, ["Jet incomplet."]);
        if (context is not null && (!double.IsFinite(context.ValueMultiplier) || context.ValueMultiplier is < 0 or > 1))
            return new([], null, null, ["Paramètre de valorisation du concassage invalide."]);

        var basicRunes = runes.Where(r => !r.Name.StartsWith("Rune Pa ", StringComparison.OrdinalIgnoreCase) &&
                !r.Name.StartsWith("Rune Ra ", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.Characteristic).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id).First());
        var observations = context?.ResalePrices ?? new Dictionary<(long, int), MarketPriceObservation>();
        var observedCoefficient = context?.CoefficientPercent;
        double? coefficient = observedCoefficient is > 0 && double.IsFinite(observedCoefficient.Value)
            ? observedCoefficient.Value / 100 : null;
        double multiplier = context?.ValueMultiplier ?? 1;
        var weights = lines.ToDictionary(line => line.Characteristic, line => line.Current is > 0
            ? CrushService.GetCrushWeight(line.Characteristic, line.Current.Value, level) : 0);
        double totalWeight = weights.Values.Sum();
        List<string> missing = [];
        List<CrushingRuneLine> rows = [];
        List<ForgemagingCrushValue> scenarios = [];
        List<MarketPriceObservation> standardObservations = [];
        Dictionary<long, double> standardQuantities = [];
        double? standardTotal = coefficient.HasValue ? 0 : null;
        if (coefficient is null) missing.Add("Coefficient de concassage non relevé : consultez l’objet au concasseur puis actualisez les données.");

        foreach (var line in lines)
        {
            var stat = line.Characteristic;
            var rune = basicRunes.GetValueOrDefault(stat);
            double weight = weights[stat], basicWeight = stat.GetBasicRuneWeight();
            double? quantity = weight / basicWeight * coefficient;
            double? focusQuantity = line.IsNative && weight > 0 ? (weight + (totalWeight - weight) / 2) / basicWeight * coefficient : null;
            double? unitPrice = rune is not null && observations.GetValueOrDefault((rune.Id, 1)) is { Price: > 0, IsCleared: false } quote
                ? MarketPriceService.ResaleLotPrice(quote.Price) : null;
            MarketValueResult? standardSale = quantity is > 0 && rune is not null
                ? MarketPriceCalculator.CalculateValue(rune.Id, quantity.Value, observations) : null;
            MarketValueResult? focusSale = focusQuantity is > 0 && rune is not null
                ? MarketPriceCalculator.CalculateValue(rune.Id, focusQuantity.Value, observations) : null;
            double? standardValue = quantity == 0 ? 0 : standardSale?.Value * multiplier;
            double? focusValue = focusSale?.Value * multiplier;
            rows.Add(new(stat, rune, quantity, standardValue, focusQuantity, focusValue, unitPrice));
            if (coefficient.HasValue && weight > 0)
            {
                if (rune is null) missing.Add($"Rune de base absente du catalogue : {stat}.");
                else if (standardSale is null) missing.Add($"Prix de revente manquant pour un lot atteignable : {rune.Name}.");
            }
            standardTotal += standardValue;
            if (standardSale is { } sale && rune is not null)
            {
                standardObservations.AddRange(sale.UsedObservations);
                if (includeRuneQuantities) standardQuantities[rune.Id] = quantity!.Value;
            }
            if (rune is not null && focusSale is { } focused && focusValue is { } value)
            {
                scenarios.Add(new(stat, value)
                {
                    RuneQuantities = includeRuneQuantities ? new Dictionary<long, double> { [rune.Id] = focusQuantity!.Value } : null,
                    PriceFreshness = DataFreshnessEvaluator.Worst(focused.UsedObservations)
                });
            }
        }

        if (standardTotal is { } total)
        {
            scenarios.Add(new(null, total)
            {
                RuneQuantities = includeRuneQuantities ? standardQuantities : null,
                PriceFreshness = standardObservations.Count > 0 ? DataFreshnessEvaluator.Worst(standardObservations) : null
            });
        }
        var best = scenarios.OrderByDescending(option => option.Value).ThenBy(option => option.Focus).FirstOrDefault();
        return new(rows, standardTotal, best, missing);
    }
}
