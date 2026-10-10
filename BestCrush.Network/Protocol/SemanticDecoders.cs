using System.Text.Json;

namespace BestCrush.Network.Protocol;

public sealed record ProtocolMap(
    string ClientBuild,
    string? PriceList,
    string? CrushResult,
    string? ItemDetail,
    string? WorkshopSlotPut,
    string? PurchaseRequest,
    string? PurchaseOffer,
    string? PurchaseReceipt,
    string? InventoryAdd,
    string? InventoryQuantity,
    string? InventoryRemove,
    string? CraftPrepare,
    string? CraftOutput,
    string? SmithmagicRequest,
    string? SmithmagicBatchRequest,
    string? SmithmagicResult,
    string? SmithmagicAux,
    string? MarketListingRequest,
    string? MarketListingCreated,
    IReadOnlySet<string> DiagnosticMessages,
    string? MarketSelectionResponse = null)
{
    public ProtocolWireNormalizer WireNormalizer { get; init; } =
        ProtocolWireNormalizer.Empty;

    public byte[] NormalizeBody(string key, byte[] body) =>
        WireNormalizer.Normalize(key, body);

    private string? ResolveKey(string semantic) => semantic switch
    {
        "price_list" => PriceList,
        "market_selection_response" => MarketSelectionResponse,
        "crush_result" => CrushResult,
        "item_detail" => ItemDetail,
        "workshop_slot_put" => WorkshopSlotPut,
        "purchase_request" => PurchaseRequest,
        "purchase_offer" => PurchaseOffer,
        "purchase_receipt" => PurchaseReceipt,
        "inventory_add" => InventoryAdd,
        "inventory_quantity" => InventoryQuantity,
        "inventory_remove" => InventoryRemove,
        "craft_prepare" => CraftPrepare,
        "craft_output" => CraftOutput,
        "smithmagic_request" => SmithmagicRequest,
        "smithmagic_batch_request" => SmithmagicBatchRequest,
        "smithmagic_result" => SmithmagicResult,
        "smithmagic_aux" => SmithmagicAux,
        "market_listing_request" => MarketListingRequest,
        "market_listing_created" => MarketListingCreated,
        _ => null
    };

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
                "kbd",
                "isa",
                "isf",
                "irz",
                "kah",
                "iuq",
                "jze",
                "kcs",
                "kbu",
                "kar",
                "kcr",
                "kda",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "itt","log","kbd","iut","irl","isf","keb","kcw","kau","kef","irz"
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
        string? purchaseReceipt = root.TryGetProperty("purchase_receipt", out JsonElement rc) ? rc.GetString() : null;
        string? inventoryAdd = root.TryGetProperty("inventory_add", out JsonElement ia) ? ia.GetString() : null;
        string? inventoryQuantity = root.TryGetProperty("inventory_quantity", out JsonElement iq) ? iq.GetString() : null;
        string? inventoryRemove = root.TryGetProperty("inventory_remove", out JsonElement irm) ? irm.GetString() : null;
        string? craftPrepare = root.TryGetProperty("craft_prepare", out JsonElement cp) ? cp.GetString() : null;
        string? craftOutput = root.TryGetProperty("craft_output", out JsonElement co) ? co.GetString() : null;
        string? smithmagicRequest = root.TryGetProperty("smithmagic_request", out JsonElement sr) ? sr.GetString() : null;
        string? smithmagicBatchRequest = root.TryGetProperty("smithmagic_batch_request", out JsonElement sbr) ? sbr.GetString() : null;
        string? smithmagicResult = root.TryGetProperty("smithmagic_result", out JsonElement sx) ? sx.GetString() : null;
        string? smithmagicAux = root.TryGetProperty("smithmagic_aux", out JsonElement sa) ? sa.GetString() : null;
        string? marketListingRequest = root.TryGetProperty("market_listing_request", out JsonElement mlr) ? mlr.GetString() : null;
        string? marketListingCreated = root.TryGetProperty("market_listing_created", out JsonElement mlc) ? mlc.GetString() : null;
        string? marketSelectionResponse = root.TryGetProperty("market_selection_response", out JsonElement ms) ? ms.GetString() : null;

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

        ProtocolMap map = new ProtocolMap(
            build,
            price,
            crush,
            itemDetail,
            workshopSlotPut,
            purchaseRequest,
            purchaseOffer,
            purchaseReceipt,
            inventoryAdd,
            inventoryQuantity,
            inventoryRemove,
            craftPrepare,
            craftOutput,
            smithmagicRequest,
            smithmagicBatchRequest,
            smithmagicResult,
            smithmagicAux,
            marketListingRequest,
            marketListingCreated,
            diagnostics,
            marketSelectionResponse);

        return map with
        {
            WireNormalizer = ProtocolWireNormalizer.Read(
                root,
                map.ResolveKey)
        };
    }
}

public sealed record MarketOffer(ulong OfferId, ulong ItemId, IReadOnlyList<ulong> Ladder);
public sealed record MarketObservation(ulong ItemId, IReadOnlyList<MarketOffer> Offers);

public sealed record ItemStatObservation(ulong EffectId, long Value);
public sealed record ItemDetailObservation(
    ulong ItemUid,
    ulong ItemId,
    ulong Quantity,
    IReadOnlyList<ItemStatObservation> Stats);
public sealed record WorkshopSlotObservation(long Delta, ulong ItemUid);
public sealed record InventoryQuantityObservation(ulong ItemUid, ulong NewQuantity);
public sealed record InventoryRemoveObservation(ulong ItemUid);

public sealed record PurchaseRequestObservation(ulong Price, ulong Quantity, ulong OfferId);
public sealed record PurchaseOfferObservation(
    ulong ItemId,
    ulong OfferId,
    IReadOnlyList<ulong> Ladder,
    IReadOnlyList<ItemStatObservation> Stats);
public sealed record PurchaseReceiptObservation(
    ulong Quantity,
    ulong OfferId);
public sealed record SmithmagicRequestObservation(ulong RuneUid, ulong Quantity);
public sealed record SmithmagicBatchRequestObservation(ulong Quantity, ulong Sequence);
public sealed record SmithmagicStackObservation(ItemDetailObservation Stack);
public sealed record SmithmagicResultObservation(int ResultCode, ItemDetailObservation Item);

public sealed record CraftRequestObservation(
    ulong Quantity,
    ulong SlotCount,
    IReadOnlyList<ItemDetailObservation> IngredientSnapshots);

public sealed record MarketListingRequestObservation(
    ulong ItemUid,
    ulong Quantity,
    ulong Price);

public sealed record MarketListingCreatedObservation(
    ulong MarketListingUid,
    ulong ItemId,
    ulong Quantity,
    ulong Price,
    IReadOnlyList<ItemStatObservation> Stats);

public sealed record RuneDrop(ulong RuneItemId, ulong Quantity);
public sealed record CrushLineObservation(
    ulong ItemUid,
    float CoefficientPercent,
    float? SecondaryPercent,
    IReadOnlyList<RuneDrop> Runes);
public sealed record CrushObservation(IReadOnlyList<CrushLineObservation> Lines);

public static class SemanticDecoders
{
    // Observed 2026-10-11: jzs carries the selected HDV item ID in
    // root field 3, and the market/category value in root field 2.
    // This decoder is intentionally for focus only: nested offer prices
    // are NOT compatible with the older jzn x1/x10/x100/x1000 ladder.
    public static ulong? TryDecodeMarketSelectionItemId(byte[] body)
    {
        List<ProtoField>? fields = ProtoWire.ReadFields(body);
        if (fields is null)
            return null;

        ulong category = fields.FirstOrDefault(field =>
            field.Number == 2 &&
            field.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        ulong itemId = fields.FirstOrDefault(field =>
            field.Number == 3 &&
            field.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        return category > 0 && itemId > 0 && itemId <= (ulong)long.MaxValue
            ? itemId
            : null;
    }

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

            ulong offerId = offer.FirstOrDefault(f =>
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

            offers.Add(new MarketOffer(offerId, resolvedItemId, ladder));
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

            ulong offerId = offer.FirstOrDefault(f => f.Number == 1 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
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
                offers.Add(new MarketOffer(offerId, resolvedItemId, ladder));
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
        ulong offerId = fields.FirstOrDefault(f =>
            f.Number == 5 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        return price > 0 && quantity > 0 && offerId > 0
            ? new PurchaseRequestObservation(price, quantity, offerId)
            : null;
    }

    public static PurchaseOfferObservation? TryDecodePurchaseOffer(byte[] body)
    {
        List<ProtoField>? fields = ProtoWire.ReadFields(body);
        if (fields is null)
            return null;

        ulong itemId = fields.FirstOrDefault(f =>
            f.Number == 1 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong offerId = fields.FirstOrDefault(f =>
            f.Number == 2 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        ProtoField? ladderField = fields.FirstOrDefault(f =>
            f.Number == 4 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is not null);

        IReadOnlyList<ulong> ladder =
            ladderField?.Bytes is null
                ? []
                : CleanLadder(
                    ProtoWire.ReadPackedVarints(
                        ladderField.Bytes
                    )
                    ?? []);

        if (itemId == 0 || offerId == 0)
            return null;

        return new PurchaseOfferObservation(
            itemId,
            offerId,
            ladder,
            DecodeStats(fields, 5));
    }

    public static PurchaseReceiptObservation? TryDecodePurchaseReceipt(byte[] body)
    {
        List<ProtoField>? fields = ProtoWire.ReadFields(body);
        if (fields is null)
            return null;

        ulong quantity = fields.FirstOrDefault(f =>
            f.Number == 2 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong offerId = fields.FirstOrDefault(f =>
            f.Number == 3 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        return quantity > 0 && offerId > 0
            ? new PurchaseReceiptObservation(quantity, offerId)
            : null;
    }

    public static ItemDetailObservation? TryDecodeInventoryAdd(byte[] body)
        => TryDecodeItemDetail(body);

    public static InventoryQuantityObservation? TryDecodeInventoryQuantity(byte[] body)
    {
        List<ProtoField>? root = ProtoWire.ReadFields(body);
        if (root is null)
            return null;

        ProtoField? stateField = root.FirstOrDefault(f =>
            f.Number == 3 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is not null);

        if (stateField?.Bytes is null)
            return null;

        List<ProtoField>? state = ProtoWire.ReadFields(stateField.Bytes);
        if (state is null)
            return null;

        ulong quantity = state.FirstOrDefault(f =>
            f.Number == 2 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong itemUid = state.FirstOrDefault(f =>
            f.Number == 3 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        return itemUid > 0
            ? new InventoryQuantityObservation(itemUid, quantity)
            : null;
    }

    public static InventoryRemoveObservation? TryDecodeInventoryRemove(byte[] body)
    {
        List<ProtoField>? fields = ProtoWire.ReadFields(body);
        if (fields is null)
            return null;

        ulong itemUid = fields.FirstOrDefault(f =>
            f.Number == 1 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        return itemUid > 0 ? new InventoryRemoveObservation(itemUid) : null;
    }

    public static ItemDetailObservation? TryDecodeCraftOutput(byte[] body)
    {
        List<ProtoField>? root = ProtoWire.ReadFields(body);
        if (root is null)
            return null;

        ProtoField? envelopeField = root.FirstOrDefault(f =>
            f.Number == 1 &&
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

    public static MarketListingRequestObservation? TryDecodeMarketListingRequest(byte[] body)
    {
        List<ProtoField>? fields = ProtoWire.ReadFields(body);
        if (fields is null)
            return null;

        ulong quantity = fields.FirstOrDefault(f =>
            f.Number == 1 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong price = fields.FirstOrDefault(f =>
            f.Number == 2 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong itemUid = fields.FirstOrDefault(f =>
            f.Number == 3 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        return itemUid > 0 && quantity > 0 && price > 0
            ? new MarketListingRequestObservation(itemUid, quantity, price)
            : null;
    }

    public static MarketListingCreatedObservation? TryDecodeMarketListingCreated(byte[] body)
    {
        List<ProtoField>? root = ProtoWire.ReadFields(body);
        if (root is null)
            return null;

        ProtoField? listingField = root.FirstOrDefault(f =>
            f.Number == 3 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is not null);

        if (listingField?.Bytes is null)
            return null;

        List<ProtoField>? listing = ProtoWire.ReadFields(listingField.Bytes);
        if (listing is null)
            return null;

        ulong marketListingUid = listing.FirstOrDefault(f =>
            f.Number == 1 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong itemId = listing.FirstOrDefault(f =>
            f.Number == 2 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong quantity = listing.FirstOrDefault(f =>
            f.Number == 3 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;
        ulong price = root.FirstOrDefault(f =>
            f.Number == 5 && f.WireType == ProtoWireType.Varint)?.Varint ?? 0;

        if (marketListingUid == 0 || itemId == 0 || quantity == 0 || price == 0)
            return null;

        return new MarketListingCreatedObservation(
            marketListingUid,
            itemId,
            quantity,
            price,
            DecodeStats(listing, 5));
    }

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
