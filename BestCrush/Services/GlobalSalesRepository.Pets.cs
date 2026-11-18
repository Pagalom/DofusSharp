using System.Text.Json;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed record PetMarketReading(PetMarketSnapshot Snapshot, DateTimeOffset ObservedAtUtc);

public sealed partial class GlobalSalesRepository
{
    internal event Action<string>? PetPricesChanged;
    internal void SavePetPrices(string server, PetMarketSnapshot snapshot, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(server) || snapshot.ItemId <= 0 || snapshot.Offers.Count == 0 ||
            snapshot.Offers.Any(offer => offer.Level is < 0 or > 100 || offer.Experience < 0 || offer.Price <= 0)) return;
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO pet_market_snapshots(server,item_id,observed_at,snapshot_json) VALUES($server,$id,$at,$json)
            ON CONFLICT(server,item_id) DO UPDATE SET observed_at=excluded.observed_at,snapshot_json=excluded.snapshot_json
            WHERE excluded.observed_at > observed_at;
            """;
        command.Parameters.AddWithValue("$server", server.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$id", snapshot.ItemId);
        command.Parameters.AddWithValue("$at", Format(at));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(snapshot));
        if (command.ExecuteNonQuery() > 0) PetPricesChanged?.Invoke(server.Trim().ToUpperInvariant());
    }

    internal IReadOnlyList<PetMarketReading> ReadPetPrices(string server, DateTimeOffset through)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT observed_at,snapshot_json FROM pet_market_snapshots WHERE server=$server AND observed_at <= $through";
        command.Parameters.AddWithValue("$server", server.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$through", Format(through));
        using var reader = command.ExecuteReader();
        var result = new List<PetMarketReading>();
        while (reader.Read())
        {
            try
            {
                if (JsonSerializer.Deserialize<PetMarketSnapshot>(reader.GetString(1)) is { } snapshot)
                    result.Add(new(snapshot, Parse(reader.GetString(0))));
            }
            catch (JsonException) { }
        }
        return result;
    }
}
