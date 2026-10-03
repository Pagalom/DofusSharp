using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using FluentAssertions;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class CrushServiceTest : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly BestCrushDbContext _context;
    private readonly CrushService _service;

    public CrushServiceTest()
    {
        _context = _database.CreateContext();
        _service = new CrushService(_context);
    }

    public void Dispose() => _database.Dispose();

    [Theory]
    [InlineData(Characteristic.Strength, 10, 100, 1, 16)]
    [InlineData(Characteristic.Strength, 10, 200, 1, 31)]
    [InlineData(Characteristic.Strength, 10, 100, 3.71, 59.36)]
    [InlineData(Characteristic.Strength, 10, 100, 0, 0)]
    [InlineData(Characteristic.Strength, 10, 100, -1, -16)]
    [InlineData(Characteristic.Ap, 1, 100, 1, 1.51)]
    [InlineData(Characteristic.Vitality, 20, 100, 1, 7)]
    [InlineData(Characteristic.Pods, 40, 100, 1, 6.4)]
    [InlineData(Characteristic.Initiative, 100, 100, 1, 16)]
    public async Task KeepsExistingWeightsLevelScalingAndUnroundedYield(
        Characteristic characteristic, double lineValue, int level, double coefficient, double expected)
    {
        await Store(new Rune(1) { Name = "Basic rune", Characteristic = characteristic });

        var result = _service.GetCrushResult(new Dictionary<Characteristic, double> { [characteristic] = lineValue }, level, coefficient);

        result.Should().HaveCount(1);
        result.Single().Key.DofusDbId.Should().Be(1);
        result.Single().Value.Should().BeApproximately(expected, 0.0000000001);
    }

    [Fact]
    public async Task PrefersLowestIdBasicRuneOverLowerIdPaAndRaVariantsIgnoringCase()
    {
        await Store(
            new Rune(101) { Name = "Rune Fo alternative", Characteristic = Characteristic.Strength },
            new Rune(2) { Name = "RUNE RA FO", Characteristic = Characteristic.Strength },
            new Rune(100) { Name = "Rune Fo", Characteristic = Characteristic.Strength },
            new Rune(1) { Name = "rune pa fo", Characteristic = Characteristic.Strength });

        var result = _service.GetCrushResult(new Dictionary<Characteristic, double> { [Characteristic.Strength] = 10 }, 100, 1);

        result.Should().HaveCount(1);
        result.Single().Key.DofusDbId.Should().Be(100);
        result.Single().Value.Should().Be(16);
    }

    [Fact]
    public async Task FallsBackToLowestIdVariantButStillReturnsBasicRuneEquivalentYield()
    {
        await Store(
            new Rune(7) { Name = "Rune Pa Vi", Characteristic = Characteristic.Vitality },
            new Rune(5) { Name = "Rune Ra Vi", Characteristic = Characteristic.Vitality });

        var result = _service.GetCrushResult(new Dictionary<Characteristic, double> { [Characteristic.Vitality] = 20 }, 100, 1);

        result.Should().HaveCount(1);
        result.Single().Key.DofusDbId.Should().Be(5);
        result.Single().Value.Should().Be(7);
    }

    [Fact]
    public async Task NormalCrushSkipsNonPositiveLinesAndCharacteristicsWithoutLocalRune()
    {
        await Store(
            new Rune(1) { Name = "Rune Fo", Characteristic = Characteristic.Strength },
            new Rune(2) { Name = "Rune Ine", Characteristic = Characteristic.Intelligence },
            new Rune(3) { Name = "Rune Age", Characteristic = Characteristic.Agility });
        var lines = new Dictionary<Characteristic, double>
        {
            [Characteristic.Strength] = 10,
            [Characteristic.Intelligence] = 0,
            [Characteristic.Agility] = -10,
            [Characteristic.Ap] = 1
        };

        var result = _service.GetCrushResult(lines, 100, 1);

        result.Should().HaveCount(1);
        result.Single().Key.Characteristic.Should().Be(Characteristic.Strength);
        result.Single().Value.Should().Be(16);
    }

    [Fact]
    public async Task FocusAddsHalfOfOtherPositiveWeightsEvenWithoutTheirRunesInCatalog()
    {
        await Store(new Rune(1) { Name = "Rune Fo", Characteristic = Characteristic.Strength });
        var lines = new Dictionary<Characteristic, double>
        {
            [Characteristic.Strength] = 10,
            [Characteristic.Wisdom] = 4,
            [Characteristic.Vitality] = 20,
            [Characteristic.Chance] = 0,
            [Characteristic.Power] = -10
        };

        var result = _service.GetFocusedCrushResult(lines, Characteristic.Strength, 100, 2);

        result.Should().HaveCount(1);
        result.Single().Key.DofusDbId.Should().Be(1);
        // At level 100: 16 focused weight + 9.5 wisdom + 3.5 vitality, then coefficient x2.
        result.Single().Value.Should().Be(58);
    }

    [Fact]
    public async Task FocusCanUseACharacteristicAbsentFromItemWhenItsRuneExists()
    {
        await Store(new Rune(1) { Name = "Rune Fo", Characteristic = Characteristic.Strength });

        var result = _service.GetFocusedCrushResult(
            new Dictionary<Characteristic, double> { [Characteristic.Wisdom] = 4 }, Characteristic.Strength, 100, 1);

        result.Should().HaveCount(1);
        result.Single().Value.Should().Be(9.5);
    }

    [Fact]
    public async Task FocusWithoutLocalTargetRuneReturnsNoResult()
    {
        await Store(new Rune(1) { Name = "Rune Sa", Characteristic = Characteristic.Wisdom });

        var result = _service.GetFocusedCrushResult(
            new Dictionary<Characteristic, double> { [Characteristic.Wisdom] = 4 }, Characteristic.Strength, 100, 1);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FocusKeepsZeroYieldEntryWhenNoLineIsPositive()
    {
        await Store(new Rune(1) { Name = "Rune Fo", Characteristic = Characteristic.Strength });
        var lines = new Dictionary<Characteristic, double> { [Characteristic.Strength] = 0, [Characteristic.Chance] = -2 };

        var focused = _service.GetFocusedCrushResult(lines, Characteristic.Strength, 100, 1);

        focused.Should().HaveCount(1);
        focused.Single().Value.Should().Be(0);
        _service.GetCrushResult(lines, 100, 1).Should().BeEmpty();
    }

    private async Task Store(params Rune[] runes)
    {
        _context.Runes.AddRange(runes);
        await _context.SaveChangesAsync();
    }
}
