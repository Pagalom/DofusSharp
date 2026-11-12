namespace BestCrush.Network.Protocol;

/// <summary>The current listing layout, verified on an actual resource deposit.
/// Instance UID in the request and announcement UID in the response are distinct.</summary>
public static class MarketListingDecoder
{
    public static MarketListingRequestObservation? DecodeRequest(byte[] body)
    {
        var root = Read(body);
        return root is not null &&
            Positive(root, 1) is { } uid &&
            Positive(root, 2) is { } quantity && quantity <= int.MaxValue &&
            Positive(root, 3) is { } price && price <= long.MaxValue
            ? new(uid, quantity, price) : null;
    }

    public static MarketListingCreatedObservation? DecodeCreated(byte[] body)
    {
        var root = Read(body);
        if (root is null || Positive(root, 1) is not { } price || price > long.MaxValue ||
            root.Count(x => x.Number == 2) != 1 ||
            root.Single(x => x.Number == 2) is not { WireType: ProtoWireType.LengthDelimited, Bytes: { } bytes })
            return null;
        var item = Read(bytes);
        // No instance effects are invented from this minimally verified layout.
        return item is not null &&
            Positive(item, 1) is { } itemId && itemId <= int.MaxValue &&
            Positive(item, 3) is { } listingUid &&
            Positive(item, 4) is { } quantity && quantity <= int.MaxValue
            ? new(listingUid, itemId, quantity, price, []) : null;
    }

    private static ulong? Positive(List<ProtoField> fields, int number)
    {
        var matches = fields.Where(x => x.Number == number).ToArray();
        return matches is [{ WireType: ProtoWireType.Varint, Varint: > 0 and var value }] ? value : null;
    }

    private static List<ProtoField>? Read(byte[] body)
    {
        if (body.Length is 0 or > 65536) return null;
        try { return ProtoWire.ReadFields(body); }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException) { return null; }
    }
}
