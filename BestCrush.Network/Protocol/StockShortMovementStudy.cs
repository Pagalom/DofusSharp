namespace BestCrush.Network.Protocol;

/// <summary>
/// Read-only P3d.6.5 study of short inventory/bank-adjacent opcodes.
/// Exact meanings of irx/iqm/iqq are unconfirmed: reports only structure
/// and private, ephemeral equalities against the last decoded iqx/iqz.
/// No item identity or quantity is exposed.
/// </summary>
public static class StockShortMovementStudy
{
    private const int MaximumBytes = 256;

    public static string? Describe(string opcode, ReadOnlySpan<byte> body,
        StockItemWireCandidate? previousDelta)
    {
        if (opcode is not ("irx" or "iqm" or "iqq"))
            return null;
        if (body.IsEmpty || body.Length > MaximumBytes)
            return "short=unrecognized";

        try
        {
            List<ProtoField>? root = ProtoWire.ReadFields(body);
            if (root is null)
                return "short=unrecognized";

            if (opcode == "irx")
            {
                ProtoField[] envelopes = root.Where(x => x.Number == 3).ToArray();
                if (envelopes.Length != 1 ||
                    envelopes[0].WireType != ProtoWireType.LengthDelimited ||
                    envelopes[0].Bytes is not { Length: > 0 and <= MaximumBytes })
                    return "short=irx shape=unrecognized";

                List<ProtoField>? inner =
                    ProtoWire.ReadFields(envelopes[0].Bytes);
                if (inner is null ||
                    !TryUniqueVarint(inner, 1, out ulong a) ||
                    !TryUniqueVarint(inner, 3, out ulong b))
                    return "short=irx shape=unrecognized";

                return "short=irx shape=f3.f1+f3.f3" +
                    " f3.f1-match=[" + Compare(a, previousDelta) + "]" +
                    " f3.f3-match=[" + Compare(b, previousDelta) + "]";
            }

            if (opcode == "iqm")
            {
                if (!TryUniqueVarint(root, 1, out ulong candidate))
                    return "short=iqm shape=unrecognized";
                return "short=iqm shape=f1" +
                    " f1-match=[" + Compare(candidate, previousDelta) + "]";
            }

            bool first = TryUniqueVarint(root, 1, out ulong v1);
            bool second = TryUniqueVarint(root, 2, out ulong v2);
            if (!first && !second)
                return "short=iqq shape=unrecognized";
            return "short=iqq shape=scalar-candidates" +
                " f1=[" + (first ? Compare(v1, previousDelta) : "missing") + "]" +
                " f2=[" + (second ? Compare(v2, previousDelta) : "missing") + "]";
        }
        catch (OverflowException)
        {
            return "short=unrecognized";
        }
    }

    private static bool TryUniqueVarint(
        IReadOnlyList<ProtoField> fields, int number, out ulong value)
    {
        value = 0;
        ProtoField[] matches = fields.Where(x => x.Number == number).ToArray();
        if (matches.Length != 1 ||
            matches[0].WireType != ProtoWireType.Varint)
            return false;
        value = matches[0].Varint;
        return true;
    }

    private static string Compare(ulong scalar, StockItemWireCandidate? reference)
    {
        if (reference is null)
            return "no-delta-baseline";

        return "f3=" + Match(scalar, reference.Field3QuantityCandidate) +
            ",f4=" + Match(scalar, reference.Field4ItemIdCandidate) +
            ",f5=" + Match(scalar, reference.Field5InstanceUidCandidate);
    }

    private static string Match(ulong input, ulong? other) =>
        other is ulong expected && input == expected ? "same" : "different";
}
