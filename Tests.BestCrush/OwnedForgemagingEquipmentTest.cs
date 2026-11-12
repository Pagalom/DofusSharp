using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class OwnedForgemagingEquipmentTest
{
    [Fact]
    public async Task OnlyTheTrackedCopyIsValuedAndKeepsItsHistoryWhenMovedToBank()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        var now = DateTime.UtcNow;
        db.Equipments.Add(new(1) { Name = "Amulette", Type = EquipmentType.Amulet, Level = 100 });
        db.Runes.Add(new(10) { Name = "Rune Fo", Characteristic = Characteristic.Strength });
        db.MarketPriceObservations.Add(new() { ServerName = "A", DofusDbId = 10, ObjectType = MarketObjectType.Rune,
            ObservedAtUtc = now, Source = MarketPriceSource.InGameAutomatic, Quantity = 1, Price = 10 });
        db.CoefficientObservations.Add(new() { DofusDbId = 1, ServerName = "A", CoefficientPercent = 200,
            Source = CoefficientSource.InGameAutomatic, ObservedAtUtc = now });
        await db.SaveChangesAsync();
        var priority = new PricePriority();
        var data = new ForgemagingDataService(db, new MarketPriceService(db, priority));
        var coefficients = new CoefficientService(db, priority);
        var key = ForgemagedEquipmentStock.InstanceKey("a", 1, 12345);
        var session = Session(key, now);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new ForgemagingSessionStore(path);
            store.Save("A", new() { ItemId = 1, ItemName = "Manuel" });
            store.Save("A", session);
            Assert.Equal("Manuel", store.Get("A", 1)!.ItemName);
            Assert.Single(new ForgemagingSessionStore(path).GetObserved("A", 1, key)!.Attempts);
            Assert.Null(store.GetObserved("B", 1, key));
            Assert.DoesNotContain(":12345", File.ReadAllText(path));
            var untracked = new LiveStockItem(1, 23456, 1, null, null, null);
            var tracked = new LiveStockItem(1, 12345, 1, null, null, null);
            var inventory = new LiveStockContainer("inventory", true, false, now, [untracked, tracked]);
            var bank = new LiveStockContainer("bank", true, false, now.AddMinutes(1), [tracked]);
            var source = new LiveStockSource("connection", "Personnage", "A", now, [inventory, bank]);
            var rows = await ForgemagedEquipmentStock.LoadAsync(source, "A", store, data, null, coefficients, null, false, default);
            Assert.Equal(2, rows.Length);
            Assert.Empty(rows.Single(row => row.Key != key).Attempts);

            var result = Assert.Single(await ForgemagedEquipmentStock.LoadAsync(source, "A", store, data, null, coefficients, null, true, default));
            Assert.Equal("bank", result.Container);
            Assert.Equal(320, result.Crushing!.Current!.Value);
            Assert.Equal(620, result.Crushing.Target!.Value);
            Assert.Equal(260, result.Gain);

            var gone = source with { Containers = [inventory with { Items = [untracked] }, bank with { Stale = true }] };
            Assert.Empty(await ForgemagedEquipmentStock.LoadAsync(gone, "A", store, data, null, coefficients, null, true, default));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnpricedRunesAndUnobservedJetChangesLeaveTheGainIncomplete()
    {
        var now = DateTime.UtcNow;
        var item = new Equipment(1) { Level = 100 };
        var session = Session("recording", now);
        var market = new ForgemagingRuneMarket([new(10, "Rune Fo", Characteristic.Strength, 1, 10)], new Dictionary<long, DateTime> { [10] = now });
        var coefficient = new ForgemagingCrushContext(200, now);
        var changed = ForgemagedEquipmentStock.Project("recording", item, "inventory", 1, now.AddMinutes(1), Roll(25), session, market, coefficient);
        Assert.True(changed.UnobservedChanges);
        Assert.Equal(770, changed.Crushing!.Target!.Value);
        Assert.Null(changed.Gain);

        session.Attempts[0].Rune = session.Attempts[0].Rune with { UnitPrice = null };
        var unpriced = ForgemagedEquipmentStock.Project("recording", item, "inventory", 1, now, Roll(20), session, market, coefficient);
        Assert.Null(unpriced.Gain);
        Assert.Equal(2, unpriced.UnpricedRunes);
        Assert.Equal(620, unpriced.Crushing!.Target!.Value);
    }

    private sealed class PricePriority : IDataPriorityProvider { public DataPriority Priority { get; set; } = DataPriority.InGameAutomatic; }

    private static ForgemagingSession Session(string key, DateTime at) => new()
    {
        ItemId = 1, ItemName = "Amulette", Level = 100, GameRecordingId = key, LastObservedRollAtUtc = at, Lines = Roll(20),
        Attempts = [new() { AtUtc = at, Rune = new(10, "Rune Fo", Characteristic.Strength, 1, 20), RuneCount = 2,
            Outcome = ForgemagingOutcome.Neutral, FromGame = true, Before = Roll(10), After = Roll(20) }]
    };

    private static List<ForgemagingLine> Roll(int value) =>
        [new() { Characteristic = Characteristic.Strength, Current = value, Minimum = 1, Maximum = 30 }];
}
