using BestCrush.Domain.Models;
using DofusSharp.DofusDb.ApiClients.Models.Jobs;
using DofusSharp.DofusDb.ApiClients.Search;

namespace BestCrush.Services;

public sealed partial class ActivityCatalogService
{
    public async Task<ProductCraftRecipe[]> GetProductCraftsAsync(CancellationToken token)
    {
        var jobs = await CachedAsync("jobs-v1", async () => (await Fetch(factory.Jobs(), new(), token)).ToArray(), token);
        return await CachedAsync("product-crafts-v1", () => LoadProductCraftsAsync(jobs, token), token);
    }

    private async Task<ProductCraftRecipe[]> LoadProductCraftsAsync(
        DofusDbJob[] jobs, CancellationToken token)
    {
        var equipmentTypes = Enum.GetValues<EquipmentType>().Select(type => type.ToDofusDbItemTypeId().ToString()).ToArray();
        var recipes = await Fetch(factory.Recipes(), new()
        {
            Predicates = [new DofusDbSearchPredicate.NotIn("resultTypeId", equipmentTypes)],
            Select = ["id", "resultId", "resultTypeId", "resultLevel", "resultName", "ingredientIds",
                "quantities", "jobId", "result", "ingredients", "resultType"]
        }, token);

        var results = new List<ProductCraftRecipe>();
        foreach (var recipe in recipes)
        {
            token.ThrowIfCancellationRequested();
            if (recipe.ResultId is not > 0 || recipe.ResultTypeId is not > 0 ||
                EquipmentTypeExtensions.EquipmentTypeFromDofusDbTypeId(recipe.ResultTypeId.Value) is not null ||
                recipe.IngredientIds is not { Count: > 0 } ids || recipe.Quantities is not { } quantities ||
                ids.Count != quantities.Count || ids.Any(id => id <= 0) || quantities.Any(count => count <= 0))
                continue;

            var item = recipe.Result;
            string? name = item?.Name?.Fr ?? item?.Name?.En ?? recipe.ResultName?.Fr ?? recipe.ResultName?.En;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var product = new CraftedProduct(recipe.ResultId.Value, name, item?.Level ?? recipe.ResultLevel ?? 0,
                item?.IconId, RecipeMarketType(recipe.ResultTypeId), recipe.ResultTypeId.Value,
                recipe.ResultType?.Name?.Fr ?? recipe.ResultType?.Name?.En ?? "Autre produit",
                jobs.FirstOrDefault(job => job.Id == recipe.JobId)?.Name?.Fr, recipe.ResultLevel ?? item?.Level);

            var items = (recipe.Ingredients ?? []).Where(ingredient => ingredient.Id is > 0)
                .DistinctBy(ingredient => ingredient.Id).ToDictionary(ingredient => ingredient.Id!.Value);
            var ingredients = ids.Select((id, index) =>
            {
                var ingredient = items.GetValueOrDefault(id);
                return new CraftRecipeIngredient(id, ingredient?.Name?.Fr ?? ingredient?.Name?.En ?? $"Objet #{id}",
                    RecipeMarketType(ingredient?.TypeId), quantities[index], ingredient?.IconId);
            }).ToArray();
            results.Add(new(product, ingredients));
        }
        return results.DistinctBy(recipe => recipe.Product.DofusDbId).ToArray();
    }

    private static MarketObjectType RecipeMarketType(long? typeId) => typeId switch
    {
        78 => MarketObjectType.Rune,
        long id when EquipmentTypeExtensions.EquipmentTypeFromDofusDbTypeId(id) is not null => MarketObjectType.Equipment,
        _ => MarketObjectType.Resource
    };
}
