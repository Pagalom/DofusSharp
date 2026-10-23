namespace BestCrush.Network.Protocol;

/// <summary>
/// P3b opt-in, probe-local comparison of profession snapshots.
/// Keeps a single 20-entry reference in RAM; it never identifies accounts,
/// servers, people or characters, and never emits raw IDs, levels or XP.
/// This is NOT safe evidence to automatically join two TCP connections.
/// </summary>
public sealed class ProfessionContinuityStudy
{
    private Dictionary<ulong, (int Level, ulong Xp)>? _baseline;
    // Previous *valid* capture in this probe run, regardless of account.
    // This is not an account identity and is never written to disk.
    private Dictionary<ulong, (int Level, ulong Xp)>? _previous;

    private const string Unverified =
        " scope=account-server account=unverified server=unverified mapping=unverified";

    public string Observe(ReadOnlySpan<byte> body)
    {
        if (!ProfessionIpzDecoder.TryDecode(body,
                out ProfessionIpzSnapshot? snapshot) || snapshot is null)
            return "p3b=ipz decoded=no baseline=unchanged" + Unverified;

        Dictionary<ulong, (int Level, ulong Xp)> current = new();
        foreach (ProfessionIpzRow row in snapshot.Rows)
        {
            if (!row.UnidentifiedVarints.TryGetValue(5, out ulong candidateId) ||
                candidateId == 0 ||
                !current.TryAdd(candidateId, (row.Level, row.ExperienceCounter)))
                return "p3b=ipz decoded=yes rows=20 f5-valid=no baseline=unchanged" +
                       Unverified;
        }

        if (_baseline is null)
        {
            _baseline = current;
            _previous = current;
            return "p3b=ipz decoded=yes rows=20 f5-valid=yes " +
                   "baseline=created" + Unverified;
        }

        bool idsEqual = _baseline.Count == current.Count &&
            _baseline.Keys.All(current.ContainsKey);
        string level = "unavailable";
        string xp = "unavailable";
        if (idsEqual)
        {
            level = _baseline.All(pair =>
                current[pair.Key].Level == pair.Value.Level) ? "yes" : "no";
            xp = _baseline.All(pair =>
                current[pair.Key].Xp == pair.Value.Xp) ? "yes" : "no";
        }

        // The old mode only compared A2 against A1; when both B and A2
        // differed, it could not say whether B and A2 matched each other.
        // Compare the current capture against both A1 and the preceding
        // VALID capture without printing any job IDs, levels, XP or hashes.
        Dictionary<ulong, (int Level, ulong Xp)> previous = _previous!;
        bool previousIdsEqual = previous.Count == current.Count &&
            previous.Keys.All(current.ContainsKey);
        string previousLevel = "unavailable";
        string previousXp = "unavailable";
        if (previousIdsEqual)
        {
            previousLevel = previous.All(pair =>
                current[pair.Key].Level == pair.Value.Level) ? "yes" : "no";
            previousXp = previous.All(pair =>
                current[pair.Key].Xp == pair.Value.Xp) ? "yes" : "no";
        }

        _previous = current;

        return "p3b=ipz decoded=yes rows=20 f5-valid=yes " +
               "baseline=compared f5-set-equal=" + (idsEqual ? "yes" : "no") +
               " levels-equal=" + level + " xp-equal=" + xp +
               Unverified +
               " previous-f5-set-equal=" + (previousIdsEqual ? "yes" : "no") +
               " previous-levels-equal=" + previousLevel +
               " previous-xp-equal=" + previousXp;
    }
}
