using System.Globalization;

namespace BestCrush.Network.Protocol;

/// <summary>
/// Manually observed post-craft state. It is never formatted in probe output.
/// The index identifies the Nth mono-row ipz in this probe run, not a player.
/// </summary>
public readonly record struct ProfessionXpReference(int Index, int Level, ulong Xp)
{
    public override string ToString() => "[local XP reference: redacted]";
}

public static class ProfessionXpReferenceInput
{
    /// <summary>
    /// Format: ipz-index:level:xp,ipz-index:level:xp.
    /// Reject ambiguous/non-monotone points before comparison.
    /// </summary>
    public static bool TryParse(string? input, out IReadOnlyList<ProfessionXpReference> points)
    {
        points = [];
        if (string.IsNullOrWhiteSpace(input) || input.Length > 400)
            return false;

        string[] parts = input.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 16)
            return false;

        List<ProfessionXpReference> parsed = [];
        int previousIndex = 0;
        ulong? previousXp = null;
        foreach (string part in parts)
        {
            string[] fields = part.Split(':', StringSplitOptions.TrimEntries);
            if (fields.Length != 3 ||
                !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int level) ||
                !ulong.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong xp) ||
                index <= previousIndex || index > 16 ||
                level is < 1 or > 200 || xp > 1_000_000_000_000 ||
                (previousXp.HasValue && xp <= previousXp.Value))
                return false;

            parsed.Add(new ProfessionXpReference(index, level, xp));
            previousIndex = index;
            previousXp = xp;
        }

        points = parsed.AsReadOnly();
        return true;
    }
}

/// <summary>
/// Opt-in P3e diagnostic, in RAM only. Keeps at most sixteen small, single-row
/// server-to-client ipz numeric-field sets from the first valid TCP session.
/// Does not save packets, hash identifiers, persist state, or infer accounts.
/// Matching a field to user-entered XP is a HYPOTHESIS, never proof of semantics.
/// </summary>
public sealed class ProfessionXpWireStudy
{
    private const int MaximumObservations = 16;
    private const int MaximumRowBytes = 256;
    private readonly object _sync = new();
    private readonly List<IReadOnlyDictionary<int, ulong>> _observations = [];
    private int? _boundSession;
    private readonly bool _checkPublicJobIds;
    private ProfessionIpzSnapshot? _publicRoster;

    public ProfessionXpWireStudy(bool checkPublicJobIds = false)
    {
        _checkPublicJobIds = checkPublicJobIds;
    }

    public int Count
    {
        get { lock (_sync) return _observations.Count; }
    }

    /// <returns>An anonymous summary for a valid mono-row ipz, otherwise null.</returns>
    public string? Observe(int sessionNumber, ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 || body.IsEmpty ||
            body.Length > (_checkPublicJobIds ? 16 * 1024 : MaximumRowBytes + 8))
            return null;

        // P3g is opt-in. A full 20-row roster is observed separately from
        // the mono-row craft messages; no numeric values leave this process.
        if (_checkPublicJobIds &&
            ProfessionIpzDecoder.TryDecode(body, out ProfessionIpzSnapshot? roster) &&
            roster is not null)
        {
            lock (_sync)
            {
                if (_boundSession.HasValue && _boundSession != sessionNumber)
                    return null;

                _boundSession ??= sessionNumber;
                if (_publicRoster is not null)
                    return null;

                _publicRoster = roster;
                return "p3g=roster-captured rows=" + roster.Rows.Count +
                    " public-mapping=unverified account=unverified server=unverified";
            }
        }

        if (body.Length > MaximumRowBytes + 8)
            return null;

        // Mono-row observations remain isolated from the full roster.
        Dictionary<int, ulong>? numeric = ParseSingleRow(body);
        if (numeric is null)
            return null;

        lock (_sync)
        {
            if (_boundSession.HasValue && _boundSession != sessionNumber)
                return null;

            _boundSession ??= sessionNumber;
            if (_observations.Count >= MaximumObservations)
                return "p3e=mono-ipz accepted=no reason=limit references=ram-only";

            _observations.Add(numeric);
            return "p3e=mono-ipz accepted=yes index=" + _observations.Count +
                " varint-path-count=" + numeric.Count +
                " reference=pending account=unverified server=unverified mapping=unverified";
        }
    }

    /// <summary>
    /// The operator supplies these values AFTER stopping the probe.
    /// Never print or retain the input text. Emit only candidate FIELD PATHS.
    /// </summary>
    public string Compare(IReadOnlyList<ProfessionXpReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        lock (_sync)
        {
            if (references.Count is < 2 or > MaximumObservations ||
                references.Select(x => x.Index).Distinct().Count() != references.Count ||
                references.Select(x => x.Xp).Distinct().Count() != references.Count ||
                references.Any(r => r.Index < 1 || r.Index > _observations.Count ||
                    r.Level is < 1 or > 200 || r.Xp > 1_000_000_000_000) ||
                references.Zip(references.Skip(1),
                    (a, b) => b.Index <= a.Index || b.Xp <= a.Xp).Any(x => x))
                return "p3e=invalid-local-references account=unverified server=unverified";

            IReadOnlyDictionary<int, ulong> first = _observations[references[0].Index - 1];
            int[] xpPaths = first.Keys
                .Where(field => references.All(r =>
                    _observations[r.Index - 1].TryGetValue(field, out ulong value) &&
                    value == r.Xp))
                .OrderBy(field => field).ToArray();
            int[] levelPaths = first.Keys
                .Where(field => references.All(r =>
                    _observations[r.Index - 1].TryGetValue(field, out ulong value) &&
                    value == (ulong)r.Level))
                .OrderBy(field => field).ToArray();

            static string Paths(int[] fields) =>
                fields.Length == 0 ? "none" :
                string.Join(",", fields.Select(f => "f1.f" + f));

            string status = xpPaths.Length switch
            {
                0 => "none",
                1 => "single-candidate",
                _ => "ambiguous"
            };
            return "p3e=compared references=" + references.Count +
                " xp-field-candidates=[" + Paths(xpPaths) + "]" +
                " level-field-candidates=[" + Paths(levelPaths) + "]" +
                " xp-status=" + status +
                " account=unverified server=unverified mapping=unverified";
        }
    }

    /// <summary>
    /// P3f: verify two different professions, each observed at least twice.
    /// The matching fields must track the operator's level and XP across
    /// *all* references before we compare stable candidate job fields.
    /// No profession names, values or identifiers are returned or persisted.
    /// </summary>
    public string CompareJobs(IReadOnlyList<ProfessionJobReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        lock (_sync)
        {
            if (!ProfessionJobReferenceInput.Validate(references, _observations.Count))
                return "p3f=invalid-local-references mapping=unverified";

            var groups = references.GroupBy(r => r.Profession,
                    StringComparer.OrdinalIgnoreCase).ToArray();

            bool levelMatches = references.All(r =>
                _observations[r.Index - 1].TryGetValue(1, out ulong value) &&
                value == (ulong)r.Level);
            bool xpMatches = references.All(r =>
                _observations[r.Index - 1].TryGetValue(2, out ulong value) &&
                value == r.Xp);

            if (!levelMatches || !xpMatches)
                return "p3f=references-not-confirmed" +
                    " level-match=" + (levelMatches ? "yes" : "no") +
                    " xp-match=" + (xpMatches ? "yes" : "no") +
                    " candidate-paths=[none]" +
                    " account=unverified server=unverified mapping=unverified";

            IReadOnlyDictionary<int, ulong> first =
                _observations[references[0].Index - 1];

            int[] candidates = first.Keys
                // f1.f1 (level) and f1.f2 (XP) are already characterized,
                // so neither can establish a stable job identifier.
                .Where(field => field is >= 3 and <= 64)
                .Where(field => references.All(r =>
                    _observations[r.Index - 1].ContainsKey(field)))
                .Where(field =>
                {
                    ulong[] valuesPerJob = [];
                    foreach (var group in groups)
                    {
                        ulong[] values = group
                            .Select(r => _observations[r.Index - 1][field])
                            .Distinct().ToArray();
                        if (values.Length != 1)
                            return false;
                        valuesPerJob = [.. valuesPerJob, values[0]];
                    }

                    return valuesPerJob.Distinct().Count() == groups.Length;
                })
                .OrderBy(field => field)
                .ToArray();

            string fields = candidates.Length == 0 ? "none" :
                string.Join(",", candidates.Select(n => "f1.f" + n));
            string status = candidates.Length switch
            {
                0 => "none",
                1 => "single-candidate",
                _ => "ambiguous"
            };

            return "p3f=compared references=" + references.Count +
                " professions=" + groups.Length +
                " level-match=yes xp-match=yes" +
                " candidate-paths=[" + fields + "]" +
                " f1.f5-stable-distinct=" +
                (candidates.Contains(5) ? "yes" : "no") +
                " candidate-status=" + status +
                " account=unverified server=unverified mapping=unverified";
        }
    }


    /// <summary>
    /// Verify the public f5 IDs against manually supplied job/level/XP
    /// references, all from the SAME captured session. null means invalid
    /// or unverified references; false is a genuine ID disagreement.
    /// </summary>
    public bool? MatchPublicJobIds(IReadOnlyList<ProfessionJobReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (!_checkPublicJobIds)
            return null;

        lock (_sync)
        {
            if (!ProfessionJobReferenceInput.Validate(references, _observations.Count))
                return null;

            foreach (ProfessionJobReference reference in references)
            {
                IReadOnlyDictionary<int, ulong> fields = _observations[reference.Index - 1];
                if (!fields.TryGetValue(1, out ulong level) ||
                    level != (ulong)reference.Level ||
                    !fields.TryGetValue(2, out ulong xp) || xp != reference.Xp ||
                    !ProfessionPublicJobIdReference.ByName.ContainsKey(reference.Profession))
                    return null;
            }

            return references.All(reference =>
                _observations[reference.Index - 1].TryGetValue(5, out ulong candidate) &&
                candidate == ProfessionPublicJobIdReference.ByName[reference.Profession]);
        }
    }

    /// <summary>
    /// Roster completeness is a separate structural observation.
    /// Names listed as missing/extra are PUBLIC profession labels only;
    /// neither levels/XP nor observed f5 numbers are ever emitted.
    /// </summary>
    public string DescribePublicRoster(bool publicIdMatchConfirmed)
    {
        lock (_sync)
        {
            if (_publicRoster is null)
                return "p3g=roster status=not-captured reference20=unverified" +
                    " account=unverified server=unverified mapping=unverified";

            if (!publicIdMatchConfirmed)
                return "p3g=roster status=captured rows=" + _publicRoster.Rows.Count +
                    " reference20=not-compared account=unverified server=unverified mapping=unverified";

            ulong[] ids = _publicRoster.Rows
                .Select(row => row.UnidentifiedVarints.TryGetValue(5, out ulong value)
                    ? value : 0)
                .ToArray();

            HashSet<ulong> present = ids.Where(id => id != 0).ToHashSet();
            HashSet<ulong> reference = ProfessionPublicJobIdReference.ReferenceTwenty.ToHashSet();
            HashSet<ulong> known = ProfessionPublicJobIdReference.ByName.Values.ToHashSet();
            ulong[] missing = reference.Except(present).OrderBy(id => id).ToArray();
            ulong[] extras = present.Except(reference).OrderBy(id => id).ToArray();
            Dictionary<ulong, string> labels = ProfessionPublicJobIdReference.ByName
                .ToDictionary(pair => pair.Value, pair => pair.Key);

            // All names printed below are from the static public list.
            string missingNames = missing.Length == 0 ? "none" :
                string.Join(",", missing.Select(id => labels[id]));
            string publicExtraNames = string.Join(",",
                extras.Where(known.Contains).Select(id => labels[id]));
            if (publicExtraNames.Length == 0)
                publicExtraNames = "none";

            int unknown = extras.Count(id => !known.Contains(id));
            int absentF5 = ids.Count(id => id == 0);
            bool unique = absentF5 == 0 && ids.Distinct().Count() == ids.Length;
            bool complete = missing.Length == 0 && extras.Length == 0 && unique;

            return "p3g=roster status=" + (complete ? "reference20-complete" : "reference20-differs") +
                " rows=" + ids.Length +
                " unique-f5=" + (unique ? "yes" : "no") +
                " reference20-present=" + (reference.Count - missing.Length) + "/20" +
                " missing-public-names=[" + missingNames + "]" +
                " extra-public-names=[" + publicExtraNames + "]" +
                " unknown-id-count=" + unknown +
                " missing-f5-count=" + absentF5 +
                " account=unverified server=unverified mapping=unverified";
        }
    }

    private static Dictionary<int, ulong>? ParseSingleRow(ReadOnlySpan<byte> body)
    {
        try
        {
            List<ProtoField>? root = ProtoWire.ReadFields(body);
            if (root is not { Count: 1 } ||
                root[0].Number != 1 ||
                root[0].WireType != ProtoWireType.LengthDelimited ||
                root[0].Bytes is not { Length: > 0 and <= MaximumRowBytes } bytes)
                return null;

            List<ProtoField>? fields = ProtoWire.ReadFields(bytes);
            if (fields is null || fields.Count is < 1 or > 24)
                return null;

            Dictionary<int, ulong> numeric = [];
            foreach (ProtoField field in fields)
            {
                // Unknown length-delimited bytes remain opaque. Duplicate
                // numeric fields make the candidate path ambiguous.
                if (field.Number is < 1 or > 64)
                    return null;
                if (field.WireType == ProtoWireType.Varint &&
                    !numeric.TryAdd(field.Number, field.Varint))
                    return null;
            }

            return numeric.Count is > 0 and <= 16 ? numeric : null;
        }
        catch (OverflowException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}


/// <summary>
/// One operator observation after a craft. Job names are a validated public
/// profession taxonomy, NOT personal character or account identifiers.
/// </summary>
public readonly record struct ProfessionJobReference(
    int Index, string Profession, int Level, ulong Xp)
{
    public override string ToString() => "[local profession reference: redacted]";
}

/// <summary>
/// P3f uses its own input validation: XP values can jump between professions
/// and must increase only within the *same* named profession.
/// </summary>
public static class ProfessionJobReferenceInput
{
    public static bool TryParse(
        string? input, out IReadOnlyList<ProfessionJobReference> references)
    {
        references = [];
        if (string.IsNullOrWhiteSpace(input) || input.Length > 900)
            return false;

        string[] parts = input.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 4 or > 16)
            return false;

        List<ProfessionJobReference> found = [];
        foreach (string part in parts)
        {
            string[] columns = part.Split(':', StringSplitOptions.TrimEntries);
            if (columns.Length != 4 ||
                !int.TryParse(columns[0], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int index) ||
                !ProfessionStudyReferenceInput.IsKnownName(columns[1]) ||
                !int.TryParse(columns[2], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int level) ||
                !ulong.TryParse(columns[3], NumberStyles.None,
                    CultureInfo.InvariantCulture, out ulong xp))
                return false;

            found.Add(new ProfessionJobReference(
                index, columns[1], level, xp));
        }

        if (!Validate(found, 16))
            return false;

        references = found.AsReadOnly();
        return true;
    }

    public static bool Validate(
        IReadOnlyList<ProfessionJobReference> references, int availableCount)
    {
        if (references.Count is < 4 or > 16)
            return false;

        int previousIndex = 0;
        foreach (ProfessionJobReference r in references)
        {
            if (r.Index <= previousIndex || r.Index > availableCount ||
                r.Level is < 1 or > 200 || r.Xp > 1_000_000_000_000 ||
                !ProfessionStudyReferenceInput.IsKnownName(r.Profession))
                return false;

            previousIndex = r.Index;
        }

        var groups = references
            .GroupBy(r => r.Profession, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (groups.Length is < 2 or > 5 || groups.Any(g => g.Count() < 2))
            return false;

        foreach (var group in groups)
        {
            ProfessionJobReference? previous = null;
            foreach (ProfessionJobReference current in group)
            {
                if (previous.HasValue &&
                    (current.Level < previous.Value.Level ||
                     current.Xp <= previous.Value.Xp))
                    return false;
                previous = current;
            }
        }

        return true;
    }
}
