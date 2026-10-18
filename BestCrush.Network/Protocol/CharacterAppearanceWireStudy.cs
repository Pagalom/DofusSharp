namespace BestCrush.Network.Protocol;

/// <summary>
/// P2h: value-blind structural study of leaf.f5 for Dofus 3.6.11.15.
/// A field differing between two characters is NOT a confirmed class field.
/// No private identifier, name, level, class value, payload or hash is logged.
/// </summary>
public static class CharacterAppearanceWireStudy
{
    private const int MaxBodyBytes = 8192;
    private const int MaxRows = 16;
    private const int MaxFields = 256;
    private const int MaxAppearanceBytes = 2048;
    private const int MaxDepth = 3;
    private const int MaxPaths = 40;

    private sealed record Row(ulong Id, byte[] Appearance);

    public static string Describe(
        string opcode, ReadOnlySpan<byte> body, CharacterNameLevelReference reference)
    {
        CharacterNameLevelReferenceInput.Validate(reference);
        if (opcode is not ("ksb" or "ksc") ||
            body.IsEmpty || body.Length > MaxBodyBytes ||
            !CharacterWireCandidateDecoder.TryDecode(
                opcode, body, out CharacterWireCandidateSnapshot? snapshot) ||
            snapshot is null)
            return "p2h=unavailable class=unknown";

        CharacterWireCandidate[] matching = snapshot.Candidates
            .Where(c => c.Level == reference.Level &&
                string.Equals(c.Name, reference.Name, StringComparison.Ordinal))
            .ToArray();
        if (matching.Length != 1)
            return "p2h=reference-not-unique class=unknown";

        List<ProtoField>? root = ReadNode(body.ToArray(), MaxBodyBytes);
        if (root is null)
            return "p2h=unavailable class=unknown";

        List<Row> rows = [];
        if (opcode == "ksb")
        {
            if (!TrySingleBytes(root, 1, out byte[]? outerBytes) ||
                outerBytes is null ||
                ReadNode(outerBytes, MaxBodyBytes) is not { } outer ||
                !TrySingleBytes(outer, 1, out byte[]? rowBytes) ||
                rowBytes is null ||
                !TryGetAppearance(rowBytes, out Row? row) ||
                row is null)
                return "p2h=unavailable class=unknown";
            rows.Add(row);
        }
        else
        {
            ProtoField[] entries = root.Where(f => f.Number == 2).ToArray();
            if (entries.Length is < 1 or > MaxRows || entries.Length != root.Count)
                return "p2h=unavailable class=unknown";

            foreach (ProtoField entry in entries)
            {
                if (entry.WireType != ProtoWireType.LengthDelimited ||
                    entry.Bytes is null ||
                    !TryGetAppearance(entry.Bytes, out Row? row) ||
                    row is null)
                    return "p2h=unavailable class=unknown";
                rows.Add(row);
            }
        }

        if (rows.Count != snapshot.Candidates.Count)
            return "p2h=unavailable class=unknown";

        Row[] selected = rows.Where(r =>
            r.Id == matching[0].CharacterIdCandidate).ToArray();
        if (selected.Length != 1)
            return "p2h=reference-not-unique class=unknown";

        if (selected[0].Appearance.Length == 0)
            return "p2h=f5-missing class=unknown";

        // A protobuf length-delimited field can also contain arbitrary
        // bytes, a string or an image. Parsing it as protobuf is speculative.
        List<ProtoField>? selectedFields = ReadNode(
            selected[0].Appearance, MaxAppearanceBytes);
        if (selectedFields is null)
            return "p2h=f5-opaque class=unknown";

        string shape = Shape(selectedFields);
        if (opcode == "ksb" || rows.Count == 1)
            return "p2h=f5-wire-shape=[" + shape +
                   "] f5-other-diff=[unavailable] class=unknown";

        HashSet<string> different = new(StringComparer.Ordinal);
        foreach (Row other in rows.Where(r => r.Id != selected[0].Id))
        {
            List<ProtoField>? otherFields = ReadNode(
                other.Appearance, MaxAppearanceBytes);
            if (otherFields is null)
                return "p2h=f5-wire-shape=[" + shape +
                       "] f5-other-diff=[opaque-peer] class=unknown";

            Diff(selectedFields, otherFields, "", 0, different);
        }

        string diff = different.Count == 0
            ? "none"
            : string.Join(",", different.OrderBy(x => x, StringComparer.Ordinal)
                .Take(MaxPaths)) + (different.Count > MaxPaths ? ",..." : "");
        return "p2h=f5-wire-shape=[" + shape +
               "] f5-other-diff=[" + diff +
               "] class=unknown";
    }

    private static bool TryGetAppearance(byte[] rowBytes, out Row? row)
    {
        row = null;
        List<ProtoField>? parent = ReadNode(rowBytes, MaxBodyBytes);
        if (parent is null ||
            !TrySingleVarint(parent, 3, out ulong id) ||
            id == 0 ||
            !TrySingleBytes(parent, 4, out byte[]? leafBytes) ||
            leafBytes is null)
            return false;

        List<ProtoField>? leaf = ReadNode(leafBytes, MaxBodyBytes);
        if (leaf is null)
            return false;

        ProtoField[] f5 = leaf.Where(f => f.Number == 5).ToArray();
        if (f5.Length == 0)
        {
            row = new Row(id, []);
            return true;
        }
        if (f5.Length != 1 ||
            f5[0].WireType != ProtoWireType.LengthDelimited ||
            f5[0].Bytes is not { Length: > 0 and <= MaxAppearanceBytes })
            return false;

        row = new Row(id, f5[0].Bytes);
        return true;
    }

    private static List<ProtoField>? ReadNode(byte[] bytes, int maxBytes)
    {
        if (bytes.Length is < 1 || bytes.Length > maxBytes)
            return null;
        try
        {
            List<ProtoField>? fields = ProtoWire.ReadFields(bytes);
            return fields is { Count: > 0 and <= MaxFields }
                ? fields : null;
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

    private static bool TrySingleBytes(
        List<ProtoField> fields, int number, out byte[]? bytes)
    {
        bytes = null;
        ProtoField[] matches = fields.Where(f => f.Number == number).ToArray();
        if (matches.Length != 1 ||
            matches[0].WireType != ProtoWireType.LengthDelimited ||
            matches[0].Bytes is not { Length: > 0 and <= MaxBodyBytes })
            return false;
        bytes = matches[0].Bytes;
        return true;
    }

    private static bool TrySingleVarint(
        List<ProtoField> fields, int number, out ulong value)
    {
        value = 0;
        ProtoField[] matches = fields.Where(f => f.Number == number).ToArray();
        if (matches.Length != 1 ||
            matches[0].WireType != ProtoWireType.Varint)
            return false;
        value = matches[0].Varint;
        return true;
    }

    private static string Shape(List<ProtoField> fields)
    {
        List<string> paths = [];
        AppendShape(fields, "", 0, paths);
        return string.Join(",", paths) +
            (paths.Count == MaxPaths ? ",..." : "");
    }

    private static void AppendShape(
        List<ProtoField> fields, string prefix, int depth,
        List<string> paths)
    {
        foreach (var group in fields
            .GroupBy(f => (f.Number, f.WireType))
            .OrderBy(g => g.Key.Number)
            .ThenBy(g => g.Key.WireType))
        {
            if (paths.Count >= MaxPaths)
                return;

            string path = prefix + "f" + group.Key.Number;
            paths.Add(path + ":" + WireType(group.Key.WireType) +
                (group.Count() > 1 ? "x" + group.Count() : ""));

            if (depth >= MaxDepth ||
                group.Key.WireType != ProtoWireType.LengthDelimited ||
                group.Count() != 1)
                continue;

            byte[]? bytes = group.First().Bytes;
            List<ProtoField>? nested = bytes is null
                ? null : ReadNode(bytes, MaxAppearanceBytes);
            if (nested is not null)
                AppendShape(nested, path + ".", depth + 1, paths);
        }
    }

    private static void Diff(
        List<ProtoField> selected, List<ProtoField> other,
        string prefix, int depth, HashSet<string> output)
    {
        foreach (var group in selected.GroupBy(f => f.Number))
        {
            if (output.Count > MaxPaths)
                return;

            ProtoField[] lhs = group.ToArray();
            ProtoField[] rhs = other.Where(f => f.Number == group.Key).ToArray();
            string path = prefix + "f" + group.Key;
            if (lhs.Length != 1 || rhs.Length != 1 ||
                lhs[0].WireType != rhs[0].WireType)
            {
                output.Add(path + ":structure");
                continue;
            }

            ProtoField a = lhs[0], b = rhs[0];
            if (a.WireType == ProtoWireType.LengthDelimited)
            {
                if (a.Bytes is null || b.Bytes is null)
                {
                    output.Add(path + ":opaque");
                    continue;
                }
                if (a.Bytes.AsSpan().SequenceEqual(b.Bytes))
                    continue;
                if (depth >= MaxDepth)
                {
                    output.Add(path + ":len");
                    continue;
                }

                List<ProtoField>? nestedA = ReadNode(a.Bytes, MaxAppearanceBytes);
                List<ProtoField>? nestedB = ReadNode(b.Bytes, MaxAppearanceBytes);
                if (nestedA is null || nestedB is null)
                    output.Add(path + ":len");
                else
                    Diff(nestedA, nestedB, path + ".", depth + 1, output);
            }
            else if (a.WireType switch
            {
                ProtoWireType.Varint => a.Varint != b.Varint,
                ProtoWireType.Fixed32 => a.Fixed32 != b.Fixed32,
                ProtoWireType.Fixed64 => a.Fixed64 != b.Fixed64,
                _ => false
            })
            {
                output.Add(path + ":" + WireType(a.WireType));
            }
        }

        // Also report fields present only in the other character.
        foreach (ProtoField b in other.Where(f =>
            selected.All(a => a.Number != f.Number)))
        {
            if (output.Count > MaxPaths)
                return;
            output.Add(prefix + "f" + b.Number + ":structure");
        }
    }

    private static string WireType(ProtoWireType type) => type switch
    {
        ProtoWireType.Varint => "v",
        ProtoWireType.Fixed32 => "32",
        ProtoWireType.Fixed64 => "64",
        ProtoWireType.LengthDelimited => "len",
        _ => "?"
    };
}
