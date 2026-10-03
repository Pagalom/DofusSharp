using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class CoefficientServiceTest : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly BestCrushDbContext _context;
    private readonly TestDataPriorityProvider _priority = new();
    private readonly CoefficientService _service;

    public CoefficientServiceTest()
    {
        _context = _database.CreateContext();
        _service = new CoefficientService(_context, _priority);
    }

    public void Dispose() => _database.Dispose();

    [Theory]
    [InlineData(DataPriority.Manual, 300)]
    [InlineData(DataPriority.InGameAutomatic, 150)]
    public async Task UsesLatestPreferredLocalSourceEvenWhenDofocusIsNewer(DataPriority priority, double expected)
    {
        _priority.Priority = priority;
        await Store(
            Coefficient(1, 100, CoefficientSource.Manual),
            Coefficient(1, 300, CoefficientSource.Manual, at: ObservedAt.AddMinutes(1)),
            Coefficient(1, 50, CoefficientSource.InGameAutomatic, at: ObservedAt.AddMinutes(2)),
            Coefficient(1, 150, CoefficientSource.InGameAutomatic, at: ObservedAt.AddMinutes(3)),
            Coefficient(1, 999, CoefficientSource.DofocusInitial, at: ObservedAt.AddMinutes(4)));

        CoefficientObservation selected = (await _service.GetLatestObservationAsync(1, Server))!;
        selected.CoefficientPercent.Should().Be(expected);
        var all = await _service.GetLatestObservationsForServerAsync(Server);
        all.Should().HaveCount(1);
        all[1].Id.Should().Be(selected.Id);
        var sources = await _service.GetLatestLocalSourceObservationsForServerAsync(Server);
        sources.Should().HaveCount(2);
        sources[(1, CoefficientSource.Manual)].CoefficientPercent.Should().Be(300);
        sources[(1, CoefficientSource.InGameAutomatic)].CoefficientPercent.Should().Be(150);
        sources.Keys.Should().NotContain((1L, CoefficientSource.DofocusInitial));
    }

    [Theory]
    [InlineData(DataPriority.Manual, CoefficientSource.Manual, 200)]
    [InlineData(DataPriority.InGameAutomatic, CoefficientSource.InGameAutomatic, 100)]
    public async Task ClearedPreferredSourceFallsBackToOtherLocalSource(DataPriority priority, CoefficientSource clearedSource, double expected)
    {
        _priority.Priority = priority;
        await Store(Coefficient(1, 100, CoefficientSource.Manual),
            Coefficient(1, 200, CoefficientSource.InGameAutomatic),
            Coefficient(1, 500, CoefficientSource.DofocusInitial),
            Coefficient(1, 0, clearedSource, at: ObservedAt.AddHours(1), cleared: true));

        (await _service.GetLatestObservationAsync(1, Server))!.CoefficientPercent.Should().Be(expected);
        var sources = await _service.GetLatestLocalSourceObservationsForServerAsync(Server);
        sources.Should().HaveCount(1);
        sources.Keys.Should().NotContain((1L, clearedSource));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClearLocalKeepsDofocusFallbackAndDoesNotClearOtherItemsOrServers(bool hasDofocus)
    {
        await Store(Coefficient(1, 100, CoefficientSource.Manual), Coefficient(1, 200, CoefficientSource.InGameAutomatic),
            Coefficient(2, 400, CoefficientSource.Manual), Coefficient(1, 500, CoefficientSource.Manual, server: "OTHER_SERVER"));
        if (hasDofocus)
            await Store(Coefficient(1, 300, CoefficientSource.DofocusInitial));

        await _service.ClearLocalAsync(1, Server);

        CoefficientObservation? effective = await _service.GetLatestObservationAsync(1, Server);
        if (hasDofocus)
        {
            effective.Should().NotBeNull();
            effective!.CoefficientPercent.Should().Be(300);
            effective.Source.Should().Be(CoefficientSource.DofocusInitial);
        }
        else
            effective.Should().BeNull();
        var all = await _service.GetLatestObservationsForServerAsync(Server);
        all.ContainsKey(1).Should().Be(hasDofocus);
        all[2].CoefficientPercent.Should().Be(400);
        (await _service.GetLatestObservationAsync(1, "OTHER_SERVER"))!.CoefficientPercent.Should().Be(500);
        var sources = await _service.GetLatestLocalSourceObservationsForServerAsync(Server);
        sources.Keys.Should().BeEquivalentTo(new[] { (2L, CoefficientSource.Manual) });
        CoefficientObservation[] clears = await _context.CoefficientObservations.Where(value => value.IsCleared).ToArrayAsync();
        clears.Should().HaveCount(2);
        clears.Should().OnlyContain(value => value.DofusDbId == 1 && value.ServerName == Server && value.CoefficientPercent == 0);
        clears.Select(value => value.Source).Should().BeEquivalentTo(new[] { CoefficientSource.Manual, CoefficientSource.InGameAutomatic });
        clears.Select(value => value.ObservedAtUtc).Distinct().Should().ContainSingle();
        (await _context.CoefficientObservations.CountAsync(value => !value.IsCleared)).Should().Be(hasDofocus ? 5 : 4);
    }

    [Fact]
    public async Task DofocusFallbackIgnoresHistoricalDofocusClearMarkers()
    {
        await Store(Coefficient(1, 500, CoefficientSource.DofocusInitial),
            Coefficient(1, 600, CoefficientSource.DofocusInitial, at: ObservedAt.AddMinutes(1)),
            Coefficient(1, 0, CoefficientSource.DofocusInitial, at: ObservedAt.AddMinutes(2), cleared: true),
            Coefficient(1, 200, CoefficientSource.Manual),
            Coefficient(1, 0, CoefficientSource.Manual, at: ObservedAt.AddMinutes(3), cleared: true),
            Coefficient(1, 300, CoefficientSource.InGameAutomatic),
            Coefficient(1, 0, CoefficientSource.InGameAutomatic, at: ObservedAt.AddMinutes(4), cleared: true));

        (await _service.GetLatestObservationAsync(1, Server))!.CoefficientPercent.Should().Be(600);
        (await _service.GetLatestObservationsForServerAsync(Server))[1].CoefficientPercent.Should().Be(600);
        (await _service.GetLatestLocalSourceObservationsForServerAsync(Server)).Should().BeEmpty();
    }

    [Fact]
    public async Task NewCaptureAfterClearBecomesEffectiveAgain()
    {
        await Store(Coefficient(1, 900, CoefficientSource.DofocusInitial),
            Coefficient(1, 0, CoefficientSource.Manual, cleared: true),
            Coefficient(1, 0, CoefficientSource.InGameAutomatic, cleared: true));
        DateTime before = DateTime.UtcNow;

        CoefficientObservation added = await _service.AddObservationAsync(1, Server, 371, CoefficientSource.InGameAutomatic);

        added.CoefficientPercent.Should().Be(371);
        added.IsCleared.Should().BeFalse();
        added.ObservedAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        (await _service.GetLatestObservationAsync(1, Server))!.Id.Should().Be(added.Id);
    }

    [Fact]
    public async Task IdenticalCoefficientsAreAppendedWithoutPriceStyleDeduplication()
    {
        CoefficientObservation first = await _service.AddObservationAsync(1, Server, 371, CoefficientSource.InGameAutomatic);
        CoefficientObservation second = await _service.AddObservationAsync(1, Server, 371, CoefficientSource.InGameAutomatic);

        second.Id.Should().NotBe(first.Id);
        using BestCrushDbContext persisted = _database.CreateContext();
        CoefficientObservation[] observations = await persisted.CoefficientObservations.ToArrayAsync();
        observations.Should().HaveCount(2);
        observations.Should().OnlyContain(value => value.CoefficientPercent == 371 && !value.IsCleared);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RejectsNonPositiveCoefficientWithoutWriting(double coefficient)
    {
        ArgumentOutOfRangeException exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.AddObservationAsync(1, Server, coefficient, CoefficientSource.Manual));

        exception.ParamName.Should().Be("coefficientPercent");
        (await _context.CoefficientObservations.AnyAsync()).Should().BeFalse();
    }

    private static CoefficientObservation Coefficient(long id, double percent, CoefficientSource source,
        string server = Server, DateTime? at = null, bool cleared = false) => new()
        {
            DofusDbId = id, ServerName = server, CoefficientPercent = percent,
            Source = source, ObservedAtUtc = at ?? ObservedAt, IsCleared = cleared
        };

    private async Task Store(params CoefficientObservation[] observations)
    {
        _context.CoefficientObservations.AddRange(observations);
        await _context.SaveChangesAsync();
    }
}
