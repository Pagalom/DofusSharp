using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class DbContextLifetimeTest
    : IDisposable
{
    private readonly TestDatabase
        _database = new();

    public void Dispose() =>
        _database.Dispose();

    [Fact]
    public async Task RunesServiceUsesOneFactoryContextPerOperation()
    {
        await using (
            BestCrushDbContext seed =
                _database.CreateDbContext())
        {
            seed.Runes.AddRange(
                new Rune(1)
                {
                    Name = "Rune Fo",
                    Characteristic =
                        Characteristic.Strength
                },
                new Rune(2)
                {
                    Name = "Rune Pa Fo",
                    Characteristic =
                        Characteristic.Strength
                });

            await seed.SaveChangesAsync();
        }

        CountingFactory factory =
            new(_database);

        RunesService service =
            new(factory);

        Task<IReadOnlyCollection<Rune>>
            allRunesTask =
                service.GetRunesAsync();

        Task<IReadOnlyDictionary<
            Characteristic,
            Rune>>
            byCharacteristicTask =
                service
                    .GetRunesByCharacteristicAsync();

        await Task.WhenAll(
            allRunesTask,
            byCharacteristicTask);

        factory.CreateCount
            .Should()
            .Be(2);

        (await allRunesTask)
            .Should()
            .HaveCount(2);

        (await byCharacteristicTask)[
                Characteristic.Strength]
            .DofusDbId
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task CrushServiceUsesOneFactoryContextPerCalculation()
    {
        await using (
            BestCrushDbContext seed =
                _database.CreateDbContext())
        {
            seed.Runes.Add(
                new Rune(1)
                {
                    Name = "Rune Fo",
                    Characteristic =
                        Characteristic.Strength
                });

            await seed.SaveChangesAsync();
        }

        CountingFactory factory =
            new(_database);

        CrushService service =
            new(factory);

        service.GetCrushResult(
            new Dictionary<
                Characteristic,
                double>
            {
                [Characteristic.Strength] =
                    10
            },
            100,
            1);

        service.GetFocusedCrushResult(
            new Dictionary<
                Characteristic,
                double>
            {
                [Characteristic.Strength] =
                    10
            },
            Characteristic.Strength,
            100,
            1);

        factory.CreateCount
            .Should()
            .Be(2);
    }

    private sealed class CountingFactory(
        IDbContextFactory<
            BestCrushDbContext> inner)
        : IDbContextFactory<
            BestCrushDbContext>
    {
        private int _createCount;

        public int CreateCount =>
            Volatile.Read(
                ref _createCount);

        public BestCrushDbContext
            CreateDbContext()
        {
            Interlocked.Increment(
                ref _createCount);

            return inner
                .CreateDbContext();
        }
    }
}
