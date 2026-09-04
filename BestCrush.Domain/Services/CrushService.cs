using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public class CrushService(
    BestCrushDbContext context)
{
    public IReadOnlyDictionary<Rune, double>
        GetCrushResult(
            Dictionary<
                Characteristic,
                double> itemLines,
            int itemLevel,
            double coefficient)
    {
        Dictionary<Rune, double>
            result = new();

        foreach ((
            Characteristic characteristic,
            double value)
            in itemLines)
        {
            if (value <= 0)
            {
                continue;
            }

            Rune? rune =
                GetBasicRune(
                    characteristic
                );

            if (rune is null)
            {
                continue;
            }

            double crushWeight =
                GetCrushWeight(
                    characteristic,
                    value,
                    itemLevel
                );

            double boostedCrushWeight =
                crushWeight *
                coefficient;

            double runeYield =
                boostedCrushWeight /
                characteristic
                    .GetBasicRuneWeight();

            result.Add(
                rune,
                runeYield
            );
        }

        return result;
    }

    public IReadOnlyDictionary<Rune, double>
        GetFocusedCrushResult(
            Dictionary<
                Characteristic,
                double> itemLines,
            Characteristic focus,
            int itemLevel,
            double coefficient)
    {
        Rune? rune =
            GetBasicRune(
                focus
            );

        if (rune is null)
        {
            return new Dictionary<
                Rune,
                double>();
        }

        double totalCrushWeight =
            0;

        foreach ((
            Characteristic characteristic,
            double value)
            in itemLines)
        {
            if (value <= 0)
            {
                continue;
            }

            double crushWeight =
                GetCrushWeight(
                    characteristic,
                    value,
                    itemLevel
                );

            if (characteristic == focus)
            {
                totalCrushWeight +=
                    crushWeight;
            }
            else
            {
                totalCrushWeight +=
                    crushWeight / 2;
            }
        }

        double boostedCrushWeight =
            totalCrushWeight *
            coefficient;

        double runeYield =
            boostedCrushWeight /
                focus.GetBasicRuneWeight();

        return new Dictionary<
            Rune,
            double>
        {
            {
                rune,
                runeYield
            }
        };
    }

    private Rune? GetBasicRune(
        Characteristic characteristic)
    {
        // Le catalogue local contient désormais les variantes
        // normales, Pa et Ra. Le calcul de concassage doit
        // continuer à valoriser le résultat en équivalent de
        // rune de base, comme auparavant.
        Rune[] candidates =
            context.Runes
                .Where(rune =>
                    rune.Characteristic ==
                    characteristic)
                .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        return candidates
            .Where(rune =>
                !IsPowerVariant(
                    rune.Name
                ))
            .OrderBy(rune =>
                rune.DofusDbId)
            .FirstOrDefault()
            ?? candidates
                .OrderBy(rune =>
                    rune.DofusDbId)
                .First();
    }

    private static bool IsPowerVariant(
        string runeName)
    {
        return
            runeName.StartsWith(
                "Rune Pa ",
                StringComparison.OrdinalIgnoreCase
            ) ||
            runeName.StartsWith(
                "Rune Ra ",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static double GetCrushWeight(
        Characteristic characteristic,
        double lineValue,
        int itemLevel) =>
            3 *
            lineValue *
            characteristic.GetWeight() *
            itemLevel /
            200 +
            1;
}
