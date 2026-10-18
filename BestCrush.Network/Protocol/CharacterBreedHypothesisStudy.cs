namespace BestCrush.Network.Protocol;

/// <summary>
/// P2i experimental comparison of a known class/breed reference with
/// the numeric f5.f3 candidate on Dofus 3.6.11.15. Produces redacted
/// booleans, never a decoded or confirmed character class.
/// </summary>
public static class CharacterBreedHypothesisStudy
{
    private const int MaxBodyBytes = 8192;
    private const int MaxF5Bytes = 2048;
    private const int MaxFields = 256;

    public static string Describe(string opcode, ReadOnlySpan<byte> body,
        CharacterNameLevelReference reference, int expectedBreedId)
    {
        CharacterNameLevelReferenceInput.Validate(reference);
        if (expectedBreedId is < 1 or > 19)
            throw new ArgumentOutOfRangeException(nameof(expectedBreedId));

        if (opcode is not ("ksb" or "ksc") ||
            body.IsEmpty || body.Length > MaxBodyBytes ||
            !CharacterWireCandidateDecoder.TryDecode(
                opcode, body, out CharacterWireCandidateSnapshot? snapshot) ||
            snapshot is null)
            return "p2i=unavailable class=unverified";

        CharacterWireCandidate[] matching = snapshot.Candidates.Where(c =>
            c.Level == reference.Level &&
            string.Equals(c.Name, reference.Name, StringComparison.Ordinal))
            .ToArray();
        if (matching.Length != 1)
            return "p2i=reference-not-unique class=unverified";

        List<ProtoField>? root = Parse(body.ToArray(), MaxBodyBytes);
        if (root is null)
            return "p2i=unavailable class=unverified";

        List<(ulong Id, ulong BreedCandidate)> candidates = [];
        if (opcode == "ksb")
        {
            if (!TrySingleBytes(root, 1, out byte[]? outer) ||
                outer is null ||
                Parse(outer, MaxBodyBytes) is not { } container ||
                !TrySingleBytes(container, 1, out byte[]? row) ||
                row is null ||
                !TryReadCandidate(row, out ulong id, out ulong breed))
                return "p2i=unavailable class=unverified";

            candidates.Add((id, breed));
        }
        else
        {
            ProtoField[] rows = root.Where(f => f.Number == 2).ToArray();
            if (rows.Length == 0 || rows.Length != root.Count ||
                rows.Length > 16)
                return "p2i=unavailable class=unverified";

            foreach (ProtoField row in rows)
            {
                if (row.WireType != ProtoWireType.LengthDelimited ||
                    row.Bytes is null ||
                    !TryReadCandidate(row.Bytes, out ulong id, out ulong breed))
                    return "p2i=unavailable class=unverified";

                candidates.Add((id, breed));
            }
        }

        if (candidates.Count != snapshot.Candidates.Count)
            return "p2i=unavailable class=unverified";

        var selected = candidates.Where(c =>
            c.Id == matching[0].CharacterIdCandidate).ToArray();
        if (selected.Length != 1)
            return "p2i=unavailable class=unverified";

        // The class ID comes from independent game reference material,
        // NOT from the wire. Compare values only in memory.
        bool equal = selected[0].BreedCandidate == (ulong)expectedBreedId;
        string otherDiffers = opcode == "ksc" && candidates.Count > 1
            ? (candidates.Where(c => c.Id != selected[0].Id)
                .All(c => c.BreedCandidate != selected[0].BreedCandidate)
                ? "yes" : "no")
            : "unavailable";

        return "p2i=f5-f3-check reference-equal=" +
               (equal ? "yes" : "no") +
               " peers-differ=" + otherDiffers +
               " class=unverified";
    }

    private static bool TryReadCandidate(
        byte[] row, out ulong id, out ulong breed)
    {
        id = 0;
        breed = 0;
        List<ProtoField>? parent = Parse(row, MaxBodyBytes);
        if (parent is null ||
            !TrySingleVarint(parent, 3, out id) || id == 0 ||
            !TrySingleBytes(parent, 4, out byte[]? leafBytes) ||
            leafBytes is null)
            return false;

        List<ProtoField>? leaf = Parse(leafBytes, MaxBodyBytes);
        if (leaf is null ||
            !TrySingleBytes(leaf, 5, out byte[]? f5Bytes) ||
            f5Bytes is null || f5Bytes.Length > MaxF5Bytes)
            return false;

        List<ProtoField>? f5 = Parse(f5Bytes, MaxF5Bytes);
        return f5 is not null &&
               TrySingleVarint(f5, 3, out breed) &&
               breed > 0;
    }

    private static List<ProtoField>? Parse(byte[] bytes, int maxLength)
    {
        if (bytes.Length is < 1 || bytes.Length > maxLength)
            return null;
        try
        {
            List<ProtoField>? fields = ProtoWire.ReadFields(bytes);
            return fields is { Count: > 0 and <= MaxFields } ? fields : null;
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

    private static bool TrySingleVarint(
        List<ProtoField> fields, int field, out ulong value)
    {
        value = 0;
        ProtoField[] matching = fields.Where(f => f.Number == field).ToArray();
        if (matching.Length != 1 ||
            matching[0].WireType != ProtoWireType.Varint)
            return false;
        value = matching[0].Varint;
        return true;
    }

    private static bool TrySingleBytes(
        List<ProtoField> fields, int field, out byte[]? value)
    {
        value = null;
        ProtoField[] matching = fields.Where(f => f.Number == field).ToArray();
        if (matching.Length != 1 ||
            matching[0].WireType != ProtoWireType.LengthDelimited ||
            matching[0].Bytes is not { Length: > 0 and <= MaxBodyBytes })
            return false;
        value = matching[0].Bytes;
        return true;
    }
}
