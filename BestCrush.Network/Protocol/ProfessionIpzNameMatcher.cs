using System.Globalization;

namespace BestCrush.Network.Protocol;

/// <summary>
/// In-memory named level and cumulative XP reference. This is not an account
/// or character identity and must never be persisted with protocol mappings.
/// </summary>
public readonly record struct ProfessionNamedReference(
    string Profession, int Level, ulong TotalExperience)
{
    public override string ToString() => "[profession: values redacted]";
}

public static class ProfessionNamedReferenceInput
{
    public static bool TryParse(
        string? input, out IReadOnlyList<ProfessionNamedReference> references)
    {
        references = [];
        if (string.IsNullOrWhiteSpace(input) || input.Length > 1200)
            return false;

        string[] parts = input.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 20)
            return false;

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        HashSet<(int Level, ulong Xp)> pairs = [];
        List<ProfessionNamedReference> result = [];
        foreach (string part in parts)
        {
            string[] values = part.Split(':', StringSplitOptions.TrimEntries);
            if (values.Length != 3 ||
                !ProfessionStudyReferenceInput.IsKnownName(values[0]) ||
                !names.Add(values[0]) ||
                !int.TryParse(values[1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int level) ||
                !ulong.TryParse(values[2], NumberStyles.None,
                    CultureInfo.InvariantCulture, out ulong xp) ||
                level is < 1 or > 200 || xp > 100_000_000_000_000 ||
                !pairs.Add((level, xp)))
                return false;

            result.Add(new ProfessionNamedReference(values[0], level, xp));
        }

        references = result.AsReadOnly();
        return true;
    }

    internal static void Validate(IReadOnlyList<ProfessionNamedReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count is < 2 or > 20 ||
            references.Any(r => !ProfessionStudyReferenceInput.IsKnownName(r.Profession) ||
                r.Level is < 1 or > 200 ||
                r.TotalExperience > 100_000_000_000_000) ||
            references.Select(r => r.Profession)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != references.Count ||
            references.Select(r => (r.Level, r.TotalExperience))
                .Distinct().Count() != references.Count)
            throw new ArgumentException("Invalid named profession references.",
                nameof(references));
    }
}

/// <summary>
/// Research-only link between a unique (level, total XP) pair and an f5
/// candidate. A unique f5 is not proof of a global profession identifier.
/// </summary>
public static class ProfessionIpzNameMatcher
{
    public static string Analyze(
        ReadOnlySpan<byte> body,
        IReadOnlyList<ProfessionNamedReference> references,
        bool showCandidateIds = false)
    {
        ProfessionNamedReferenceInput.Validate(references);

        if (!ProfessionIpzDecoder.TryDecode(body,
                out ProfessionIpzSnapshot? snapshot, out string? reason))
            return $"rejected-layout:{reason ?? "unknown"}";

        var rows = snapshot!.Rows;
        ulong[] candidateIds = rows
            .Where(row => row.UnidentifiedVarints.ContainsKey(5))
            .Select(row => row.UnidentifiedVarints[5]).ToArray();

        bool f5Unique = candidateIds.Length == rows.Count &&
            candidateIds.Distinct().Count() == rows.Count;
        int matches = 0;
        List<(string Name, ulong Candidate)> candidates = [];

        foreach (ProfessionNamedReference reference in references)
        {
            ProfessionIpzRow[] found = rows
                .Where(row => row.Level == reference.Level &&
                    row.ExperienceCounter == reference.TotalExperience)
                .ToArray();

            if (found.Length != 1)
                continue;

            matches++;
            if (f5Unique &&
                found[0].UnidentifiedVarints.TryGetValue(5, out ulong id))
                candidates.Add((reference.Profession, id));
        }

        string summary = $"rows={rows.Count} pairs={matches}/{references.Count}" +
            $" f5-unique={f5Unique.ToString().ToLowerInvariant()}" +
            $" candidate-links={candidates.Count}/{references.Count}" +
            " mapping=unverified";

        if (showCandidateIds && candidates.Count > 0)
            summary += " CANDIDATE_IDS=[" +
                string.Join(";", candidates.Select(c =>
                    $"{c.Name}:f5={c.Candidate.ToString(CultureInfo.InvariantCulture)}")) +
                "]";

        return summary;
    }
}
