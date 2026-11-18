using BestCrush.Network.Protocol;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

public sealed record BreedingLevelPrice(long ItemId, int Level, long Price, bool Fertile, DateTimeOffset ObservedAtUtc,
    long? ThirdQuartile = null, int OfferCount = 0)
{
    internal long SuggestedPrice => Math.Max(1, Price - 1);
}

public sealed partial class GlobalSalesRepository
{
    internal event Action<string>? BreedingPricesChanged;

    internal void SaveBreedingPrices(string server, BreedingMarketLevels snapshot, DateTimeOffset observedAt)
    {
        string normalizedServer = server.Trim().ToUpperInvariant();
        using var db = Open();
        using var transaction = db.BeginTransaction();
        bool changed = false;
        DateTimeOffset? latest = null;
        using (var command = db.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT MAX(observed_at) FROM breeding_market_levels WHERE server=$server AND item_id=$item;";
            command.Parameters.AddWithValue("$server", normalizedServer);
            command.Parameters.AddWithValue("$item", snapshot.ItemId);
            if (command.ExecuteScalar() is string date) latest = Parse(date);
        }
        if (latest > observedAt) return;

        foreach (var level in snapshot.Offers.GroupBy(offer => (offer.Level, offer.Fertile)))
        {
            using var command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO breeding_market_levels(server,item_id,level,price,fertile,observed_at)
                VALUES($server,$item,$level,$price,$fertile,$at)
                ON CONFLICT(server,item_id,level,fertile) DO UPDATE SET price=excluded.price, observed_at=excluded.observed_at
                WHERE excluded.observed_at > observed_at
                   OR (excluded.observed_at = observed_at AND excluded.price < price);
                """;
            command.Parameters.AddWithValue("$server", normalizedServer);
            command.Parameters.AddWithValue("$item", snapshot.ItemId);
            command.Parameters.AddWithValue("$level", level.Key.Level);
            command.Parameters.AddWithValue("$fertile", level.Key.Fertile);
            command.Parameters.AddWithValue("$price", level.Min(offer => offer.Price));
            command.Parameters.AddWithValue("$at", Format(observedAt));
            changed |= command.ExecuteNonQuery() > 0;

            command.CommandText = """
                INSERT INTO breeding_market_summary(server,item_id,level,fertile,quartile,offer_count)
                VALUES($server,$item,$level,$fertile,$quartile,$count)
                ON CONFLICT(server,item_id,level,fertile) DO UPDATE SET quartile=excluded.quartile, offer_count=excluded.offer_count
                WHERE $at > $previous OR excluded.offer_count > offer_count;
                """;
            double estimate = MarketAnalyticsCalculator.Aggregate(
                level.Select(offer => (double)offer.Price), MarketAnalyticsAggregation.ThirdQuartile);
            long quartile = estimate >= long.MaxValue ? long.MaxValue : (long)Math.Round(estimate);
            command.Parameters.AddWithValue("$quartile", quartile);
            command.Parameters.AddWithValue("$count", level.Count());
            command.Parameters.AddWithValue("$previous", latest is { } previous ? Format(previous) : "");
            changed |= command.ExecuteNonQuery() > 0;
        }
        if (changed && latest < observedAt)
        {
            using var command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM breeding_market_summary WHERE server=$server AND item_id=$item AND EXISTS (
                    SELECT 1 FROM breeding_market_levels l WHERE l.server=$server AND l.item_id=$item
                        AND l.level=breeding_market_summary.level AND l.fertile=breeding_market_summary.fertile
                        AND l.observed_at < $at);
                """;
            command.Parameters.AddWithValue("$server", normalizedServer);
            command.Parameters.AddWithValue("$item", snapshot.ItemId);
            command.Parameters.AddWithValue("$at", Format(observedAt));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        if (changed)
            BreedingPricesChanged?.Invoke(normalizedServer);
    }

    internal IReadOnlyList<BreedingLevelPrice> ReadBreedingPrices(string server, DateTimeOffset from, DateTimeOffset through)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT l.item_id,l.level,l.price,l.fertile,l.observed_at,s.quartile,COALESCE(s.offer_count,0)
            FROM breeding_market_levels l LEFT JOIN breeding_market_summary s
                ON s.server=l.server AND s.item_id=l.item_id AND s.level=l.level AND s.fertile=l.fertile
            WHERE l.server=$server AND l.observed_at >= $from AND l.observed_at <= $through;
            """;
        command.Parameters.AddWithValue("$server", server.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$from", Format(from));
        command.Parameters.AddWithValue("$through", Format(through));
        using var reader = command.ExecuteReader();
        var result = new List<BreedingLevelPrice>();
        while (reader.Read()) result.Add(new(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2),
            reader.GetBoolean(3), Parse(reader.GetString(4)), reader.IsDBNull(5) ? null : reader.GetInt64(5), reader.GetInt32(6)));
        return result;
    }
}
