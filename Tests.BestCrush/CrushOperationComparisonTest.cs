using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.BestCrush;

public sealed class CrushOperationComparisonTest
{
    [Fact]
    public async Task CopiesOfTheSameEquipmentKeepSeparateFmCostsAndDrops()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var at = DateTime.UtcNow;
        var rune = new Rune(10) { Name = "Rune Fo", Characteristic = Characteristic.Strength };
        var roll = new ForgemagingLine { Characteristic = Characteristic.Strength, Current = 10, Minimum = 1, Maximum = 20 };
        var item = new Equipment(42) { Name = "Amulette", Level = 100 };
        try
        {
            var store = new ForgemagingSessionStore(path);
            var services = new ServiceCollection().AddSingleton(store);
            using var provider = services.BuildServiceProvider();
            void Record(ulong uid, int price) => store.Save("A", new()
            {
                ItemId = 42, GameRecordingId = ForgemagedEquipmentStock.InstanceKey("A", 42, uid),
                LastObservedRollAtUtc = at, Lines = [roll], Level = 100,
                Attempts = [new() { AtUtc = at, FromGame = true, RuneCount = 1, Outcome = ForgemagingOutcome.Failure,
                    Rune = new(10, rune.Name, rune.Characteristic, 1, price), Before = [roll], After = [roll] }]
            });
            Record(10001, 4);
            Record(10002, 12);
            Assert.Equal(42, store.FindObservedItemIds("A", [10001])[10001]);
            Assert.Empty(store.FindObservedItemIds("B", [10001]));
            NetworkCrushResultLine Line(ulong uid, int count) => new(42, 200, [new(10, count)])
            { ObservedItem = new ItemDetailObservation(uid, 42, 1, []) { StatsComplete = false } };
            var history = new CrushHistorySessionWriteModel("A", at, at, null, 5, null,
                [new(42, "Amulette", 200, CoefficientSource.InGameAutomatic, 1), new(42, "Amulette", 200, CoefficientSource.InGameAutomatic, 2)], []);
            var observations = new Dictionary<(long, int), MarketPriceObservation>
            {
                [(10, 10)] = new() { ObjectType = MarketObjectType.Rune, DofusDbId = 10, ServerName = "A", Price = 100,
                    Quantity = 10, Source = MarketPriceSource.InGameAutomatic, ObservedAtUtc = at }
            };
            var result = await CrushOperationAssessment.AddToHistoryAsync(provider, history, [Line(10002, 30), Line(10001, 33)],
                new Dictionary<long, Equipment?> { [42] = item }, new Dictionary<long, Rune> { [10] = rune }, observations);
            var first = Assert.IsType<CrushEquipmentComparison>(result.Equipments[0].Comparison);
            var second = Assert.IsType<CrushEquipmentComparison>(result.Equipments[1].Comparison);
            Assert.Equal(12, first.KnownFmCost);
            Assert.Equal(4, second.KnownFmCost);
            Assert.Equal(304, first.EstimatedValue);
            Assert.Equal(285, first.ObtainedValue);
            Assert.Equal(-19, first.Difference);
            Assert.Equal(32, Assert.Single(first.Runes).Estimated);
            Assert.Equal(30, Assert.Single(first.Runes).Obtained);
            Assert.Equal(ForgemagingOutcome.Failure, Assert.Single(first.FmBreakdown).Outcome);
            Assert.Null(first.Margin);
            Assert.Equal(173, (first with { RecordedAcquisitionCost = 100 }).Margin);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MigrationPreservesHistoryAndCostCanBeSetOnOneCopy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new BestCrushDbContext(new DbContextOptionsBuilder<BestCrushDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260912225432_AddMarketPriceHistoryIndex");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO CrushHistorySessions (Id,ServerName,StartedAtUtc,CompletedAtUtc,DiscountPercent)
            VALUES ('C3AB1078-1EDD-4679-8F34-6CA41F578A0A','A','2026-11-11','2026-11-11',0);
            INSERT INTO CrushHistoryEquipments (Id, SessionId, DofusDbId, EquipmentName, CoefficientPercent, CoefficientSource, RowY)
            VALUES ('703B3C55-E0DA-4F8D-AB45-68E299299197','C3AB1078-1EDD-4679-8F34-6CA41F578A0A',42,'Ancien',100,0,1);
            """);
        await db.Database.MigrateAsync();
        Assert.Null((await db.CrushHistoryEquipments.SingleAsync()).ReadComparison());
        var history = new HistoryService(db);
        var comparison = new CrushEquipmentComparison(15, 20, null, 1, 2, 0, 9, []);
        Guid id = await history.SaveCrushSessionAsync(new("A", DateTime.UtcNow, DateTime.UtcNow, 0, 0, 0,
            [new(42, "Exemplaire", 100, CoefficientSource.InGameAutomatic, 1) { Comparison = comparison },
             new(42, "Exemplaire", 100, CoefficientSource.InGameAutomatic, 2) { Comparison = comparison }], []));
        db.ChangeTracker.Clear();
        var copies = await db.CrushHistoryEquipments.Where(e => e.SessionId == id).OrderBy(e => e.RowY).ToArrayAsync();
        await history.SetCrushAcquisitionCostAsync(copies[0].Id, "A", 10);
        db.ChangeTracker.Clear();
        copies = await db.CrushHistoryEquipments.Where(e => e.SessionId == id).OrderBy(e => e.RowY).ToArrayAsync();
        Assert.Equal(8, copies[0].ReadComparison()!.Margin);
        Assert.Equal(9, copies[1].ReadComparison()!.Margin);
    }

    [Fact]
    public void RankingUsesRuneVariantWeightAndTheCheapestUnitPrice()
    {
        Assert.Equal(1, RuneMarketRanking.Weight(new Rune(1) { Name = "Rune Fo", Characteristic = Characteristic.Strength }));
        Assert.Equal(3, RuneMarketRanking.Weight(new Rune(2) { Name = "Rune Pa Fo", Characteristic = Characteristic.Strength }));
        Assert.Equal(10, RuneMarketRanking.Weight(new Rune(3) { Name = "Rune Ra Fo", Characteristic = Characteristic.Strength }));
        Assert.Equal((100, 1200L), RuneMarketRanking.Reference(15, 150, 1200, 13000));
        Assert.Null(RuneMarketRanking.Reference(null, 0, null, null));
    }
}
