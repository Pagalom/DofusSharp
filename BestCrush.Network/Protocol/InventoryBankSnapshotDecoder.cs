namespace BestCrush.Network.Protocol;

/// <summary>
/// P3d.6 research only. irl is a candidate character inventory snapshot;
/// irp is a candidate bank snapshot. Neither is authoritative for business
/// stock until the numeric field semantics and account scope are verified.
/// </summary>
public enum StockSnapshotCandidateStatus
{
    Malformed,
    Incomplete,
    EmptyCandidate,
    CompleteCandidate
}

/// <summary>
/// Candidate protobuf scalars, deliberately not a domain inventory item.
/// f3/f4/f5 meanings are validated for kch, not yet for irl/irp.
/// Effects are counted but not needed for inventory identification.
/// </summary>
public sealed record StockItemWireCandidate(
    ulong? Field3QuantityCandidate,
    ulong? Field4ItemIdCandidate,
    ulong? Field5InstanceUidCandidate,
    int EffectFields);

public sealed record StockSnapshotWireCandidate(
    string Container,
    StockSnapshotCandidateStatus Status,
    IReadOnlyList<StockItemWireCandidate> Rows)
{
    /// <summary>Safe metadata-only report. No IDs, quantities or effect values.</summary>
    public string Describe()
    {
        int complete = Rows.Count(x =>
            x.Field3QuantityCandidate is > 0 &&
            x.Field4ItemIdCandidate is > 0 &&
            x.Field5InstanceUidCandidate is > 0);
        int effects = Rows.Count(x => x.EffectFields > 0);
        int duplicateUids = Rows
            .Where(x => x.Field5InstanceUidCandidate is > 0)
            .GroupBy(x => x.Field5InstanceUidCandidate!.Value)
            .Sum(x => x.Count() - 1);

        string status = Status switch
        {
            StockSnapshotCandidateStatus.Malformed => "malformed",
            StockSnapshotCandidateStatus.Incomplete => "incomplete",
            StockSnapshotCandidateStatus.EmptyCandidate => "empty-candidate",
            StockSnapshotCandidateStatus.CompleteCandidate => "complete-candidate",
            _ => "unknown"
        };
        return "container=" + Container +
            " status=" + status +
            " rows=" + Rows.Count +
            " fields345=" + complete +
            " incomplete-rows=" + (Rows.Count - complete) +
            " effects-present=" + effects +
            " duplicate-f5=" + duplicateUids;
    }
}

/// <summary>
/// Bounded pure decoder for two observed envelopes: irl/irp root.f2[]
/// -> row.f1 -> {f3, f4, f5, optional f7[]}. Does not modify state.
/// </summary>
public static class InventoryBankSnapshotDecoder
{
    private const int MaxBodyBytes = 256 * 1024;
    private const int MaxRows = 4096;
    private const int MaxRowBytes = 8192;

    public static StockSnapshotWireCandidate? Decode(
        string opcode, ReadOnlySpan<byte> body)
    {
        string container = opcode switch
        {
            "irl" => "inventory",
            "irp" => "bank",
            _ => ""
        };
        if (container.Length == 0)
            return null;

        static StockSnapshotWireCandidate Failed(string name) =>
            new(name, StockSnapshotCandidateStatus.Malformed, []);

        if (body.IsEmpty || body.Length > MaxBodyBytes)
            return Failed(container);

        try
        {
            List<ProtoField>? root = ProtoWire.ReadFields(body);
            if (root is null)
                return Failed(container);

            // Metadata scalars f4 (irl) and f1 (irp) were observed in
            // snapshots. A payload with neither rows nor metadata is NOT
            // evidence of an empty inventory or bank.
            int marker = opcode == "irl" ? 4 : 1;
            bool hasMarker = root.Any(f =>
                f.Number == marker && f.WireType == ProtoWireType.Varint);
            if (root.Any(f => f.Number == 2 &&
                f.WireType != ProtoWireType.LengthDelimited))
                return Failed(container);

            ProtoField[] rows = root.Where(f =>
                f.Number == 2 && f.WireType == ProtoWireType.LengthDelimited)
                .ToArray();
            if (rows.Length > MaxRows)
                return Failed(container);
            if (rows.Length == 0)
                return new StockSnapshotWireCandidate(container,
                    hasMarker ? StockSnapshotCandidateStatus.EmptyCandidate :
                        StockSnapshotCandidateStatus.Incomplete, []);

            bool incomplete = !hasMarker;
            List<StockItemWireCandidate> items = new(rows.Length);
            foreach (ProtoField row in rows)
            {
                if (row.Bytes is null || row.Bytes.Length == 0 ||
                    row.Bytes.Length > MaxRowBytes)
                {
                    incomplete = true;
                    items.Add(new(null, null, null, 0));
                    continue;
                }

                List<ProtoField>? rowFields = ProtoWire.ReadFields(row.Bytes);
                if (rowFields is null)
                    return Failed(container);
                ProtoField[] itemFields = rowFields.Where(f =>
                    f.Number == 1 && f.WireType == ProtoWireType.LengthDelimited)
                    .ToArray();
                if (itemFields.Length != 1 ||
                    itemFields[0].Bytes is null ||
                    itemFields[0].Bytes.Length > MaxRowBytes)
                {
                    incomplete = true;
                    items.Add(new(null, null, null, 0));
                    continue;
                }

                List<ProtoField>? fields =
                    ProtoWire.ReadFields(itemFields[0].Bytes);
                if (fields is null)
                    return Failed(container);

                ulong? f3 = GetUniquePositive(fields, 3);
                ulong? f4 = GetUniquePositive(fields, 4);
                ulong? f5 = GetUniquePositive(fields, 5);
                if (f3 is null || f4 is null || f5 is null)
                    incomplete = true;

                // Count only: effects can be changed via smithmagic on
                // equippable items or feeding pets. Their presence is
                // independent of the identity/stock decoding.
                int effects = fields.Count(f =>
                    f.Number == 7 && f.WireType == ProtoWireType.LengthDelimited);
                items.Add(new(f3, f4, f5, effects));
            }

            // An UID collision means that this is not yet a trustworthy
            // complete snapshot even if every row has valid scalar fields.
            ulong[] uids = items.Where(x =>
                    x.Field5InstanceUidCandidate.HasValue)
                .Select(x => x.Field5InstanceUidCandidate!.Value).ToArray();
            if (uids.Length != uids.Distinct().Count())
                incomplete = true;

            return new StockSnapshotWireCandidate(container,
                incomplete ? StockSnapshotCandidateStatus.Incomplete :
                    StockSnapshotCandidateStatus.CompleteCandidate, items);
        }
        catch (OverflowException)
        {
            return Failed(container);
        }
    }

    private static ulong? GetUniquePositive(
        IReadOnlyList<ProtoField> fields, int number)
    {
        ProtoField[] candidates = fields
            .Where(f => f.Number == number).ToArray();
        return candidates.Length == 1 &&
               candidates[0].WireType == ProtoWireType.Varint &&
               candidates[0].Varint > 0
            ? candidates[0].Varint : null;
    }

    public static string? Describe(string opcode, ReadOnlySpan<byte> body) =>
        Decode(opcode, body)?.Describe();
}
