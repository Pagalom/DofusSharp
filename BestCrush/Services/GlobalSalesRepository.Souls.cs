using BestCrush.Domain.Models;

namespace BestCrush.Services;

public sealed partial class GlobalSalesRepository
{
    public void SaveSoulQuote(SoulCaptureQuote quote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quote.Server);
        if (quote.BossId <= 0 || !double.IsFinite(quote.Price) || quote.Price <= 0)
            throw new ArgumentOutOfRangeException(nameof(quote));
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO soul_capture_quotes VALUES($s,$boss,$price,$at)
            ON CONFLICT(server,boss_id) DO UPDATE SET price=$price,observed_at=$at
            WHERE excluded.observed_at > soul_capture_quotes.observed_at;
            """;
        command.Parameters.AddWithValue("$s", quote.Server.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$boss", quote.BossId);
        command.Parameters.AddWithValue("$price", quote.Price);
        command.Parameters.AddWithValue("$at", Format(quote.ObservedAtUtc));
        if (command.ExecuteNonQuery() > 0) Changed?.Invoke(quote.Server.Trim().ToUpperInvariant());
    }

    public IReadOnlyList<SoulCaptureQuote> ReadSoulQuotes(string server)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT boss_id,price,observed_at FROM soul_capture_quotes WHERE server=$s";
        command.Parameters.AddWithValue("$s", server.Trim().ToUpperInvariant());
        using var reader = command.ExecuteReader();
        List<SoulCaptureQuote> quotes = [];
        while (reader.Read()) quotes.Add(new(server, reader.GetInt64(0), reader.GetDouble(1), Parse(reader.GetString(2))));
        return quotes.Concat(ReadAutomaticSoulQuotes(server)).OrderByDescending(x => x.ObservedAtUtc).DistinctBy(x => x.BossId).ToArray();
    }
}
