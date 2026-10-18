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

    public int Count
    {
        get { lock (_sync) return _observations.Count; }
    }

    /// <returns>An anonymous summary for a valid mono-row ipz, otherwise null.</returns>
    public string? Observe(int sessionNumber, ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 || body.IsEmpty ||
            body.Length > MaximumRowBytes + 8)
            return null;

        // This shape is deliberately distinct from the existing 20-row
        // ProfessionIpzDecoder and is never passed into that decoder.
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
