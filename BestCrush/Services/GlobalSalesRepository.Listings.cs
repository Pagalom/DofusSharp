using System.Globalization;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed partial class GlobalSalesRepository
{
    public event Action<string>? ListingsChanged;

    internal bool SaveProductionSale(string server, string scope, ProductionSale sale)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO production_market_sales
            SELECT server,scope,$sale,listing_uid,$at FROM production_market_listings
            WHERE server=$server AND scope=$scope AND listing_uid=$listing AND observed_at<=$at
            """;
        command.Parameters.AddWithValue("$server", server.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$scope", scope); command.Parameters.AddWithValue("$sale", sale.Id);
        command.Parameters.AddWithValue("$listing", sale.ListingId); command.Parameters.AddWithValue("$at", Format(sale.At));
        if (command.ExecuteNonQuery() == 0) return false;
        ListingsChanged?.Invoke(server); return true;
    }

    internal IReadOnlyList<ProductionSale> ReadProductionSales(string server, string scope, DateTimeOffset now)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "SELECT sale_uid,listing_uid,sold_at FROM production_market_sales WHERE server=$s AND scope=$scope AND sold_at<=$at ORDER BY sold_at";
        command.Parameters.AddWithValue("$s", server.Trim().ToUpperInvariant()); command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$at", Format(now)); using var reader = command.ExecuteReader();
        var sales = new List<ProductionSale>();
        while (reader.Read()) sales.Add(new(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture)));
        return sales;
    }

    internal bool SaveListing(string serverName, MarketListingCreatedObservation listing, DateTimeOffset observedAtUtc,
        ProductionListingEvidence? evidence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        if (listing.MarketListingUid == 0 || listing.ItemId is 0 or > long.MaxValue ||
            listing.Quantity is 0 or > int.MaxValue || listing.Price is 0 or > long.MaxValue)
            return false;

        string server = serverName.Trim().ToUpperInvariant();
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO personal_market_listings(server,listing_uid,item_id,quantity,observed_at)
            VALUES($s,$uid,$id,$quantity,$at)
            ON CONFLICT(server,listing_uid) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$s", server);
        command.Parameters.AddWithValue("$uid", listing.MarketListingUid.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$id", (long)listing.ItemId);
        command.Parameters.AddWithValue("$quantity", (int)listing.Quantity);
        command.Parameters.AddWithValue("$at", Format(observedAtUtc));
        if (command.ExecuteNonQuery() == 0)
            return false;
        if (evidence is not null && evidence.ItemId == (long)listing.ItemId && listing.Quantity == 1)
        {
            using var detail = db.CreateCommand();
            detail.Transaction = transaction;
            detail.CommandText = "INSERT OR IGNORE INTO production_market_listings(server,listing_uid,scope,item_id,instance_uid,level,variant,fertile,observed_at,price) VALUES($s,$uid,$scope,$id,$instance,$level,$variant,$fertile,$at,$price)";
            detail.Parameters.AddWithValue("$s", server);
            detail.Parameters.AddWithValue("$uid", listing.MarketListingUid.ToString(CultureInfo.InvariantCulture));
            detail.Parameters.AddWithValue("$scope", evidence.Scope);
            detail.Parameters.AddWithValue("$id", evidence.ItemId);
            detail.Parameters.AddWithValue("$instance", evidence.InstanceUid.ToString(CultureInfo.InvariantCulture));
            detail.Parameters.AddWithValue("$level", evidence.Level);
            detail.Parameters.AddWithValue("$variant", evidence.Variant);
            detail.Parameters.AddWithValue("$fertile", evidence.Fertile is { } fertile ? (object)(fertile ? 1 : 0) : DBNull.Value);
            detail.Parameters.AddWithValue("$at", Format(observedAtUtc));
            detail.Parameters.AddWithValue("$price", (long)listing.Price);
            detail.ExecuteNonQuery();
        }
        transaction.Commit();
        ListingsChanged?.Invoke(server);
        return true;
    }

    internal IReadOnlyList<ProductionListing> ReadProductionListings(string server, string scope, DateTimeOffset now)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "SELECT listing_uid,item_id,instance_uid,level,variant,fertile,observed_at,price FROM production_market_listings WHERE server=$s AND scope=$scope AND observed_at<=$now ORDER BY observed_at,listing_uid";
        command.Parameters.AddWithValue("$s", server.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$scope", scope); command.Parameters.AddWithValue("$now", Format(now));
        using var reader = command.ExecuteReader(); var rows = new List<ProductionListing>();
        while (reader.Read()) rows.Add(new(reader.GetString(0), scope, reader.GetInt64(1),
            ulong.Parse(reader.GetString(2), CultureInfo.InvariantCulture), reader.GetInt32(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5) != 0,
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture)) { Price = reader.GetInt64(7) });
        return rows;
    }

    internal PersonalListingTotals ReadListingTotals(string serverName, DateTimeOffset now, int days = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            WITH recent AS (
                SELECT item_id, SUM(quantity) AS units FROM personal_market_listings
                WHERE server=$s AND observed_at>$start AND observed_at<=$end GROUP BY item_id
            ), identities AS (
                SELECT item_id,boss_id FROM soul_market_prices WHERE server=$s
                UNION
                SELECT item_id,CAST(substr(variant,6) AS INTEGER) FROM global_sales_histories
                WHERE server=$s AND substr(variant,1,5)='boss:'
                    AND substr(variant,6)<>'' AND substr(variant,6) NOT GLOB '*[^0-9]*'
            ), bosses AS (
                SELECT item_id,MIN(boss_id) AS boss_id FROM identities
                GROUP BY item_id HAVING MIN(boss_id)=MAX(boss_id) AND MIN(boss_id)>0
            )
            SELECT recent.item_id,bosses.boss_id,recent.units FROM recent LEFT JOIN bosses USING(item_id);
            """;
        command.Parameters.AddWithValue("$s", serverName.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$start", Format(now.AddDays(-days)));
        command.Parameters.AddWithValue("$end", Format(now));
        using var reader = command.ExecuteReader();
        Dictionary<long, long> items = [], bosses = [];
        while (reader.Read())
        {
            long units = reader.GetInt64(2);
            items.Add(reader.GetInt64(0), units);
            if (!reader.IsDBNull(1))
            {
                long boss = reader.GetInt64(1);
                bosses[boss] = bosses.GetValueOrDefault(boss) + units;
            }
        }
        return new(items, bosses);
    }
}

internal sealed record PersonalListingTotals(
    IReadOnlyDictionary<long, long> ByItem, IReadOnlyDictionary<long, long> ByBoss)
{
    internal long For(ActivityRecommendation row) => row.BossId is { } boss
        ? ByBoss.GetValueOrDefault(boss)
        : ByItem.GetValueOrDefault(row.ItemId);
}
