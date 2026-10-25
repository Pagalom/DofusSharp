namespace BestCrush.Network.Protocol;

/// <summary>
/// P3d.6.2: read-only, per-TCP-session comparison of candidate item
/// identifiers in irl/irp snapshots and iqx/iqz deltas. Does not prove
/// ownership, transfer direction or protobuf scalar semantics.
/// </summary>
public sealed class StockSnapshotDeltaStudy
{
    private const int MaxSessions = 16;
    private const int MaxDeltaBytes = 4096;
    private readonly Dictionary<int, SessionStocks> _sessions = [];

    private sealed class SessionStocks
    {
        internal Dictionary<ulong, StockItemWireCandidate>? Inventory;
        internal Dictionary<ulong, StockItemWireCandidate>? Bank;
    }

    public void DropSession(int sessionNumber) => _sessions.Remove(sessionNumber);

    public string? Observe(int sessionNumber, string opcode, ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 || opcode is not ("irl" or "irp" or "iqx" or "iqz"))
            return null;

        if (!_sessions.TryGetValue(sessionNumber, out SessionStocks? state))
        {
            if (_sessions.Count >= MaxSessions)
                _sessions.Clear();
            state = new SessionStocks();
            _sessions.Add(sessionNumber, state);
        }

        if (opcode is "irl" or "irp")
        {
            StockSnapshotWireCandidate? snapshot =
                InventoryBankSnapshotDecoder.Decode(opcode, body);
            if (snapshot is null)
                return null;

            Dictionary<ulong, StockItemWireCandidate>? rows =
                snapshot.Status == StockSnapshotCandidateStatus.CompleteCandidate
                    ? snapshot.Rows.ToDictionary(
                        x => x.Field5InstanceUidCandidate!.Value)
                    : null;

            if (opcode == "irl")
                state.Inventory = rows;
            else
                state.Bank = rows;

            return snapshot.Describe() + " baseline=" +
                (rows is null ? "not-stored" : "stored");
        }

        StockItemWireCandidate? delta = DecodeDelta(opcode, body);
        if (delta is null)
            return "delta=unrecognized";

        return "delta=fields345-candidate" +
            " bank=[" + Compare(state.Bank, delta) + "]" +
            " inventory=[" + Compare(state.Inventory, delta) + "]";
    }

    private static string Compare(
        IReadOnlyDictionary<ulong, StockItemWireCandidate>? reference,
        StockItemWireCandidate delta)
    {
        if (reference is null)
            return "baseline-unobserved";

        if (!reference.TryGetValue(delta.Field5InstanceUidCandidate!.Value,
            out StockItemWireCandidate? existing))
            return "uid-candidate-not-found";

        return "uid-candidate-found itemId-candidate=" +
            (existing.Field4ItemIdCandidate == delta.Field4ItemIdCandidate
                ? "same" : "different") +
            " quantity-candidate=" +
            (existing.Field3QuantityCandidate == delta.Field3QuantityCandidate
                ? "same" : "different");
    }

    private static StockItemWireCandidate? DecodeDelta(
        string opcode, ReadOnlySpan<byte> body)
    {
        int rootNumber = opcode == "iqx" ? 2 : 1;
        if (body.IsEmpty || body.Length > MaxDeltaBytes)
            return null;

        try
        {
            List<ProtoField>? root = ProtoWire.ReadFields(body);
            ProtoField[]? containers = root?.Where(f =>
                f.Number == rootNumber &&
                f.WireType == ProtoWireType.LengthDelimited &&
                f.Bytes is { Length: > 0 and <= MaxDeltaBytes }).ToArray();
            if (containers?.Length != 1)
                return null;

            List<ProtoField>? inner = ProtoWire.ReadFields(containers[0].Bytes!);
            ProtoField[]? objects = inner?.Where(f =>
                f.Number == 1 &&
                f.WireType == ProtoWireType.LengthDelimited &&
                f.Bytes is { Length: > 0 and <= MaxDeltaBytes }).ToArray();
            if (objects?.Length != 1)
                return null;

            List<ProtoField>? fields = ProtoWire.ReadFields(objects[0].Bytes!);
            if (fields is null)
                return null;

            static ulong? UniquePositive(List<ProtoField> fields, int number)
            {
                ProtoField[] matches = fields.Where(x => x.Number == number)
                    .ToArray();
                return matches.Length == 1 &&
                       matches[0].WireType == ProtoWireType.Varint &&
                       matches[0].Varint > 0
                    ? matches[0].Varint : null;
            }

            ulong? quantity = UniquePositive(fields, 3);
            ulong? itemId = UniquePositive(fields, 4);
            ulong? uid = UniquePositive(fields, 5);
            if (quantity is null || itemId is null || uid is null)
                return null;

            return new StockItemWireCandidate(quantity, itemId, uid,
                fields.Count(x => x.Number == 7 &&
                    x.WireType == ProtoWireType.LengthDelimited));
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
