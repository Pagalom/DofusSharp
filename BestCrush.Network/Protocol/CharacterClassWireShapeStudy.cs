namespace BestCrush.Network.Protocol;

/// <summary>
/// P2g research-only, value-blind structural investigation of potential
/// class fields near the already identified name/level wire paths.
/// This does NOT determine ClassId or confirm character identity.
/// </summary>
public static class CharacterClassWireShapeStudy
{
    private const int MaxBodyBytes = 8192;
    private const int MaxFields = 256;

    public static string Describe(string opcode, ReadOnlySpan<byte> body,
        CharacterNameLevelReference reference)
    {
        CharacterNameLevelReferenceInput.Validate(reference);
        if (opcode is not ("ksb" or "ksc") ||
            body.IsEmpty || body.Length > MaxBodyBytes ||
            !CharacterWireCandidateDecoder.TryDecode(opcode, body,
                out CharacterWireCandidateSnapshot? snapshot) ||
            snapshot is null)
            return "p2g=unavailable class=unknown";

        CharacterWireCandidate[] selected = snapshot.Candidates
            .Where(c => c.Level == reference.Level &&
                string.Equals(c.Name, reference.Name, StringComparison.Ordinal))
            .ToArray();
        if (selected.Length != 1)
            return "p2g=reference-not-unique class=unknown";

        List<ProtoField>? root = Read(body.ToArray());
        if (root is null)
            return "p2g=unavailable class=unknown";

        List<(List<ProtoField> Parent, List<ProtoField> Leaf, ulong Candidate)> rows = [];
        if (opcode == "ksb")
        {
            if (!TrySingleNested(root, 1, out List<ProtoField>? outer) ||
                outer is null ||
                !TrySingleNested(outer, 1, out List<ProtoField>? parent) ||
                parent is null ||
                !TryRow(parent, out var leaf, out ulong id) || leaf is null)
                return "p2g=unavailable class=unknown";

            rows.Add((parent, leaf, id));
        }
        else
        {
            foreach (ProtoField f in root.Where(f => f.Number == 2))
            {
                if (f.WireType != ProtoWireType.LengthDelimited ||
                    f.Bytes is null)
                    return "p2g=unavailable class=unknown";

                List<ProtoField>? parent = Read(f.Bytes);
                if (parent is null ||
                    !TryRow(parent, out List<ProtoField>? leaf, out ulong id) ||
                    leaf is null)
                    return "p2g=unavailable class=unknown";

                rows.Add((parent, leaf, id));
            }
        }

        if (rows.Count != snapshot.Candidates.Count)
            return "p2g=unavailable class=unknown";

        var chosen = rows.Where(r => r.Candidate == selected[0].CharacterIdCandidate)
            .ToArray();
        if (chosen.Length != 1)
            return "p2g=reference-not-unique class=unknown";

        // No scalar values are included in output, only wire type, field
        // number, repetition count and anonymous equality differences.
        string leafShape = DescribeShape(chosen[0].Leaf);
        string parentShape = DescribeShape(chosen[0].Parent);
        string differences = "unavailable";

        if (opcode == "ksc" && rows.Count >= 2)
        {
            HashSet<string> differing = new(StringComparer.Ordinal);
            foreach (var other in rows.Where(r =>
                r.Candidate != selected[0].CharacterIdCandidate))
            {
                CollectDifferentFields("leaf.", chosen[0].Leaf, other.Leaf,
                    3, 6, differing);
                CollectDifferentFields("parent.", chosen[0].Parent, other.Parent,
                    3, 4, differing);
            }
            differences = differing.Count == 0
                ? "none"
                : string.Join(",", differing.OrderBy(x => x, StringComparer.Ordinal)
                    .Take(32)) + (differing.Count > 32 ? ",..." : "");
        }

        return "p2g=observed leaf=[" + leafShape +
               "] parent=[" + parentShape +
               "] between-characters-diff=[" + differences +
               "] class=unknown";
    }

    private static List<ProtoField>? Read(byte[] bytes)
    {
        if (bytes.Length is < 1 or > MaxBodyBytes)
            return null;
        var fields = ProtoWire.ReadFields(bytes);
        return fields is { Count: > 0 and <= MaxFields } ? fields : null;
    }

    private static bool TrySingleNested(
        List<ProtoField> fields, int number,
        out List<ProtoField>? nested)
    {
        nested = null;
        var matches = fields.Where(f => f.Number == number).ToArray();
        if (matches.Length != 1 ||
            matches[0].WireType != ProtoWireType.LengthDelimited ||
            matches[0].Bytes is null)
            return false;

        nested = Read(matches[0].Bytes);
        return nested is not null;
    }

    private static bool TryRow(
        List<ProtoField> parent, out List<ProtoField>? leaf, out ulong id)
    {
        leaf = null;
        id = 0;
        var ids = parent.Where(f => f.Number == 3).ToArray();
        if (ids.Length != 1 ||
            ids[0].WireType != ProtoWireType.Varint ||
            ids[0].Varint == 0 ||
            !TrySingleNested(parent, 4, out leaf))
            return false;

        id = ids[0].Varint;
        return true;
    }

    private static string DescribeShape(List<ProtoField> fields) =>
        string.Join(",", fields
            .GroupBy(f => (f.Number, f.WireType))
            .OrderBy(g => g.Key.Number)
            .ThenBy(g => g.Key.WireType)
            .Select(g => "f" + g.Key.Number + ":" +
                WireLabel(g.Key.WireType) +
                (g.Count() > 1 ? "x" + g.Count() : "")));

    private static string WireLabel(ProtoWireType type) => type switch
    {
        ProtoWireType.Varint => "v",
        ProtoWireType.LengthDelimited => "len",
        ProtoWireType.Fixed32 => "32",
        ProtoWireType.Fixed64 => "64",
        _ => "unknown"
    };

    private static void CollectDifferentFields(
        string prefix, List<ProtoField> first, List<ProtoField> other,
        int omittedField1, int omittedField2, HashSet<string> output)
    {
        foreach (var field in first
            .GroupBy(f => f.Number)
            .Where(g => g.Count() == 1 && g.Key != omittedField1 &&
                g.Key != omittedField2))
        {
            ProtoField a = field.First();
            ProtoField[] b = other.Where(f => f.Number == field.Key).ToArray();
            if (b.Length != 1 || b[0].WireType != a.WireType)
                continue;

            bool equal = a.WireType switch
            {
                ProtoWireType.Varint => a.Varint == b[0].Varint,
                ProtoWireType.Fixed32 => a.Fixed32 == b[0].Fixed32,
                ProtoWireType.Fixed64 => a.Fixed64 == b[0].Fixed64,
                ProtoWireType.LengthDelimited => a.Bytes is not null &&
                    b[0].Bytes is not null &&
                    a.Bytes.AsSpan().SequenceEqual(b[0].Bytes),
                _ => true
            };
            if (!equal)
                output.Add(prefix + "f" + field.Key + ":" +
                    WireLabel(a.WireType));
        }
    }
}
