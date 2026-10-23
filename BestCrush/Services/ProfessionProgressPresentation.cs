namespace BestCrush.Services;

/// <summary>
/// Pure UI projection of cumulative profession XP. Never deduces level
/// boundaries from a level number, because Dofus progression is non-linear.
/// This is NOT a writer for account or character professions.
/// </summary>
public static class ProfessionProgressPresentation
{
    public readonly record struct LevelProgress(
        ulong EarnedInLevel, ulong RequiredInLevel, decimal Percent);

    /// <summary>
    /// A true bar exists only when the current cumulative XP and both
    /// trustworthy level boundaries are available and consistent.
    /// Missing/contradictory data produces an unknown bar, never zero XP.
    /// </summary>
    public static LevelProgress? For(AccountProfessionProgress profession)
    {
        ArgumentNullException.ThrowIfNull(profession);
        if (profession.Level is < 1 or > 200 ||
            profession.LevelStartCumulativeExperience is not { } start ||
            profession.NextLevelCumulativeExperience is not { } next ||
            next <= start ||
            profession.CumulativeExperience < start ||
            profession.CumulativeExperience > next)
            return null;

        ulong required = next - start;
        ulong earned = profession.CumulativeExperience - start;
        decimal percent = decimal.Round(
            (decimal)earned / required * 100m, 2,
            MidpointRounding.AwayFromZero);
        return new LevelProgress(earned, required, percent);
    }
}
