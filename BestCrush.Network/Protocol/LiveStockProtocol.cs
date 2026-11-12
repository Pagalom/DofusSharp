namespace BestCrush.Network.Protocol;

public enum LiveStockOperation { Upsert, Quantity, Remove }
public sealed record LiveStockItem(ulong ItemId, ulong Uid, ulong Quantity,
    IReadOnlyList<StockEffectWireCandidate>? Effects, byte[][]? EffectWire, string? EffectIssue);
public sealed record LiveStockMutation(string Container, LiveStockOperation Operation,
    IReadOnlyList<LiveStockItem> Items, ulong Uid = 0, ulong Quantity = 0);

/// <summary>Current 3.7.12.12 stock wire shapes, checked against reversible bank transfers.</summary>
public static class LiveStockProtocol
{
    public static bool IsStockOpcode(string opcode) => opcode is "irl" or "irp" or "iqx" or "iqz" or "irx" or "iqm" or "itf";

    public static LiveStockItem FromCandidate(StockItemWireCandidate row) => new(
        row.Field4ItemIdCandidate!.Value, row.Field5InstanceUidCandidate!.Value,
        row.Field3QuantityCandidate!.Value, row.Effects?.ToArray(), row.CopyExactEffectWireEntries(), row.EffectDecodeIssue);

    public static LiveStockMutation? Decode(string opcode, byte[] body)
    {
        if (body.Length is 0 or > 256 * 1024) return null;
        var root = ProtoWire.ReadFields(body);
        if (root is null) return null;
        if (opcode is "iqx" or "iqz")
        {
            int number = opcode == "iqx" ? 2 : 1;
            var envelopes = root.Where(x => x.Number == number).ToArray();
            if (envelopes.Length != 1 || envelopes[0].WireType != ProtoWireType.LengthDelimited || envelopes[0].Bytes is null)
                return null;
            var parent = ProtoWire.ReadFields(envelopes[0].Bytes!);
            if (parent is null) return null;
            var rows = parent.Where(x => x.Number == 1).ToArray();
            if (rows.Length is 0 or > 4096 || rows.Any(x => x.WireType != ProtoWireType.LengthDelimited || x.Bytes is null))
                return null;
            List<LiveStockItem> items = [];
            foreach (var row in rows)
            {
                // Reuse all scalar/effect limits and validation of the full snapshot decoder.
                byte[] snapshot = [8, 1, .. LengthField(2, LengthField(1, row.Bytes!))];
                var decoded = InventoryBankSnapshotDecoder.Decode("irp", snapshot);
                if (decoded?.Status != StockSnapshotCandidateStatus.CompleteCandidate || decoded.Rows.Count != 1)
                    return null;
                items.Add(FromCandidate(decoded.Rows[0]));
            }
            if (items.Select(x => x.Uid).Distinct().Count() != items.Count) return null;
            return new(opcode == "iqx" ? "inventory" : "bank", LiveStockOperation.Upsert, items);
        }
        if (opcode == "irx")
        {
            var wrappers = root.Where(x => x.Number == 3).ToArray();
            if (wrappers.Length != 1 || wrappers[0].WireType != ProtoWireType.LengthDelimited || wrappers[0].Bytes is null)
                return null;
            var state = ProtoWire.ReadFields(wrappers[0].Bytes!);
            if (state is null || !TryScalar(state, 3, out ulong uid) || uid == 0) return null;
            // Protobuf omits default zero. Missing quantity means deletion only in this recognized wrapper.
            ulong quantity = 0;
            if (state.Any(x => x.Number == 1) && !TryScalar(state, 1, out quantity)) return null;
            return new("inventory", LiveStockOperation.Quantity, [], uid, quantity);
        }
        if (opcode is "iqm" or "itf")
        {
            if (!TryScalar(root, 1, out ulong uid) || uid == 0) return null;
            return new(opcode == "iqm" ? "inventory" : "bank", LiveStockOperation.Remove, [], uid);
        }
        return null;
    }

    private static bool TryScalar(List<ProtoField> fields, int field, out ulong value)
    {
        value = 0;
        var values = fields.Where(x => x.Number == field).ToArray();
        if (values.Length != 1 || values[0].WireType != ProtoWireType.Varint) return false;
        value = values[0].Varint;
        return true;
    }

    private static byte[] LengthField(int field, byte[] body)
    {
        List<byte> result = [(byte)(field * 8 + 2)];
        uint length = (uint)body.Length;
        while (length >= 128) { result.Add((byte)((length & 127) | 128)); length >>= 7; }
        result.Add((byte)length);
        result.AddRange(body);
        return result.ToArray();
    }
}

public sealed record LiveStockContainer(string Container, bool Complete, bool Stale,
    DateTime UpdatedUtc, IReadOnlyList<LiveStockItem> Items, string? Issue = null);

/// <summary>UID keys are local to this single connection/container; quantities are ABSOLUTE, never additive.</summary>
public sealed class LiveStockBook
{
    private readonly Dictionary<string, LiveStockContainer> _containers = [];
    public IReadOnlyList<LiveStockContainer> Containers => _containers.Values.ToArray();

    public LiveStockContainer? Observe(string opcode, byte[] body, DateTime observedUtc)
    {
        if (!LiveStockProtocol.IsStockOpcode(opcode)) return null;
        string container = opcode is "irp" or "iqz" or "itf" ? "bank" : "inventory";
        _containers.TryGetValue(container, out var previous);
        if (previous is not null && observedUtc < previous.UpdatedUtc) return null;
        if (opcode is "irl" or "irp")
        {
            var snapshot = InventoryBankSnapshotDecoder.Decode(opcode, body);
            bool valid = snapshot?.Status is StockSnapshotCandidateStatus.CompleteCandidate or StockSnapshotCandidateStatus.EmptyCandidate;
            var state = new LiveStockContainer(container, valid, !valid, observedUtc,
                valid ? snapshot!.Rows.Select(LiveStockProtocol.FromCandidate).ToArray() : previous?.Items ?? [],
                valid ? null : "snapshot-invalid");
            _containers[container] = state;
            return state;
        }
        var mutation = LiveStockProtocol.Decode(opcode, body);
        if (mutation is null)
        {
            var invalid = new LiveStockContainer(container, previous?.Complete ?? false, true, observedUtc,
                previous?.Items ?? [], "update-unrecognized");
            _containers[container] = invalid;
            return invalid;
        }
        var rows = (previous?.Items ?? []).ToDictionary(x => x.Uid);
        bool stale = previous?.Stale ?? false;
        string? issue = previous?.Issue;
        if (mutation.Operation == LiveStockOperation.Upsert)
        {
            foreach (var item in mutation.Items)
            {
                if (rows.TryGetValue(item.Uid, out var old) && old.ItemId != item.ItemId)
                { stale = true; issue = "uid-type-conflict"; continue; }
                rows[item.Uid] = item;
            }
        }
        else if (mutation.Operation == LiveStockOperation.Remove || mutation.Quantity == 0)
            rows.Remove(mutation.Uid);
        else if (rows.TryGetValue(mutation.Uid, out var existing))
            rows[mutation.Uid] = existing with { Quantity = mutation.Quantity };
        else { stale = true; issue = "quantity-without-item"; }
        var current = new LiveStockContainer(container, previous?.Complete ?? false, stale, observedUtc, rows.Values.ToArray(), issue);
        _containers[container] = current;
        return current;
    }
}
