namespace BestCrush.Network.Protocol;

/// <summary>Current HDV purchase messages, verified against a single-item equipment purchase.</summary>
public static class CurrentPurchaseDecoder
{
    public static PurchaseRequestObservation? DecodeRequest(byte[] body)
    {
        var fields = ProtoWire.ReadFields(body);
        return Scalar(fields, 1) is > 0 and var quantity && Scalar(fields, 3) is > 0 and var offer &&
            Scalar(fields, 5) is > 0 and var price ? new(price, quantity, offer) : null;
    }

    public static PurchaseOfferObservation? DecodeOffer(byte[] body)
    {
        var fields = ProtoWire.ReadFields(body);
        return Scalar(fields, 2) is > 0 and var item && Scalar(fields, 4) is > 0 and var offer
            ? new(item, offer, [], []) : null;
    }

    /// <summary>
    /// Public lot minima refreshed after a purchase or a listing. This is not
    /// the player's requested listing price or a completed-sale receipt.
    /// The caller must resolve the item as a catalogue rune/resource.
    /// </summary>
    public static MarketObservation? DecodeStackableMarket(byte[] body)
    {
        if (body.Length is 0 or > 128) return null;
        var fields = ProtoWire.ReadFields(body);
        if (fields is not { Count: 4 } ||
            Scalar(fields, 2) is not (> 0 and <= int.MaxValue and var item) ||
            Scalar(fields, 3) is not (> 0 and <= int.MaxValue) ||
            Scalar(fields, 4) is not (> 0 and var offer)) return null;
        var prices = fields.Where(field => field.Number == 1).ToArray();
        if (prices is not [{ WireType: ProtoWireType.LengthDelimited, Bytes: { } packed }]) return null;
        var ladder = ProtoWire.ReadPackedVarints(packed);
        if (ladder is not { Count: 4 } || ladder.Any(price => price > long.MaxValue) ||
            !ladder.Any(price => price > 0)) return null;
        return new(item, [new(offer, item, ladder)]);
    }

    public static PurchaseReceiptObservation? DecodeReceipt(byte[] body)
    {
        var fields = ProtoWire.ReadFields(body);
        return Scalar(fields, 2) is > 0 and var offer && Scalar(fields, 3) is > 0 and var quantity
            ? new(quantity, offer) : null;
    }

    private static ulong? Scalar(List<ProtoField>? fields, int number)
    {
        var matching = fields?.Where(field => field.Number == number).Take(2).ToArray();
        return matching is [{ WireType: ProtoWireType.Varint } field] ? field.Varint : null;
    }
}
