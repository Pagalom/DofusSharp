using System.Security.Cryptography;

namespace BestCrush.Network.Protocol;

public sealed record PetItemReading(int Level, long? Experience, string Variant, bool Legendary, bool TradeRestricted);
public sealed record PetMarketOffer(int Level, long Experience, long Price, string Variant, bool Legendary);
public sealed record PetMarketSnapshot(long ItemId, IReadOnlyList<PetMarketOffer> Offers);

/// <summary>Pet effects shared by inventory f7 entries and market f5 entries; no numeric-stat fallback.</summary>
public static class PetMarketDecoder
{
    public static PetItemReading? Read(IReadOnlyList<byte[]>? entries, ulong category)
    {
        if (category is not (18 or 121) || entries is not { Count: > 0 and <= 128 }) return null;
        int? level = null;
        long? experience = null;
        bool legendary = false, restricted = false;
        var variant = new List<string>();
        foreach (var bytes in entries)
        {
            if (bytes.Length is 0 or > 1024 || ProtoWire.ReadFields(bytes) is not { } effect ||
                Scalar(effect, 10) is not { } code) return null;
            if (code is 2821 or 2819)
            {
                if (effect.Where(field => field.Number == 9).ToArray() is not [var payload] ||
                    payload.Bytes is null || ProtoWire.ReadFields(payload.Bytes) is not { } nested) return null;
                if (code == 2821)
                {
                    if (level.HasValue || Scalar(nested, 1) != category ||
                        DefaultScalar(nested, 4) is not (<= 100 and var number)) return null;
                    level = (int)number;
                }
                else
                {
                    if (experience.HasValue || DefaultScalar(nested, 4) is not (<= int.MaxValue and var xp)) return null;
                    experience = (long)xp;
                }
            }
            else if (code is 981 or 982 or 983) restricted = true;
            else if (code >= 1000)
            {
                legendary |= code is 2862 or 2864 or 1175;
                // Keep additional powers distinct. XP and native numeric stats must not split levels.
                variant.Add(Convert.ToHexString(SHA256.HashData(bytes)));
            }
        }
        if (level is null) return null;
        variant.Sort(StringComparer.Ordinal);
        string signature = variant.Count == 0 ? "normal" : string.Join('.', variant);
        return new(level.Value, experience, signature, legendary, restricted);
    }

    public static PetMarketSnapshot? Decode(byte[] body)
    {
        if (body.Length is 0 or > 1024 * 1024 || ProtoWire.ReadFields(body) is not { } root ||
            Scalar(root, 2) is not (18 or 121) ||
            SemanticDecoders.TryDecodeJzsEquipmentPrices(body) is not { } market) return null;
        ulong category = Scalar(root, 2)!.Value;
        var quotes = market.Offers.GroupBy(offer => offer.OfferId).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Ladder[0]);
        var offers = new List<PetMarketOffer>();
        foreach (var field in root.Where(field => field.Number == 1))
        {
            if (field.Bytes is null || ProtoWire.ReadFields(field.Bytes) is not { } row ||
                Scalar(row, 3) is not { } listing || !quotes.TryGetValue(listing, out ulong price) ||
                price is 0 or > long.MaxValue) continue;
            var effects = row.Where(value => value.Number == 5).ToArray();
            if (effects.Any(effect => effect.Bytes is null) ||
                Read(effects.Select(effect => effect.Bytes!).ToArray(), category) is not
                    { Experience: { } xp, TradeRestricted: false } reading) continue;
            offers.Add(new(reading.Level, xp, (long)price, reading.Variant, reading.Legendary));
        }
        return offers.Count == 0 ? null : new((long)market.ItemId, offers);
    }

    private static ulong? Scalar(List<ProtoField> fields, int number) =>
        fields.Where(field => field.Number == number).ToArray() is [{ WireType: ProtoWireType.Varint } field]
            ? field.Varint : null;
    private static ulong? DefaultScalar(List<ProtoField> fields, int number) =>
        fields.Any(field => field.Number == number) ? Scalar(fields, number) : 0;
}
