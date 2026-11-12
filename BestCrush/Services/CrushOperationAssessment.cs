using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

internal static class CrushOperationAssessment
{
    internal static async Task<CrushHistorySessionWriteModel> AddToHistoryAsync(IServiceProvider services,
        CrushHistorySessionWriteModel history, IReadOnlyList<NetworkCrushResultLine> lines,
        IReadOnlyDictionary<long, Equipment?> equipment, IReadOnlyDictionary<long, Rune> runes,
        IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> observations)
    {
        var quotes = observations.Values.Where(quote => !quote.IsCleared && quote.Price > 0 && quote.Quantity > 0)
            .GroupBy(quote => quote.DofusDbId).ToDictionary(group => group.Key, group => group.Min(q => q.Price / (double)q.Quantity));
        var runeMarket = runes.Values.Where(rune => Enum.IsDefined(rune.Characteristic))
            .Select(rune => new ForgemagingRune(rune.DofusDbId, rune.Name, rune.Characteristic,
                ForgemagingDataService.RuneGain(rune.Name, rune.Characteristic),
                quotes.TryGetValue(rune.DofusDbId, out double price) ? price : null)).ToArray();
        var store = services.GetService<ForgemagingSessionStore>();
        var keys = lines.Select(line => line.ObservedItem is { } item
            ? ForgemagedEquipmentStock.InstanceKey(history.ServerName, line.EquipmentDofusDbId, item.ItemUid) : null).ToArray();
        Dictionary<string, ForgemagingSession> sessions;
        try
        {
            sessions = store?.GetObserved(history.ServerName, lines.Select((line, index) => (line.EquipmentDofusDbId, Key: keys[index]))
                .Where(row => row.Key is not null).Select(row => (row.EquipmentDofusDbId, row.Key!))) ?? [];
        }
        catch (InvalidDataException error)
        {
            services.GetService<ILogger<CrushSessionService>>()?.LogWarning(error, "Could not read FM sessions for the crushing history");
            sessions = [];
        }

        var recipeEquipment = equipment.Values.OfType<Equipment>().Where(item => item.Recipe.Count + item.EquipmentRecipe.Count > 0).ToArray();
        var costs = new Dictionary<long, long?>();
        if (recipeEquipment.Length > 0)
        {
            var prices = services.GetRequiredService<MarketPriceService>();
            var craft = services.GetRequiredService<CraftCostService>();
            var resourcePrices = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Resource, history.ServerName);
            var equipmentPrices = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Equipment, history.ServerName);
            foreach (var item in recipeEquipment)
                costs[item.DofusDbId] = craft.Calculate(item, resourcePrices, equipmentPrices).TotalCost;
        }

        Dictionary<ulong, ForgemagingEffectDefinition>? effects = null;
        if (lines.Any(line => line.ObservedItem is { StatsComplete: true, Stats.Count: > 0 }) &&
            services.GetService<ActivityCatalogService>() is { } catalog)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try { effects = (await catalog.GetForgemagingEffectsAsync(timeout.Token)).ToDictionary(effect => effect.Code); }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or System.Text.Json.JsonException)
            { services.GetService<ILogger<CrushSessionService>>()?.LogDebug(error, "Crush effect catalogue unavailable"); }
        }

        double multiplier = 1 - history.DiscountPercent / 100;
        var assessed = history.Equipments.Select((row, index) =>
        {
            var line = lines[index];
            var item = equipment.GetValueOrDefault(line.EquipmentDofusDbId);
            var session = keys[index] is { } key ? sessions.GetValueOrDefault(key) : null;
            var roll = ResolveRoll(line, item, session, effects, history.StartedAtUtc);
            return row with { Comparison = Compare(line, item?.Level ?? session?.Level, roll,
                session?.Attempts.Where(attempt => attempt.FromGame && attempt.AtUtc <= history.StartedAtUtc).ToArray() ?? [],
                runeMarket, quotes, costs.GetValueOrDefault(line.EquipmentDofusDbId), multiplier) };
        }).ToArray();
        return history with { Equipments = assessed };
    }

    private static IReadOnlyList<ForgemagingLine>? ResolveRoll(NetworkCrushResultLine line, Equipment? equipment,
        ForgemagingSession? session, IReadOnlyDictionary<ulong, ForgemagingEffectDefinition>? effects, DateTime crushedAt)
    {
        var lastAttempt = session?.Attempts.LastOrDefault(attempt => attempt.FromGame && attempt.AtUtc <= crushedAt);
        var workshopAt = session?.LastObservedRollAtUtc is { } date && date <= crushedAt ? date : lastAttempt?.AtUtc;
        var workshopRoll = session?.LastObservedRollAtUtc == workshopAt &&
            session?.Lines is { Count: > 0 } lines && lines.All(l => l.Current.HasValue) ? lines : lastAttempt?.After;
        if (line.ObservedItem is { StatsComplete: true, Stats.Count: > 0 } observed && effects is not null &&
            (workshopRoll is null || workshopAt is null || observed.ObservedAtUtc >= workshopAt) &&
            ForgemagedEquipmentStock.MapEffects(observed.Stats.Select(stat => (stat.EffectId, (long?)stat.Value)), effects) is { } stats)
        {
            var native = equipment?.Characteristics.Select(line => line.Characteristic).ToHashSet() ?? [];
            return stats.Select(stat => new ForgemagingLine { Characteristic = stat.Key, Current = stat.Value,
                Minimum = native.Contains(stat.Key) ? 0 : null, Maximum = native.Contains(stat.Key) ? 0 : null }).ToArray();
        }
        // A more recent unreadable observation does not certify that the workshop's jet was still in use.
        return line.ObservedItem?.ObservedAtUtc > workshopAt ? null : workshopRoll;
    }

    internal static CrushEquipmentComparison Compare(NetworkCrushResultLine line, int? level, IReadOnlyList<ForgemagingLine>? roll,
        IReadOnlyList<ForgemagingAttempt> attempts, IReadOnlyList<ForgemagingRune> market,
        IReadOnlyDictionary<long, double> unitPrices, long? craftCost, double multiplier)
    {
        var comparison = level is { } itemLevel && roll is not null
            ? ForgemagingCrushAssessment.CompareRolls(itemLevel, roll, roll, market, new(line.CoefficientPercent, null, multiplier),
                includeRuneQuantities: true) : null;
        var expected = comparison is { IsComplete: true } ? comparison.Target : null;
        var expectedQuantities = expected?.RuneQuantities ?? new Dictionary<long, double>();
        var obtained = line.Runes.GroupBy(rune => rune.DofusDbId).ToDictionary(g => g.Key, g => g.Sum(rune => rune.Quantity));
        double? obtainedValue = obtained.Keys.All(unitPrices.ContainsKey)
            ? obtained.Sum(rune => rune.Value * unitPrices[rune.Key]) * multiplier : null;
        var names = market.ToDictionary(rune => rune.Id, rune => rune.Name);
        var rows = obtained.Keys.Union(expectedQuantities.Keys).Select(id => new CrushRuneComparison(
            names.GetValueOrDefault(id, $"Rune #{id}"), expected is null ? null : expectedQuantities.GetValueOrDefault(id),
            obtained.GetValueOrDefault(id))).OrderBy(rune => rune.Name).ToArray();
        return new(expected?.Value, obtainedValue, expected?.Focus, attempts.Sum(a => a.RuneCount),
            attempts.Sum(a => (a.Rune.UnitPrice ?? 0) * a.RuneCount),
            attempts.Where(a => a.Rune.UnitPrice is null).Sum(a => a.RuneCount), craftCost, rows)
        {
            FmBreakdown = attempts.GroupBy(a => (a.Rune.Id, a.Rune.Name, a.Outcome)).Select(group => new CrushFmRuneUse(
                group.Key.Name, group.Key.Outcome, group.Sum(a => a.RuneCount),
                group.Sum(a => (a.Rune.UnitPrice ?? 0) * a.RuneCount),
                group.Where(a => a.Rune.UnitPrice is null).Sum(a => a.RuneCount)))
                .OrderBy(rune => rune.Name).ThenBy(rune => rune.Outcome).ToArray()
        };
    }
}
