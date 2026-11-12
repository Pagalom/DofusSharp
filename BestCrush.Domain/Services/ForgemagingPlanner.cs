using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

/// <summary>
/// Empirical DoFocus model published with the November 2026 video. See docs/forgemaging-model.md.
/// Advice compares the immediate cost of useful stat gains; collateral repair costs are not predicted.
/// </summary>
public static class ForgemagingPlanner
{
    private const double Epsilon = 1e-9;
    public static string? Validate(ForgemagingSession session)
    {
        if (session.Lines.Count == 0) return "Cet équipement ne possède aucune caractéristique exploitable.";
        if (session.Lines.Select(l => l.Characteristic).Distinct().Count() != session.Lines.Count)
            return "Une caractéristique apparaît plusieurs fois.";
        if (session.Lines.Any(l => l.Current is null)) return "Renseignez le jet actuel de chaque caractéristique.";
        if (session.Lines.Any(l => Math.Abs((long)l.Current!.Value) > 100000 || Math.Abs((long)l.Target) > 100000))
            return "Une valeur de caractéristique est trop élevée.";
        if (session.Sink is { } sink && (!double.IsFinite(sink) || sink < 0 || sink > 100000))
            return "Le puits doit être un nombre positif ou nul.";
        if (session.Budget is { } budget && (!double.IsFinite(budget) || budget < 0)) return "Le budget doit être positif ou nul.";
        foreach (var line in session.Lines)
        {
            if (line.Target > LineCap(line)) return "Un objectif dépasse la limite de cette caractéristique.";
        }
        if (ExcessWeight(session.Lines, targets: true) > 101 + Epsilon)
            return "Le poids total des overs et exos visés dépasse 101.";
        return null;
    }

    public static IReadOnlyList<ForgemagingAdvice> Recommend(ForgemagingSession session, IReadOnlyList<ForgemagingRune> runes)
    {
        if (Validate(session) is not null) return [];
        var advice = new List<ForgemagingAdvice>();
        bool otherNaturalMissing = session.Lines.Any(l => l.IsNative && l.Current < l.Target &&
            l.Characteristic is not (Characteristic.Ap or Characteristic.Mp or Characteristic.Range));
        foreach (var line in session.Lines.Where(l => l.Current < l.Target))
        foreach (var rune in runes.Where(r => r.Characteristic == line.Characteristic && r.Gain > 0))
        {
            var odds = Evaluate(session.Level, session.Lines, rune);
            if (odds.Blocked is not null) continue;
            int useful = Math.Min(rune.Gain, line.Target - line.Current!.Value);
            double? cost = rune.UnitPrice is > 0 ? rune.UnitPrice / (useful * odds.Success) : null;
            bool beyondNatural = !line.IsNative || line.Current + rune.Gain > line.Maximum;
            int phase = beyondNatural ? 2 : 0;
            if (phase == 0 && otherNaturalMissing && session.Sink > 0 &&
                line.Characteristic is Characteristic.Ap or Characteristic.Mp or Characteristic.Range) phase = 1;
            double consumed = rune.Weight * (line.Current + rune.Gain < 0 ? .5 : 1);
            advice.Add(new(rune, odds, useful, cost, session.Sink is { } well ? well + Epsilon >= consumed : null, phase));
        }
        return advice.OrderBy(a => a.Phase).ThenBy(a => a.CostPerUsefulPoint is null)
            .ThenBy(a => a.CostPerUsefulPoint / a.Rune.Characteristic.GetWeight())
            .ThenBy(a => a.Rune.Id).ToArray();
    }

    public static ForgemagingOdds Evaluate(int level, IReadOnlyList<ForgemagingLine> lines, ForgemagingRune rune)
    {
        if (rune.Gain <= 0 || lines.Any(l => l.Current is null)) throw new ArgumentException("Jet ou rune incomplet.");
        var line = lines.FirstOrDefault(l => l.Characteristic == rune.Characteristic);
        int current = line?.Current ?? 0, after = checked(current + rune.Gain);
        double weight = rune.Characteristic.GetWeight();
        if (after > (line is null ? Math.Floor(101 / weight + Epsilon) : LineCap(line)))
            return new(0, 0, 100, "Limite", "Limite de la caractéristique dépassée.");
        var projected = lines.Select(l => l.Copy()).ToList();
        if (line is null) projected.Add(new() { Characteristic = rune.Characteristic, Current = after });
        else projected.First(l => l.Characteristic == rune.Characteristic).Current = after;
        if (ExcessWeight(projected) > 101 + Epsilon)
            return new(0, 0, 100, "Limite", "Poids total des overs et exos supérieur à 101.");

        if (line is null || !line.IsNative)
        {
            if (rune.Characteristic is Characteristic.Ap or Characteristic.Mp or Characteristic.Range)
                return new(1, 0, 99, "Exo");
            double total = after * weight;
            double success = total <= 2 ? 57 : total <= 15 ? 57 - (total - 2) * 38 / 13 : total < 25 ? 19 - (total - 15) * 1.9 : 0;
            double scExo = Math.Max(1, .077 * success), snExo = .923 * success;
            return new(scExo, snExo, 100 - scExo - snExo, "Exo");
        }
        int min = Math.Min(line.Minimum!.Value, line.Maximum!.Value), max = Math.Max(line.Minimum.Value, line.Maximum.Value);
        double qualityDenominator = 0, qualityNumerator = 0;
        foreach (var other in lines.Where(l => l != line && l.IsNative))
        {
            int otherMax = Math.Max(other.Minimum!.Value, other.Maximum!.Value);
            if (otherMax < 0) continue;
            qualityDenominator += otherMax * other.Characteristic.GetWeight();
            qualityNumerator += Math.Clamp(other.Current!.Value, 0, otherMax) * other.Characteristic.GetWeight();
        }
        double quality = qualityDenominator > 0 ? qualityNumerator / qualityDenominator : 0;
        if (lines.Any(l => !l.IsNative && l.Current > 0 && l.Characteristic is Characteristic.Ap or Characteristic.Mp)) quality = 1;
        bool over = after > max, fixedLine = min == max;
        double position = fixedLine ? 1 : (after - min) / (double)(max - min);
        double difficulty = 5 + 12 * Math.Max(0, current / (double)rune.Gain - 14) + 60 * Math.Pow(quality, 12);
        if (over) difficulty += 65 + 4 * Math.Max(0, (after - max) * weight);
        else if (!fixedLine && position >= .8 - Epsilon) difficulty += 45;
        if (level < 170) difficulty -= 20 * Math.Max(0, 1.1 - quality);
        double sc = Math.Max(2, (100 - difficulty) / 2);
        double sn = over ? Math.Max(5, 40 - .375 * (difficulty - 100)) : difficulty <= 100 ? Math.Min(50, 100 - sc) : Math.Max(15, 50 - .375 * (difficulty - 100));
        sn = Math.Clamp(sn, 0, 100 - sc);
        return new(sc, sn, 100 - sc - sn, over ? "Over" : fixedLine ? "Repassage" : position >= .8 - Epsilon ? "Palier 80 %" : "Naturel");
    }

    public static int LineCap(ForgemagingLine line) => Math.Max(line.IsNative ? Math.Max(line.Minimum!.Value, line.Maximum!.Value) : 0,
        (int)Math.Floor(101 / line.Characteristic.GetWeight() + Epsilon));
    public static double ExcessWeight(IEnumerable<ForgemagingLine> lines, bool targets = false) => lines.Sum(l =>
    {
        int value = targets ? l.Target : l.Current ?? 0;
        return value <= 0 ? 0 : Math.Max(0, value - (l.IsNative ? Math.Max(l.Minimum!.Value, l.Maximum!.Value) : 0)) * l.Characteristic.GetWeight();
    });

    /// <summary>Reconstructs a well only from a known previous well and a fully entered actual result.</summary>
    public static double? ResultingSink(double? beforeSink, IReadOnlyList<ForgemagingLine> before,
        IReadOnlyList<ForgemagingLine> after, ForgemagingRune rune, ForgemagingOutcome outcome)
    {
        if (beforeSink is null) return null;
        if (outcome == ForgemagingOutcome.Critical) return beforeSink;
        if (before.Any(l => l.Current is null) || after.Any(l => l.Current is null)) throw new ArgumentException("Jet incomplet.");
        double Power(ForgemagingLine l) => l.Current!.Value * l.Characteristic.GetWeight() * (l.Current < 0 ? .5 : 1);
        var expected = before.Select(l => l.Copy()).ToList();
        var target = expected.Single(l => l.Characteristic == rune.Characteristic);
        double consumed = rune.Weight * (target.Current + rune.Gain < 0 ? .5 : 1);
        if (outcome == ForgemagingOutcome.Neutral) target.Current += rune.Gain;
        double lost = expected.Sum(Power) - after.Sum(Power);
        return Math.Round(Math.Max(0, beforeSink.Value + lost - consumed), 6);
    }
}
