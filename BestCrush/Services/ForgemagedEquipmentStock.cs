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
    internal bool HasOver { get; init; }
    internal bool HasExo { get; init; }
    public EquipmentAcquisition? Acquisition { get; init; }
    public long? AcquisitionCost { get; init; }
    internal bool FmCostUnknown => RuneCount == 0 && (HasOver || HasExo);
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
            // Effect 795 is a presence flag, encoded as zero even on native hunting weapons.
            // Its catalogue characteristic is 0; it must not be discarded as a non-stat effect.
            if (effect.Code == 795)
            {
                if (effect.Value is not (0 or 1) || !values.TryAdd(Characteristic.Hunting, 1)) return null;
                continue;
            }
            if (!definitions.TryGetValue(effect.Code, out var definition)) return null;
            if (definition.Stat is not { } stat) continue;
            if (effect.Value is not (>= int.MinValue and <= int.MaxValue)) return null;
            long total = values.GetValueOrDefault(stat) + effect.Value.Value * definition.Sign;
            if (total is < int.MinValue or > int.MaxValue) return null;
            values[stat] = (int)total;
        }
        return values;
    }

    internal static Dictionary<Characteristic,int>? MapWireEffects(IReadOnlyList<byte[]> entries,
        IReadOnlyDictionary<ulong,ForgemagingEffectDefinition> definitions) =>
        LiveForgemagingProtocol.ReadEffects(entries) is { } effects?MapEffects(effects.Select(e=>(e.Code,e.Value)),definitions):null;

    internal static async Task<OwnedForgemagingEquipment[]> LoadAsync(LiveStockSource source, string server,
        ForgemagingSessionStore store, ForgemagingDataService data, ActivityCatalogService? catalog,
        CoefficientService? coefficients, IBestCrushSettingsProvider? settings, bool crushing, CancellationToken token, long? itemId = null)
    {
        var stock = source.Containers.Where(c => c.Complete && !c.Stale && c.Container is "inventory" or "bank")
            .SelectMany(c => c.Items.Where(i => i.Uid > 0 && i.Quantity > 0 && i.ItemId is > 0 and <= long.MaxValue &&
                    (itemId is null || (long)i.ItemId == itemId))
                .Select(i => (Container: c, Item: i, Key: InstanceKey(server, (long)i.ItemId, i.Uid))))
            .OrderByDescending(row => row.Container.UpdatedUtc).DistinctBy(row => row.Key).ToArray();
        if (stock.Length == 0) return [];

        var instances = stock.Where(row => row.Item.Quantity == 1)
            .Select(row => (ItemId: (long)row.Item.ItemId, row.Key)).ToArray();
        var sessions = store.GetObserved(server, instances);
        var equipment = (await data.GetOwnedEquipmentAsync(stock.Select(row => (long)row.Item.ItemId).Distinct().ToArray(), token, forCrushing: crushing))
            .ToDictionary(item => item.DofusDbId);
        stock = stock.Where(row => equipment.ContainsKey((long)row.Item.ItemId)).ToArray();
        if (stock.Length == 0) return [];

        Dictionary<ulong, ForgemagingEffectDefinition>? effects = null;
        if (catalog is not null && stock.Any(row => row.Item.EffectWire is {Length: >0} || row.Item.EffectIssue is null && row.Item.Effects is { Count: > 0 }))
        {
            try { effects = (await catalog.GetForgemagingEffectsAsync(token)).ToDictionary(effect => effect.Code); }
            catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or OperationCanceledException or IOException or System.Text.Json.JsonException)
            { /* The verified workshop history still works without the effect catalogue. */ }
        }
        var acquisitions = crushing ? store.GetAcquisitions(server, instances) : [];
        var craftedIds = instances.Where(row => acquisitions.GetValueOrDefault(row.Key)?.Kind == EquipmentAcquisitionKind.Craft)
            .Select(row => row.ItemId).Distinct().ToArray();
        var craftCosts = craftedIds.Length > 0 ? await data.GetEquipmentCraftCostsAsync(server, craftedIds, token) : [];
        var market = crushing ? await data.GetRuneMarketAsync(server, token, gameOnly: true) : null;
        var observations = crushing && coefficients is not null
            ? await coefficients.GetLatestObservationsForServerAsync(server, token) : [];
        var multiplier = settings?.CrushValueMultiplier ?? 1;
        var rows = new List<OwnedForgemagingEquipment>();
        foreach (var row in stock)
        {
            var item = equipment[(long)row.Item.ItemId];
            var coefficient = observations.GetValueOrDefault(item.DofusDbId);
            var context = new ForgemagingCrushContext(coefficient?.CoefficientPercent, coefficient?.ObservedAtUtc, multiplier);
            var roll = ReadStockRoll(item, row.Item, effects);
            rows.Add(Project(row.Key, item, row.Container.Container, row.Item.Quantity, row.Container.UpdatedUtc,
                roll, sessions.GetValueOrDefault(row.Key), market, context,
                acquisitions.GetValueOrDefault(row.Key), craftCosts.GetValueOrDefault(item.DofusDbId)));
        }
        return rows.ToArray();
    }

    internal static ForgemagingLine[]? ReadStockRoll(Equipment equipment, LiveStockItem item,
        IReadOnlyDictionary<ulong, ForgemagingEffectDefinition>? effects)
    {
        if(effects is null)return null;
        var values=item.EffectWire is { } raw?MapWireEffects(raw,effects):
            item.EffectIssue is null && item.Effects is {Count:>0} entries
                ?MapEffects(entries.Select(e=>(e.EffectCode,(long?)e.Value)),effects):null;
        if(values is null)return null;
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
        ForgemagingRuneMarket? market, ForgemagingCrushContext context, EquipmentAcquisition? acquisition = null,
        CraftCostResult? craft = null)
    {
        var attempts = session?.Attempts.Where(attempt => attempt.FromGame).ToArray() ?? [];
        var last = attempts.LastOrDefault();
        var workshopRoll = session?.Lines is { Count: > 0 } current && current.All(line => line.Current.HasValue) ? current : last?.After;
        bool fromWorkshop = workshopRoll is { Count: > 0 } &&
            (stockRoll is null || (session?.LastObservedRollAtUtc ?? last?.AtUtc) > stockAt);
        var roll = fromWorkshop ? workshopRoll : stockRoll;
        bool hasOver = roll?.Any(line => line.Maximum is { } max && line.Current > max) == true;
        bool hasExo = item.Characteristics.Count > 0 && roll?.Any(line => !line.IsNative && line.Current > 0) == true;
        var before = attempts.FirstOrDefault()?.Before;
        var comparison = market is null ? null : ForgemagingCrushAssessment.CompareRolls(item.Level, before ?? roll, roll,
            market.Runes, context with { ResalePrices = market.ResalePrices });
        if (before is null && comparison is not null)
            comparison = comparison with { Current = null, PreparationCeiling = null };
        var knownCost = attempts.Sum(attempt => (attempt.Rune.UnitPrice ?? 0) * attempt.RuneCount);
        var unpriced = attempts.Where(attempt => attempt.Rune.UnitPrice is null).Sum(attempt => attempt.RuneCount);
        bool unobservedChanges = last is not null &&
            (roll is not null && !SameRoll(last.After, roll) ||
                attempts.Zip(attempts.Skip(1)).Any(pair => !SameRoll(pair.First.After, pair.Second.Before)));
        long? acquisitionCost = acquisition?.Kind switch
        {
            EquipmentAcquisitionKind.Purchase => acquisition.PricePaid,
            EquipmentAcquisitionKind.Craft => craft?.TotalCost,
            _ => null
        };
        bool fmCostKnown = unpriced == 0 && !unobservedChanges && (attempts.Length > 0 || (!hasOver && !hasExo));
        double? gain = comparison is { IsComplete: true, Target: { } target } && fmCostKnown
            ? target.Value - knownCost - acquisitionCost : null;
        bool fresh = context.ObservedAtUtc is { } observed && DataFreshnessEvaluator.Evaluate(observed) == DataFreshness.Fresh &&
            comparison is { IsComplete: true, Target.PriceFreshness: null or DataFreshness.Fresh } &&
            (comparison.Current?.PriceFreshness is null or DataFreshness.Fresh) &&
            (acquisition?.Kind != EquipmentAcquisitionKind.Craft || (craft is { IsComplete: true } &&
                craft.Resources.All(line => line.Purchase?.Freshness == DataFreshness.Fresh)));
        return new(key, item, container, quantity, roll, fromWorkshop, attempts, comparison, gain,
            context.CoefficientPercent is > 0, fresh, knownCost, unpriced, unobservedChanges)
        {
            HasOver = hasOver, HasExo = hasExo,
            Acquisition = acquisition, AcquisitionCost = acquisitionCost
        };
    }

    private static bool SameRoll(IReadOnlyList<ForgemagingLine> left, IReadOnlyList<ForgemagingLine> right) =>
        left.Concat(right).All(line => line.Current is not null) &&
        left.Where(line => line.Current != 0).Select(line => (line.Characteristic, line.Current)).ToHashSet()
            .SetEquals(right.Where(line => line.Current != 0).Select(line => (line.Characteristic, line.Current)));
}
