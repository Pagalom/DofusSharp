using BestCrush.Network.Protocol;
using BestCrush.NetworkProbe.Capture;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class StockCaptureDatabaseTest : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
    private static byte[] Bank() => Message(Unsigned(1, 1),
        Bytes(2, Bytes(1, Message(Unsigned(3, 7), Unsigned(4, 2415), Unsigned(5, 901),
            Bytes(7, Message(Bytes(8, [8,16]), Unsigned(10,112)))))));

    private object Query(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = _path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()!;
    }

    [Fact]
    public void UnknownEffectsArePersistedExactlyWithoutInventingNumericValues()
    {
        using (var db = new StockCaptureDatabase(_path))
            new StockSnapshotDeltaStudy(snapshotObserver: db.Save).Observe(1,"irp",Bank());
        Assert.Equal("2415", Query("SELECT item_id_candidate FROM stock_items"));
        Assert.Equal("7", Query("SELECT quantity_candidate FROM stock_items"));
        Assert.Equal(DBNull.Value, Query("SELECT effects_json FROM stock_items"));
        Assert.Equal("missing-value", Query("SELECT effect_issue FROM stock_items"));
        var wire = JsonSerializer.Deserialize<byte[][]>((string)Query("SELECT effect_wire_json FROM stock_items"));
        Assert.Equal(Message(Bytes(8,[8,16]),Unsigned(10,112)),wire![0]);
        Assert.Equal("unverified",Query("SELECT ownership FROM stock_snapshots"));
    }

    [Fact]
    public void SessionsContainersAndCaptureRunsRemainSeparate()
    {
        using (var db = new StockCaptureDatabase(_path))
        {
            var study = new StockSnapshotDeltaStudy(snapshotObserver: db.Save);
            study.Observe(1,"irp",Bank());
            study.Observe(2,"irp",Bank());
            study.Observe(1,"irl",Message(Unsigned(4,1)));
        }
        using (var db = new StockCaptureDatabase(_path))
            new StockSnapshotDeltaStudy(snapshotObserver: db.Save).Observe(1,"irp",Bank());
        Assert.Equal(4L,Query("SELECT count(*) FROM stock_snapshots"));
        Assert.Equal(2L,Query("SELECT count(DISTINCT run_id) FROM stock_snapshots"));
        Assert.Equal(2L,Query("SELECT count(DISTINCT container) FROM stock_snapshots"));
        Assert.Equal(2L,Query("SELECT count(DISTINCT session) FROM stock_snapshots"));
    }

    [Fact]
    public void InvalidAndEmptySnapshotsRemainDistinctAndDeltasDoNotRewriteHistory()
    {
        using (var db = new StockCaptureDatabase(_path))
        {
            var study = new StockSnapshotDeltaStudy(snapshotObserver: db.Save);
            study.Observe(1,"irp",Bank());
            study.Observe(1,"iqx",Message(Unsigned(3,3),Unsigned(4,2415),Unsigned(5,901)));
            study.Observe(1,"irp",[0]);
            study.Observe(1,"irp",Message(Unsigned(1,1)));
        }
        Assert.Equal(3L,Query("SELECT count(*) FROM stock_snapshots"));
        Assert.Equal(1L,Query("SELECT count(*) FROM stock_items"));
        Assert.Equal(1L,Query("SELECT count(*) FROM stock_snapshots WHERE status='Malformed'"));
        Assert.Equal(1L,Query("SELECT count(*) FROM stock_snapshots WHERE status='EmptyCandidate'"));
    }

    [Fact]
    public void FailedWriteRollsBackEntireSnapshot()
    {
        using(var db = new StockCaptureDatabase(_path))
            Assert.Throws<InvalidDataException>(() => db.Save(1,new("bank",
                StockSnapshotCandidateStatus.CompleteCandidate,[new(null,1,1,0)])));
        Assert.Equal(0L,Query("SELECT count(*) FROM stock_snapshots"));
    }

    [Fact]
    public void ExistingUnrelatedDatabaseIsNotModified()
    {
        SQLitePCL.Batteries_V2.Init();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource=_path, Pooling=false }.ToString()))
        {
            connection.Open();
            using var command=connection.CreateCommand();
            command.CommandText="CREATE TABLE keep_me(value TEXT); INSERT INTO keep_me VALUES('original');";
            command.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() => new StockCaptureDatabase(_path));
        Assert.Equal("original",Query("SELECT value FROM keep_me"));
        Assert.Equal(0L,Query("SELECT count(*) FROM sqlite_master WHERE name='stock_items'"));
    }

    [Theory]
    [InlineData(65, true)]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public void LargeEffectListsArePreservedWithinExplicitBounds(int count, bool retained)
    {
        byte[] effect = Message(Unsigned(10, 112));
        byte[] body = Message(Unsigned(4, 1), Bytes(2, Bytes(1, Message(
            Unsigned(3, 1), Unsigned(4, 10), Unsigned(5, 11),
            Message(Enumerable.Range(0, count).Select(_ => Bytes(7, effect)).ToArray())))));
        using (var db = new StockCaptureDatabase(_path))
        {
            string report = new StockSnapshotDeltaStudy(snapshotObserver: db.Save).Observe(1, "irl", body)!;
            Assert.Contains(retained ? "wire-grouping=candidate" : "wire-grouping=not-ready", report);
        }
        Assert.Equal("too-many-effects", Query("SELECT effect_issue FROM stock_items"));
        Assert.Equal(DBNull.Value, Query("SELECT effects_json FROM stock_items"));
        object wire = Query("SELECT effect_wire_json FROM stock_items");
        if (retained)
        {
            var entries = JsonSerializer.Deserialize<byte[][]>((string)wire)!;
            Assert.Equal(count, entries.Length);
            Assert.All(entries, entry => Assert.Equal(effect, entry));
        }
        else Assert.Equal(DBNull.Value, wire);
    }

    public void Dispose() => File.Delete(_path);
}
