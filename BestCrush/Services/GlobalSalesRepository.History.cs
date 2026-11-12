using System.Text.Json;
using BestCrush.Domain.Models;

namespace BestCrush.Services;

public sealed partial class GlobalSalesRepository
{
    public event Action<string>? Changed;
    public static string BossHistoryVariant(long bossId) => $"boss:{bossId}";

    public bool SaveHistory(GlobalSalesHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Last24Hours.Count + history.Last30Days.Count > 2048 ||
            history.Last24Hours.Concat(history.Last30Days).Any(x => x.UnitsSold < 0 || x.UnitPrice < 0))
            throw new ArgumentException("Historique global invalide.", nameof(history));
        // Validate all summaries (including overflow) before starting a write.
        var periods = new[] { 1, 7, 30 }.Select(history.Period).ToArray();
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO global_sales_histories VALUES($s,$t,$id,$v,$at,$json)
            ON CONFLICT(server,object_type,item_id,variant) DO UPDATE SET observed_at=$at,history_json=$json
            WHERE excluded.observed_at > global_sales_histories.observed_at;
            """;
        BindMarket(command, history.Market);
        command.Parameters.AddWithValue("$at", Format(history.ObservedAtUtc));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(history));
        if (command.ExecuteNonQuery() == 0) { transaction.Rollback(); return false; }
        foreach (var period in periods)
        {
            using var summary = db.CreateCommand();
            summary.Transaction = transaction;
            summary.CommandText = """
                INSERT INTO global_sales_periods VALUES($s,$t,$id,$v,$start,$end,$units,$at)
                ON CONFLICT(server,object_type,item_id,variant,period_start,period_end)
                DO UPDATE SET units_sold=$units,observed_at=$at
                WHERE excluded.observed_at > global_sales_periods.observed_at;
                """;
            BindMarket(summary, period.Market);
            summary.Parameters.AddWithValue("$start", Format(period.PeriodStartUtc));
            summary.Parameters.AddWithValue("$end", Format(period.PeriodEndUtc));
            summary.Parameters.AddWithValue("$units", period.UnitsSold);
            summary.Parameters.AddWithValue("$at", Format(period.ObservedAtUtc));
            summary.ExecuteNonQuery();
        }
        transaction.Commit();
        Changed?.Invoke(history.Market.ServerName);
        return true;
    }

    public IReadOnlyList<GlobalSalesHistory> ReadHistories(string serverName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT history_json FROM global_sales_histories WHERE server=$server";
        command.Parameters.AddWithValue("$server", serverName.Trim().ToUpperInvariant());
        using var reader = command.ExecuteReader();
        List<GlobalSalesHistory> histories = [];
        while (reader.Read())
            if (JsonSerializer.Deserialize<GlobalSalesHistory>(reader.GetString(0)) is { } history) histories.Add(history);
        return histories;
    }
}
