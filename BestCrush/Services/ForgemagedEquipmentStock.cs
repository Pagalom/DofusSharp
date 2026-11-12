using System.Security.Cryptography;
using System.Text;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed record OwnedForgemagingEquipment(string Key, Equipment Equipment, string Container, ulong Quantity,
    IReadOnlyList<ForgemagingLine>? Roll, bool RollFromWorkshop, IReadOnlyList<ForgemagingAttempt> Attempts,
    ForgemagingCrushComparison? Crushing, double? Gain, bool HasCoefficient, bool Fresh,
    double KnownRuneCost, int UnpricedRunes, bool UnobservedChanges)
{
    internal int RuneCount => Attempts.Sum(attempt => attempt.RuneCount);
}

internal static class ForgemagedEquipmentStock
{
    internal static string InstanceKey(string server, long itemId, ulong uid) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"fm-equipment:v1:{server.ToUpperInvariant()}:{itemId}:{uid}"))));

    internal static Dictionary<Characteristic, int>? MapEffects(IEnumerable<(ulong Code, long? Value)> effects,
        IReadOnlyDictionary<ulong, ForgemagingEffectDefinition> definitions)
    {
        Dictionary<Characteristic, int> values = [];
        foreach (var effect in effects)
        {
            if (!definitions.TryGetValue(effect.Code, out var definition)) return null;
            if (definition.Stat is not { } stat) continue;
            if (effect.Value is not (>= int.MinValue and <= int.MaxValue)) return null;
            long total = values.GetValueOrDefault(stat) + effect.Value.Value * definition.Sign;
            if (total is < int.MinValue or > int.MaxValue) return null;
            values[stat] = (int)total;
        }
        return values;
    }

    internal static async Task<OwnedForgemagingEquipment[]> LoadAsync(LiveStockSource source, string server,
        ForgemagingSessionStore store, ForgemagingDataService data, ActivityCatalogService? catalog,
        CoefficientService? coefficients, IBestCrushSettingsProvider? settings, bool crushing, CancellationToken token)
    {
        var stock = source.Containers.Where(c => c.Complete && !c.Stale && c.Container is "inventory" or "bank")
            .SelectMany(c => c.Items.Where(i => i.Uid > 0 && i.Quantity > 0 && i.ItemId is > 0 and <= long.MaxValue)
                .Select(i => (Container: c, Item: i, Key: InstanceKey(server, (long)i.ItemId, i.Uid))))
            .OrderByDescending(row => row.Container.UpdatedUtc).DistinctBy(row => row.Key).ToArray();
        if (stock.Length == 0) return [];

        var sessions = store.GetObserved(server, stock.Where(row => row.Item.Quantity == 1)
            .Select(row => ((long)row.Item.ItemId, row.Key)));
        if (crushing)
            stock = stock.Where(row => sessions.TryGetValue(row.Key, out var session) && session.Attempts.Any(a => a.FromGame)).ToArray();
        if (stock.Length == 0) return [];
        var equipment = (await data.GetOwnedEquipmentAsync(stock.Select(row => (long)row.Item.ItemId).Distinct().ToArray(), token))
            .ToDictionary(item => item.DofusDbId);
        stock = stock.Where(row => equipment.ContainsKey((long)row.Item.ItemId)).ToArray();
        if (stock.Length == 0) return [];

        Dictionary<ulong, ForgemagingEffectDefinition>? effects = null;
        if (catalog is not null && stock.Any(row => row.Item.EffectIssue is null && row.Item.Effects is { Count: > 0 }))
        {
            try { effects = (await catalog.GetForgemagingEffectsAsync(token)).ToDictionary(effect => effect.Code); }
            catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or OperationCanceledException or IOException or System.Text.Json.JsonException)
            { /* The verified workshop history still works without the effect catalogue. */ }
        }
        var market = crushing ? await data.GetRuneMarketAsync(server, token) : null;
        var observations = crushing && coefficients is not null
            ? await coefficients.GetLatestObservationsForServerAsync(server, token) : [];
        var multiplier = settings?.CrushValueMultiplier ?? 1;
        var rows = new List<OwnedForgemagingEquipment>();
        foreach (var row in stock)
        {
            var item = equipment[(long)row.Item.ItemId];
            var session = sessions.GetValueOrDefault(row.Key);
            var roll = ReadStockRoll(item, row.Item, effects);
            var coefficient = observations.GetValueOrDefault(item.DofusDbId);
            var context = new ForgemagingCrushContext(coefficient?.CoefficientPercent, coefficient?.ObservedAtUtc, multiplier);
            rows.Add(Project(row.Key, item, row.Container.Container, row.Item.Quantity, row.Container.UpdatedUtc,
                roll, session, market, context));
        }
        return rows.ToArray();
    }

    private static ForgemagingLine[]? ReadStockRoll(Equipment equipment, LiveStockItem item,
        IReadOnlyDictionary<ulong, ForgemagingEffectDefinition>? effects)
    {
        if (effects is null || item.EffectIssue is not null || item.Effects is not { Count: > 0 } entries ||
            MapEffects(entries.Select(entry => (entry.EffectCode, (long?)entry.Value)), effects) is not { } values)
            return null;
        var bounds = equipment.Characteristics.GroupBy(line => line.Characteristic).ToDictionary(group => group.Key, group => group.First());
        return bounds.Keys.Union(values.Keys).Order().Select(stat =>
        {
            var native = bounds.GetValueOrDefault(stat);
            return new ForgemagingLine
            {
                Characteristic = stat, Current = values.GetValueOrDefault(stat),
                Minimum = native is null ? null : Math.Min(native.From, native.To),
                Maximum = native is null ? null : Math.Max(native.From, native.To)
            };
        }).ToArray();
    }

    internal static OwnedForgemagingEquipment Project(string key, Equipment item, string container, ulong quantity,
        DateTime stockAt, IReadOnlyList<ForgemagingLine>? stockRoll, ForgemagingSession? session,
        ForgemagingRuneMarket? market, ForgemagingCrushContext context)
    {
        var attempts = session?.Attempts.Where(attempt => attempt.FromGame).ToArray() ?? [];
        var last = attempts.LastOrDefault();
        var workshopRoll = session?.Lines is { Count: > 0 } current && current.All(line => line.Current.HasValue) ? current : last?.After;
        bool fromWorkshop = workshopRoll is { Count: > 0 } &&
            (stockRoll is null || (session?.LastObservedRollAtUtc ?? last?.AtUtc) > stockAt);
        var roll = fromWorkshop ? workshopRoll : stockRoll;
        var before = attempts.FirstOrDefault()?.Before;
        var comparison = market is null ? null : ForgemagingCrushAssessment.CompareRolls(item.Level, before, roll, market.Runes, context);
        var knownCost = attempts.Sum(attempt => (attempt.Rune.UnitPrice ?? 0) * attempt.RuneCount);
        var unpriced = attempts.Where(attempt => attempt.Rune.UnitPrice is null).Sum(attempt => attempt.RuneCount);
        bool unobservedChanges = last is not null &&
            (roll is not null && !SameRoll(last.After, roll) ||
                attempts.Zip(attempts.Skip(1)).Any(pair => !SameRoll(pair.First.After, pair.Second.Before)));
        double? gain = comparison is { IsComplete: true, PreparationCeiling: { } improvement } && unpriced == 0 && !unobservedChanges
            ? improvement - knownCost : null;
        var neededStats = (before ?? []).Concat(roll ?? []).Where(line => line.Current is > 0).Select(line => line.Characteristic).Distinct();
        bool fresh = context.ObservedAtUtc is { } observed && DataFreshnessEvaluator.Evaluate(observed) == DataFreshness.Fresh &&
            market is not null && neededStats.All(stat => market.Runes
                .Where(rune => rune.Characteristic == stat && !rune.Name.StartsWith("Rune Pa ", StringComparison.OrdinalIgnoreCase) &&
                    !rune.Name.StartsWith("Rune Ra ", StringComparison.OrdinalIgnoreCase))
                .OrderBy(rune => rune.Id).FirstOrDefault() is { } rune &&
                market.ObservedAt.TryGetValue(rune.Id, out var date) && DataFreshnessEvaluator.Evaluate(date) == DataFreshness.Fresh);
        return new(key, item, container, quantity, roll, fromWorkshop, attempts, comparison, gain,
            context.CoefficientPercent is > 0, fresh, knownCost, unpriced, unobservedChanges);
    }

    private static bool SameRoll(IReadOnlyList<ForgemagingLine> left, IReadOnlyList<ForgemagingLine> right) =>
        left.Concat(right).All(line => line.Current is not null) &&
        left.Where(line => line.Current != 0).Select(line => (line.Characteristic, line.Current)).ToHashSet()
            .SetEquals(right.Where(line => line.Current != 0).Select(line => (line.Characteristic, line.Current)));
}
