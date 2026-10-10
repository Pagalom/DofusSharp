using BestCrush.Network.Protocol;

namespace BestCrush.NetworkProbe.Protocol;

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
        ItemDetailObservation item,
        PurchaseReceiptObservation? receipt = null)
    {
        ulong offerId = receipt?.OfferId ?? request.OfferId;
        ulong quantity = request.Quantity;

        Console.WriteLine();
        Console.WriteLine(
            $"[PURCHASE] ItemId={item.ItemId} UID={item.ItemUid} x{quantity} " +
            $"price={request.Price:N0} K offer={offerId}");
        Console.WriteLine($"  Stats : {FormatStats(item.Stats)}");
        Console.WriteLine();
    }


    public static void WriteMarketListing(
        MarketListingRequestObservation request,
        MarketListingCreatedObservation created,
        ItemDetailObservation? knownItem)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"[LISTING] ItemId={created.ItemId} UID={request.ItemUid} x{request.Quantity} " +
            $"price={request.Price:N0} K marketListingUid={created.MarketListingUid}");

        IReadOnlyList<ItemStatObservation> stats =
            created.Stats.Count > 0
                ? created.Stats
                : knownItem?.Stats ?? Array.Empty<ItemStatObservation>();

        Console.WriteLine($"  Stats : {FormatStats(stats)}");
        Console.WriteLine();
    }

    public static void WriteCraftRequest(CraftRequestObservation request)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"[CRAFT-REQUEST] x{request.Quantity} slots={request.SlotCount}");

        foreach (ItemDetailObservation ingredient in request.IngredientSnapshots)
        {
            Console.WriteLine(
                $"  Ingredient snapshot : ItemId={ingredient.ItemId} UID={ingredient.ItemUid} " +
                $"stack={ingredient.Quantity}");
        }

        Console.WriteLine();
    }

    public static void WriteCraftResult(
        CraftRequestObservation request,
        SmithmagicResultObservation result)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"[CRAFT] PASS | ItemId={result.Item.ItemId} UID={result.Item.ItemUid} x{result.Item.Quantity} " +
            $"rawCode={result.ResultCode}");
        Console.WriteLine($"  Stats : {FormatStats(result.Item.Stats)}");
        Console.WriteLine(
            $"  Ingrédients : {request.IngredientSnapshots.Count} pile(s) sélectionnée(s) par la recette");
        Console.WriteLine();
    }

    public static void WriteMarketListingReturn(
        ulong marketListingUid,
        MarketListingRequestObservation original,
        MarketListingCreatedObservation created,
        ItemDetailObservation returnedItem)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"[LISTING-RETURN] marketListingUid={marketListingUid} " +
            $"ItemId={returnedItem.ItemId} oldUID={original.ItemUid} newUID={returnedItem.ItemUid} " +
            $"price={original.Price:N0} K");

        Console.WriteLine(
            $"  UID : {(original.ItemUid == returnedItem.ItemUid ? "conservé" : "remplacé")}");

        Console.WriteLine($"  Stats : {FormatStats(returnedItem.Stats)}");
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
