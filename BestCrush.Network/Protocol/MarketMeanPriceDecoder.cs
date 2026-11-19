namespace BestCrush.Network.Protocol;

/// <summary>Server isr full mean-price cache. Presence is UI history availability evidence, never an HDV minimum or a sale.</summary>
public sealed record MarketMeanPriceSnapshot(IReadOnlySet<long> WithMeanPrice, int EntryCount);

public static class MarketMeanPriceDecoder
{
    public static MarketMeanPriceSnapshot? Decode(byte[] body)
    {
        try { return DecodeCore(body); }
        catch (OverflowException) { return null; }
    }

    private static MarketMeanPriceSnapshot? DecodeCore(byte[] body)
    {
        if (body.Length is 0 or > 4 * 1024 * 1024 || ProtoWire.ReadFields(body) is not { Count: > 0 and <= 100_000 } fields)
            return null;
        var ids = new HashSet<long>(); var available = new HashSet<long>();
        foreach (var field in fields)
        {
            if (field is not { Number: 3, WireType: ProtoWireType.LengthDelimited, Bytes: { } bytes } ||
                ProtoWire.ReadFields(bytes) is not { Count: >= 1 and <= 2 } row ||
                row.Any(value => value.WireType != ProtoWireType.Varint || value.Number is not (1 or 2)) ||
                row.Where(value => value.Number == 1).ToArray() is not [var id] || id.Varint is 0 or > long.MaxValue ||
                row.Count(value => value.Number == 2) > 1 || !ids.Add((long)id.Varint)) return null;
            ulong price = row.FirstOrDefault(value => value.Number == 2)?.Varint ?? 0;
            if (price > long.MaxValue) return null;
            // Protobuf omits a zero price. Both omitted and explicitly zero mean no mean price.
            if (price > 0) available.Add((long)id.Varint);
        }
        return new(available, ids.Count);
    }
}
