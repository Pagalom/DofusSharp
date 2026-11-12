using Microsoft.Data.Sqlite;

namespace BestCrush.Services;

public sealed partial class GlobalSalesRepository
{
    // Collapse previous format-specific observations without losing the latest
    // market snapshot, manual quote, or global sales history on upgrade.
    private static void MigrateSoulPrices(SqliteConnection db)
    {
        using var transaction = db.BeginTransaction();
        bool HasLegacyFormat(string table)
        {
            using var query = db.CreateCommand(); query.Transaction = transaction;
            query.CommandText = $"PRAGMA table_info({table})";
            using var rows = query.ExecuteReader();
            while (rows.Read()) if (rows.GetString(1) == "loot_slots") return true;
            return false;
        }
        void Execute(string sql)
        {
            using var command = db.CreateCommand(); command.Transaction = transaction;
            command.CommandText = sql; command.ExecuteNonQuery();
        }
        if (HasLegacyFormat("soul_market_prices")) Execute("""
            CREATE TABLE soul_market_prices_unified(
                server TEXT NOT NULL,item_id INTEGER NOT NULL,boss_id INTEGER NOT NULL,
                lot_quantity INTEGER NOT NULL,lot_price INTEGER NOT NULL CHECK(lot_price>0),group_id INTEGER NOT NULL,
                observed_at TEXT NOT NULL,PRIMARY KEY(server,item_id,lot_quantity));
            INSERT INTO soul_market_prices_unified
            SELECT p.server,p.item_id,p.boss_id,p.lot_quantity,MIN(p.lot_price),p.group_id,p.observed_at
            FROM soul_market_prices p
            WHERE p.observed_at=(SELECT MAX(q.observed_at) FROM soul_market_prices q WHERE q.server=p.server AND q.item_id=p.item_id)
            GROUP BY p.server,p.item_id,p.boss_id,p.lot_quantity;
            DROP TABLE soul_market_prices;
            ALTER TABLE soul_market_prices_unified RENAME TO soul_market_prices;
            """);
        if (HasLegacyFormat("soul_capture_quotes")) Execute("""
            CREATE TABLE soul_capture_quotes_unified(
                server TEXT NOT NULL,boss_id INTEGER NOT NULL,price INTEGER NOT NULL CHECK(price>0),observed_at TEXT NOT NULL,
                PRIMARY KEY(server,boss_id));
            INSERT INTO soul_capture_quotes_unified
            SELECT p.server,p.boss_id,MIN(p.price),p.observed_at FROM soul_capture_quotes p
            WHERE p.observed_at=(SELECT MAX(q.observed_at) FROM soul_capture_quotes q WHERE q.server=p.server AND q.boss_id=p.boss_id)
            GROUP BY p.server,p.boss_id;
            DROP TABLE soul_capture_quotes;
            ALTER TABLE soul_capture_quotes_unified RENAME TO soul_capture_quotes;
            """);
        Execute("""
            INSERT INTO global_sales_histories
            SELECT server,object_type,item_id,REPLACE(variant,':all-slots',''),observed_at,
                REPLACE(history_json,':all-slots"','"') FROM global_sales_histories WHERE variant LIKE 'boss:%:all-slots'
            ON CONFLICT(server,object_type,item_id,variant) DO UPDATE SET observed_at=excluded.observed_at,history_json=excluded.history_json
            WHERE excluded.observed_at>global_sales_histories.observed_at;
            INSERT INTO global_sales_periods
            SELECT server,object_type,item_id,REPLACE(variant,':all-slots',''),period_start,period_end,units_sold,observed_at
                FROM global_sales_periods WHERE variant LIKE 'boss:%:all-slots'
            ON CONFLICT(server,object_type,item_id,variant,period_start,period_end) DO UPDATE SET units_sold=excluded.units_sold,observed_at=excluded.observed_at
            WHERE excluded.observed_at>global_sales_periods.observed_at;
            DELETE FROM global_sales_histories WHERE variant LIKE 'boss:%:all-slots';
            DELETE FROM global_sales_periods WHERE variant LIKE 'boss:%:all-slots';
            """);
        transaction.Commit();
    }
}
