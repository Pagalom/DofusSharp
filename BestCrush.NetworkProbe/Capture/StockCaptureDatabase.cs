using System.Globalization;
using System.Text.Json;
using BestCrush.Network.Protocol;
using Microsoft.Data.Sqlite;

namespace BestCrush.NetworkProbe.Capture;

/// <summary>Append-only research snapshots, NOT an authoritative current inventory.</summary>
internal sealed class StockCaptureDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _sync = new();
    private readonly string _runId = Guid.NewGuid().ToString("N");

    public StockCaptureDatabase(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (Path.GetFileName(fullPath).Equals("bestcrush.db", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a separate research database, not bestcrush.db.");
        SQLitePCL.Batteries_V2.Init();
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = fullPath, Pooling = false }.ToString());
        try
        {
            _connection.Open();
            using var check = _connection.CreateCommand();
            check.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            if (Convert.ToInt64(check.ExecuteScalar()) > 0)
            {
                check.CommandText = "SELECT version FROM stock_capture_schema";
                if (Convert.ToInt64(check.ExecuteScalar()) != 1)
                    throw new InvalidOperationException("Unsupported stock capture database.");
            }
            using var schema = _connection.CreateCommand();
            schema.CommandText = """
                PRAGMA foreign_keys=ON;
                CREATE TABLE IF NOT EXISTS stock_capture_schema(version INTEGER NOT NULL);
                INSERT INTO stock_capture_schema SELECT 1 WHERE NOT EXISTS(SELECT 1 FROM stock_capture_schema);
                CREATE TABLE IF NOT EXISTS stock_snapshots(
                    id INTEGER PRIMARY KEY, run_id TEXT NOT NULL, session INTEGER NOT NULL,
                    container TEXT NOT NULL, captured_utc TEXT NOT NULL, status TEXT NOT NULL,
                    source_rows INTEGER NOT NULL, ownership TEXT NOT NULL DEFAULT 'unverified');
                CREATE TABLE IF NOT EXISTS stock_items(
                    snapshot_id INTEGER NOT NULL REFERENCES stock_snapshots(id), ordinal INTEGER NOT NULL,
                    item_id_candidate TEXT NOT NULL, uid_candidate TEXT NOT NULL, quantity_candidate TEXT NOT NULL,
                    effects_json TEXT, effect_wire_json TEXT, effect_issue TEXT,
                    PRIMARY KEY(snapshot_id, ordinal));
                CREATE TABLE IF NOT EXISTS stock_events(id INTEGER PRIMARY KEY,run_id TEXT NOT NULL,session INTEGER NOT NULL,opcode TEXT NOT NULL,captured_utc TEXT NOT NULL,body BLOB NOT NULL);
                CREATE INDEX IF NOT EXISTS stock_scope ON stock_snapshots(run_id, session, container, id);
                """;
            schema.ExecuteNonQuery();
        }
        catch { _connection.Dispose(); throw; }
    }

    public void Save(int session, StockSnapshotWireCandidate snapshot)
    {
        if (session <= 0 || snapshot.Container is not ("bank" or "inventory"))
            throw new ArgumentException("Invalid stock scope.");
        // Failed/partial snapshots are recorded as events with NO usable rows.
        // They must never be mistaken for an empty bank or a current complete stock.
        bool complete = snapshot.Status == StockSnapshotCandidateStatus.CompleteCandidate;
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO stock_snapshots(run_id, session, container, captured_utc, status, source_rows)
                VALUES($run, $session, $container, $time, $status, $rows);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$run", _runId);
            command.Parameters.AddWithValue("$session", session);
            command.Parameters.AddWithValue("$container", snapshot.Container);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$status", snapshot.Status.ToString());
            command.Parameters.AddWithValue("$rows", snapshot.Rows.Count);
            long id = (long)command.ExecuteScalar()!;
            if (complete)
            {
                int ordinal = 0;
                foreach (var row in snapshot.Rows)
                {
                    if (row.Field3QuantityCandidate is not > 0 || row.Field4ItemIdCandidate is not > 0 ||
                        row.Field5InstanceUidCandidate is not > 0)
                        throw new InvalidDataException("Complete snapshot has incomplete scalars.");
                    using var insert = _connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = """
                        INSERT INTO stock_items VALUES($snapshot,$ordinal,$item,$uid,$qty,$effects,$wire,$issue)
                        """;
                    insert.Parameters.AddWithValue("$snapshot", id);
                    insert.Parameters.AddWithValue("$ordinal", ordinal++);
                    insert.Parameters.AddWithValue("$item", row.Field4ItemIdCandidate.Value.ToString(CultureInfo.InvariantCulture));
                    insert.Parameters.AddWithValue("$uid", row.Field5InstanceUidCandidate.Value.ToString(CultureInfo.InvariantCulture));
                    insert.Parameters.AddWithValue("$qty", row.Field3QuantityCandidate.Value.ToString(CultureInfo.InvariantCulture));
                    insert.Parameters.AddWithValue("$effects", row.Effects is null ? DBNull.Value : JsonSerializer.Serialize(row.Effects));
                    byte[][]? wire = row.CopyExactEffectWireEntries();
                    insert.Parameters.AddWithValue("$wire", wire is null ? DBNull.Value : JsonSerializer.Serialize(wire));
                    insert.Parameters.AddWithValue("$issue", (object?)row.EffectDecodeIssue ?? DBNull.Value);
                    insert.ExecuteNonQuery();
                }
            }
            transaction.Commit();
        }
    }

    public void SaveEvent(int session, string opcode, byte[] body)
    {
        if (session <= 0 || body.Length > 256 * 1024) throw new ArgumentException("Invalid event.");
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "INSERT INTO stock_events(run_id,session,opcode,captured_utc,body) VALUES($r,$s,$o,$t,$b)";
            command.Parameters.AddWithValue("$r", _runId);
            command.Parameters.AddWithValue("$s", session);
            command.Parameters.AddWithValue("$o", opcode);
            command.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$b", body);
            command.ExecuteNonQuery();
        }
    }

    public void Dispose() => _connection.Dispose();
}
