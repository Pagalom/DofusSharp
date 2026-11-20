using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BestCrush.Network.Protocol;

public sealed record PersonalMarketHistoryEntry(string Id, long ItemId, int Quantity, long Price,
    DateTimeOffset At, bool Returned, IReadOnlyList<byte[]> Effects);

/// <summary>kxt, observed 2026-11-19: 989 sold / 11 returned, checked against the personal history UI.</summary>
public static class PersonalMarketHistoryDecoder
{
    public static IReadOnlyList<PersonalMarketHistoryEntry>? Decode(byte[] body, DateTimeOffset observedAt)
    {
        if (body.Length is 0 or > 4 * 1024 * 1024 || ProtoWire.ReadFields(body) is not { } root || root.Count > 1000 ||
            root.Any(f => f.Number != 1 || f.Bytes is null)) return null;
        var result = new List<PersonalMarketHistoryEntry>(); var occurrences = new Dictionary<string, int>();
        foreach (var row in root)
        {
            if (ProtoWire.ReadFields(row.Bytes!) is not { } fields ||
                Number(fields, 1) is not (> 0 and <= long.MaxValue and var price) ||
                fields.Where(f => f.Number == 3).ToArray() is not [{ Bytes: { Length: <= 40 } date }] ||
                !DateTimeOffset.TryParse(ProtoWire.TryUtf8(date), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) ||
                at > observedAt.AddMinutes(1) || at.Year < 2000 ||
                fields.Where(f => f.Number == 5).ToArray() is not [{ Bytes: { } itemBytes }] ||
                ProtoWire.ReadFields(itemBytes) is not { } item ||
                Number(item, 2) is not (> 0 and <= long.MaxValue and var id) ||
                Number(item, 3) is not (> 0 and <= int.MaxValue and var quantity)) return null;
            var status = fields.Where(f => f.Number == 4).ToArray();
            if (status.Length > 1 || status is [{ WireType: not ProtoWireType.Varint }] ||
                status is [{ Varint: > 1 }]) return null;
            var effects = item.Where(f => f.Number == 1).ToArray();
            if (effects.Length > 128 || effects.Any(f => f.Bytes is not { Length: > 0 and <= 1024 })) return null;
            bool returned = status is [{ Varint: 1 }];
            // Do not use the history's ordering or its observation date as a sale identity.
            string signature = $"{id}:{quantity}:{price}:{at.UtcTicks}:{returned}:" +
                string.Join('.', effects.Select(e => Convert.ToHexString(SHA256.HashData(e.Bytes!))).Order());
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
            int occurrence = occurrences.GetValueOrDefault(hash); occurrences[hash] = occurrence + 1;
            result.Add(new(hash + ":" + occurrence, (long)id, (int)quantity, (long)price, at, returned,
                effects.Select(f => f.Bytes!).ToArray()));
        }
        return result;
    }

    private static ulong? Number(List<ProtoField> fields, int number) => fields.Where(f => f.Number == number).ToArray()
        is [{ WireType: ProtoWireType.Varint } field] ? field.Varint : null;
}
