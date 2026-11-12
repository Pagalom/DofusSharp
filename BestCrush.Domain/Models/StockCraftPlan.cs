using System.Numerics;

namespace BestCrush.Domain.Models;

public sealed record StockIngredientPlan(long ItemId, string Name, MarketObjectType ObjectType,
    int Required, BigInteger Available, int Used, int Missing, MarketPurchaseResult? Purchase,
    MarketValueResult? OwnedValue, MarketValueResult? SurplusValue)
{
    public long? IconId { get; init; }
    public bool HasValuation => OwnedValue is not null && Purchase is not null && SurplusValue is not null;
    // Do not manufacture a profit by valuing leftover purchases above their cost basis.
    public double? SurplusCredit => Purchase is not null && SurplusValue is not null
        ? Math.Min(SurplusValue.Value.Value, Purchase.PurchasedQuantity > 0
            ? (double)Purchase.TotalCost * Purchase.SurplusQuantity / Purchase.PurchasedQuantity : 0) : null;
}

public sealed record CraftedProduct(long DofusDbId, string Name, int Level, long? DofusDbIconId,
    MarketObjectType MarketType, long TypeId, string TypeName, string? Job, int? JobLevel) : IItem;

public sealed record CraftRecipeIngredient(long ItemId, string Name, MarketObjectType MarketType, int Count, long? IconId);
public sealed record ProductCraftRecipe(CraftedProduct Product, IReadOnlyList<CraftRecipeIngredient> Ingredients);

public sealed record StockCraftPlan(IItem Product, int Quantity, BigInteger CraftableNow,
    IReadOnlyList<StockIngredientPlan> Ingredients)
{
    public MarketObjectType MarketType => Product is CraftedProduct product ? product.MarketType : MarketObjectType.Equipment;
    public bool HasRecipe => Ingredients.Count > 0;
    public bool HasOwnedIngredients => Ingredients.Any(x => x.Used > 0);
    public bool HasCompleteValuation => HasRecipe && Ingredients.All(x => x.HasValuation);
    public double? CashRequired => Ingredients.All(x => x.Purchase is not null)
        ? Ingredients.Sum(x => (double)x.Purchase!.TotalCost) : null;
    public double? OwnedValue => Ingredients.All(x => x.OwnedValue is not null)
        ? Ingredients.Sum(x => x.OwnedValue!.Value.Value) : null;
    public double? SurplusCredit => Ingredients.All(x => x.SurplusCredit is not null)
        ? Ingredients.Sum(x => x.SurplusCredit!.Value) : null;
    public double? EconomicCost => HasCompleteValuation ? OwnedValue + CashRequired - SurplusCredit : null;
    public IEnumerable<MarketPriceObservation> Observations => Ingredients.SelectMany(x =>
        (x.Purchase?.UsedObservations ?? []).Concat(x.OwnedValue?.UsedObservations ?? [])
        .Concat(x.SurplusValue?.UsedObservations ?? []));
}
