namespace BestCrush.Services;

/// <summary>
/// Cumulative DOFUS 3 profession XP reference for levels 1..200:
/// threshold(level) = 10 * level * (level - 1).
/// Reference: DOFUS 3.7 profession experience table, independently
/// cross-checked against observed (level, cumulative XP) samples.
/// https://dofuswiki.fandom.com/wiki/Profession#Experience
/// https://smart-calculators.net/fr-FR/tools/dofus-xp-calculator
///
/// This is a level threshold reference, not a network packet decoder.
/// Never infer character/account identity or persist observed XP.
/// </summary>
public static class ProfessionLevelExperienceReference
{
    /// <summary>
    /// Supplies cumulative XP at the start and end of a level only when
    /// the observed XP belongs strictly within that level.
    /// An XP counter equal to the next level's threshold but paired with
    /// the old level is contradictory/stale: do not draw a false 100% bar.
    /// Level 200 is terminal and displayed separately by the UI.
    /// </summary>
    public static bool TryGetBoundaries(
        int level, ulong cumulativeExperience,
        out ulong start, out ulong next)
    {
        start = 0;
        next = 0;

        if (level is < 1 or >= 200)
            return false;

        ulong n = (ulong)level;
        ulong candidateStart = 10UL * n * (n - 1);
        ulong candidateNext = 10UL * n * (n + 1);

        if (cumulativeExperience < candidateStart ||
            cumulativeExperience >= candidateNext)
            return false;

        start = candidateStart;
        next = candidateNext;
        return true;
    }
}
