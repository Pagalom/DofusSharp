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

    internal CharacterIdentityFieldObservation(
        string opcode, Dictionary<int, ulong> siblings)
    {
        Opcode = opcode;
        NumericSiblings = new ReadOnlyDictionary<int, ulong>(siblings);
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
            List<byte[]> candidates = [body.ToArray()];
            foreach (int fieldNumber in path)
            {
                List<byte[]> next = [];
                foreach (byte[] bytes in candidates)
                {
                    if (bytes.Length > MaxNodeBytes)
                        return "rejected-layout";
                    List<ProtoField>? parent = ProtoWire.ReadFields(bytes);
                    if (parent is null || parent.Count > MaxNodeFields)
                        return "rejected-layout";
                    foreach (ProtoField field in parent)
                    {
                        if (field.Number == fieldNumber &&
                            field.WireType == ProtoWireType.LengthDelimited &&
                            field.Bytes is { Length: > 0 and <= MaxNodeBytes })
                        {
                            next.Add(field.Bytes);
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
            foreach (byte[] leafBytes in candidates)
            {
                List<ProtoField>? fields = ProtoWire.ReadFields(leafBytes);
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

                Dictionary<int, ulong> values = [];
                HashSet<int> duplicates = [];
                foreach (ProtoField field in fields)
                {
                    if (field.WireType != ProtoWireType.Varint ||
                        field.Number is 3 or 6)
                        continue;
                    if (!values.TryAdd(field.Number, field.Varint))
                        duplicates.Add(field.Number);
                }
                foreach (int number in duplicates)
                    values.Remove(number);
                siblings = values;
            }

            if (matched == 0 || siblings is null)
                return "match=absent";

            observation = new CharacterIdentityFieldObservation(opcode, siblings);
            string siblingFields = siblings.Count == 0
                ? "none"
                : string.Join(",", siblings.Keys.OrderBy(k => k).Select(k => "f" + k));
            return "match=unique numeric-siblings=[" + siblingFields +
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

        int[] matchedFields = first.NumericSiblings
            .Where(pair => pair.Value != 0 &&
                second.NumericSiblings.TryGetValue(pair.Key, out ulong other) &&
                other == pair.Value)
            .Select(pair => pair.Key)
            .OrderBy(x => x).ToArray();

        string fields = matchedFields.Length == 0
            ? "none"
            : string.Join(",", matchedFields.Select(x => "f" + x));
        return "same-session=yes shared-equal-numeric=[" + fields +
               "] id=unverified identity=unverified";
    }
}
