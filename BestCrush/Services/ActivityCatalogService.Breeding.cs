using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using DofusSharp.DofusDb.ApiClients;
using DofusSharp.DofusDb.ApiClients.Models.Items;
using DofusSharp.DofusDb.ApiClients.Search;

namespace BestCrush.Services;

internal sealed record BreedingFuelItem(long Id, string Name, int Points, int Capacity, PaddockGauge Gauge);
public sealed record BreedingSpecies(long Id, string Name, long? FamilyId, int? Generation = null)
{
    internal string? FamilyName => FamilyId switch { 1 => "Dragodinde", 2 => "Muldo", 3 => "Volkorne", _ => null };
}
internal sealed record BreedingMarketItem(long Id, string Name);

public sealed partial class ActivityCatalogService
{
    internal Task<BreedingFuelItem[]> GetBreedingFuelsAsync(CancellationToken token) =>
        CachedAsync("breeding-fuels-v2", async () =>
        {
            var query = new DofusDbSearchQuery
            {
                Predicates = [new DofusDbSearchPredicate.Eq("typeId", "326")],
                Select = ["id", "name", "possibleEffects"]
            };
            var items = await Fetch(factory.Items(), query, token);
            Dictionary<long, PaddockGauge> productionGauges = [];
            if (Host != DofusDbClient.ProductionUri.Host && items.Any(item => UnidentifiedFuelEffect(item) is not null))
            {
                var reference = await Fetch(DofusDbClient.Production().Items(), query, token);
                productionGauges = reference.Select(item => ReadBreedingFuel(item)).OfType<BreedingFuelItem>()
                    .DistinctBy(item => item.Id).ToDictionary(item => item.Id, item => item.Gauge);
            }
            var fuels = items.Select(item => ReadBreedingFuel(item,
                    item.Id is { } id && productionGauges.TryGetValue(id, out var gauge) ? gauge : null))
                .OfType<BreedingFuelItem>().DistinctBy(item => item.Id).ToArray();
            if (fuels.Length == 0)
                throw new InvalidDataException("Le catalogue ne contient aucun carburant d’élevage identifiable.");
            return fuels;
        }, token);

    internal async Task<BreedingSpecies[]> GetBreedingSpeciesAsync(CancellationToken token)
    {
        var items = (await GetBreedingMarketItemsAsync(token)).ToDictionary(item => item.Id);
        var mounts = await CachedAsync("breeding-species-v1", async () => (await Fetch(factory.Mounts(), new()
        {
            Select = ["id", "name", "familyId"]
        }, token)).Where(mount => mount.Id > 0)
            .Select(mount => new BreedingSpecies(mount.Id!.Value, mount.Name?.Fr ?? $"Monture #{mount.Id}", null))
            .DistinctBy(mount => mount.Id).ToArray(), token);
        var previous = mounts.ToDictionary(mount => mount.Id);
        return BreedingLineage.Rides.Values.Select(ride => new BreedingSpecies(ride.Id,
                items.GetValueOrDefault(ride.ItemId)?.Name ?? previous.GetValueOrDefault(ride.Id)?.Name ?? $"Monture #{ride.Id}",
                ride.Family, ride.Generation))
            .Concat(mounts).DistinctBy(mount => mount.Id).ToArray();
    }

    internal Task<BreedingMarketItem[]> GetBreedingMarketItemsAsync(CancellationToken token) =>
        CachedAsync("breeding-market-items-v2", async () => (await Fetch(factory.Items(), new()
        {
            Predicates = [new DofusDbSearchPredicate.In("typeId", ["331", "332", "333"])],
            Select = ["id", "name"]
        }, token)).Where(item => item.Id > 0 && item.Name?.Fr is not null)
            .Select(item => new BreedingMarketItem(item.Id!.Value, item.Name!.Fr!))
            .DistinctBy(item => item.Id).ToArray(), token);

    internal static BreedingFuelItem? ReadBreedingFuel(DofusDbItem item, PaddockGauge? productionGauge = null)
    {
        var effect = item.PossibleEffects?.FirstOrDefault(effect =>
            GaugeOf(effect.EffectId ?? effect.ActionId) is not null);
        var gauge = GaugeOf(effect?.EffectId ?? effect?.ActionId);
        if (effect is null && productionGauge.HasValue)
        {
            // The beta catalog can omit the action IDs while keeping the dose and cap.
            effect = UnidentifiedFuelEffect(item);
            gauge = productionGauge;
        }
        if (item.Id is not > 0 || item.Name?.Fr is not { Length: > 0 } name ||
            gauge is null || effect?.Value is not > 0)
            return null;

        int capacity = effect.DiceSide switch
        {
            > 0 and <= BreedingFuel.Capacity / 100 => effect.DiceSide.Value * 100,
            0 when effect.DiceNum == 1 => BreedingFuel.Capacity,
            _ => 0
        };
        return capacity == 0 ? null : new(item.Id.Value, name, effect.Value.Value, capacity, gauge.Value);
    }

    private static DofusDbItemRawEffect? UnidentifiedFuelEffect(DofusDbItem item) =>
        item.PossibleEffects is { Count: 1 } &&
        item.PossibleEffects.First() is { EffectId: null, ActionId: null } effect ? effect : null;

    private static PaddockGauge? GaugeOf(long? effect) => effect switch
    {
        3810 => PaddockGauge.SerenityLoss,
        3812 => PaddockGauge.SerenityGain,
        3814 => PaddockGauge.Endurance,
        3816 => PaddockGauge.Maturity,
        3818 => PaddockGauge.Love,
        3820 => PaddockGauge.Experience,
        _ => null
    };
}
