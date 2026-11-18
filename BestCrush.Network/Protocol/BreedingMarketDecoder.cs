namespace BestCrush.Network.Protocol;

public sealed record BreedingMarketLevelOffer(int Level, long Price, bool Fertile);
public sealed record BreedingMarketLevels(long ItemId, IReadOnlyList<BreedingMarketLevelOffer> Offers);

public static class BreedingMarketDecoder
{
    public static BreedingMarketLevels? Decode(byte[] body)
    {
        if (body.Length is 0 or > 1024 * 1024 || ProtoWire.ReadFields(body) is not { } root ||
            Scalar(root, 2) is not (331 or 332 or 333) || Scalar(root, 3) is not (> 0 and <= long.MaxValue) ||
            SemanticDecoders.TryDecodeJzsEquipmentPrices(body) is not { } market)
            return null;
        ulong category = Scalar(root, 2)!.Value;
        var prices = market.Offers.GroupBy(offer => offer.OfferId).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Ladder[0]);
        var offers = new List<BreedingMarketLevelOffer>();
        foreach (var field in root.Where(field => field.Number == 1))
        {
            if (Children(field) is not { } row || Scalar(row, 3) is not { } listing ||
                !prices.TryGetValue(listing, out ulong price) || price is 0 or > long.MaxValue)
                continue;
            // In a mount offer, effect 2821 contains the mount category (f1)
            // and the actual level (f4). The adjacent f5 value on 3829 is binary.
            var effects = row.Where(value => value.Number == 5).Select(Children).OfType<List<ProtoField>>().ToArray();
            var levelEffects = effects.Where(value => Scalar(value, 10) == 2821).ToArray();
            if (levelEffects is not [var effect] || effect.Where(value => value.Number == 9).ToArray() is not [var value] ||
                Children(value) is not { } nested || Scalar(nested, 1) != category ||
                Scalar(nested, 4) is not (>= 1 and <= 200))
                continue;
            offers.Add(new((int)Scalar(nested, 4)!.Value, (long)price, effects.Any(value => Scalar(value, 10) == 3830)));
        }
        return offers.Count == 0 ? null : new((long)market.ItemId, offers);
    }

    private static ulong? Scalar(List<ProtoField> fields, int number) =>
        fields.Where(field => field.Number == number).ToArray() is [{ WireType: ProtoWireType.Varint } field]
            ? field.Varint : null;

    private static List<ProtoField>? Children(ProtoField field) =>
        field.Bytes is { } bytes ? ProtoWire.ReadFields(bytes) : null;
}
