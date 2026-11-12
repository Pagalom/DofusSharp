using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class ForgemagingDataTest
{
    private sealed class PriorityStub : IDataPriorityProvider { public DataPriority Priority { get; set; } = DataPriority.InGameAutomatic; }
    [Fact]
    public async Task LocalRunePricesRespectServerLotPriorityAndClearsAndCatalogExcludesPetsAndTrophies()
    {
        using var database = new TestDatabase(); await using var db = database.CreateContext();
        var item = new Equipment(1) { Name = "Amulette", Type = EquipmentType.Amulet };
        item.Characteristics.Add(new(item, Characteristic.Strength, 10, 20)); db.Equipments.Add(item);
        db.Equipments.AddRange(new Equipment(2) { Type = EquipmentType.Pet }, new Equipment(3) { Type = EquipmentType.Trophy });
        db.Runes.AddRange(new Rune(10) { Name = "Rune Fo", Characteristic = Characteristic.Strength },
            new Rune(11) { Name = "Rune Pa Fo", Characteristic = Characteristic.Strength });
        var at = DateTime.UtcNow.AddHours(-1);
        MarketPriceObservation Price(string server, long price, int quantity, DateTime date, bool clear = false) => new()
        { ObjectType = MarketObjectType.Rune, DofusDbId = 10, ServerName = server, Price = price, Quantity = quantity,
            Source = MarketPriceSource.InGameAutomatic, ObservedAtUtc = date, IsCleared = clear };
        db.MarketPriceObservations.AddRange(Price("A", 5, 1, at), Price("A", 0, 1, at.AddMinutes(1), true),
            Price("A", 200, 10, at), Price("B", 1, 1, at));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new ForgemagingDataService(db, new MarketPriceService(db, new PriorityStub()));
        Assert.Single(await service.GetCatalogAsync(default));
        var result = await service.LoadAsync(1, "A", default);
        Assert.Equal(20, result.Runes.Single(r => r.Id == 10).UnitPrice);
        Assert.Null(result.Runes.Single(r => r.Id == 11).UnitPrice);
        Assert.Null(result.Session.Lines.Single().Current); Assert.Null(result.Session.Sink);
        Assert.Equal(20, result.Session.Lines.Single().Target);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CrushingCoefficientUsesTheSelectedServerAndTheEffectiveSource()
    {
        using var database = new TestDatabase(); await using var db = database.CreateContext();
        db.Equipments.Add(new(1) { Name = "Amulette", Type = EquipmentType.Amulet });
        db.CoefficientObservations.AddRange(
            new() { DofusDbId = 1, ServerName = "A", CoefficientPercent = 400, Source = CoefficientSource.InGameAutomatic, ObservedAtUtc = DateTime.UtcNow.AddMinutes(-1) },
            new() { DofusDbId = 1, ServerName = "B", CoefficientPercent = 900, Source = CoefficientSource.InGameAutomatic, ObservedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var priority = new PriorityStub();
        var service = new ForgemagingDataService(db, new MarketPriceService(db, priority), new CoefficientService(db, priority));
        Assert.Equal(400, (await service.LoadAsync(1, "A", default)).Crushing!.CoefficientPercent);
        Assert.Equal(900, (await service.LoadAsync(1, "B", default)).Crushing!.CoefficientPercent);
        Assert.Null((await service.LoadAsync(1, "C", default)).Crushing!.CoefficientPercent);
    }
}
