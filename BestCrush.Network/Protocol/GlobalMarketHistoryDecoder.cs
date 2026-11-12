using System.Globalization;
using System.Text;

namespace BestCrush.Network.Protocol;

public sealed record GlobalMarketSalePoint(long UnitPrice, long UnitsSold, DateTimeOffset RecordedAtUtc);
public sealed record GlobalMarketHistory(long ItemId, IReadOnlyList<GlobalMarketSalePoint> Last24Hours,
    IReadOnlyList<GlobalMarketSalePoint> Last30Days);

/// <summary>Observed iqp/ire schema, DOFUS 3.7.12.12. Field migration belongs to protocol-map.json.</summary>
public static class GlobalMarketHistoryDecoder
{
    private const int MaximumBody = 256 * 1024;
    private const int MaximumPoints = 2048;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static long? DecodeRequest(ReadOnlySpan<byte> body)
    {
        if (body.Length > 16) return null;
        try
        {
            var fields = ProtoWire.ReadFields(body);
            return fields is [ { Number: 1, WireType: ProtoWireType.Varint } item ] && item.Varint is > 0 and <= long.MaxValue
                ? (long)item.Varint : null;
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException) { return null; }
    }

    public static GlobalMarketHistory? DecodeResponse(ReadOnlySpan<byte> body, long expectedItemId, DateTimeOffset capturedAtUtc)
    {
        if (expectedItemId <= 0 || body.Length is 0 or > MaximumBody) return null;
        try
        {
            var fields = ProtoWire.ReadFields(body);
            if (fields is null || fields.Count is 0 or > MaximumPoints ||
                fields.Any(x => x.Number is not (1 or 3) || x.WireType != ProtoWireType.LengthDelimited)) return null;
            List<GlobalMarketSalePoint> hours = [], days = [];
            foreach (var field in fields)
            {
                var row = field.Bytes is { } bytes ? ProtoWire.ReadFields(bytes) : null;
                if (row is null || row.Any(x => x.Number is < 1 or > 4) ||
                    row.GroupBy(x => x.Number).Any(x => x.Count() > 1)) return null;
                var price = row.SingleOrDefault(x => x.Number == 1);
                var item = row.SingleOrDefault(x => x.Number == 2);
                var date = row.SingleOrDefault(x => x.Number == 3);
                var count = row.SingleOrDefault(x => x.Number == 4);
                if (price is not { WireType: ProtoWireType.Varint } || price.Varint > long.MaxValue ||
                    item is not { WireType: ProtoWireType.Varint } || item.Varint != (ulong)expectedItemId ||
                    date is not { WireType: ProtoWireType.LengthDelimited, Bytes.Length: >= 20 and <= 40 } ||
                    count is not null && (count.WireType != ProtoWireType.Varint || count.Varint > long.MaxValue)) return null;
                string text = StrictUtf8.GetString(date.Bytes!);
                if (!text.EndsWith('Z') || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) ||
                    timestamp > capturedAtUtc.AddMinutes(1) || timestamp < capturedAtUtc.AddDays(-32)) return null;
                var point = new GlobalMarketSalePoint((long)price.Varint, (long)(count?.Varint ?? 0), timestamp);
                (field.Number == 1 ? hours : days).Add(point);
            }
            // Duplicated points would inflate demand. Empty complete responses
            // cannot identify an item and are not treated as known zero demand.
            if (hours.GroupBy(x => x.RecordedAtUtc).Any(x => x.Count() > 1) ||
                days.GroupBy(x => x.RecordedAtUtc).Any(x => x.Count() > 1)) return null;
            return new(expectedItemId, hours.OrderBy(x => x.RecordedAtUtc).ToArray(),
                days.OrderBy(x => x.RecordedAtUtc).ToArray());
        }
        catch (Exception ex) when (ex is OverflowException or DecoderFallbackException or ArgumentOutOfRangeException)
        { return null; }
    }
}
