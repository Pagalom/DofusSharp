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
    string Name, int Level, ulong CumulativeExperience);

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
}
