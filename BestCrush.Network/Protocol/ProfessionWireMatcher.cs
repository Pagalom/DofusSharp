namespace BestCrush.Network.Protocol;

/// <summary>
/// Local, ephemeral profession reference. Represents the XP shown within
/// the current level, not necessarily Dofus' cumulative network XP.
/// Never persist, log or pass the numeric values to an output event.
/// </summary>
public readonly record struct ProfessionReference(int Level, int ProgressXp);

public static class ProfessionReferenceInput
{
    public static bool TryParse(string? input, out IReadOnlyList<ProfessionReference> values)
    {
        values = [];
        if (string.IsNullOrWhiteSpace(input) || input.Length > 320)
            return false;

        string[] segments = input.Split(',', StringSplitOptions.TrimEntries);
        if (segments.Length is < 2 or > 20)
            return false;

        List<ProfessionReference> parsed = [];
        HashSet<ProfessionReference> unique = [];
        foreach (string segment in segments)
        {
            string[] parts = segment.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int level) ||
                !int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int progress) ||
                level is < 2 or > 200 || progress is < 0 or > 1_000_000)
                return false;

            ProfessionReference reference = new(level, progress);
            if (!unique.Add(reference))
                return false;
            parsed.Add(reference);
        }

        values = parsed.AsReadOnly();
        return true;
    }
}

/// <summary>
/// Bounded, value-blind exploratory correlation of two exact numeric fields
/// (level + visible XP) within the SAME protobuf message node. Field numbers,
/// common ancestor path and the number of distinct matched reference pairs
/// are the ONLY returned information. No claim of professional identity.
/// </summary>
public static class ProfessionWireMatcher
{
    private const int MaxBodyBytes = 256 * 1024;
    private const int MaxWireFields = 50000;
    private const int MaxDepth = 6;
    private const int MaxNodeNumericFields = 64;
    private const int MaxNodeChildren = 12000;
    private const int MaxCandidates = 6;

    private readonly record struct Candidate(string Path, int LevelField, int XpField);

    private sealed class ScanState(int fuel)
    {
        internal int Fuel = fuel;
        internal readonly Dictionary<Candidate, HashSet<int>> Hits = [];
    }

    public static string? Describe(
        ReadOnlySpan<byte> body, IReadOnlyList<ProfessionReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count is < 2 or > 20 ||
            references.Distinct().Count() != references.Count ||
            references.Any(r => r.Level is < 2 or > 200 ||
                r.ProgressXp is < 0 or > 1_000_000))
            throw new ArgumentException("Invalid reference set.", nameof(references));

        if (body.IsEmpty || body.Length > MaxBodyBytes)
            return null;

        ScanState state = new(MaxWireFields);
        Scan(body, "", 0, state, references);

        // Two *different* level/XP pairs sharing the same field locations
        // reduces coincidental matches. Still only a tentative correlation.
        var findings = state.Hits
            .Where(item => item.Value.Count >= 2)
            .OrderByDescending(item => item.Value.Count)
            .ThenBy(item => item.Key.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Key.LevelField)
            .ThenBy(item => item.Key.XpField)
            .Take(MaxCandidates)
            .Select(item =>
                $"node={item.Key.Path} level=f{item.Key.LevelField}" +
                $" xp=f{item.Key.XpField} pairs={item.Value.Count}");

        string summary = string.Join(" ; ", findings);
        return summary.Length == 0 ? null : summary;
    }

    private static void Scan(
        ReadOnlySpan<byte> data, string path, int depth,
        ScanState state, IReadOnlyList<ProfessionReference> references)
    {
        if (depth > MaxDepth || state.Fuel <= 0)
            return;

        int cursor = 0;
        List<(int Field, ulong Value)> numbers = [];
        List<(int Field, int Offset, int Length)> children = [];

        // Validate this complete node before analyzing or recursing.
        // Length-delimited fields might instead be text/bytes/packed
        // scalars; any hypothetical child failing protobuf parsing
        // is ignored without invalidating the parent.
        while (cursor < data.Length)
        {
            if (--state.Fuel < 0)
                return;

            if (!TryVarint(data, ref cursor, out ulong tag))
                return;
            ulong field = tag >> 3;
            int wire = (int)(tag & 7);
            if (field is 0 or > 536870911 || wire is not (0 or 1 or 2 or 5))
                return;

            if (wire == 0)
            {
                if (!TryVarint(data, ref cursor, out ulong value))
                    return;
                if (numbers.Count < MaxNodeNumericFields)
                    numbers.Add(((int)field, value));
            }
            else if (wire is 1 or 5)
            {
                int count = wire == 1 ? 8 : 4;
                if (data.Length - cursor < count)
                    return;
                cursor += count;
            }
            else
            {
                if (!TryVarint(data, ref cursor, out ulong length) ||
                    length > (ulong)(data.Length - cursor))
                    return;
                if (children.Count < MaxNodeChildren)
                    children.Add(((int)field, cursor, (int)length));
                cursor += (int)length;
            }
        }

        if (numbers.Count > 1)
        {
            for (int i = 0; i < references.Count; i++)
            {
                ProfessionReference reference = references[i];
                foreach (var level in numbers)
                {
                    if (level.Value != (ulong)reference.Level)
                        continue;

                    foreach (var xp in numbers)
                    {
                        if (level.Field == xp.Field ||
                            xp.Value != (ulong)reference.ProgressXp)
                            continue;

                        Candidate key = new(path.Length == 0 ? "root" : path,
                            level.Field, xp.Field);
                        if (!state.Hits.TryGetValue(key, out HashSet<int>? hits))
                        {
                            hits = [];
                            state.Hits.Add(key, hits);
                        }
                        hits.Add(i);
                    }
                }
            }
        }

        if (depth == MaxDepth)
            return;

        foreach (var child in children)
        {
            if (state.Fuel <= 0)
                break;
            if (child.Length is <= 0 or > MaxBodyBytes)
                continue;

            // No child contents leave this stack frame; no private values
            // are stored in the returned diagnostic or in session state.
            string childPath = path.Length == 0
                ? $"f{child.Field}"
                : $"{path}.f{child.Field}";
            Scan(data.Slice(child.Offset, child.Length),
                childPath, depth + 1, state, references);
        }
    }

    private static bool TryVarint(
        ReadOnlySpan<byte> data, ref int cursor, out ulong value)
    {
        value = 0;
        for (int index = 0; index < 10 && cursor < data.Length; index++)
        {
            byte part = data[cursor++];
            if (index == 9 && (part & 0xfe) != 0)
                return false;
            value |= (ulong)(part & 0x7f) << (index * 7);
            if ((part & 0x80) == 0)
                return true;
        }

        return false;
    }
}
