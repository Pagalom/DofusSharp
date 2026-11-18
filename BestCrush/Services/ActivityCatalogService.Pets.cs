using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using DofusSharp.DofusDb.ApiClients;
using DofusSharp.DofusDb.ApiClients.Models.Items;
using DofusSharp.DofusDb.ApiClients.Search;

namespace BestCrush.Services;

public sealed record PetSpecies(long Id, string Name, long? IconId, long TypeId) : IItem
{
    public long DofusDbId => Id;
    public long? DofusDbIconId => IconId;
    public int Level => 0;
}
public sealed record PetFood(long Id, string Name, long? IconId, MarketObjectType MarketType, double Experience, bool Craftable) : IItem
{
    public long DofusDbId => Id;
    public long? DofusDbIconId => IconId;
    public int Level => 0;
}

public sealed partial class ActivityCatalogService
{
    private ActivityCatalogService? _petProductionReference;
    // The usual Debug build uses beta DTOs, whose food actions are incomplete.
    // Pet feeding is played on the live client; use its production catalogue consistently.
    private ActivityCatalogService PetReference => Host==DofusDbClient.ProductionUri.Host ? this :
        _petProductionReference ??= new(DofusDbClient.Production(),http,cacheDirectory);
    internal Task<ProductCraftRecipe[]> GetPetRecipesAsync(IReadOnlyCollection<long> ids,CancellationToken token) =>
        PetReference.GetProductCraftsAsync(ids,token);

    internal Task<PetSpecies[]> GetPetSpeciesAsync(CancellationToken token) => !ReferenceEquals(PetReference,this)
        ? PetReference.GetPetSpeciesAsync(token) : CachedAsync("pet-species-v1", async () =>
        (await Fetch(factory.Items(), new()
        {
            Predicates = [new DofusDbSearchPredicate.In("typeId", ["18", "121"])],
            Select = ["id", "name", "iconId", "typeId", "evolutiveEffectIds"]
        }, token)).Where(item => item.Id > 0 && item.EvolutiveEffectIds is { Count: > 0 } && item.Name?.Fr is not null)
            .Select(item => new PetSpecies(item.Id!.Value, item.Name!.Fr!, item.IconId, item.TypeId!.Value)).ToArray(), token);

    internal Task<PetFood[]> GetPetFoodsAsync(CancellationToken token) => !ReferenceEquals(PetReference,this)
        ? PetReference.GetPetFoodsAsync(token) : CachedAsync("pet-foods-3.7.14-v1", async () =>
    {
        var types = await Fetch(factory.ItemTypes(), new()
        {
            Select = ["id", "superTypeId"]
        }, token);
        var allowed = types.Where(type => type.SuperTypeId == 9 && type.Id > 0).Select(type => type.Id!.Value).ToHashSet();
        var foods = await Fetch(factory.Items(), new()
        {
            Predicates = [new DofusDbSearchPredicate.In("typeId", allowed.Append(209).Select(id => id.ToString()).ToArray())],
            Select = ["id", "name", "iconId", "typeId", "recyclingNuggets", "possibleEffects", "hasRecipe"]
        }, token);
        var result = foods.Select(item => ReadPetFood(item, allowed)).OfType<PetFood>().DistinctBy(item => item.Id).ToArray();
        if (!result.Any(food => food.Id == 20125 && food.Experience == 500))
            throw new InvalidDataException("Le catalogue des nourritures ne permet pas d’identifier la Croquette enrichie.");
        return result;
    }, token);

    internal static PetFood? ReadPetFood(DofusDbItem item, IReadOnlySet<long> resourceTypes)
    {
        if (item.Id is not > 0 || item.Name?.Fr is not { Length: > 0 } name || item.TypeId is not { } type) return null;
        double xp = item.TypeId == 209
            ? item.PossibleEffects?.FirstOrDefault(effect => (effect.EffectId ?? effect.ActionId) == 2820)?.Value ?? 0
            : resourceTypes.Contains(type) ? (item.RecyclingNuggets ?? 0) * PetExperience.ResourceMultiplier : 0;
        return double.IsFinite(xp) && xp > 0 ? new(item.Id.Value, name, item.IconId,
            type == 78 ? MarketObjectType.Rune : MarketObjectType.Resource, xp, item.HasRecipe == true) : null;
    }
}
