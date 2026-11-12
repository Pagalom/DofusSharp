using System.Text.Json;
using BestCrush.Network.Protocol;
using BestCrush.NetworkProbe.Capture;
using Microsoft.Data.Sqlite;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class StockCaptureReportTest : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "stock-report-" + Guid.NewGuid());
    private string Db => Path.Combine(_folder, "captures.db");
    public StockCaptureReportTest() => Directory.CreateDirectory(_folder);

    private static StockSnapshotWireCandidate Snapshot(string container, ulong quantity = 7) =>
        InventoryBankSnapshotDecoder.Decode(container == "bank" ? "irp" : "irl",
            Message(Unsigned(container == "bank" ? 1 : 4, 1), Bytes(2, Bytes(1, Message(
                Unsigned(3, quantity), Unsigned(4, 2415), Unsigned(5, 901))))))!;

    [Fact]
    public void LatestScopeNeverBorrowsMissingBankFromAnotherRun()
    {
        using (var db = new StockCaptureDatabase(Db)) db.Save(1, Snapshot("bank"));
        using (var db = new StockCaptureDatabase(Db)) db.Save(1, Snapshot("inventory"));
        var report = StockCaptureReport.Read(Db);
        Assert.Single(report.Snapshots);
        Assert.Equal("inventory", Assert.Single(report.Items).Container);
        Assert.Contains("Non captur", report.RenderHtml(new Dictionary<string, StockReportCatalogEntry>()));
    }

    [Fact]
    public void InvalidLatestSnapshotDoesNotRestoreOlderCompleteStock()
    {
        using (var db = new StockCaptureDatabase(Db))
        {
            db.Save(1, Snapshot("bank"));
            db.Save(1, new("bank", StockSnapshotCandidateStatus.Malformed, []));
        }
        var report = StockCaptureReport.Read(Db);
        Assert.Empty(report.Items);
        Assert.Equal("Malformed", Assert.Single(report.Snapshots).Status);
    }

    [Fact]
    public void OlderScopeCanBeSelectedExplicitlyAndContainersDoNotMixAcrossSessions()
    {
        using (var db = new StockCaptureDatabase(Db))
        {
            db.Save(1, Snapshot("bank"));
            db.Save(2, Snapshot("inventory"));
        }
        string run = StockCaptureReport.Read(Db).Snapshots[0].RunId;
        Assert.Equal("bank", Assert.Single(StockCaptureReport.Read(Db, run, 1).Items).Container);
        Assert.Throws<ArgumentException>(() => StockCaptureReport.Read(Db, run));
        Assert.Throws<InvalidDataException>(() => StockCaptureReport.Read(Db, run, 9));
    }

    [Fact]
    public void HtmlEscapesCatalogContentAndJsonPreservesUInt64QuantityAndSeparateRows()
    {
        using (var db = new StockCaptureDatabase(Db))
        {
            db.Save(1, Snapshot("bank", ulong.MaxValue));
            db.Save(1, Snapshot("inventory", ulong.MaxValue));
        }
        var report = StockCaptureReport.Read(Db);
        var catalog = new Dictionary<string, StockReportCatalogEntry> {
            ["2415"] = new("2415", "<script>alert('x')</script>", 10, "Arme", "test") };
        string html = report.RenderHtml(catalog);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Equal(2, report.Items.Count);
        report.Write(_folder, catalog);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "stock.json")));
        Assert.Equal(ulong.MaxValue.ToString(), json.RootElement.GetProperty("Items")[0].GetProperty("Quantity").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("Items").GetArrayLength());
    }

    [Fact]
    public void CorruptedRowCountIsRejected()
    {
        using (var db = new StockCaptureDatabase(Db)) db.Save(1, Snapshot("bank"));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE stock_snapshots SET source_rows=2";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => StockCaptureReport.Read(Db));
    }

    [Fact]
    public void MissingDatabaseIsNeverCreatedByReading()
    {
        Assert.Throws<SqliteException>(() => StockCaptureReport.Read(Db));
        Assert.False(File.Exists(Db));
    }

    public void Dispose() => Directory.Delete(_folder, true);
}
