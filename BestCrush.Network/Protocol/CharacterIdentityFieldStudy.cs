using System.Collections.ObjectModel;
using System.Text;

namespace BestCrush.Network.Protocol;

/// <summary>
/// P2b exploratory wire observation; in-memory only, no account or
/// character values in its textual representation.
/// </summary>
public sealed class CharacterIdentityFieldObservation
{
    public string Opcode { get; }
    public IReadOnlyDictionary<int, ulong> NumericSiblings { get; }
    public IReadOnlyDictionary<int, ulong> NumericParent { get; }

    internal CharacterIdentityFieldObservation(
        string opcode, Dictionary<int, ulong> siblings,
        Dictionary<int, ulong> parent)
    {
        Opcode = opcode;
        NumericSiblings = new ReadOnlyDictionary<int, ulong>(siblings);
        NumericParent = new ReadOnlyDictionary<int, ulong>(parent);
    }

    public override string ToString() => "[character identity fields: redacted]";
}

/// <summary>
/// Compare ksb/ksc leaf-node fields using an exact local name+level
/// reference, never treating matching numeric fields as a confirmed ID.
/// The operator's reference is not part of any returned diagnostic.
/// </summary>
public static class CharacterIdentityFieldStudy
{
    private const int MaxBodyBytes = 8192;
    private const int MaxNodeBytes = 8192;
    private const int MaxNodeFields = 1024;
    private const int MaxMatches = 2;

    // The parent of the name/level record is f1.f1 in ksb, f2 in ksc.
    // A numeric field such as parent f3 may exist even when the name/level
    // record itself contains no other numeric sibling fields.
    private sealed record Candidate(byte[] Body, byte[] ParentBody);

    public static string Observe(string opcode, ReadOnlySpan<byte> body,
        CharacterNameLevelReference reference,
        out CharacterIdentityFieldObservation? observation)
    {
        CharacterNameLevelReferenceInput.Validate(reference);
        observation = null;

        int[] path = opcode switch
        {
            "ksb" => [1, 1, 4],
            "ksc" => [2, 4],
            _ => []
        };
        if (path.Length == 0)
            return "unsupported";
        if (body.IsEmpty || body.Length > MaxBodyBytes)
            return "skipped-size";

        try
        {
            byte[] expectedName = Encoding.UTF8.GetBytes(reference.Name);
            byte[] root = body.ToArray();
            List<Candidate> candidates = [new(root, [])];
            foreach (int fieldNumber in path)
            {
                List<Candidate> next = [];
                foreach (Candidate candidate in candidates)
                {
                    if (candidate.Body.Length > MaxNodeBytes)
                        return "rejected-layout";
                    List<ProtoField>? parent = ProtoWire.ReadFields(candidate.Body);
                    if (parent is null || parent.Count > MaxNodeFields)
                        return "rejected-layout";
                    foreach (ProtoField field in parent)
                    {
                        if (field.Number == fieldNumber &&
                            field.WireType == ProtoWireType.LengthDelimited &&
                            field.Bytes is { Length: > 0 and <= MaxNodeBytes })
                        {
                            next.Add(new Candidate(field.Bytes, candidate.Body));
                            if (next.Count > MaxNodeFields)
                                return "rejected-layout";
                        }
                    }
                }
                candidates = next;
                if (candidates.Count == 0)
                    return "match=absent";
            }

            int matched = 0;
            Dictionary<int, ulong>? siblings = null;
            Dictionary<int, ulong>? parentNumbers = null;
            foreach (Candidate candidate in candidates)
            {
                List<ProtoField>? fields = ProtoWire.ReadFields(candidate.Body);
                if (fields is null || fields.Count > MaxNodeFields)
                    return "rejected-layout";

                if (fields.Count(f => f.Number == 6 &&
                        f.WireType == ProtoWireType.LengthDelimited &&
                        f.Bytes is not null &&
                        f.Bytes.AsSpan().SequenceEqual(expectedName)) != 1 ||
                    fields.Count(f => f.Number == 3 &&
                        f.WireType == ProtoWireType.Varint &&
                        f.Varint == (ulong)reference.Level) != 1)
                    continue;

                matched++;
                if (matched >= MaxMatches)
                    return "match=ambiguous";

                // Only extract parent values after finding a unique exact
                // name + level match in the child. Never correlate a parent
                // of a different character from ksc's repeated rows.
                List<ProtoField>? parentFields =
                    ProtoWire.ReadFields(candidate.ParentBody);
                if (parentFields is null || parentFields.Count > MaxNodeFields)
                    return "rejected-layout";

                siblings = UniqueVarints(fields, excludeLevel: true);
                parentNumbers = UniqueVarints(parentFields, excludeLevel: false);
            }

            if (matched == 0 || siblings is null || parentNumbers is null)
                return "match=absent";

            observation = new CharacterIdentityFieldObservation(
                opcode, siblings, parentNumbers);
            string kscRows = opcode == "ksc"
                ? " " + DescribeKscRows(root, expectedName, reference.Level)
                : "";
            return "match=unique numeric-siblings=[" +
                   FormatFields(siblings.Keys) +
                   "] parent-numeric=[" + FormatFields(parentNumbers.Keys) +
                   "] identity=unverified" + kscRows;
        }
        catch (OverflowException)
        {
            return "rejected-layout";
        }
        catch (ArgumentOutOfRangeException)
        {
            return "rejected-layout";
        }
    }

    /// <summary>
    /// Describe repeated f2 entries from the SAME ksc message. No names,
    /// candidate IDs, hashes or value-derived identifiers are returned.
    /// This is structural evidence only; it cannot establish that ksc
    /// lists account characters without further semantic validation.
    /// </summary>
    private static string DescribeKscRows(
        byte[] root, byte[] expectedName, int expectedLevel)
    {
        List<ProtoField>? rootFields = ProtoWire.ReadFields(root);
        if (rootFields is null || rootFields.Count > MaxNodeFields)
            return "ksc-rows=[layout=unverified]";

        ProtoField[] rows = rootFields.Where(f => f.Number == 2 &&
            f.WireType == ProtoWireType.LengthDelimited).ToArray();
        if (rows.Length is < 1 or > 32)
            return "ksc-rows=[layout=unverified]";

        List<(ulong Id, byte[] Name, int Level)> observations = [];
        foreach (ProtoField row in rows)
        {
            if (row.Bytes is not { Length: > 0 and <= MaxNodeBytes })
                return "ksc-rows=[layout=unverified]";

            List<ProtoField>? parent = ProtoWire.ReadFields(row.Bytes);
            if (parent is null || parent.Count > MaxNodeFields)
                return "ksc-rows=[layout=unverified]";

            ProtoField[] ids = parent.Where(f => f.Number == 3 &&
                f.WireType == ProtoWireType.Varint && f.Varint > 0).ToArray();
            ProtoField[] leaves = parent.Where(f => f.Number == 4 &&
                f.WireType == ProtoWireType.LengthDelimited).ToArray();
            if (ids.Length != 1 || leaves.Length != 1 ||
                leaves[0].Bytes is not { Length: > 0 and <= MaxNodeBytes })
                return "ksc-rows=[layout=unverified]";

            List<ProtoField>? leaf = ProtoWire.ReadFields(leaves[0].Bytes);
            if (leaf is null || leaf.Count > MaxNodeFields)
                return "ksc-rows=[layout=unverified]";

            ProtoField[] names = leaf.Where(f => f.Number == 6 &&
                f.WireType == ProtoWireType.LengthDelimited &&
                f.Bytes is { Length: > 0 and <= 80 }).ToArray();
            ProtoField[] levels = leaf.Where(f => f.Number == 3 &&
                f.WireType == ProtoWireType.Varint &&
                f.Varint is >= 1 and <= 200).ToArray();
            if (names.Length != 1 || levels.Length != 1)
                return "ksc-rows=[layout=unverified]";

            observations.Add((ids[0].Varint, names[0].Bytes!,
                (int)levels[0].Varint));
        }

        int matchingRows = observations.Count(r =>
            r.Level == expectedLevel &&
            r.Name.AsSpan().SequenceEqual(expectedName));
        if (matchingRows != 1)
            return "ksc-rows=[match=ambiguous]";

        // Distinctness is calculated locally without leaking values.
        bool namesDistinct = observations
            .Select((record, index) => observations
                .Skip(index + 1).All(other =>
                    !record.Name.AsSpan().SequenceEqual(other.Name)))
            .All(b => b);
        bool idsDistinct = observations.Select(r => r.Id).Distinct().Count() ==
            observations.Count;

        ulong selectedF3 = observations.Single(r =>
            r.Level == expectedLevel &&
            r.Name.AsSpan().SequenceEqual(expectedName)).Id;
        bool selectedF3Unique = observations.Count(r => r.Id == selectedF3) == 1;

        return "ksc-rows=[total=" + rows.Length +
               " complete=" + observations.Count +
               " names-distinct=" + namesDistinct.ToString().ToLowerInvariant() +
               " f3-distinct=" + idsDistinct.ToString().ToLowerInvariant() +
               " selected-f3-unique=" +
               selectedF3Unique.ToString().ToLowerInvariant() +
               " semantic=unverified]";
    }

    private static Dictionary<int, ulong> UniqueVarints(
        List<ProtoField> fields, bool excludeLevel)
    {
        Dictionary<int, ulong> values = [];
        HashSet<int> duplicates = [];
        foreach (ProtoField field in fields)
        {
            if (field.WireType != ProtoWireType.Varint ||
                (excludeLevel && field.Number == 3))
                continue;
            if (!values.TryAdd(field.Number, field.Varint))
                duplicates.Add(field.Number);
        }
        foreach (int duplicate in duplicates)
            values.Remove(duplicate);
        return values;
    }

    private static string FormatFields(IEnumerable<int> fields)
    {
        int[] numbers = fields.OrderBy(n => n).ToArray();
        return numbers.Length == 0
            ? "none"
            : string.Join(",", numbers.Select(n => "f" + n));
    }

    private static string SharedEqualFields(
        IReadOnlyDictionary<int, ulong> first,
        IReadOnlyDictionary<int, ulong> second) =>
        FormatFields(first
            .Where(pair => pair.Value != 0 &&
                second.TryGetValue(pair.Key, out ulong other) &&
                pair.Value == other)
            .Select(pair => pair.Key));

    public static string Compare(
        CharacterIdentityFieldObservation first,
        CharacterIdentityFieldObservation second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Opcode == second.Opcode ||
            !new[] { first.Opcode, second.Opcode }.OrderBy(x => x)
                .SequenceEqual(new[] { "ksb", "ksc" }))
            throw new ArgumentException("Expected different ksb/ksc message families.");

        string leaf = SharedEqualFields(
            first.NumericSiblings, second.NumericSiblings);
        string parent = SharedEqualFields(
            first.NumericParent, second.NumericParent);
        return "same-session=yes shared-equal-numeric=[" + leaf +
               "] shared-equal-parent=[" + parent +
               "] id=unverified identity=unverified";
    }
}
