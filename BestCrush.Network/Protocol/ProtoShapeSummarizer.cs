namespace BestCrush.Network.Protocol;

/// <summary>
/// A bounded protobuf wire-shape observation: field numbers and wire types only.
/// No varint values, UTF-8 content, byte arrays, raw lengths of nested fields,
/// hashes or character identifiers are exposed. Nested structures are heuristic.
/// </summary>
public static class ProtoShapeSummarizer
{
    private const int MaxBodyBytes = 256 * 1024;
    private const int MaxFields = 512;
    private const int MaxEntries = 48;
    private const int MaxNestedBytes = 2048;
    private const int MaxTotalNestedBytes = 16 * 1024;
    private const int MaximumDepth = 2;

    public static string Describe(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
            return "empty";
        if (body.Length > MaxBodyBytes)
            return "too-large";

        int remainingFields = MaxFields;
        int nestedBudget = MaxTotalNestedBytes;
        Dictionary<string, int> entries = new(StringComparer.Ordinal);

        if (!TryReadShape(body, "", 0, entries,
                ref remainingFields, ref nestedBudget, out bool truncated))
            return "unrecognized";

        IEnumerable<KeyValuePair<string, int>> visible = entries
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Take(MaxEntries);

        string description = string.Join(" ", visible.Select(entry =>
            entry.Value == 1
                ? entry.Key
                : $"{entry.Key}x{entry.Value}"));

        if (entries.Count > MaxEntries)
            description += " ...";

        if (truncated)
            description += " [partial]";

        return description.Length == 0 ? "unrecognized" : description;
    }

    private static bool TryReadShape(
        ReadOnlySpan<byte> data,
        string prefix,
        int depth,
        Dictionary<string, int> entries,
        ref int remainingFields,
        ref int nestedBudget,
        out bool truncated)
    {
        truncated = false;
        int cursor = 0;
        int fieldsSeen = 0;

        while (cursor < data.Length)
        {
            if (remainingFields <= 0)
            {
                truncated = true;
                return true;
            }

            if (!TryReadVarint(data, ref cursor, out ulong tag))
                return false;

            ulong fieldNumber = tag >> 3;
            int wireType = (int)(tag & 7);
            if (fieldNumber == 0 || fieldNumber > 536870911 ||
                wireType is not (0 or 1 or 2 or 5))
                return false;

            remainingFields--;
            fieldsSeen++;

            string wireLabel = wireType switch
            {
                0 => "v",
                1 => "64",
                2 => "len",
                _ => "32"
            };

            string fieldPath = prefix + fieldNumber;
            string fieldLabel = fieldPath + ":" + wireLabel;
            entries.TryGetValue(fieldLabel, out int frequency);
            entries[fieldLabel] = frequency + 1;

            if (wireType == 0)
            {
                if (!TryReadVarint(data, ref cursor, out _))
                    return false;
            }
            else if (wireType is 1 or 5)
            {
                int count = wireType == 1 ? 8 : 4;
                if (data.Length - cursor < count)
                    return false;
                cursor += count;
            }
            else
            {
                if (!TryReadVarint(data, ref cursor, out ulong rawLength) ||
                    rawLength > (ulong)(data.Length - cursor))
                    return false;

                int length = (int)rawLength;
                // A field of wire type 2 can contain text, bytes, packed
                // scalars OR a protobuf message. Do not assume it is nested.
                // "?f2.f1" explicitly marks an unverified nested candidate.
                if (depth < MaximumDepth && length is > 0 and <= MaxNestedBytes &&
                    nestedBudget >= length && remainingFields > 0)
                {
                    nestedBudget -= length;
                    int tentativeFields = remainingFields;
                    int tentativeNestedBudget = nestedBudget;
                    Dictionary<string, int> candidate = new(StringComparer.Ordinal);
                    bool possible = TryReadShape(
                        data.Slice(cursor, length),
                        "?" + fieldPath + ".",
                        depth + 1,
                        candidate,
                        ref tentativeFields,
                        ref tentativeNestedBudget,
                        out bool nestedTruncated);

                    if (possible && !nestedTruncated && candidate.Count > 0)
                    {
                        foreach (KeyValuePair<string, int> kv in candidate)
                        {
                            entries.TryGetValue(kv.Key, out int previous);
                            entries[kv.Key] = previous + kv.Value;
                        }

                        remainingFields = tentativeFields;
                        nestedBudget = tentativeNestedBudget;
                    }
                }

                cursor += length;
            }
        }

        return fieldsSeen > 0;
    }

    private static bool TryReadVarint(
        ReadOnlySpan<byte> data, ref int cursor, out ulong value)
    {
        value = 0;
        for (int i = 0; i < 10 && cursor < data.Length; i++)
        {
            byte next = data[cursor++];
            if (i == 9 && (next & 0xfe) != 0)
                return false;

            value |= (ulong)(next & 0x7f) << (i * 7);
            if ((next & 0x80) == 0)
                return true;
        }

        return false;
    }
}
