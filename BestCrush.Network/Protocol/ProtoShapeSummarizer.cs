namespace BestCrush.Network.Protocol;

/// <summary>
/// Privacy-preserving protobuf wire-shape exploration. Only field numbers,
/// wire types and repetition counts are reported. Never output scalar values,
/// strings, bytes, hashes, wire values or network endpoint identifiers.
/// </summary>
public static class ProtoShapeSummarizer
{
    private const int MaxBodyBytes = 256 * 1024;
    private const int MaxRootFields = 32768;
    private const int MaxNestedFields = 512;
    private const int MaxEntries = 48;
    private const int MaxNestedBytes = 4096;
    private const int MaxTotalNestedBytes = 24 * 1024;
    private const int MaxSamplesPerPath = 3;
    private const int MaximumDepth = 2;

    public static string Describe(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
            return "empty";
        if (body.Length > MaxBodyBytes)
            return "too-large";

        Dictionary<string, int> root = new(StringComparer.Ordinal);
        Dictionary<string, int> nestedSamples = new(StringComparer.Ordinal);
        Dictionary<string, int> sampledPaths = new(StringComparer.Ordinal);

        int rootBudget = MaxRootFields;
        int nestedFieldsBudget = MaxNestedFields;
        int nestedByteBudget = MaxTotalNestedBytes;

        // The root is always scanned independently from the nested budget.
        // A message with hundreds of repeated records can be counted
        // completely, even when only the first few children are inspected.
        if (!Scan(body, "", 0,
                root, nestedSamples, sampledPaths,
                ref rootBudget, ref nestedFieldsBudget, ref nestedByteBudget,
                out bool rootPartial))
            return "unrecognized";

        string output = "root=[" + Format(root) + "]";
        if (nestedSamples.Count != 0)
            output += " sampled=[" + Format(nestedSamples) + "]";

        if (rootPartial)
            output += " [partial-root]";

        return output;
    }

    private static string Format(Dictionary<string, int> entries)
    {
        IEnumerable<KeyValuePair<string, int>> selected = entries
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Take(MaxEntries);

        string result = string.Join(" ", selected.Select(x =>
            x.Value == 1 ? x.Key : $"{x.Key}x{x.Value}"));

        return entries.Count > MaxEntries ? result + " ..." : result;
    }

    private static bool Scan(
        ReadOnlySpan<byte> data,
        string prefix,
        int depth,
        Dictionary<string, int> rootEntries,
        Dictionary<string, int> nestedEntries,
        Dictionary<string, int> sampledPaths,
        ref int rootBudget,
        ref int nestedFieldsBudget,
        ref int nestedByteBudget,
        out bool partial)
    {
        partial = false;
        int cursor = 0;
        int seen = 0;

        while (cursor < data.Length)
        {
            if (depth == 0 ? rootBudget == 0 : nestedFieldsBudget == 0)
            {
                partial = true;
                return true;
            }

            if (!TryReadVarint(data, ref cursor, out ulong tag))
                return false;

            ulong fieldNumber = tag >> 3;
            int wireType = (int)(tag & 7);
            if (fieldNumber == 0 || fieldNumber > 536870911 ||
                wireType is not (0 or 1 or 2 or 5))
                return false;

            if (depth == 0)
                rootBudget--;
            else
                nestedFieldsBudget--;
            seen++;

            string label = wireType switch
            {
                0 => "v",
                1 => "64",
                2 => "len",
                _ => "32"
            };

            string fieldPath = prefix + fieldNumber;
            string fieldLabel = fieldPath + ":" + label;
            Dictionary<string, int> output = depth == 0
                ? rootEntries
                : nestedEntries;
            output.TryGetValue(fieldLabel, out int count);
            output[fieldLabel] = count + 1;

            if (wireType == 0)
            {
                if (!TryReadVarint(data, ref cursor, out _))
                    return false;
            }
            else if (wireType is 1 or 5)
            {
                int size = wireType == 1 ? 8 : 4;
                if (data.Length - cursor < size)
                    return false;
                cursor += size;
            }
            else
            {
                if (!TryReadVarint(data, ref cursor, out ulong rawLength) ||
                    rawLength > (ulong)(data.Length - cursor))
                    return false;

                int length = (int)rawLength;
                // Length-delimited fields might instead be text, opaque
                // bytes or packed integers. A "?" prefix explicitly marks
                // an unverified nested-message hypothesis.
                if (depth < MaximumDepth &&
                    length is > 0 and <= MaxNestedBytes &&
                    nestedByteBudget >= length &&
                    nestedFieldsBudget > 0)
                {
                    sampledPaths.TryGetValue(fieldPath, out int attempts);
                    if (attempts < MaxSamplesPerPath)
                    {
                        sampledPaths[fieldPath] = attempts + 1;
                        nestedByteBudget -= length;

                        Dictionary<string, int> candidate = new(StringComparer.Ordinal);
                        int trialRoot = rootBudget;
                        int trialNestedFields = nestedFieldsBudget;
                        int trialNestedBytes = nestedByteBudget;

                        // Roll back speculative subfields from invalid
                        // candidates. Never expose partial child structures.
                        bool valid = Scan(
                            data.Slice(cursor, length),
                            "?" + fieldPath + ".",
                            depth + 1,
                            rootEntries,
                            candidate,
                            sampledPaths,
                            ref trialRoot,
                            ref trialNestedFields,
                            ref trialNestedBytes,
                            out bool childPartial);

                        nestedFieldsBudget = trialNestedFields;
                        nestedByteBudget = trialNestedBytes;

                        if (valid && !childPartial && candidate.Count > 0)
                        {
                            foreach (KeyValuePair<string, int> sub in candidate)
                            {
                                nestedEntries.TryGetValue(sub.Key, out int prior);
                                nestedEntries[sub.Key] = prior + sub.Value;
                            }
                        }
                    }
                }

                cursor += length;
            }
        }

        return seen > 0;
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
