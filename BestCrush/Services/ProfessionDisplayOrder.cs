namespace BestCrush.Services;

/// <summary>
/// Visual-only ordering of the professions shown on character session cards.
/// Highest total cumulative XP first, then name for deterministic ties.
/// Does not alter the underlying Game snapshot or its per-window ownership.
/// </summary>
public static class ProfessionDisplayOrder
{
    public static IOrderedEnumerable<AccountProfessionProgress> ByCumulativeExperience(
        IEnumerable<AccountProfessionProgress> professions)
    {
        ArgumentNullException.ThrowIfNull(professions);

        return professions
            .OrderByDescending(job => job.CumulativeExperience)
            .ThenBy(job => job.Name, StringComparer.CurrentCultureIgnoreCase);
    }
}
