using System.Globalization;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed partial class GlobalSalesRepository
{
    public event Action<string>? ListingsChanged;

    internal bool SaveListing(string serverName, MarketListingCreatedObservation listing, DateTimeOffset observedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        if (listing.MarketListingUid == 0 || listing.ItemId is 0 or > long.MaxValue ||
            listing.Quantity is 0 or > int.MaxValue)
            return false;

        string server = serverName.Trim().ToUpperInvariant();
        using var db = Open();
        using var command = db.CreateCommand();
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
        ListingsChanged?.Invoke(server);
        return true;
    }

    internal PersonalListingTotals ReadListingTotals(string serverName, DateTimeOffset now)
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
        command.Parameters.AddWithValue("$start", Format(now.AddHours(-24)));
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
