using System.Numerics;
using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

/// <summary>Independent recipe plan; never reserves stock for other recipes.</summary>
public sealed class StockCraftPlanner(MarketPriceService prices)
{
    public StockCraftPlan Calculate(Equipment equipment, int quantity,
        IReadOnlyDictionary<long, BigInteger> stock, EquipmentProfitabilityContext market) =>
        Calculate(equipment, equipment.Recipe.Select(x => new CraftRecipeIngredient(x.Resource.DofusDbId,
                x.Resource.Name, MarketObjectType.Resource, x.Count, x.Resource.DofusDbIconId))
            .Concat(equipment.EquipmentRecipe.Select(x => new CraftRecipeIngredient(x.IngredientEquipment.DofusDbId,
                x.IngredientEquipment.Name, MarketObjectType.Equipment, x.Count, x.IngredientEquipment.DofusDbIconId))),
            quantity, stock, market);

    public StockCraftPlan Calculate(ProductCraftRecipe recipe, int quantity,
        IReadOnlyDictionary<long, BigInteger> stock, EquipmentProfitabilityContext market) =>
        Calculate(recipe.Product, recipe.Ingredients, quantity, stock, market);

    private StockCraftPlan Calculate(IItem product, IEnumerable<CraftRecipeIngredient> ingredients, int quantity,
        IReadOnlyDictionary<long, BigInteger> stock, EquipmentProfitabilityContext market)
    {
        if (quantity is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(quantity));
        List<StockIngredientPlan> lines = [];
        BigInteger? craftable = null;
        foreach (var group in ingredients.GroupBy(x => x.ItemId))
        {
            if (group.Any(x => x.Count <= 0) || group.Select(x => x.MarketType).Distinct().Count() != 1)
                throw new ArgumentException("Recette incohérente.", nameof(ingredients));
            int perCraft = checked(group.Sum(x => x.Count));
            int required = checked(perCraft * quantity);
            if (required > 250_000) throw new ArgumentOutOfRangeException(nameof(quantity), "Lot de fabrication trop important.");
            BigInteger available = BigInteger.Max(0, stock.GetValueOrDefault(group.Key));
            BigInteger possible = available / perCraft;
            craftable = craftable is null ? possible : BigInteger.Min(craftable.Value, possible);
            int used = (int)BigInteger.Min(required, available);
            int missing = required - used;
            var first = group.First();
            var observations = PricesFor(market, first.MarketType);
            MarketPurchaseResult? purchase;
            if (missing == 0) purchase = new(0, 0, 0, new Dictionary<int,int>());
            else if (first.MarketType == MarketObjectType.Equipment)
            {
                purchase = observations.TryGetValue((group.Key,1), out var single) && single.Price > 0 && !single.IsCleared
                    ? new(checked(single.Price * missing), missing, missing,
                        new Dictionary<int,int>{{1,missing}}, [single]) : null;
            }
            else purchase = prices.CalculateMinimumPurchaseCost(group.Key, missing, observations);
            var owned = prices.CalculateValue(group.Key, used, observations);
            MarketValueResult? surplus = purchase is null ? null
                : prices.CalculateValue(group.Key, purchase.SurplusQuantity, observations);
            lines.Add(new StockIngredientPlan(group.Key, first.Name, first.MarketType, required, available, used, missing, purchase, owned, surplus)
            { IconId = first.IconId });
        }
        return new(product, quantity, craftable ?? BigInteger.Zero,
            lines.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
    }

    public static IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> PricesFor(
        EquipmentProfitabilityContext market, MarketObjectType type) => type switch
    {
        MarketObjectType.Equipment => market.EquipmentPrices,
        MarketObjectType.Rune => market.RunePrices,
        _ => market.ResourcePrices
    };
}
