using BestCrush.Domain.Models;

namespace BestCrush.Services;

public static class RecipeArtwork
{
    public static IItem? Find(Equipment? equipment, CraftResourceCostLine line) => line.ObjectType == MarketObjectType.Equipment
        ? equipment?.EquipmentRecipe.FirstOrDefault(x => x.IngredientEquipment.DofusDbId == line.DofusDbId)?.IngredientEquipment
        : equipment?.Recipe.FirstOrDefault(x => x.Resource.DofusDbId == line.DofusDbId)?.Resource;
}
