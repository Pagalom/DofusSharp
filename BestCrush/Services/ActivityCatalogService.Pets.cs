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

    // Immediate inventory view: never wait for a catalogue download or cache refresh.
    internal async Task<PetSpecies[]> GetKnownPetSpeciesAsync(CancellationToken token)
    {
        if (!ReferenceEquals(PetReference,this)) return await PetReference.GetKnownPetSpeciesAsync(token);
        string key=CacheKey("pet-species-v1");
        if (_memory.TryGetValue(key,out var known)) return (PetSpecies[])known;
        try
        {
            string path=CachePath(key);
            return File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<Cached<PetSpecies[]>>(
                await File.ReadAllTextAsync(path,token))?.Data ?? [] : [];
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return []; }
    }

    internal Task<PetSpecies[]> GetPetSpeciesAsync(CancellationToken token) => !ReferenceEquals(PetReference,this)
        ? PetReference.GetPetSpeciesAsync(token) : CachedAsync("pet-species-v1", async () =>
        (await Fetch(factory.Items(), new()
        {
            Predicates = [new DofusDbSearchPredicate.In("typeId", ["18", "121"])],
            Select = ["id", "name", "iconId", "typeId", "evolutiveEffectIds"]
        }, token)).Where(item => item.Id > 0 && item.EvolutiveEffectIds is { Count: > 0 } && item.Name?.Fr is not null)
            .Select(item => new PetSpecies(item.Id!.Value, item.Name!.Fr!, item.IconId, item.TypeId!.Value)).ToArray(), token);

    internal Task<PetFood[]> GetPetFoodsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(FixedPetFoodCatalog.Foods);
    }
}
