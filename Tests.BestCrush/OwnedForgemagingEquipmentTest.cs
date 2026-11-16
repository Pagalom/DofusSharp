using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using System.Security.Cryptography;
using Encoding = System.Text.Encoding;
using System.Text.Json;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class OwnedForgemagingEquipmentTest
{
    [Fact]
    public async Task DetailShowsRuneArtOutcomesAndJetStatusWithoutTimestamps()
    {
        var equipment = new Equipment(1) { Level = 1 };
        equipment.Characteristics.Add(new(equipment, Characteristic.Intelligence, 1, 5));
        equipment.Characteristics.Add(new(equipment, Characteristic.Chance, 1, 4));
        ForgemagingLine Line(Characteristic stat, int amount, int? max = null) =>
            new() { Characteristic = stat, Current = amount, Maximum = max, Minimum = max.HasValue ? 1 : null };
        var intelligence = Line(Characteristic.Intelligence, 7, 5);
        var chance = Line(Characteristic.Chance, 2, 4);
        var rune = new ForgemagingRune(1517, "Rune Ine", Characteristic.Intelligence, 1, 65);
        var row = new OwnedForgemagingEquipment("boots", equipment, "inventory", 1,
            [intelligence, chance, Line(Characteristic.Strength, 1)], true,
            [
                new() { Rune = rune, Outcome = ForgemagingOutcome.Failure },
                new() { Rune = rune, Outcome = ForgemagingOutcome.Neutral,
                    Before = [Line(Characteristic.Intelligence, 6), Line(Characteristic.Chance, 3)],
                    After = [intelligence, chance] },
                new() { Rune = rune, Outcome = ForgemagingOutcome.Critical,
                    Before = [Line(Characteristic.Intelligence, 6)], After = [intelligence] }
            ],
            new(new(null, 74), new(null, 75), 1, [], true), -220, true, true, 195, 0, false)
        {
            Acquisition = new(EquipmentAcquisitionKind.Purchase, 100, DateTime.UtcNow),
            AcquisitionCost = 100
        };

        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<OwnedForgemagingDetail>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(OwnedForgemagingDetail.Item)] = row,
                    [nameof(OwnedForgemagingDetail.RuneIcons)] = new Dictionary<long, long> { [rune.Id] = 78037 },
                    [nameof(OwnedForgemagingDetail.IsCrushing)] = true
                }));
            return WebUtility.HtmlDecode(output.ToHtmlString());
        });

        Assert.Contains("Prix d’obtention", html);
        Assert.Contains("Achat HDV", html);
        Assert.Contains("100 K", html);
        Assert.Contains("owned-fm-comparison", html);
        Assert.Contains(">Avant FM</dt>", html);
        Assert.Contains(">Après FM</dt>", html);
        Assert.Contains(">Coût FM</dt>", html);
        Assert.Contains("195 K", html);
        Assert.Contains(">Jets</h3>", html);
        Assert.Contains("owned-fm-roll-line over", html);
        Assert.Contains("owned-fm-roll-line exo", html);
        Assert.Contains("src=\"img/runes/78037.png\"", html);
        Assert.Contains("data-bs-title=\"Rune Ine\"", html);
        Assert.Contains(">Échec</span>", html);
        Assert.Contains("class=\"critical\"", html);
        Assert.Contains(">-1</strong>", html);
        Assert.Contains(">65 K</td>", html);
        Assert.DoesNotContain(">Heure</th>", html);
        Assert.DoesNotContain(">Gains et pertes</th>", html);
    }

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
            store.RecordAcquisition("A", 1, key, new(EquipmentAcquisitionKind.Purchase, 100, now));
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

            var owned = await ForgemagedEquipmentStock.LoadAsync(source, "A", store, data, null, coefficients, null, true, default);
            Assert.Equal(2, owned.Length);
            var result = Assert.Single(owned, row => row.Key == key);
            Assert.Null(Assert.Single(owned, row => row.Key != key).Gain);
            Assert.Equal("bank", result.Container);
            Assert.Equal(288, result.Crushing!.Current!.Value);
            Assert.Equal(558, result.Crushing.Target!.Value);
            Assert.Equal(100, result.AcquisitionCost);
            Assert.Equal(418, result.Gain);

            var gone = source with { Containers = [inventory with { Items = [untracked] }, bank with { Stale = true }] };
            var otherCopy = Assert.Single(await ForgemagedEquipmentStock.LoadAsync(gone, "A", store, data, null, coefficients, null, true, default));
            Assert.NotEqual(key, otherCopy.Key);
            Assert.Null(otherCopy.Acquisition);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task StockShowsOverAndExoWithoutAnFmSessionButDoesNotInventGain()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        var now = DateTime.UtcNow;
        var boots = new Equipment(8341) { Name = "Bottananas", Type = EquipmentType.Boots, Level = 1 };
        boots.Characteristics.Add(new(boots, Characteristic.Strength, 5, 6));
        db.Equipments.Add(boots);
        db.Runes.AddRange(new Rune(10) { Name = "Rune Fo", Characteristic = Characteristic.Strength },
            new Rune(11) { Name = "Rune Sa", Characteristic = Characteristic.Wisdom });
        db.MarketPriceObservations.AddRange(new MarketPriceObservation { ServerName = "Kourial", DofusDbId = 10,
            ObjectType = MarketObjectType.Rune, ObservedAtUtc = now, Source = MarketPriceSource.InGameAutomatic, Price = 20 },
            new MarketPriceObservation { ServerName = "Kourial", DofusDbId = 11, ObjectType = MarketObjectType.Rune,
                ObservedAtUtc = now, Source = MarketPriceSource.InGameAutomatic, Price = 20 });
        db.CoefficientObservations.Add(new() { DofusDbId = 8341, ServerName = "Kourial", CoefficientPercent = 200,
            Source = CoefficientSource.InGameAutomatic, ObservedAtUtc = now });
        await db.SaveChangesAsync();

        var directory = Directory.CreateTempSubdirectory("stock-fm-test");
        string cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("catalogue-example-fm-effects-v1")));
        string cacheFile = Path.Combine(directory.FullName, cacheKey + ".json");
        try
        {
            var host = new Uri("https://catalogue.example");
            var factory = DofusDbClient.CreateProductionFactory(host, null);
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(new
            {
                UpdatedUtc = now,
                Data = new ForgemagingEffectDefinition[] { new(118, Characteristic.Strength, 1), new(124, Characteristic.Wisdom, 1) }
            }));
            var catalog = new ActivityCatalogService(factory, Moq.Mock.Of<IHttpClientFactory>(), directory.FullName);
            var store = new ForgemagingSessionStore(Path.Combine(directory.FullName, "sessions.json"));
            var priority = new PricePriority();
            var data = new ForgemagingDataService(db, new MarketPriceService(db, priority));
            var coefficients = new CoefficientService(db, priority);
            var inventory = new LiveStockContainer("inventory", true, false, now,
            [
                new(8341, 101, 1, [new(118, 7)], null, null),
                new(8341, 102, 1, [new(118, 6)], null, null),
                new(8341, 103, 1, [new(118, 7)], null, "partial")
            ]);
            var bank = new LiveStockContainer("bank", true, false, now,
                [new(8341, 104, 1, [new(118, 6), new(124, 1)], null, null)]);
            var source = new LiveStockSource("live", "Personnage", "Kourial", now, [inventory, bank]);
            string Key(ulong uid) => ForgemagedEquipmentStock.InstanceKey("Kourial", 8341, uid);
            store.RecordAcquisition("Kourial", 8341, Key(101), new(EquipmentAcquisitionKind.Purchase, 20, now));
            var rows = (await ForgemagedEquipmentStock.LoadAsync(source, "Kourial", store, data, catalog, coefficients, null, true, default))
                .ToDictionary(row => row.Key);
            Assert.Equal(4, rows.Count);
            Assert.True(rows[Key(101)].HasOver);
            Assert.Equal(7, Assert.Single(rows[Key(101)].Roll!).Current);
            Assert.True(rows[Key(101)].FmCostUnknown);
            Assert.False(rows[Key(102)].HasOver);
            Assert.Null(rows[Key(103)].Roll);
            Assert.False(rows[Key(103)].Crushing!.IsComplete);
            Assert.True(rows[Key(104)].HasExo);
            Assert.Equal("bank", rows[Key(104)].Container);
            Assert.All(rows.Values, row =>
            {
                Assert.Null(row.Crushing!.Current);
                Assert.Null(row.Gain);
                Assert.Empty(row.Attempts);
            });
        }
        finally
        {
            File.Delete(cacheFile);
            File.Delete(Path.Combine(directory.FullName, "sessions.json"));
            directory.Delete();
        }
    }

    [Fact]
    public void UnpricedRunesAndUnobservedJetChangesLeaveTheGainIncomplete()
    {
        var now = DateTime.UtcNow;
        var item = new Equipment(1) { Level = 100 };
        var session = Session("recording", now);
        var market = new ForgemagingRuneMarket([new(10, "Rune Fo", Characteristic.Strength, 1, 10)],
            new Dictionary<(long, int), MarketPriceObservation>
            { [(10, 1)] = Utils.EconomicTestData.Price(10, 1, 11, type: MarketObjectType.Rune, at: now) });
        var coefficient = new ForgemagingCrushContext(200, now);
        var acquisition = new EquipmentAcquisition(EquipmentAcquisitionKind.Purchase, 100, now);
        var changed = ForgemagedEquipmentStock.Project("recording", item, "inventory", 1, now.AddMinutes(1), Roll(25), session, market, coefficient, acquisition);
        Assert.True(changed.UnobservedChanges);
        Assert.Equal(770, changed.Crushing!.Target!.Value);
        Assert.Null(changed.Gain);

        session.Attempts[0].Rune = session.Attempts[0].Rune with { UnitPrice = null };
        var unpriced = ForgemagedEquipmentStock.Project("recording", item, "inventory", 1, now, Roll(20), session, market, coefficient, acquisition);
        Assert.Null(unpriced.Gain);
        Assert.Equal(2, unpriced.UnpricedRunes);
        Assert.Equal(620, unpriced.Crushing!.Target!.Value);
    }

    [Fact]
    public async Task CraftedUnmodifiedEquipmentUsesWholeLotCostAndNeedsNoFmHistory()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        var item = new Equipment(42) { Level = 100 };
        item.Recipe.Add(new(item, new Resource(43) { Name = "Ressource" }, 24));
        var now = DateTime.UtcNow;
        db.Equipments.Add(item);
        db.Runes.Add(new Rune(10) { Name = "Rune Fo", Characteristic = Characteristic.Strength });
        db.MarketPriceObservations.AddRange(Utils.EconomicTestData.Price(43, 1, 105, server: "A", at: now),
            Utils.EconomicTestData.Price(43, 10, 600, server: "A", at: now));
        await db.SaveChangesAsync();
        var data = new ForgemagingDataService(db, new MarketPriceService(db, new PricePriority()));
        var craft = Assert.Single(await data.GetEquipmentCraftCostsAsync("A", [42], default)).Value;
        Assert.Equal(1620, craft.TotalCost);
        Assert.Equal(2, Assert.Single(craft.Resources).Purchase!.Lots[10]);
        Assert.Equal(4, Assert.Single(craft.Resources).Purchase!.Lots[1]);

        var market = new ForgemagingRuneMarket([new(10, "Rune Fo", Characteristic.Strength, 1, 10)],
            new Dictionary<(long, int), MarketPriceObservation>
            { [(10, 1)] = Utils.EconomicTestData.Price(10, 1, 101, type: MarketObjectType.Rune, at: now) });
        var context = new ForgemagingCrushContext(200, now);
        var craftOrigin = new EquipmentAcquisition(EquipmentAcquisitionKind.Craft, null, now);
        var row = ForgemagedEquipmentStock.Project("craft", item, "inventory", 1, now, Roll(10), null, market, context, craftOrigin, craft);
        Assert.Empty(row.Attempts);
        Assert.Equal(1620, row.AcquisitionCost);
        Assert.Equal(1580, row.Gain);
        Assert.Null(row.Crushing!.Current);
        var standardRunes = new CrushService(database).GetCrushResult(
            new Dictionary<Characteristic, double> { [Characteristic.Strength] = 10 }, 100, coefficient: 2);
        var prices = new MarketPriceService(db, new PricePriority());
        var standardRevenue = standardRunes.Sum(rune => prices.CalculateValue(rune.Key.DofusDbId, rune.Value, market.ResalePrices)!.Value.Value);
        Assert.Equal(standardRevenue, row.Crushing.Target!.Value);

        var unknown = ForgemagedEquipmentStock.Project("unknown", item, "inventory", 1, now, Roll(10), null, market, context);
        Assert.Null(unknown.AcquisitionCost);
        Assert.Null(unknown.Gain);
    }

    [Fact]
    public void AcquisitionSurvivesChangedStockUidAndJoinsLaterFmHistory()
    {
        var folder = Directory.CreateTempSubdirectory("acquisition-alias");
        string path = Path.Combine(folder.FullName, "sessions.json");
        try
        {
            var store = new ForgemagingSessionStore(path);
            var now = DateTime.UtcNow;
            string Key(ulong uid) => ForgemagedEquipmentStock.InstanceKey("A", 1, uid);
            LiveStockContainer Snapshot(ulong uid, DateTime at) =>
                new("inventory", true, false, at, [new(1, uid, 1, null, [[1, 2]], null)]);
            store.RecordAcquisition("A", 1, Key(100), new(EquipmentAcquisitionKind.Purchase, 40, now));
            Assert.True(store.ReattachObserved("A", Snapshot(100, now), Snapshot(200, now.AddMinutes(1))));
            store.Save("A", Session(Key(200), now.AddMinutes(2)));
            store = new(path);
            Assert.Equal(40, store.GetAcquisitions("A", [(1, Key(200))])[Key(200)].PricePaid);
            Assert.Single(store.GetObserved("A", 1, Key(200))!.Attempts);
            Assert.Empty(store.GetAcquisitions("B", [(1, Key(200))]));
        }
        finally
        {
            File.Delete(path);
            folder.Delete();
        }
    }

    [Fact]
    public void ReconnectionPrefersTheConfirmedWorkshopRollOverAnOlderInventoryJet()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new ForgemagingSessionStore(path);
            var now = DateTime.UtcNow;
            string Key(ulong uid) => ForgemagedEquipmentStock.InstanceKey("A", 1, uid);
            LiveStockContainer Snapshot(ulong uid, byte strength, DateTime at) => new("inventory", true, false, at,
                [new(1, uid, 1, [new(118, strength)], [[40, strength, 80, 118]], null)]);
            store.RecordAcquisition("A", 1, Key(100), new(EquipmentAcquisitionKind.Purchase, 10, now.AddMinutes(-2)));
            store.Save("A", Session(Key(100), now));
            var effects = new Dictionary<ulong, ForgemagingEffectDefinition> { [118] = new(118, Characteristic.Strength, 1) };
            var stale = Snapshot(100, 10, now.AddMilliseconds(-1));
            Assert.False(store.ReattachObserved("A", stale, Snapshot(200, 10, now.AddMinutes(1)), effects));
            Assert.True(store.ReattachObserved("A", stale, Snapshot(200, 20, now.AddMinutes(1)), effects));
            Assert.Equal(10, store.GetAcquisitions("A", [(1, Key(200))])[Key(200)].PricePaid);
            Assert.Single(store.GetObserved("A", 1, Key(200))!.Attempts);
        }
        finally { File.Delete(path); }
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
