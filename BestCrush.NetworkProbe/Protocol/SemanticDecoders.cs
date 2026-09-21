using System.Text.Json;

namespace BestCrush.NetworkProbe.Protocol;

internal sealed record ProtocolMap(string ClientBuild, string? PriceList, string? CrushResult)
{
    public static ProtocolMap Load(string path)
    {
        if (!File.Exists(path))
            return new ProtocolMap("3.6.11.15", "jzn", "kci");

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        string build = root.TryGetProperty("clientBuild", out JsonElement b)
            ? b.GetString() ?? "unknown"
            : "unknown";

        string? price = root.TryGetProperty("price_list", out JsonElement p) ? p.GetString() : null;
        string? crush = root.TryGetProperty("crush_result", out JsonElement c) ? c.GetString() : null;

        return new ProtocolMap(build, price, crush);
    }
}

internal sealed record MarketOffer(ulong ListingId, ulong ItemId, IReadOnlyList<ulong> Ladder);
internal sealed record MarketObservation(ulong ItemId, IReadOnlyList<MarketOffer> Offers);

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

    public static void WriteCrush(CrushObservation crush)
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

            Console.WriteLine(
                $"  #{index++,2} UID={line.ItemUid}  coef={line.CoefficientPercent:F5}%{second}  -> {runeText}");

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
