namespace BestCrush.Domain.Services;

/// <summary>Client 3.7.14.18, EvolutiveItemType 1. Level zero is the unfed pet.</summary>
public static class PetExperience
{
    public const int MaximumLevel = 100;
    public const double ResourceMultiplier = 10;
    private static readonly int[] Thresholds = [0,1,2,3,4,6,8,10,12,14,16,19,22,25,29,33,38,43,49,56,63,71,80,90,102,115,129,145,163,183,205,229,256,286,319,356,397,442,492,547,608,676,751,833,924,1024,1135,1257,1392,1540,1703,1883,2081,2299,2538,2801,3090,3408,3757,4141,4562,5024,5531,6088,6699,7369,8103,8908,9790,10756,11815,12975,14245,15635,17157,18823,20646,22641,24823,27209,29819,32673,35793,39203,42930,47003,51453,56315,61626,67427,73762,80679,88231,96476,105476,115299,126019,137716,150479,164403,179592];
    public static int RequiredAtLevel(int level) => level is >= 0 and <= MaximumLevel
        ? Thresholds[level] : throw new ArgumentOutOfRangeException(nameof(level));
    public static int LevelAt(long experience)
    {
        if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
        int level = Array.BinarySearch(Thresholds, experience > int.MaxValue ? int.MaxValue : (int)experience);
        return level >= 0 ? level : Math.Min(MaximumLevel, ~level - 1);
    }
    public static int MealExperience(double perUnit, int quantity)
    {
        if (!double.IsFinite(perUnit) || perUnit <= 0 || quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(perUnit));
        double value = Math.Floor(perUnit * quantity);
        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }
    public static int? UnitsFor(double perUnit, int experience)
    {
        if (!double.IsFinite(perUnit) || perUnit <= 0 || experience < 0) return null;
        if (experience == 0) return 0;
        double estimate = Math.Ceiling(experience / perUnit);
        if (estimate > int.MaxValue) return null;
        int quantity = (int)estimate;
        // Preserve the client's binary floating-point boundaries, including tiny rune yields.
        while (MealExperience(perUnit, quantity) < experience)
        {
            if (quantity == int.MaxValue) return null;
            quantity++;
        }
        while (quantity > 0 && MealExperience(perUnit, quantity - 1) >= experience) quantity--;
        return quantity;
    }
}
