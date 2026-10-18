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
            return "match=unique numeric-siblings=[" +
                   FormatFields(siblings.Keys) +
                   "] parent-numeric=[" + FormatFields(parentNumbers.Keys) +
                   "] identity=unverified";
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
