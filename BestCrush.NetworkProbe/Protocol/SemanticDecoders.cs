using System.Text.Json;

namespace BestCrush.NetworkProbe.Protocol;

internal sealed record ProtocolMap(
    string ClientBuild,
    string? PriceList,
    string? CrushResult,
    string? ItemDetail,
    string? WorkshopSlotPut,
    string? PurchaseRequest,
    string? PurchaseOffer,
    string? InventoryAdd,
    string? SmithmagicRequest,
    string? SmithmagicBatchRequest,
    string? SmithmagicResult,
    string? SmithmagicAux,
    IReadOnlySet<string> DiagnosticMessages)
{
    public static ProtocolMap Load(string path)
    {
        if (!File.Exists(path))
            return new ProtocolMap(
                "3.6.11.15",
                "jzn",
                "kci",
                "kdb",
                "kec",
                "kei",
                "kef",
                "isa",
                "jze",
                "kcs",
                "kbu",
                "kar",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "itt","log","kbd","iut","irl","isf","keb","kcw"
                });

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        string build = root.TryGetProperty("clientBuild", out JsonElement b)
            ? b.GetString() ?? "unknown"
            : "unknown";

        string? price = root.TryGetProperty("price_list", out JsonElement p) ? p.GetString() : null;
        string? crush = root.TryGetProperty("crush_result", out JsonElement c) ? c.GetString() : null;
        string? itemDetail = root.TryGetProperty("item_detail", out JsonElement d) ? d.GetString() : null;
        string? workshopSlotPut = root.TryGetProperty("workshop_slot_put", out JsonElement e)
            ? e.GetString()
            : root.TryGetProperty("crush_slot_put", out JsonElement legacy) ? legacy.GetString() : null;
        string? purchaseRequest = root.TryGetProperty("purchase_request", out JsonElement pr) ? pr.GetString() : null;
        string? purchaseOffer = root.TryGetProperty("purchase_offer", out JsonElement po) ? po.GetString() : null;
        string? inventoryAdd = root.TryGetProperty("inventory_add", out JsonElement ia) ? ia.GetString() : null;
        string? smithmagicRequest = root.TryGetProperty("smithmagic_request", out JsonElement sr) ? sr.GetString() : null;
        string? smithmagicBatchRequest = root.TryGetProperty("smithmagic_batch_request", out JsonElement sbr) ? sbr.GetString() : null;
        string? smithmagicResult = root.TryGetProperty("smithmagic_result", out JsonElement sx) ? sx.GetString() : null;
        string? smithmagicAux = root.TryGetProperty("smithmagic_aux", out JsonElement sa) ? sa.GetString() : null;

        HashSet<string> diagnostics = new(StringComparer.Ordinal);
        if (root.TryGetProperty("diagnostic_messages", out JsonElement dm) &&
            dm.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in dm.EnumerateArray())
            {
                string? value = entry.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    diagnostics.Add(value);
            }
        }

        return new ProtocolMap(
            build,
            price,
            crush,
            itemDetail,
            workshopSlotPut,
            purchaseRequest,
            purchaseOffer,
            inventoryAdd,
            smithmagicRequest,
            smithmagicBatchRequest,
            smithmagicResult,
            smithmagicAux,
            diagnostics);
    }
}

internal sealed record MarketOffer(ulong ListingId, ulong ItemId, IReadOnlyList<ulong> Ladder);
internal sealed record MarketObservation(ulong ItemId, IReadOnlyList<MarketOffer> Offers);

internal sealed record ItemStatObservation(ulong EffectId, long Value);
internal sealed record ItemDetailObservation(
    ulong ItemUid,
    ulong ItemId,
    ulong Quantity,
    IReadOnlyList<ItemStatObservation> Stats);
internal sealed record WorkshopSlotObservation(long Delta, ulong ItemUid);

internal sealed record PurchaseRequestObservation(ulong Price, ulong Quantity, ulong ListingId);
internal sealed record PurchaseOfferObservation(
    ulong ItemId,
    ulong ListingId,
    IReadOnlyList<ItemStatObservation> Stats);
internal sealed record SmithmagicRequestObservation(ulong RuneUid, ulong Quantity);
internal sealed record SmithmagicBatchRequestObservation(ulong Quantity, ulong Sequence);
internal sealed record SmithmagicStackObservation(ItemDetailObservation Stack);
internal sealed record SmithmagicResultObservation(int ResultCode, ItemDetailObservation Item);

internal sealed record RuneDrop(ulong RuneItemId, ulong Quantity);
internal sealed record CrushLineObservation(
    ulong ItemUid,
    float CoefficientPercent,
    float? SecondaryPercent,
    IReadOnlyList<RuneDrop> Runes);
internal sealed record CrushObservation(IReadOnlyList<CrushLineObservation> Lines);

internal static class SemanticDecoders
{
    public static MarketObservation? TryDecodeMarket(byte[] body)
    {
        List<ProtoField>? root = ProtoWire.ReadFields(body);
        if (root is null)
            return null;

        MarketObservation? currentJzn = TryDecodeCurrentJzn(root);
        if (currentJzn is not null)
            return currentJzn;

        MarketObservation? structured = TryDecodeStructuredMarket(root);
        if (structured is not null)
            return structured;

        return TryDecodeCompactMarket(root);
    }

    // Dofus 3.6.11.15 / jzn observed on wire:
    // root f1 = item id
    // root f2 = repeated offer
    // root f3 = market/category id
    // offer f2 = item id
    // offer f5 = listing id
    // offer f6 = packed [x1, x10, x100, x1000] prices
    private static MarketObservation? TryDecodeCurrentJzn(List<ProtoField> root)
    {
        ulong itemId = root.FirstOrDefault(f =>
            f.Number == 1 &&
            f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        if (itemId == 0)
            return null;

        List<MarketOffer> offers = new();

        foreach (ProtoField offerField in root.Where(f =>
                     f.Number == 2 &&
                     f.WireType == ProtoWireType.LengthDelimited &&
                     f.Bytes is not null))
        {
            List<ProtoField>? offer = ProtoWire.ReadFields(offerField.Bytes!);
            if (offer is null)
                continue;

            ulong inlineItemId = offer.FirstOrDefault(f =>
                f.Number == 2 &&
                f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

            ulong listingId = offer.FirstOrDefault(f =>
                f.Number == 5 &&
                f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

            ProtoField? ladderField = offer.FirstOrDefault(f =>
                f.Number == 6 &&
                f.WireType == ProtoWireType.LengthDelimited &&
                f.Bytes is not null);

            if (ladderField?.Bytes is null)
                continue;

            List<ulong>? rawLadder = ProtoWire.ReadPackedVarints(ladderField.Bytes);
            if (rawLadder is null || rawLadder.Count == 0)
                continue;

            List<ulong> ladder = CleanLadder(rawLadder);
            ulong resolvedItemId = inlineItemId != 0 ? inlineItemId : itemId;

            offers.Add(new MarketOffer(listingId, resolvedItemId, ladder));
        }

        // A jzn carrying only f1/f3 means the item is known but currently has
        // no price ladder. Keep it visible in the probe instead of treating it
        // as a decode failure.
        return new MarketObservation(itemId, offers);
    }

    private static MarketObservation? TryDecodeStructuredMarket(List<ProtoField> root)
    {
        ulong itemId = root.FirstOrDefault(f => f.Number == 2 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        List<MarketOffer> offers = new();

        foreach (ProtoField offerField in root.Where(f =>
                     f.Number == 3 &&
                     f.WireType == ProtoWireType.LengthDelimited &&
                     f.Bytes is not null))
        {
            List<ProtoField>? offer = ProtoWire.ReadFields(offerField.Bytes!);
            if (offer is null)
                continue;

            ulong listingId = offer.FirstOrDefault(f => f.Number == 1 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
            ulong inlineItemId = offer.FirstOrDefault(f => f.Number == 5 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

            ProtoField? ladderField = offer.FirstOrDefault(f =>
                f.Number == 6 &&
                f.WireType == ProtoWireType.LengthDelimited &&
                f.Bytes is not null);

            List<ulong> ladder = ladderField?.Bytes is not null
                ? CleanLadder(ProtoWire.ReadPackedVarints(ladderField.Bytes) ?? new List<ulong>())
                : new List<ulong>();

            ulong resolvedItemId = inlineItemId != 0 ? inlineItemId : itemId;
            if (resolvedItemId != 0 && ladder.Count > 0)
                offers.Add(new MarketOffer(listingId, resolvedItemId, ladder));
        }

        if (itemId == 0 && offers.Count > 0)
            itemId = offers[0].ItemId;

        return itemId != 0 && offers.Count > 0
            ? new MarketObservation(itemId, offers)
            : null;
    }

    private static MarketObservation? TryDecodeCompactMarket(List<ProtoField> root)
    {
        ulong itemId = 0;
        List<List<ulong>> ladders = new();
        CollectCompactMarket(root, 0, ref itemId, ladders);

        List<List<ulong>> valid = ladders
            .Select(CleanLadder)
            .Where(x => x.Count > 0 && x.Any(v => v > 10))
            .ToList();

        if (itemId == 0 || valid.Count == 0)
            return null;

        List<MarketOffer> offers = valid
            .Select((ladder, index) => new MarketOffer((ulong)index, itemId, ladder))
            .ToList();

        return new MarketObservation(itemId, offers);
    }

    private static void CollectCompactMarket(
        IReadOnlyList<ProtoField> fields,
        int depth,
        ref ulong itemId,
        List<List<ulong>> ladders)
    {
        foreach (ProtoField field in fields)
        {
            if (field.WireType == ProtoWireType.Varint)
            {
                if (depth <= 1 &&
                    field.Number is 1 or 2 or 5 &&
                    field.Varint is >= 10 and <= 100_000 &&
                    itemId == 0)
                {
                    itemId = field.Varint;
                }

                continue;
            }

            if (field.WireType != ProtoWireType.LengthDelimited || field.Bytes is null || field.Bytes.Length == 0)
                continue;

            List<ProtoField>? nested = ProtoWire.ReadFields(field.Bytes);
            if (nested is not null && nested.Count > 0 && depth < 3)
            {
                CollectCompactMarket(nested, depth + 1, ref itemId, ladders);
                continue;
            }

            List<ulong>? packed = ProtoWire.ReadPackedVarints(field.Bytes);
            if (packed is null || packed.Count == 0 || packed.Count > 16)
                continue;

            if (packed.Any(v => v > 10))
                ladders.Add(packed);
        }
    }

    private static List<ulong> CleanLadder(IReadOnlyList<ulong> source)
    {
        List<ulong> values = source.ToList();
        if (values.Count == 0)
            return values;

        if (values.Count > 1 &&
            values[0] is >= 1 and <= 10 &&
            values.Count == checked((int)values[0] + 1))
        {
            values.RemoveAt(0);
        }
        else if (values.Count == 5 &&
                 values[0] <= 20 &&
                 values[1] > 0 &&
                 values[2] >= values[1])
        {
            values.RemoveAt(0);
        }

        List<ulong> nonZero = values.Where(v => v > 0).ToList();
        if (values.Count >= 2 &&
            nonZero.Count >= 2 &&
            values[0] > values[^1] &&
            nonZero.SequenceEqual(nonZero.OrderByDescending(v => v)))
        {
            values.Reverse();
        }

        return values;
    }

    public static ItemDetailObservation? TryDecodeItemDetail(byte[] body)
    {
        List<ProtoField>? root = ProtoWire.ReadFields(body);
        ProtoField? envelopeField = root?.FirstOrDefault(f =>
            f.Number == 2 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is not null);

        if (envelopeField?.Bytes is null)
            return null;

        List<ProtoField>? envelope = ProtoWire.ReadFields(envelopeField.Bytes);
        ProtoField? itemField = envelope?.FirstOrDefault(f =>
            f.Number == 5 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is not null);

        return itemField?.Bytes is null
            ? null
            : TryDecodeItemObject(itemField.Bytes);
    }

    public static PurchaseRequestObservation? TryDecodePurchaseRequest(byte[] body)
    {
        List<ProtoField>? fields = ProtoWire.ReadFields(body);
        if (fields is null)
            return null;

        ulong price = fields.FirstOrDefault(f =>
            f.Number == 1 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong quantity = fields.FirstOrDefault(f =>
            f.Number == 2 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong listingId = fields.FirstOrDefault(f =>
            f.Number == 5 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        return price > 0 && quantity > 0 && listingId > 0
            ? new PurchaseRequestObservation(price, quantity, listingId)
            : null;
    }

    public static PurchaseOfferObservation? TryDecodePurchaseOffer(byte[] body)
    {
        List<ProtoField>? fields = ProtoWire.ReadFields(body);
        if (fields is null)
            return null;

        ulong itemId = fields.FirstOrDefault(f =>
            f.Number == 1 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong listingId = fields.FirstOrDefault(f =>
            f.Number == 2 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        if (itemId == 0 || listingId == 0)
            return null;

        return new PurchaseOfferObservation(
            itemId,
            listingId,
            DecodeStats(fields, 5));
    }

    public static ItemDetailObservation? TryDecodeInventoryAdd(byte[] body)
        => TryDecodeItemDetail(body);

    public static SmithmagicRequestObservation? TryDecodeSmithmagicRequest(byte[] body)
    {
        List<ProtoField>? fields = ProtoWire.ReadFields(body);
        if (fields is null)
            return null;

        ulong runeUid = fields.FirstOrDefault(f =>
            f.Number == 1 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong quantity = fields.FirstOrDefault(f =>
            f.Number == 3 && f.WireType == ProtoWireType.Varint)?.Varint ?? 1;

        return runeUid > 0
            ? new SmithmagicRequestObservation(runeUid, quantity)
            : null;
    }

    public static SmithmagicBatchRequestObservation? TryDecodeSmithmagicBatchRequest(byte[] body)
    {
        List<ProtoField>? fields = ProtoWire.ReadFields(body);
        if (fields is null)
            return null;

        ulong quantity = fields.FirstOrDefault(f =>
            f.Number == 2 && f.WireType == ProtoWireType.Varint)?.Varint ?? 1;
        ulong sequence = fields.FirstOrDefault(f =>
            f.Number == 3 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        return quantity > 0
            ? new SmithmagicBatchRequestObservation(quantity, sequence)
            : null;
    }

    public static SmithmagicStackObservation? TryDecodeSmithmagicStack(byte[] body)
    {
        List<ProtoField>? root = ProtoWire.ReadFields(body);
        ProtoField? envelopeField = root?.FirstOrDefault(f =>
            f.Number == 3 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is not null);

        if (envelopeField?.Bytes is null)
            return null;

        List<ProtoField>? envelope = ProtoWire.ReadFields(envelopeField.Bytes);
        ProtoField? itemField = envelope?.FirstOrDefault(f =>
            f.Number == 5 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is not null);

        if (itemField?.Bytes is null)
            return null;

        ItemDetailObservation? item = TryDecodeItemObject(itemField.Bytes);
        return item is null ? null : new SmithmagicStackObservation(item);
    }

    public static SmithmagicResultObservation? TryDecodeSmithmagicResult(byte[] body)
    {
        List<ProtoField>? root = ProtoWire.ReadFields(body);
        if (root is null)
            return null;

        int resultCode = checked((int)(root.FirstOrDefault(f =>
            f.Number == 2 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0));

        ProtoField? resultField = root.FirstOrDefault(f =>
            f.Number == 3 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is not null);

        if (resultField?.Bytes is null)
            return null;

        List<ProtoField>? result = ProtoWire.ReadFields(resultField.Bytes);
        ProtoField? itemField = result?.FirstOrDefault(f =>
            f.Number == 2 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is not null);

        if (itemField?.Bytes is null)
            return null;

        ItemDetailObservation? item = TryDecodeItemObject(itemField.Bytes);
        return item is null
            ? null
            : new SmithmagicResultObservation(resultCode, item);
    }

    private static ItemDetailObservation? TryDecodeItemObject(byte[] itemBytes)
    {
        List<ProtoField>? item = ProtoWire.ReadFields(itemBytes);
        if (item is null)
            return null;

        ulong uid = item.FirstOrDefault(f =>
            f.Number == 1 &&
            f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        ulong quantity = item.FirstOrDefault(f =>
            f.Number == 2 &&
            f.WireType == ProtoWireType.Varint)?.Varint ?? 1;

        ulong itemId = item.FirstOrDefault(f =>
            f.Number == 5 &&
            f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        if (uid == 0 || itemId == 0)
            return null;

        return new ItemDetailObservation(
            uid,
            itemId,
            quantity,
            DecodeStats(item, 3));
    }

    private static IReadOnlyList<ItemStatObservation> DecodeStats(
        IReadOnlyList<ProtoField> fields,
        int fieldNumber)
    {
        List<ItemStatObservation> stats = new();

        foreach (ProtoField statField in fields.Where(f =>
                     f.Number == fieldNumber &&
                     f.WireType == ProtoWireType.LengthDelimited &&
                     f.Bytes is not null))
        {
            List<ProtoField>? stat = ProtoWire.ReadFields(statField.Bytes!);
            if (stat is null)
                continue;

            ulong effectId = stat.FirstOrDefault(f =>
                f.Number == 1 &&
                f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

            ProtoField? valueField = stat.FirstOrDefault(f =>
                f.Number == 10 &&
                f.WireType == ProtoWireType.Varint);

            if (effectId == 0 || valueField is null)
                continue;

            stats.Add(new ItemStatObservation(
                effectId,
                unchecked((long)valueField.Varint)));
        }

        return stats;
    }

    public static WorkshopSlotObservation? TryDecodeWorkshopSlot(byte[] body)
    {
        List<ProtoField>? fields = ProtoWire.ReadFields(body);
        if (fields is null)
            return null;

        ProtoField? deltaField = fields.FirstOrDefault(f =>
            f.Number == 1 &&
            f.WireType == ProtoWireType.Varint);

        ulong uid = fields.FirstOrDefault(f =>
            f.Number == 2 &&
            f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        if (uid == 0)
            return null;

        long delta = deltaField is null
            ? 1
            : unchecked((long)deltaField.Varint);

        return new WorkshopSlotObservation(delta, uid);
    }

    public static CrushObservation? TryDecodeCrush(byte[] body)
    {
        List<ProtoField>? outer = ProtoWire.ReadFields(body);
        if (outer is null)
            return null;

        List<CrushLineObservation> lines = new();

        // Dofus 3.6.11.15 / kci observed on wire:
        // root f1 = repeated result row, one per destroyed item instance
        // row f1 = repeated rune result
        //   rune f1 = rune item id
        //   rune f3 = quantity obtained
        // row f2 = coefficient/yield as float32 fraction
        // row f3 = destroyed item instance UID
        // row f4 = second float32, extremely close to f2; purpose unknown
        foreach (ProtoField rowField in outer.Where(f =>
                     f.Number == 1 &&
                     f.WireType == ProtoWireType.LengthDelimited &&
                     f.Bytes is not null))
        {
            List<ProtoField>? row = ProtoWire.ReadFields(rowField.Bytes!);
            if (row is null)
                continue;

            ulong uid = row.FirstOrDefault(f =>
                f.Number == 3 &&
                f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

            ProtoField? yieldField = row.FirstOrDefault(f =>
                f.Number == 2 &&
                f.WireType == ProtoWireType.Fixed32);

            if (uid == 0 || yieldField is null)
                continue;

            float yieldFraction = ProtoWire.ToFloat(yieldField.Fixed32);
            if (!float.IsFinite(yieldFraction) || yieldFraction < 0 || yieldFraction > 10)
                continue;

            ProtoField? secondaryField = row.FirstOrDefault(f =>
                f.Number == 4 &&
                f.WireType == ProtoWireType.Fixed32);

            float? secondary = secondaryField is null
                ? null
                : ProtoWire.ToFloat(secondaryField.Fixed32) * 100f;

            List<RuneDrop> runes = new();
            foreach (ProtoField runeField in row.Where(f =>
                         f.Number == 1 &&
                         f.WireType == ProtoWireType.LengthDelimited &&
                         f.Bytes is not null))
            {
                List<ProtoField>? rune = ProtoWire.ReadFields(runeField.Bytes!);
                if (rune is null)
                    continue;

                ulong runeId = rune.FirstOrDefault(f =>
                    f.Number == 1 &&
                    f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

                ulong count = rune.FirstOrDefault(f =>
                    f.Number == 3 &&
                    f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

                if (runeId != 0)
                    runes.Add(new RuneDrop(runeId, count));
            }

            lines.Add(new CrushLineObservation(
                uid,
                yieldFraction * 100f,
                secondary,
                runes));
        }

        return lines.Count > 0
            ? new CrushObservation(lines)
            : null;
    }

}

internal static class ConsoleRenderer
{
    public static void WriteMarket(MarketObservation market)
    {
        Console.WriteLine();
        Console.WriteLine($"[MARKET] ItemId={market.ItemId} — {market.Offers.Count} offre(s)");

        if (market.Offers.Count == 0)
        {
            Console.WriteLine("  Message sans offre/prix (métadonnée ou état intermédiaire).");
        }
        else if (market.Offers.Count == 1 && market.Offers[0].Ladder.Count <= 4)
        {
            string[] labels = ["x1", "x10", "x100", "x1000"];
            for (int i = 0; i < market.Offers[0].Ladder.Count; i++)
                Console.WriteLine($"  {labels[i],-5} {market.Offers[0].Ladder[i]:N0} K");
        }
        else
        {
            int n = 1;
            foreach (MarketOffer offer in market.Offers
                         .OrderBy(o => EffectiveUnitPrice(o.Ladder)))
            {
                List<string> parts = new();
                ulong[] quantities = [1, 10, 100, 1000];

                for (int i = 0; i < Math.Min(offer.Ladder.Count, quantities.Length); i++)
                {
                    ulong price = offer.Ladder[i];
                    if (price > 0)
                        parts.Add($"x{quantities[i]} {price:N0} K");
                }

                string prices = parts.Count > 0
                    ? string.Join(" | ", parts)
                    : "(aucun prix)";
                Console.WriteLine($"  #{n++,2}  {prices}");
            }
        }

        Console.WriteLine();
    }

    private static decimal EffectiveUnitPrice(IReadOnlyList<ulong> ladder)
    {
        ulong[] quantities = [1, 10, 100, 1000];
        decimal best = decimal.MaxValue;

        for (int i = 0; i < Math.Min(ladder.Count, quantities.Length); i++)
        {
            if (ladder[i] == 0)
                continue;

            decimal unit = ladder[i] / (decimal)quantities[i];
            if (unit < best)
                best = unit;
        }

        return best;
    }

    public static void WriteItemDetail(ItemDetailObservation item)
    {
        string stats = item.Stats.Count == 0
            ? "(aucune stat)"
            : string.Join(", ", item.Stats.Select(x => $"{x.EffectId}={x.Value}"));

        Console.WriteLine(
            $"[ITEM] UID={item.ItemUid} -> ItemId={item.ItemId} x{item.Quantity} | stats: {stats}");
    }

    public static void WriteWorkshopSlot(WorkshopSlotObservation slot)
    {
        string action = slot.Delta >= 0 ? "ajout" : "retrait";
        Console.WriteLine($"[WORKSHOP] {action} UID={slot.ItemUid} delta={slot.Delta}");
    }

    public static void WritePurchase(
        PurchaseRequestObservation request,
        PurchaseOfferObservation offer,
        ItemDetailObservation item)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"[PURCHASE] ItemId={item.ItemId} UID={item.ItemUid} x{item.Quantity} " +
            $"price={request.Price:N0} K listing={request.ListingId}");
        Console.WriteLine($"  Stats : {FormatStats(item.Stats)}");
        Console.WriteLine();
    }

    public static void WriteSmithmagicStack(SmithmagicStackObservation stack)
    {
        Console.WriteLine(
            $"[FM-RUNE-STACK] UID={stack.Stack.ItemUid} ItemId={stack.Stack.ItemId} " +
            $"restant={stack.Stack.Quantity}");
    }

    public static void WriteSmithmagic(
        SmithmagicRequestObservation? request,
        ItemDetailObservation? rune,
        ItemDetailObservation? before,
        SmithmagicResultObservation result)
    {
        Console.WriteLine();

        string runeText = rune is not null
            ? $"ItemId={rune.ItemId} UID={rune.ItemUid}"
            : request is not null
                ? $"UID={request.RuneUid}"
                : "?";

        ulong quantity = request?.Quantity ?? 1;
        string status = result.ResultCode switch
        {
            2 => "PASS",
            1 => "FAIL",
            _ => $"CODE-{result.ResultCode}"
        };

        List<string>? deltas = before is null
            ? null
            : BuildStatDeltas(before.Stats, result.Item.Stats);

        HashSet<ulong> runeEffects = rune is null
            ? new HashSet<ulong>()
            : rune.Stats.Select(x => x.EffectId).ToHashSet();

        bool collateralLoss = before is not null &&
            HasCollateralLoss(before.Stats, result.Item.Stats, runeEffects);

        Console.WriteLine(
            $"[FM] {status} | Target ItemId={result.Item.ItemId} UID={result.Item.ItemUid} | " +
            $"Rune {runeText} x{quantity} | rawCode={result.ResultCode}");

        Console.WriteLine($"  Avant : {(before is null ? "?" : FormatStats(before.Stats))}");
        Console.WriteLine($"  Après : {FormatStats(result.Item.Stats)}");

        if (deltas is not null)
        {
            Console.WriteLine(
                $"  Delta : {(deltas.Count == 0 ? "aucun changement" : string.Join(", ", deltas))}");
            Console.WriteLine($"  Perte collatérale : {(collateralLoss ? "oui" : "non")}");
        }

        Console.WriteLine();
    }


    private static string FormatStats(IReadOnlyList<ItemStatObservation> stats)
        => stats.Count == 0
            ? "(aucune stat)"
            : string.Join(", ", stats
                .OrderBy(x => x.EffectId)
                .Select(x => $"{x.EffectId}={x.Value}"));

    private static bool HasCollateralLoss(
        IReadOnlyList<ItemStatObservation> before,
        IReadOnlyList<ItemStatObservation> after,
        IReadOnlySet<ulong> runeEffects)
    {
        Dictionary<ulong, long> b = before.ToDictionary(x => x.EffectId, x => x.Value);
        Dictionary<ulong, long> a = after.ToDictionary(x => x.EffectId, x => x.Value);

        foreach (ulong effectId in b.Keys.Union(a.Keys))
        {
            b.TryGetValue(effectId, out long oldValue);
            a.TryGetValue(effectId, out long newValue);

            if (newValue < oldValue && !runeEffects.Contains(effectId))
                return true;
        }

        return false;
    }

    private static List<string> BuildStatDeltas(
        IReadOnlyList<ItemStatObservation> before,
        IReadOnlyList<ItemStatObservation> after)
    {
        Dictionary<ulong, long> b = before.ToDictionary(x => x.EffectId, x => x.Value);
        Dictionary<ulong, long> a = after.ToDictionary(x => x.EffectId, x => x.Value);

        return b.Keys
            .Union(a.Keys)
            .OrderBy(x => x)
            .Select(effectId =>
            {
                b.TryGetValue(effectId, out long oldValue);
                a.TryGetValue(effectId, out long newValue);
                long delta = newValue - oldValue;
                return (effectId, oldValue, newValue, delta);
            })
            .Where(x => x.delta != 0)
            .Select(x => $"{x.effectId}: {x.oldValue}->{x.newValue} ({x.delta:+#;-#;0})")
            .ToList();
    }

    public static void WriteCrush(
        CrushObservation crush,
        IReadOnlyDictionary<ulong, ItemDetailObservation>? itemDetails = null)
    {
        Console.WriteLine();
        Console.WriteLine($"[CRUSH] {crush.Lines.Count} objet(s) détruit(s)");

        Dictionary<ulong, ulong> totals = new();
        int index = 1;

        foreach (CrushLineObservation line in crush.Lines)
        {
            string runeText = line.Runes.Count == 0
                ? "(aucune rune)"
                : string.Join(", ", line.Runes.Select(r => $"{r.RuneItemId} x{r.Quantity}"));

            string second = line.SecondaryPercent is float secondary
                ? $" | f4={secondary:F5}%"
                : string.Empty;

            string itemText = itemDetails is not null &&
                              itemDetails.TryGetValue(line.ItemUid, out ItemDetailObservation? item)
                ? $"ItemId={item.ItemId} x{item.Quantity}"
                : "ItemId=?";

            Console.WriteLine(
                $"  #{index++,2} {itemText} UID={line.ItemUid}  coef={line.CoefficientPercent:F5}%{second}  -> {runeText}");

            foreach (RuneDrop rune in line.Runes)
            {
                totals.TryGetValue(rune.RuneItemId, out ulong current);
                totals[rune.RuneItemId] = current + rune.Quantity;
            }
        }

        if (crush.Lines.Count > 0)
        {
            float min = crush.Lines.Min(x => x.CoefficientPercent);
            float max = crush.Lines.Max(x => x.CoefficientPercent);
            float avg = crush.Lines.Average(x => x.CoefficientPercent);

            Console.WriteLine($"  Coefficient : min {min:F5}% | moy {avg:F5}% | max {max:F5}%");
        }

        if (itemDetails is not null)
        {
            Dictionary<ulong, ulong> itemTotals = new();
            foreach (CrushLineObservation line in crush.Lines)
            {
                if (!itemDetails.TryGetValue(line.ItemUid, out ItemDetailObservation? item))
                    continue;

                itemTotals.TryGetValue(item.ItemId, out ulong current);
                itemTotals[item.ItemId] = current + item.Quantity;
            }

            if (itemTotals.Count > 0)
            {
                Console.WriteLine("  Objets détruits :");
                foreach ((ulong itemId, ulong quantity) in itemTotals.OrderBy(x => x.Key))
                    Console.WriteLine($"    ItemId={itemId} x{quantity}");
            }
        }

        Console.WriteLine("  Totaux runes :");
        if (totals.Count == 0)
        {
            Console.WriteLine("    (aucune)");
        }
        else
        {
            foreach ((ulong runeId, ulong quantity) in totals.OrderBy(x => x.Key))
                Console.WriteLine($"    {runeId} x{quantity}");
        }

        Console.WriteLine();
    }

    public static void WriteProtoDebug(string label, byte[] body)
    {
        Console.WriteLine($"[{label} DEBUG] hex={Convert.ToHexString(body)}");
        WriteProtoFields(body, 0);
    }

    private static void WriteProtoFields(byte[] bytes, int depth)
    {
        if (depth > 4)
            return;

        List<ProtoField>? fields = ProtoWire.ReadFields(bytes);
        if (fields is null)
        {
            Console.WriteLine($"{new string(' ', depth * 2)}(protobuf illisible)");
            return;
        }

        string indent = new string(' ', depth * 2);

        foreach (ProtoField field in fields)
        {
            switch (field.WireType)
            {
                case ProtoWireType.Varint:
                    Console.WriteLine($"{indent}f{field.Number} varint={field.Varint}");
                    break;

                case ProtoWireType.Fixed32:
                    Console.WriteLine($"{indent}f{field.Number} f32/raw=0x{field.Fixed32:X8} float={ProtoWire.ToFloat(field.Fixed32):G9}");
                    break;

                case ProtoWireType.Fixed64:
                    Console.WriteLine($"{indent}f{field.Number} fixed64=0x{field.Fixed64:X16}");
                    break;

                case ProtoWireType.LengthDelimited when field.Bytes is not null:
                    List<ProtoField>? nested = ProtoWire.ReadFields(field.Bytes);
                    if (nested is not null && nested.Count > 0)
                    {
                        Console.WriteLine($"{indent}f{field.Number} message[{field.Bytes.Length}]");
                        WriteProtoFields(field.Bytes, depth + 1);
                    }
                    else
                    {
                        List<ulong>? packed = ProtoWire.ReadPackedVarints(field.Bytes);
                        string values = packed is null ? "?" : string.Join(", ", packed);
                        Console.WriteLine($"{indent}f{field.Number} bytes[{field.Bytes.Length}] packed=[{values}]");
                    }
                    break;
            }
        }
    }
}
