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
        internal StockItemWireCandidate? LastDelta;
        internal string? LastDeltaOpcode;
    }

    public void DropSession(int sessionNumber) => _sessions.Remove(sessionNumber);

    public string? Observe(int sessionNumber, string opcode, ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 || opcode is not
            ("irl" or "irp" or "iqx" or "iqz" or "irx" or "iqm" or "iqq"))
            return null;

        if (!_sessions.TryGetValue(sessionNumber, out SessionStocks? state))
        {
            if (_sessions.Count >= MaxSessions)
                _sessions.Clear();
            state = new SessionStocks();
            _sessions.Add(sessionNumber, state);
        }

        if (opcode is "irx" or "iqm" or "iqq")
        {
            // Short adjustment messages are not yet understood as
            // stock changes. Compare values only, never mutate baselines.
            return StockShortMovementStudy.Describe(
                opcode, body, state.LastDelta);
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

            // Never compare an unrelated delta against an older transaction
            // after another authoritative snapshot has arrived.
            state.LastDelta = null;
            state.LastDeltaOpcode = null;
            return snapshot.Describe() + " baseline=" +
                (rows is null ? "not-stored" : "stored");
        }

        StockItemWireCandidate? delta = DecodeDelta(opcode, body);
        if (delta is null)
        {
            // A malformed/unrecognized intervening delta breaks adjacency.
            // Do not compare a later delta to a stale earlier one.
            state.LastDelta = null;
            state.LastDeltaOpcode = null;
            return "delta=unrecognized";
        }

        string previous = ComparePreviousDelta(
            state.LastDeltaOpcode, state.LastDelta, delta);
        state.LastDelta = delta;
        state.LastDeltaOpcode = opcode;

        return "delta=fields345-candidate" +
            " bank=[" + Compare(state.Bank, delta) + "]" +
            " inventory=[" + Compare(state.Inventory, delta) + "]" +
            " previous-delta=[" + previous + "]";
    }

    private static string Compare(
        IReadOnlyDictionary<ulong, StockItemWireCandidate>? reference,
        StockItemWireCandidate delta)
    {
        if (reference is null)
            return "baseline-unobserved";

        if (!reference.TryGetValue(delta.Field5InstanceUidCandidate!.Value,
            out StockItemWireCandidate? existing))
        {
            int sameType = reference.Values.Count(row =>
                row.Field4ItemIdCandidate == delta.Field4ItemIdCandidate);
            int sameTypeQuantity = reference.Values.Count(row =>
                row.Field4ItemIdCandidate == delta.Field4ItemIdCandidate &&
                row.Field3QuantityCandidate == delta.Field3QuantityCandidate);
            int sameTypeEffectsCount = reference.Values.Count(row =>
                row.Field4ItemIdCandidate == delta.Field4ItemIdCandidate &&
                row.EffectFields == delta.EffectFields);
            // Candidate counts are only descriptive. Two copies of the
            // same item can have different rolls, quantities or instances.
            int exactEffectsRows = reference.Values.Count(row =>
                row.Field4ItemIdCandidate == delta.Field4ItemIdCandidate &&
                HasComparableEffects(row, delta) &&
                EffectsEqual(row, delta));
            int exactEffectsAndQuantityRows = reference.Values.Count(row =>
                row.Field4ItemIdCandidate == delta.Field4ItemIdCandidate &&
                row.Field3QuantityCandidate == delta.Field3QuantityCandidate &&
                HasComparableEffects(row, delta) &&
                EffectsEqual(row, delta));
            return "uid-candidate-not-found" +
                " same-type-rows=" + sameType +
                " same-type-quantity-rows=" + sameTypeQuantity +
                " same-type-effects-count-rows=" + sameTypeEffectsCount +
                " exact-effects-status=" + EffectsStatus(delta) +
                " same-type-exact-effects-rows=" + exactEffectsRows +
                " same-type-quantity-exact-effects-rows=" + exactEffectsAndQuantityRows;
        }

        return "uid-candidate-found itemId-candidate=" +
            (existing.Field4ItemIdCandidate == delta.Field4ItemIdCandidate
                ? "same" : "different") +
            " quantity-candidate=" +
            (existing.Field3QuantityCandidate == delta.Field3QuantityCandidate
                ? "same" : "different") +
            " exact-effects=" + CompareEffects(existing, delta);
    }

    private static string EffectsStatus(StockItemWireCandidate item) =>
        item.Effects is null ? "unrecognized" :
            item.Effects.Count == 0 ? "no-effects" : "decoded";

    private static bool HasComparableEffects(
        StockItemWireCandidate first, StockItemWireCandidate second) =>
        first.Effects is { Count: > 0 } &&
        second.Effects is { Count: > 0 };

    private static bool EffectsEqual(
        StockItemWireCandidate first, StockItemWireCandidate second) =>
        first.Effects!.OrderBy(x => x.EffectCode)
            .ThenBy(x => x.Value)
            .SequenceEqual(second.Effects!.OrderBy(x => x.EffectCode)
                .ThenBy(x => x.Value));

    private static string CompareEffects(
        StockItemWireCandidate first, StockItemWireCandidate second)
    {
        if (first.Effects is null || second.Effects is null)
            return "unrecognized";
        if (first.Effects.Count == 0 || second.Effects.Count == 0)
            return "not-applicable";
        return EffectsEqual(first, second) ? "same" : "different";
    }

    private static string ComparePreviousDelta(
        string? opcode, StockItemWireCandidate? previous,
        StockItemWireCandidate current)
    {
        if (previous is null || opcode is not ("iqx" or "iqz"))
            return "none";

        // This is merely the preceding delta in the same TCP session.
        // It does not prove that both frames describe the same operation.
        return "opcode=" + opcode +
            " f3=" + (previous.Field3QuantityCandidate ==
                current.Field3QuantityCandidate ? "same" : "different") +
            " f4=" + (previous.Field4ItemIdCandidate ==
                current.Field4ItemIdCandidate ? "same" : "different") +
            " f5=" + (previous.Field5InstanceUidCandidate ==
                current.Field5InstanceUidCandidate ? "same" : "different") +
            " effects-count=" + (previous.EffectFields ==
                current.EffectFields ? "same" : "different") +
            " exact-effects=" + CompareEffects(previous, current);
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
                    x.WireType == ProtoWireType.LengthDelimited),
                StockEffectWireCandidateParser.Read(fields));
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
