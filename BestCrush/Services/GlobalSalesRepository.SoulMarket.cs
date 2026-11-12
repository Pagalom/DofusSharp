using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed partial class GlobalSalesRepository
{
    public bool SaveSoulMarket(string server, SoulMarketObservation market, DateTimeOffset observedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);
        if (market.ItemId <= 0 || market.Offers.Count is 0 or > 128 ||
            market.Offers.Any(x => x.BossId <= 0 || x.GroupId <= 0 ||
                x.Prices.Count != 4 || x.Prices.All(p => p == 0) || x.Prices.Any(p => p > long.MaxValue)) ||
            market.Offers.Select(x => x.BossId).Distinct().Count() != 1)
            throw new ArgumentException("Offres de capture invalides.", nameof(market));
        string name = server.Trim().ToUpperInvariant();
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using var check = db.CreateCommand(); check.Transaction = transaction;
        check.CommandText = "SELECT MAX(observed_at) FROM soul_market_prices WHERE server=$s AND item_id=$item";
        check.Parameters.AddWithValue("$s", name); check.Parameters.AddWithValue("$item", market.ItemId);
        if (check.ExecuteScalar() is string previous && Parse(previous) >= observedAtUtc) return false;
        using var clear = db.CreateCommand(); clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM soul_market_prices WHERE server=$s AND item_id=$item";
        clear.Parameters.AddWithValue("$s", name); clear.Parameters.AddWithValue("$item", market.ItemId); clear.ExecuteNonQuery();
        int[] quantities = [1, 10, 100, 1000];
        foreach (var group in market.Offers.SelectMany(offer => offer.Prices.Select((price, index) =>
                     new { Offer = offer, Price = price, Quantity = quantities[index] })).Where(x => x.Price > 0)
                     .GroupBy(x => (x.Offer.BossId, x.Quantity)))
        {
            var best = group.MinBy(x => x.Price)!;
            using var insert = db.CreateCommand(); insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO soul_market_prices VALUES($s,$item,$boss,$quantity,$price,$group,$at)";
            insert.Parameters.AddWithValue("$s", name); insert.Parameters.AddWithValue("$item", market.ItemId);
            insert.Parameters.AddWithValue("$boss", best.Offer.BossId);
            insert.Parameters.AddWithValue("$quantity", best.Quantity); insert.Parameters.AddWithValue("$price", (long)best.Price);
            insert.Parameters.AddWithValue("$group", best.Offer.GroupId); insert.Parameters.AddWithValue("$at", Format(observedAtUtc));
            insert.ExecuteNonQuery();
        }
        transaction.Commit(); Changed?.Invoke(name); return true;
    }

    internal BossSoulItem? ReadCapturedSoulItem(string server, long itemId)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT boss_id,group_id FROM soul_market_prices WHERE server=$s AND item_id=$item LIMIT 2
            """;
        command.Parameters.AddWithValue("$s", server.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$item", itemId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var item = new BossSoulItem(itemId, reader.GetInt64(0), reader.GetInt64(1));
        return reader.Read() ? null : item;
    }

    private IReadOnlyList<SoulCaptureQuote> ReadAutomaticSoulQuotes(string server)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = """
            SELECT boss_id,item_id,lot_quantity,lot_price,observed_at FROM soul_market_prices
            WHERE server=$s
            ORDER BY lot_quantity ASC,CAST(lot_price AS REAL)/lot_quantity ASC,observed_at DESC
            """;
        command.Parameters.AddWithValue("$s", server.Trim().ToUpperInvariant());
        using var reader = command.ExecuteReader(); List<SoulCaptureQuote> result = [];
        while (reader.Read()) result.Add(new(server, reader.GetInt64(0),
            (double)reader.GetInt64(3) / reader.GetInt32(2), Parse(reader.GetString(4)))
            { IsAutomatic = true, ItemId = reader.GetInt64(1), LotQuantity = reader.GetInt32(2), LotPrice = reader.GetInt64(3) });
        // Lowest single-capture price per boss; a larger lot remains a labelled fallback.
        return result.DistinctBy(x => x.BossId).ToArray();
    }
}
