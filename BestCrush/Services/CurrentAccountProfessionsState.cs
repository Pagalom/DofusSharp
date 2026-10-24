namespace BestCrush.Services;

/// <summary>
/// Future account-level profession owner: the combination of a verified
/// account identity and a verified game server. Character IDs, TCP
/// connections and the manually selected server are not valid substitutes.
/// AccountIdentity is an opaque in-memory identity, never a username or token.
/// </summary>
public readonly record struct AccountServerKey(
    string AccountIdentity, string ServerName);

/// <summary>
/// One profession's confirmed display information. Name and profession ID
/// must come from a validated protocol mapping, not guessed f5 candidates.
/// </summary>
public sealed record AccountProfessionProgress(
    string Name, int Level, ulong CumulativeExperience,
    // Presentation-only. Verified window-local Game samples may attach
    // boundaries from the validated DOFUS 3 profession XP reference.
    // Unknown or contradictory thresholds remain null; no guessed progress.
    ulong? LevelStartCumulativeExperience = null,
    ulong? NextLevelCumulativeExperience = null);

/// <summary>
/// Account + server snapshot, independent of all character profiles.
/// No profession observation is considered authoritative until account,
/// server, and the global profession mapping are separately verified.
/// </summary>
public sealed record AccountProfessionsSnapshot(
    AccountServerKey Key,
    IReadOnlyList<AccountProfessionProgress> Professions);

/// <summary>
/// Read-only P3d workspace scaffold. Intentionally no ingestion API:
/// P3c account/server identity and global profession IDs are unverified.
/// Adding a trusted writer requires its own protocol validation. Nothing is
/// restored, persisted, or inferred from character selection or TCP flow.
/// </summary>
public sealed class CurrentAccountProfessionsState
{
    public AccountProfessionsSnapshot? ActiveSnapshot { get; private set; }

    /// <summary>
    /// Presentation lookup only. The owning account must have been verified
    /// independently and linked explicitly to this character. Matching the
    /// selected server or the active character alone must never be enough.
    /// P3c remains pending and no writer sets either value today.
    /// </summary>
    public AccountProfessionsSnapshot? ForCharacter(
        CharacterSessionProfile character) =>
        ForVerifiedSnapshot(character, ActiveSnapshot);

    /// <summary>
    /// Pure comparison boundary for future *verified* account snapshots.
    /// No network ingestion, account inference or persistence is permitted.
    /// Keeping the comparison separate lets tests prove two accounts on the
    /// same server remain isolated without ever populating the live state.
    /// </summary>
    internal static AccountProfessionsSnapshot? ForVerifiedSnapshot(
        CharacterSessionProfile character,
        AccountProfessionsSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (character.VerifiedAccountServerKey is not { } verified ||
            string.IsNullOrWhiteSpace(verified.AccountIdentity) ||
            string.IsNullOrWhiteSpace(verified.ServerName) ||
            !string.Equals(character.Key.ServerName, verified.ServerName,
                StringComparison.OrdinalIgnoreCase))
            return null;

        return snapshot is not null &&
            string.Equals(snapshot.Key.AccountIdentity, verified.AccountIdentity,
                StringComparison.Ordinal) &&
            string.Equals(snapshot.Key.ServerName, verified.ServerName,
                StringComparison.OrdinalIgnoreCase)
                ? snapshot
                : null;
    }
}
