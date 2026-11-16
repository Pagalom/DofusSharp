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
