using System.Globalization;

namespace BestCrush.Network.Protocol;

/// <summary>
/// Ephemeral, operator-supplied reference used only to study ipz on one
/// isolated TCP session. No player information is persisted or logged.
/// </summary>
public readonly record struct ProfessionStudyReference(
    string Profession, int Level, ulong TotalExperience,
    ulong ExperienceWithinLevel, ulong ExperienceRequiredForNextLevel)
{
    public ulong LowerThreshold => TotalExperience - ExperienceWithinLevel;
    public ulong UpperThreshold => checked(LowerThreshold + ExperienceRequiredForNextLevel);

    public override string ToString() => "[profession reference: redacted]";
}

public static class ProfessionStudyReferenceInput
{
    // Public profession taxonomy only. These are NOT numeric network IDs.
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alchimiste", "Tailleur", "Bûcheron", "Paysan", "Chasseur",
        "Éleveur", "Forgeron", "Costumage", "Façonneur", "Bijoutier",
        "Mineur", "Pêcheur", "Cordonnier", "Bricoleur", "Sculpteur",
        "Cordomage", "Façomage", "Forgemage", "Joaillomage", "Sculptemage"
    };

    public static bool IsKnownName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Names.Contains(name);

    public static bool TryParse(
        string? input, out IReadOnlyList<ProfessionStudyReference> references)
    {
        references = [];
        if (string.IsNullOrWhiteSpace(input) || input.Length > 1800)
            return false;

        string[] entries = input.Split(',', StringSplitOptions.TrimEntries);
        if (entries.Length is < 2 or > 20)
            return false;

        List<ProfessionStudyReference> found = [];
        HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<(int, ulong)> usedPairs = [];

        foreach (string entry in entries)
        {
            string[] values = entry.Split(':', StringSplitOptions.TrimEntries);
            if (values.Length != 5 || !Names.Contains(values[0]) ||
                !usedNames.Add(values[0]) ||
                !int.TryParse(values[1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int level) ||
                !ulong.TryParse(values[2], NumberStyles.None,
                    CultureInfo.InvariantCulture, out ulong total) ||
                !ulong.TryParse(values[3], NumberStyles.None,
                    CultureInfo.InvariantCulture, out ulong progress) ||
                !ulong.TryParse(values[4], NumberStyles.None,
                    CultureInfo.InvariantCulture, out ulong required) ||
                level is < 2 or > 199 || progress > total ||
                required is 0 or > 10_000_000 ||
                progress >= required ||
                total > 100_000_000_000_000 ||
                !usedPairs.Add((level, total)))
                return false;

            found.Add(new ProfessionStudyReference(
                values[0], level, total, progress, required));
        }

        references = found.AsReadOnly();
        return true;
    }
}

/// <summary>
/// P1e: compares wire fields against locally supplied thresholds.
/// Field 4 is counted across ALL wire types, not just varints.
/// An ID/name association is only a *candidate*, optionally visible
/// with explicit CLI opt-in. No confirmed mapping is inferred.
/// </summary>
public static class ProfessionIpzStudy
{
    public static string Analyze(
        ReadOnlySpan<byte> body,
        IReadOnlyList<ProfessionStudyReference> references,
        bool showCandidateIds = false)
    {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Count is < 2 or > 20 ||
            references.Any(r => !ProfessionStudyReferenceInput.IsKnownName(r.Profession) ||
                r.Level is < 2 or > 199 ||
                r.TotalExperience > 100_000_000_000_000 ||
                r.ExperienceWithinLevel > r.TotalExperience ||
                r.ExperienceRequiredForNextLevel is 0 or > 10_000_000 ||
                r.ExperienceWithinLevel >= r.ExperienceRequiredForNextLevel) ||
            references.Select(r => r.Profession)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != references.Count ||
            references.Select(r => (r.Level, r.TotalExperience))
                .Distinct().Count() != references.Count)
            throw new ArgumentException("Invalid local study references.", nameof(references));

        if (!ProfessionIpzDecoder.TryDecode(
                body, out ProfessionIpzSnapshot? snapshot, out string? reason))
            return $"rejected-layout:{reason ?? "unknown"}";

        // The strict decoder has already checked root and row envelopes.
        List<ProtoField> root = ProtoWire.ReadFields(body)!;
        List<List<ProtoField>> wireRows = root
            .Select(row => ProtoWire.ReadFields(row.Bytes!)!)
            .ToList();

        int f4Rows = wireRows.Count(fields => fields.Any(f => f.Number == 4));
        int f4Varints = wireRows.Sum(fields => fields.Count(f => f.Number == 4 &&
            f.WireType == ProtoWireType.Varint));
        int f4Length = wireRows.Sum(fields => fields.Count(f => f.Number == 4 &&
            f.WireType == ProtoWireType.LengthDelimited));
        int f4Fixed32 = wireRows.Sum(fields => fields.Count(f => f.Number == 4 &&
            f.WireType == ProtoWireType.Fixed32));
        int f4Fixed64 = wireRows.Sum(fields => fields.Count(f => f.Number == 4 &&
            f.WireType == ProtoWireType.Fixed64));

        int f5Present = snapshot!.Rows.Count(row =>
            row.UnidentifiedVarints.ContainsKey(5));
        int f5Distinct = snapshot.Rows
            .Where(row => row.UnidentifiedVarints.ContainsKey(5))
            .Select(row => row.UnidentifiedVarints[5])
            .Distinct().Count();
        bool uniqueF5 = f5Present == 20 && f5Distinct == 20;

        int uniquePairs = 0;
        int ambiguousPairs = 0;
        int f3Matches = 0;
        int f3MissingZero = 0;
        int f6Matches = 0;
        List<(string Profession, ulong Candidate)> mapped = [];

        foreach (ProfessionStudyReference reference in references)
        {
            int[] matchingRowIndexes = snapshot.Rows
                .Select((row, index) => (row, index))
                .Where(x => x.row.Level == reference.Level &&
                    x.row.ExperienceCounter == reference.TotalExperience)
                .Select(x => x.index).ToArray();

            if (matchingRowIndexes.Length != 1)
            {
                ambiguousPairs++;
                continue;
            }

            uniquePairs++;
            int rowIndex = matchingRowIndexes[0];
            List<ProtoField> wire = wireRows[rowIndex];
            ProfessionIpzRow row = snapshot.Rows[rowIndex];

            ProtoField[] f3 = wire.Where(f => f.Number == 3).ToArray();
            bool lowerMatch = false;
            if (f3.Length == 0 && reference.LowerThreshold == 0)
            {
                f3Matches++;
                f3MissingZero++;
                lowerMatch = true;
            }
            else if (f3.Length == 1 &&
                f3[0].WireType == ProtoWireType.Varint &&
                f3[0].Varint == reference.LowerThreshold)
            {
                f3Matches++;
                lowerMatch = true;
            }

            ProtoField[] f6 = wire.Where(f => f.Number == 6).ToArray();
            bool upperMatch = false;
            if (f6.Length == 1 &&
                f6[0].WireType == ProtoWireType.Varint &&
                f6[0].Varint == reference.UpperThreshold)
            {
                f6Matches++;
                upperMatch = true;
            }

            if (uniqueF5 && lowerMatch && upperMatch &&
                row.UnidentifiedVarints.TryGetValue(5, out ulong candidate))
                mapped.Add((reference.Profession, candidate));
        }

        string result = $"rows={snapshot.Rows.Count} pairs={uniquePairs}/{references.Count}" +
            $" ambiguous={ambiguousPairs}" +
            $" f3-lower={f3Matches}/{uniquePairs}" +
            $" (implicit-zero={f3MissingZero})" +
            $" f6-upper={f6Matches}/{uniquePairs}" +
            $" f4=rows:{f4Rows}/20,v:{f4Varints},len:{f4Length}," +
            $"32:{f4Fixed32},64:{f4Fixed64}" +
            $" f5=present:{f5Present}/20,distinct:{f5Distinct}" +
            $" candidate-links={mapped.Count}/{references.Count}" +
            " mapping=unverified";

        // Explicit opt-in; even then, only disclose candidate f5 for
        // unambiguously matched reference rows and not other wire data.
        // Its semantic identity is NOT proven and cannot be auto-saved.
        if (showCandidateIds && mapped.Count > 0)
            result += " CANDIDATE_IDS=[" +
                string.Join(";", mapped.Select(x =>
                    $"{x.Profession}:f5={x.Candidate}")) + "]";

        return result;
    }
}
