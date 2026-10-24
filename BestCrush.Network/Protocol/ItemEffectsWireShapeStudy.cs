namespace BestCrush.Network.Protocol;

/// <summary>
/// Research-only and value-free shape summary for the *candidate* instance
/// effects found at item.f7 in current iqx/iqz/kch messages.
/// Never reports effects' scalar values, item IDs, UIDs or raw payload bytes.
/// These wire layouts are hypotheses until confirmed by independent captures.
/// </summary>
public static class ItemEffectsWireShapeStudy
{
    private const int MaximumMessageBytes = 4096;
    private const int MaximumEffectBytes = 1024;
    private const int MaximumEffects = 64;
    private const int MaximumShapeVariants = 4;

    public static string? Describe(
        string opcode, ReadOnlySpan<byte> body,
        IReadOnlyList<long>? expectedEffectValues = null)
    {
        // Only three explicit candidate envelopes. This is not a generic
        // deserializer for arbitrary network traffic or a stock decoder.
        int rootField = opcode switch
        {
            "iqx" => 2,
            "iqz" => 1,
            "kch" => 3,
            _ => 0
        };
        if (rootField == 0 || body.IsEmpty || body.Length > MaximumMessageBytes)
            return null;

        try
        {
            List<ProtoField>? root = ProtoWire.ReadFields(body);
            ProtoField? envelope = root?.FirstOrDefault(f =>
                f.Number == rootField &&
                f.WireType == ProtoWireType.LengthDelimited &&
                f.Bytes is { Length: > 0 and <= MaximumMessageBytes });
            if (envelope?.Bytes is null)
                return null;

            List<ProtoField>? envelopeFields = ProtoWire.ReadFields(envelope.Bytes);
            ProtoField? itemField = envelopeFields?.FirstOrDefault(f =>
                f.Number == 1 &&
                f.WireType == ProtoWireType.LengthDelimited &&
                f.Bytes is { Length: > 0 and <= MaximumMessageBytes });
            if (itemField?.Bytes is null)
                return null;

            List<ProtoField>? item = ProtoWire.ReadFields(itemField.Bytes);
            if (item is null ||
                !HasPositiveVarint(item, 3) ||
                !HasPositiveVarint(item, 4) ||
                !HasPositiveVarint(item, 5))
                return null;

            ProtoField[] effects = item.Where(f =>
                f.Number == 7 &&
                f.WireType == ProtoWireType.LengthDelimited &&
                f.Bytes is not null).ToArray();
            if (effects.Length > MaximumEffects)
                return "effects=over-limit";

            Dictionary<string, int> shapes = new(StringComparer.Ordinal);
            foreach (ProtoField effect in effects)
            {
                if (effect.Bytes!.Length > MaximumEffectBytes)
                    return "effects=too-large";
                string shape = ProtoShapeSummarizer.Describe(effect.Bytes);
                shapes.TryGetValue(shape, out int count);
                shapes[shape] = count + 1;
            }

            string variants = string.Join("; ", shapes
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Take(MaximumShapeVariants)
                .Select(x => x.Key + "x" + x.Value));
            if (shapes.Count > MaximumShapeVariants)
                variants += "; ...";
            string result = "effects=" + effects.Length +
                " wire-shapes=[" + (variants.Length == 0 ? "none" : variants) + "]";
            if (expectedEffectValues is not null)
            {
                // Local references are compared without ever writing values,
                // including to diagnostics. Check every effect, not only a sample.
                if (expectedEffectValues.Count is < 1 or > 16)
                    return null;
                result += " match=[f5:" +
                    MatchField(effects, 5, expectedEffectValues) +
                    " f10:" + MatchField(effects, 10, expectedEffectValues) + "]";
            }
            return result;
        }
        catch (OverflowException)
        {
            // A malformed protobuf tag must never produce an exception that
            // might accidentally expose raw packet or user details in a log.
            return null;
        }
    }

    /// <summary>
    /// Diagnostic-only: extracts candidate (effect value, effect code) pairs
    /// without exposing object identity. Caller must keep the result private.
    /// Uses the same strictly bounded opcode/envelope assumptions as Describe.
    /// </summary>
    public static IReadOnlyList<(long Value, ulong Code)>? ReadCandidatePairs(
        string opcode, ReadOnlySpan<byte> body)
    {
        int rootField = opcode switch
        {
            "iqx" => 2,
            "iqz" => 1,
            "kch" => 3,
            _ => 0
        };
        if (rootField == 0 || body.IsEmpty || body.Length > MaximumMessageBytes)
            return null;

        List<ProtoField>? root = ProtoWire.ReadFields(body);
        ProtoField? envelope = root?.FirstOrDefault(f =>
            f.Number == rootField &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is { Length: > 0 and <= MaximumMessageBytes });
        if (envelope?.Bytes is null)
            return null;

        List<ProtoField>? wrapper = ProtoWire.ReadFields(envelope.Bytes);
        ProtoField? itemField = wrapper?.FirstOrDefault(f =>
            f.Number == 1 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is { Length: > 0 and <= MaximumMessageBytes });
        if (itemField?.Bytes is null)
            return null;

        List<ProtoField>? item = ProtoWire.ReadFields(itemField.Bytes);
        if (item is null ||
            !HasPositiveVarint(item, 3) ||
            !HasPositiveVarint(item, 4) ||
            !HasPositiveVarint(item, 5))
            return null;

        List<(long Value, ulong Code)> pairs = [];
        foreach (ProtoField effect in item.Where(f =>
                     f.Number == 7 && f.WireType == ProtoWireType.LengthDelimited))
        {
            if (effect.Bytes is null || effect.Bytes.Length > MaximumEffectBytes ||
                pairs.Count >= MaximumEffects)
                return null;

            List<ProtoField>? fields = ProtoWire.ReadFields(effect.Bytes);
            if (fields is null)
                return null;

            ProtoField[] values = fields.Where(f =>
                f.Number == 5 && f.WireType == ProtoWireType.Varint).ToArray();
            ProtoField[] codes = fields.Where(f =>
                f.Number == 10 && f.WireType == ProtoWireType.Varint).ToArray();
            if (values.Length != 1 || codes.Length != 1)
                return null;

            pairs.Add((unchecked((long)values[0].Varint), codes[0].Varint));
        }
        return pairs;
    }

    private static string MatchField(
        IReadOnlyList<ProtoField> effects,
        int fieldNumber,
        IReadOnlyList<long> references)
    {
        if (effects.Count != references.Count)
            return "incomplete";

        List<long> values = new(effects.Count);
        foreach (ProtoField effect in effects)
        {
            List<ProtoField>? fields = ProtoWire.ReadFields(effect.Bytes!);
            if (fields is null)
                return "unrecognized";
            ProtoField[] candidates = fields.Where(f =>
                f.Number == fieldNumber &&
                f.WireType == ProtoWireType.Varint).ToArray();
            if (candidates.Length != 1)
                return "unrecognized";
            values.Add(unchecked((long)candidates[0].Varint));
        }
        // Multiset comparison: field order may vary between bank/inventory.
        return values.OrderBy(x => x).SequenceEqual(references.OrderBy(x => x))
            ? "yes" : "no";
    }

    private static bool HasPositiveVarint(IReadOnlyList<ProtoField> fields, int number) =>
        fields.Any(f => f.Number == number &&
            f.WireType == ProtoWireType.Varint && f.Varint > 0);
}
