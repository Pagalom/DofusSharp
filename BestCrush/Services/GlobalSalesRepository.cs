using System.Globalization;
using BestCrush.Domain.Models;
using Microsoft.Data.Sqlite;

namespace BestCrush.Services;

/// <summary>
/// Stores global sale PERIODS separately from asking prices and personal trades.
/// Re-reading a period replaces its summary instead of adding its units again.
/// No network opcode is assumed to carry these statistics until characterized.
/// </summary>
public sealed partial class GlobalSalesRepository
{
    private readonly string _path;

    public GlobalSalesRepository(string path)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        SQLitePCL.Batteries_V2.Init();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS soul_market_prices(
                server TEXT NOT NULL,item_id INTEGER NOT NULL,boss_id INTEGER NOT NULL,
                lot_quantity INTEGER NOT NULL,lot_price INTEGER NOT NULL CHECK(lot_price>0),group_id INTEGER NOT NULL,
                observed_at TEXT NOT NULL,PRIMARY KEY(server,item_id,lot_quantity));
            CREATE TABLE IF NOT EXISTS soul_capture_quotes(
                server TEXT NOT NULL,boss_id INTEGER NOT NULL,
                price INTEGER NOT NULL CHECK(price>0),observed_at TEXT NOT NULL,
                PRIMARY KEY(server,boss_id));
            CREATE TABLE IF NOT EXISTS global_sales_histories(
                server TEXT NOT NULL, object_type INTEGER NOT NULL, item_id INTEGER NOT NULL,
                variant TEXT NOT NULL, observed_at TEXT NOT NULL, history_json TEXT NOT NULL,
                PRIMARY KEY(server,object_type,item_id,variant));
            CREATE TABLE IF NOT EXISTS global_sales_periods(
                server TEXT NOT NULL, object_type INTEGER NOT NULL, item_id INTEGER NOT NULL,
                variant TEXT NOT NULL, period_start TEXT NOT NULL, period_end TEXT NOT NULL,
                units_sold INTEGER NOT NULL CHECK(units_sold >= 0), observed_at TEXT NOT NULL,
                PRIMARY KEY(server,object_type,item_id,variant,period_start,period_end));
            CREATE TABLE IF NOT EXISTS personal_market_listings(
                server TEXT NOT NULL, listing_uid TEXT NOT NULL, item_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL CHECK(quantity > 0), observed_at TEXT NOT NULL,
                PRIMARY KEY(server,listing_uid));
            CREATE INDEX IF NOT EXISTS personal_market_listings_time
                ON personal_market_listings(server,observed_at);
            """;
        command.ExecuteNonQuery();
        MigrateSoulPrices(db);
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = _path, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    public bool Save(GlobalSalesObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO global_sales_periods VALUES($s,$t,$id,$v,$start,$end,$units,$at)
            ON CONFLICT(server,object_type,item_id,variant,period_start,period_end)
            DO UPDATE SET units_sold=$units,observed_at=$at
            WHERE excluded.observed_at > global_sales_periods.observed_at;
            """;
        BindMarket(command, observation.Market);
        command.Parameters.AddWithValue("$start", Format(observation.PeriodStartUtc));
        command.Parameters.AddWithValue("$end", Format(observation.PeriodEndUtc));
        command.Parameters.AddWithValue("$units", observation.UnitsSold);
        command.Parameters.AddWithValue("$at", Format(observation.ObservedAtUtc));
        return command.ExecuteNonQuery() > 0;
    }

    public GlobalSalesObservation? ReadLatest(SalesMarketKey market, DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(market);
        using var db = Open();
        using var command = db.CreateCommand();
        // Select ONE period. Overlapping rolling windows are never added.
        // Future observations cannot hide an earlier usable snapshot.
        command.CommandText = """
            SELECT period_start,period_end,units_sold,observed_at FROM global_sales_periods
            WHERE server=$s AND object_type=$t AND item_id=$id AND variant=$v
                AND observed_at<=$at AND period_end<=$at
            ORDER BY period_end DESC,observed_at DESC,period_start ASC LIMIT 1;
            """;
        BindMarket(command, market);
        command.Parameters.AddWithValue("$at", Format(asOfUtc));
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(market, Parse(reader.GetString(0)), Parse(reader.GetString(1)),
            reader.GetInt64(2), Parse(reader.GetString(3))) : null;
    }

    private static void BindMarket(SqliteCommand command, SalesMarketKey market)
    {
        command.Parameters.AddWithValue("$s", market.ServerName);
        command.Parameters.AddWithValue("$t", (int)market.ObjectType);
        command.Parameters.AddWithValue("$id", market.ItemId);
        command.Parameters.AddWithValue("$v", market.Variant);
    }
    private static string Format(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string time) => DateTimeOffset.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
