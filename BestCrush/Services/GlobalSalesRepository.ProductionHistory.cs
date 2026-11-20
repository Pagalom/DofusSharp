using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed partial class GlobalSalesRepository
{
    internal int MatchProductionHistory(string server, string scope, IReadOnlyList<PersonalMarketHistoryEntry> history, DateTimeOffset now)
    {
        var listings = ReadProductionListings(server, scope, now);
        var recorded = ReadProductionSales(server, scope, now);
        var returned = ReadProductionReturns(server, scope, now);
        var consumed = recorded.Select(s => s.ListingId).Concat(returned.Select(r => r.ListingId)).ToHashSet();
        var seen = recorded.Select(s => s.Id).Concat(returned.Select(r => r.Id)).ToHashSet();
        int added = 0;
        foreach (var sale in history.Where(s => s.Quantity == 1 && !seen.Contains(s.Id)).OrderBy(s => s.At))
        {
            var candidates = listings.Where(l => !consumed.Contains(l.Id) && l.ItemId == sale.ItemId &&
                l.Price == sale.Price && l.At <= sale.At).ToArray();
            if (candidates.Length == 0) continue;
            var detail = ProductionListingReader.FromStock(scope, new((ulong)sale.ItemId, 0, 1, null, sale.Effects.ToArray(), null));
            if (detail is not null)
                candidates = candidates.Where(l => l.Level == detail.Level && l.Variant == detail.Variant && l.Fertile == detail.Fertile).ToArray();
            // Missing effects cannot disambiguate different levels or powers. Economically identical units use FIFO.
            if (candidates.Length == 0 || candidates.Select(l => (l.Level, l.Variant, l.Fertile)).Distinct().Count() != 1) continue;
            var listing = candidates.OrderBy(l => l.At).ThenBy(l => l.Id).First();
            bool saved = sale.Returned ? SaveProductionReturn(server, scope, new(sale.Id, listing.Id, sale.At))
                : SaveProductionSale(server, scope, new(sale.Id, listing.Id, sale.At));
            if (!saved) continue;
            consumed.Add(listing.Id); seen.Add(sale.Id); added++;
        }
        return added;
    }

    internal bool SaveProductionReturn(string server, string scope, ProductionReturn returned)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO production_market_returns
            SELECT server,scope,$return,listing_uid,$at FROM production_market_listings
            WHERE server=$server AND scope=$scope AND listing_uid=$listing AND observed_at<=$at
            """;
        command.Parameters.AddWithValue("$server", server.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$scope", scope); command.Parameters.AddWithValue("$return", returned.Id);
        command.Parameters.AddWithValue("$listing", returned.ListingId); command.Parameters.AddWithValue("$at", Format(returned.At));
        if (command.ExecuteNonQuery() == 0) return false;
        ListingsChanged?.Invoke(server); return true;
    }

    internal IReadOnlyList<ProductionReturn> ReadProductionReturns(string server, string scope, DateTimeOffset now)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "SELECT return_uid,listing_uid,returned_at FROM production_market_returns WHERE server=$s AND scope=$scope AND returned_at<=$at ORDER BY returned_at";
        command.Parameters.AddWithValue("$s", server.Trim().ToUpperInvariant()); command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$at", Format(now)); using var reader = command.ExecuteReader();
        var rows = new List<ProductionReturn>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1), System.DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture)));
        return rows;
    }
}
