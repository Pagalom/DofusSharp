using System.Numerics;
using System.Text.Json;
using BestCrush.Network.Protocol;
using Microsoft.Data.Sqlite;

namespace BestCrush.Services;

public sealed record LiveStockSource(string Id, string Label, string? ServerName, DateTime UpdatedUtc,
    IReadOnlyList<LiveStockContainer> Containers)
{
    internal BigInteger? QuantityIn(long itemId, string container)
    {
        var state = Containers.FirstOrDefault(x => x.Container == container);
        if (state is null || !state.Complete || state.Stale) return null;
        return state.Items.Where(x => x.ItemId == (ulong)itemId).Aggregate(BigInteger.Zero, (sum, x) => sum + x.Quantity);
    }
}

/// <summary>Separate durable stock database. No account identity is inferred from a local window.</summary>
public sealed class LiveStockRepository
{
    private readonly string _path;
    private readonly object _sync = new();
    public LiveStockRepository(string path)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        SQLitePCL.Batteries_V2.Init();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS live_sources(id TEXT PRIMARY KEY,label TEXT NOT NULL,server TEXT,updated_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS live_containers(source TEXT NOT NULL,container TEXT NOT NULL,state_json TEXT NOT NULL,
                PRIMARY KEY(source,container));
            CREATE TABLE IF NOT EXISTS live_catalog(item_id TEXT PRIMARY KEY,name TEXT NOT NULL,level INTEGER,category TEXT,updated_utc TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        db.Open(); return db;
    }
    public void Save(string source, string label, string? server, LiveStockContainer state)
    {
        lock (_sync)
        {
            using var db = Open();
            using var tx = db.BeginTransaction();
            using var command = db.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO live_sources VALUES($s,$l,$server,$t) ON CONFLICT(id) DO UPDATE SET label=$l,server=coalesce($server,server),updated_utc=$t;
                INSERT INTO live_containers VALUES($s,$c,$j) ON CONFLICT(source,container) DO UPDATE SET state_json=$j;
                """;
            command.Parameters.AddWithValue("$s", source);
            command.Parameters.AddWithValue("$l", label);
            command.Parameters.AddWithValue("$server", (object?)server ?? DBNull.Value);
            command.Parameters.AddWithValue("$t", state.UpdatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$c", state.Container);
            command.Parameters.AddWithValue("$j", JsonSerializer.Serialize(state));
            command.ExecuteNonQuery(); tx.Commit();
        }
    }
    public IReadOnlyList<LiveStockSource> ReadSources()
    {
        lock (_sync)
        {
            using var db = Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT s.id,s.label,s.server,s.updated_utc,c.state_json FROM live_sources s LEFT JOIN live_containers c ON c.source=s.id ORDER BY s.updated_utc DESC";
            using var reader = command.ExecuteReader();
            var sources = new Dictionary<string, (string Label, string? Server, DateTime Time, List<LiveStockContainer> Containers)>();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                if (!sources.ContainsKey(id)) sources[id] = (reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind), []);
                if (!reader.IsDBNull(4)) sources[id].Containers.Add(JsonSerializer.Deserialize<LiveStockContainer>(reader.GetString(4))!);
            }
            return sources.Select(x => new LiveStockSource(x.Key, x.Value.Label, x.Value.Server, x.Value.Time, x.Value.Containers)).ToArray();
        }
    }
}
