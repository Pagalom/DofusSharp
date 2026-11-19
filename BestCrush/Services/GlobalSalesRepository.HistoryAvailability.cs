using System.Text.Json;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed record MarketHistoryAvailability(DateTimeOffset ObservedAtUtc, IReadOnlySet<long> WithMeanPrice)
{
    // A timer cannot make a disabled history button usable. Retain the last known absence
    // until a newer mean-price snapshot or a successful detailed history contradicts it.
    internal bool IsUnavailable(long itemId, DateTimeOffset? latestHistory, DateTimeOffset now) =>
        itemId > 0 && ObservedAtUtc <= now && !WithMeanPrice.Contains(itemId) &&
        !(latestHistory is { } history && history <= now && history >= ObservedAtUtc);
}

public sealed partial class GlobalSalesRepository
{
    internal event Action<string>? HistoryAvailabilityChanged;

    internal bool SaveHistoryAvailability(string server, MarketMeanPriceSnapshot snapshot, DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);
        if (snapshot.EntryCount <= 0 || snapshot.EntryCount > 100_000 || snapshot.WithMeanPrice.Count > snapshot.EntryCount ||
            snapshot.WithMeanPrice.Any(id => id <= 0)) throw new ArgumentException("Invalid mean-price snapshot.");
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO market_history_availability(server,observed_at,available_ids) VALUES($s,$at,$ids)
            ON CONFLICT(server) DO UPDATE SET observed_at=$at,available_ids=$ids
            WHERE excluded.observed_at > market_history_availability.observed_at;
            """;
        command.Parameters.AddWithValue("$s", server.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$at", Format(observedAt));
        command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(snapshot.WithMeanPrice.Order().ToArray()));
        bool changed = command.ExecuteNonQuery() > 0;
        if (changed) HistoryAvailabilityChanged?.Invoke(server);
        return changed;
    }

    internal MarketHistoryAvailability? ReadHistoryAvailability(string server, DateTimeOffset now)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "SELECT observed_at,available_ids FROM market_history_availability WHERE server=$s AND observed_at<=$at;";
        command.Parameters.AddWithValue("$s", server.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$at", Format(now));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var observed = Parse(reader.GetString(0));
        try
        {
            var ids = JsonSerializer.Deserialize<long[]>(reader.GetString(1));
            return ids is not null && ids.Length <= 100_000 && ids.All(id => id > 0)
                ? new(observed, ids.ToHashSet()) : null;
        }
        catch (JsonException) { return null; }
    }
}
