namespace BestCrush.Services;

/// <summary>
/// UI-only visibility projection. An accepted TCP/OS candidate must also
/// have an in-game character window title to show its card. Never join
/// by nickname, class, list order or player-provided server name.
/// Returning to the character-select title hides the card without
/// treating that title as a verified network logout.
/// </summary>
internal static class CandidateCharacterSheetVisibility
{
    internal static IReadOnlyList<(CandidateWindowRosterCard Candidate,
        DofusClientWindow Client)> Visible(
        IReadOnlyList<CandidateWindowRosterCard> candidates,
        IReadOnlyList<DofusClientWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(windows);

        Dictionary<DofusClientWindowKey, DofusClientWindow> live =
            windows.Where(window => window.HasCharacterTitle)
                .GroupBy(window => window.Key)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single());

        return candidates
            .Where(candidate => live.ContainsKey(candidate.Window))
            .Select(candidate => (Candidate: candidate,
                Client: live[candidate.Window]))
            .ToArray();
    }
}
