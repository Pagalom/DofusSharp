using BestCrush.Network.Protocol;

namespace BestCrush.Services;

internal sealed record ForgemagingWireEffect(ulong Code, long? Value);
internal sealed record ForgemagingWireItem(ulong Uid, long ItemId, ulong Quantity, IReadOnlyList<ForgemagingWireEffect> Effects);
internal sealed record ForgemagingWireResult(int Code, ForgemagingWireItem? Item, double? Sink, int? PoolStatus);

/// <summary>Bounded workshop-only decoding. No heuristic scan through arbitrary messages.</summary>
internal static class LiveForgemagingProtocol
{
    internal static List<ProtoField>? Fields(byte[] body) => body.Length <= 16384 ? ProtoWire.ReadFields(body) : null;
    internal static ulong? Scalar(List<ProtoField>? fields, int number)
    {
        var rows = fields?.Where(f => f.Number == number).ToArray();
        return rows is { Length: 1 } && rows[0].WireType == ProtoWireType.Varint ? rows[0].Varint : null;
    }
    internal static List<ProtoField>? Child(List<ProtoField>? fields, int number)
    {
        var rows = fields?.Where(f => f.Number == number).ToArray();
        return rows is { Length: 1 } && rows[0].WireType == ProtoWireType.LengthDelimited && rows[0].Bytes is { } bytes ? Fields(bytes) : null;
    }
    private static double? Float(List<ProtoField>? fields, int number)
    {
        var rows = fields?.Where(f => f.Number == number).ToArray();
        if (rows is not { Length: 1 } || rows[0].WireType != ProtoWireType.Fixed32) return null;
        float value = BitConverter.UInt32BitsToSingle(rows[0].Fixed32);
        return float.IsFinite(value) && value is >= 0 and <= 100000 ? value : null;
    }
    internal static ForgemagingWireItem? Item(List<ProtoField>? fields)
    {
        if (fields is null || Scalar(fields, 5) is not > 0 || Scalar(fields, 4) is not (> 0 and <= int.MaxValue)) return null;
        ulong quantity = Scalar(fields, 3) ?? 0;
        if (quantity is 0 or > 1000000000) return null;
        var effects = fields.Where(f => f.Number == 7).ToArray();
        if (effects.Length > 64) return null;
        List<ForgemagingWireEffect> decoded = [];
        foreach (var effect in effects)
        {
            if (effect.WireType != ProtoWireType.LengthDelimited || effect.Bytes is not { Length: <= 1024 } bytes) return null;
            var row = Fields(bytes); if (row is null || Scalar(row, 10) is not > 0) return null;
            var values = row.Where(f => f.Number == 5).ToArray();
            long? value = values.Length == 0 && row.All(f => f.Number == 10) ? 0 :
                values is { Length: 1 } && values[0].WireType == ProtoWireType.Varint ? unchecked((long)values[0].Varint) : null;
            if (value is < -100000 or > 100000) return null;
            decoded.Add(new(Scalar(row, 10)!.Value, value));
        }
        return new(Scalar(fields, 5)!.Value, checked((long)Scalar(fields, 4)!.Value), quantity, decoded);
    }
    // Observed on Pagalom / 3.7.13.17: workshop item, absolute pool, job id.
    internal static (ForgemagingWireItem Item, double Sink)? WorkshopItem(byte[] body)
    {
        var root = Fields(body);
        var description = Child(root, 3);
        if (Scalar(description, 4) is not > 0 || Float(root, 2) is not { } sink || Item(Child(description, 1)) is not { } item) return null;
        return (item, Math.Round(sink, 4));
    }

    // Six controlled 3.7.13.17 trials: result 1; description 2 -> pool 2, pool direction 3, item 4.
    internal static ForgemagingWireResult? Result(byte[] body)
    {
        var root = Fields(body);
        if (Scalar(root, 1) is not (>= 1 and <= 4) || root is null) return null;
        int code = (int)Scalar(root, 1)!.Value;
        var description = Child(root, 2);
        if (description is null)
            return code == 3 && root.Count == 1 ? new(code, null, null, null) : null;
        var item = Item(Child(description, 4));
        if (item is null || item.Quantity != 1) return null;
        double? sink = description.Any(f => f.Number == 2) ? Float(description, 2) : 0;
        ulong? status = description.Any(f => f.Number == 3) ? Scalar(description, 3) : 0;
        if (sink is null || status is null or > 2) return null;
        return new(code, item, Math.Round(sink.Value, 4), (int)status.Value);
    }
}
