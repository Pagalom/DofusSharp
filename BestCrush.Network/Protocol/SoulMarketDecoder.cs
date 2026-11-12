namespace BestCrush.Network.Protocol;

public sealed record SoulMarketOffer(long OfferId, long BossId, long GroupId, IReadOnlyList<ulong> Prices);
public sealed record SoulMarketObservation(long ItemId, IReadOnlyList<SoulMarketOffer> Offers);

/// <summary>Boss-soul jzs rows observed on DOFUS 3.7.12.12, 2026-10-29.</summary>
public static class SoulMarketDecoder
{
    public static bool IsBossSoulSelection(byte[] body)
    {
        try { return body.Length <= 1024 * 1024 && ProtoWire.ReadFields(body)?.Any(x =>
            x.Number == 2 && x.WireType == ProtoWireType.Varint && x.Varint == 327) == true; }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException) { return false; }
    }

    public static SoulMarketObservation? Decode(byte[] body)
    {
        if (body.Length is 0 or > 1024 * 1024) return null;
        try
        {
            var root = ProtoWire.ReadFields(body);
            if (root is null || root.Count is < 3 or > 130 || root.Any(x => x.Number is < 1 or > 3) ||
                Scalar(root, 2) != 327 || Scalar(root, 3) is not { } item || item == 0 || item > long.MaxValue) return null;
            List<SoulMarketOffer> offers = [];
            HashSet<long> ids = [];
            foreach (var field in root.Where(x => x.Number == 1))
            {
                var row = Children(field);
                if (row is null || row.Count != 7 || row.Any(x => x.Number is < 1 or > 5) ||
                    Scalar(row, 1) != item || Scalar(row, 4) != 327 ||
                    Scalar(row, 3) is not (> 0 and <= long.MaxValue) ||
                    row.Count(x => x.Number == 2) != 1 || row.Count(x => x.Number == 5) != 3) return null;
                long id = (long)Scalar(row, 3)!.Value;
                if (!ids.Add(id)) return null;
                var price = row.Single(x => x.Number == 2);
                var ladder = price.WireType == ProtoWireType.LengthDelimited && price.Bytes is { Length: <= 40 } bytes
                    ? ProtoWire.ReadPackedVarints(bytes) : null;
                if (ladder is not { Count: 4 } || ladder.All(x => x == 0) || ladder.Any(x => x > long.MaxValue)) return null;
                Dictionary<ulong, List<ProtoField>> effects = [];
                foreach (var effect in row.Where(x => x.Number == 5))
                {
                    var fields = Children(effect);
                    if (fields is null || Scalar(fields, 10) is not { } effectId ||
                        !effects.TryAdd(effectId, fields)) return null;
                }
                if (!effects.TryGetValue(4057, out var groupEffect) || groupEffect.Count != 2 ||
                    !effects.TryGetValue(4058, out var bossEffect) || bossEffect.Count != 2) return null;
                var group = Nested(groupEffect, 9);
                var boss = Nested(bossEffect, 9);
                if (group is not { Count: 2 } ||
                    Scalar(group, 2) is not (> 0 and <= long.MaxValue) ||
                    boss is not { Count: 2 } || Scalar(boss, 1) != 5 ||
                    Scalar(boss, 4) is not (> 0 and <= long.MaxValue)) return null;
                offers.Add(new(id, (long)Scalar(boss, 4)!.Value, (long)Scalar(group, 2)!.Value, ladder));
            }
            // Empty follow-up notifications must not erase the last actual market snapshot.
            return offers.Count == 0 ? null : new((long)item, offers);
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException) { return null; }
    }

    private static ulong? Scalar(List<ProtoField> fields, int number)
    {
        var values = fields.Where(x => x.Number == number).ToArray();
        return values is [{ WireType: ProtoWireType.Varint } field] ? field.Varint : null;
    }
    private static List<ProtoField>? Children(ProtoField field) =>
        field is { WireType: ProtoWireType.LengthDelimited, Bytes: { } bytes } ? ProtoWire.ReadFields(bytes) : null;
    private static List<ProtoField>? Nested(List<ProtoField> fields, int number)
    {
        var values = fields.Where(x => x.Number == number).ToArray();
        return values.Length == 1 ? Children(values[0]) : null;
    }
}
