using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush.Network;

// Tests the actual history persistence boundary. Building the write model from kci
// remains in CrushSessionService and is deliberately not reproduced in this fixture.
public sealed class CrushHistorySnapshotTest : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly BestCrushDbContext _context;
    private readonly HistoryService _service;
    private static readonly DateTime StartedAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public CrushHistorySnapshotTest()
    {
        _context = _database.CreateContext();
        _service = new HistoryService(_context);
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task PersistsTheSuppliedSnapshotAndItsPriceProvenanceWithoutRecalculation()
    {
        Equipment equipment = new(42) { Name = "Current equipment name", DofusDbIconId = 420 };
        Rune rune = new(100) { Name = "Current rune name", DofusDbIconId = 1000 };
        _context.AddRange(equipment, rune);
        await _context.SaveChangesAsync();

        Guid id = await _service.SaveCrushSessionAsync(Snapshot());

        // Later catalogs and prices must not become the historical operation's values.
        equipment.Name = "Renamed equipment";
        equipment.DofusDbIconId = 421;
        rune.Name = "Renamed rune";
        rune.DofusDbIconId = 1001;
        _context.MarketPriceObservations.Add(new MarketPriceObservation
        {
            ObjectType = MarketObjectType.Rune, DofusDbId = 100, ServerName = "TEST",
            Price = 9999, Quantity = 10, Source = MarketPriceSource.Manual,
            ObservedAtUtc = StartedAt.AddDays(1)
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        CrushHistorySession saved = await Read(id);
        saved.ServerName.Should().Be("TEST");
        saved.StartedAtUtc.Should().Be(StartedAt);
        saved.CompletedAtUtc.Should().Be(StartedAt.AddSeconds(2));
        saved.TotalValue.Should().Be(80);
        saved.DiscountPercent.Should().Be(5);
        saved.DiscountedTotalValue.Should().Be(76);
        var savedEquipment = saved.Equipments.Should().ContainSingle().Which;
        savedEquipment.EquipmentName.Should().Be("Equipment at crush time");
        savedEquipment.DofusDbId.Should().Be(42);
        savedEquipment.DofusDbIconId.Should().Be(420);
        savedEquipment.CoefficientPercent.Should().Be(371);
        savedEquipment.CoefficientSource.Should().Be(CoefficientSource.InGameAutomatic);
        savedEquipment.RowY.Should().Be(2);
        var savedRune = saved.Runes.Should().ContainSingle().Which;
        savedRune.RuneName.Should().Be("Rune at crush time");
        savedRune.DofusDbId.Should().Be(100);
        savedRune.DofusDbIconId.Should().Be(1000);
        savedRune.Quantity.Should().Be(10);
        savedRune.Value.Should().Be(80);
        var lot = savedRune.Lots.Should().ContainSingle().Which;
        lot.Count.Should().Be(1);
        lot.LotQuantity.Should().Be(10);
        lot.LotPrice.Should().Be(80);
        lot.IsEstimated.Should().BeTrue();
        lot.PriceSource.Should().Be(MarketPriceSource.InGameAutomatic);
        lot.PriceObservedAtUtc.Should().Be(StartedAt.AddMinutes(-1));
    }

    [Fact]
    public async Task MissingCatalogEntriesAndIncompleteValuationRemainPersistableSnapshots()
    {
        var incomplete = Snapshot() with
        {
            TotalValue = null,
            DiscountedTotalValue = null,
            Runes = [new CrushHistoryRuneWriteModel(100, "Rune #100", 10, null, [])]
        };
        Guid id = await _service.SaveCrushSessionAsync(incomplete);
        _context.ChangeTracker.Clear();

        CrushHistorySession saved = await Read(id);
        saved.TotalValue.Should().BeNull();
        saved.DiscountedTotalValue.Should().BeNull();
        saved.Equipments.Single().DofusDbIconId.Should().BeNull();
        saved.Runes.Single().DofusDbIconId.Should().BeNull();
        saved.Runes.Single().RuneName.Should().Be("Rune #100");
        saved.Runes.Single().Quantity.Should().Be(10);
        saved.Runes.Single().Value.Should().BeNull();
        saved.Runes.Single().Lots.Should().BeEmpty();
    }

    [Fact]
    public async Task RepeatedSaveAppendsAnotherSnapshotInsteadOfDeduplicating()
    {
        Guid first = await _service.SaveCrushSessionAsync(Snapshot());
        Guid second = await _service.SaveCrushSessionAsync(Snapshot());
        second.Should().NotBe(first);
        (await _context.CrushHistorySessions.CountAsync()).Should().Be(2);
        (await _context.CrushHistoryEquipments.CountAsync()).Should().Be(2);
        (await _context.CrushHistoryRunes.CountAsync()).Should().Be(2);
        (await _context.CrushHistoryRuneLots.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsMissingServerOrRunesBeforeWritingAnything(bool missingServer)
    {
        var invalid = missingServer
            ? Snapshot() with { ServerName = " " }
            : Snapshot() with { Runes = [] };
        Func<Task> save = () => _service.SaveCrushSessionAsync(invalid);
        await save.Should().ThrowAsync<ArgumentException>();
        (await _context.CrushHistorySessions.CountAsync()).Should().Be(0);
        (await _context.CrushHistoryEquipments.CountAsync()).Should().Be(0);
        (await _context.CrushHistoryRunes.CountAsync()).Should().Be(0);
    }

    private Task<CrushHistorySession> Read(Guid id) => _context.CrushHistorySessions
        .Include(session => session.Equipments)
        .Include(session => session.Runes).ThenInclude(rune => rune.Lots)
        .SingleAsync(session => session.Id == id);

    private static CrushHistorySessionWriteModel Snapshot() => new(
        "TEST", StartedAt, StartedAt.AddSeconds(2), 80, 5, 76,
        [new CrushHistoryEquipmentWriteModel(42, "Equipment at crush time", 371, CoefficientSource.InGameAutomatic, 2)],
        [new CrushHistoryRuneWriteModel(100, "Rune at crush time", 10, 80,
            [new CrushHistoryRuneLotWriteModel(1, 10, 80, true, MarketPriceSource.InGameAutomatic, StartedAt.AddMinutes(-1))])]);
}
