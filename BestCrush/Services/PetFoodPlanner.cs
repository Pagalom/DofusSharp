using System.Numerics;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

public sealed record PetMeal(PetFood Food, int Quantity, int FromStock, int Crafted, int Bought)
{
    public int Experience => PetExperience.MealExperience(Food.Experience, Quantity);
}
public sealed record PetShoppingLine(long ItemId, string Name, MarketObjectType Type, MarketPurchaseResult Purchase, bool Ingredient);
public sealed record PetCraftStep(ProductCraftRecipe Recipe, int Quantity, IReadOnlyList<StockIngredientPlan> Ingredients);
public sealed record PetNutritionPlan(IReadOnlyList<PetMeal> Meals, IReadOnlyList<PetShoppingLine> Purchases,
    IReadOnlyList<PetCraftStep> Crafts, long CashRequired, double? StockValue,
    IReadOnlyDictionary<long,int> StockConsumed)
{
    public double? EconomicCost => StockValue + CashRequired;
    public int Experience => Meals.GroupBy(meal => meal.Food.Id).Sum(group =>
        PetExperience.MealExperience(group.First().Food.Experience, group.Sum(meal => meal.Quantity)));
    public static PetNutritionPlan Empty { get; } = new([],[],[],0,0,new Dictionary<long,int>());
}

/// <summary>
/// Selects one food by its estimated kamas per XP, then prices that food's actual lots.
/// Stock, direct purchase and accessible recipes contribute to the unit-cost estimate;
/// different foods and stock prefixes are never combined or searched recursively.
/// Only source data and a bounded cache of completed plans survive between targets.
/// </summary>
internal sealed class PetFoodPlanner(MarketPriceService prices, IReadOnlyList<PetFood> foods,
    IReadOnlyList<ProductCraftRecipe> recipes, IReadOnlyDictionary<long,BigInteger> stock,
    EquipmentProfitabilityContext market,PetNutritionCache? cache=null)
{
    private readonly PetNutritionCache _cache=cache ?? new();
    private readonly Dictionary<(MarketObjectType,long),IReadOnlyDictionary<(long,int),MarketPriceObservation>> _quotes=[];
    private sealed record Supply(double? PurchaseUnit,double? StockUnit,int Available);
    private sealed record Ingredient(Supply Supply,int Count);
    private sealed record FoodSource(PetFood Food,Supply Supply,Ingredient[][] Recipes);
    private readonly Dictionary<(MarketObjectType,long),Supply> _supplies=[];
    private FoodSource[]? _sources;
    private readonly ILookup<long,ProductCraftRecipe> _recipes=recipes.ToLookup(recipe=>recipe.Product.DofusDbId);
    private readonly PetFood[] _foods=foods.Where(food=>double.IsFinite(food.Experience) && food.Experience>0 && (stock.GetValueOrDefault(food.Id)>0 ||
        StockCraftPlanner.PricesFor(market,food.MarketType).Keys.Any(key=>key.DofusDbId==food.Id) ||
        recipes.Any(recipe=>recipe.Product.DofusDbId==food.Id))).ToArray();

    internal PetNutritionPlan? Calculate(int needed,CancellationToken token=default,int maximumExperience=int.MaxValue)
    {
        token.ThrowIfCancellationRequested();
        if (needed<=0) return PetNutritionPlan.Empty;
        if (maximumExperience<needed) return null;
        if (_cache.TryGet((needed,maximumExperience),out var cached)) return cached;
        _sources ??= _foods.Select(food=>new FoodSource(food,SupplyFor(food.Id,food.MarketType),
            _recipes[food.Id].Where(recipe=>recipe.Ingredients.Count>0 &&
                recipe.Ingredients.All(i=>i.ItemId!=food.Id && i.Count>0))
                .Select(recipe=>recipe.Ingredients.GroupBy(i=>i.ItemId)
                    .Select(group=>new Ingredient(SupplyFor(group.Key,group.First().MarketType),checked(group.Sum(i=>i.Count)))).ToArray())
                .ToArray())).ToArray();
        FoodSource? selected=null;
        int quantity=0;
        double bestRatio=double.PositiveInfinity,bestCash=double.PositiveInfinity;
        foreach(var source in _sources)
        {
            token.ThrowIfCancellationRequested();
            if(PetExperience.UnitsFor(source.Food.Experience,needed) is not { } units ||
                PetExperience.MealExperience(source.Food.Experience,units)>maximumExperience) continue;
            var estimate=Estimate(source,units);
            if(estimate.Cash>long.MaxValue) continue;
            double ratio=estimate.Cost is { } cost ? cost/(units*source.Food.Experience) : double.PositiveInfinity;
            if(selected is null || ratio<bestRatio || ratio==bestRatio && (estimate.Cash<bestCash ||
                estimate.Cash==bestCash && source.Food.Id<selected.Food.Id))
            {
                selected=source;quantity=units;bestRatio=ratio;bestCash=estimate.Cash;
            }
        }
        var plan=selected is null ? null : BuildFood(selected.Food,quantity,stock,token);
        token.ThrowIfCancellationRequested();
        return _cache.Store((needed,maximumExperience),plan);
    }

    private Supply SupplyFor(long id,MarketObjectType type)
    {
        if(_supplies.TryGetValue((type,id),out var supply)) return supply;
        var quote=Quotes(id,type).Values.MinBy(q=>(double)q.Price/q.Quantity);
        return _supplies[(type,id)]=new(quote is null ? null : (double)quote.Price/quote.Quantity,
            prices.CalculateValue(id,1,Quotes(id,type))?.Value,
            (int)BigInteger.Min(int.MaxValue,BigInteger.Max(0,stock.GetValueOrDefault(id))));
    }

    // Scalar estimates only: no shopping lists, meal mixtures or quantity-dependent
    // memoization for rejected foods. Lot rounding is applied to the selected food.
    private static (double? Cost,double Cash) Estimate(FoodSource source,int quantity)
    {
        double? bestCost=null;
        double bestCash=double.PositiveInfinity;
        void ConsiderEstimate(double? cost,double cash)
        {
            if(cost.HasValue && !bestCost.HasValue || cost<bestCost || cost==bestCost && cash<bestCash)
            {bestCost=cost;bestCash=cash;}
        }
        int owned=Math.Min(quantity,source.Supply.Available);
        for(int stockChoice=0;stockChoice<(owned>0 ? 2 : 1);stockChoice++)
        {
            int use=stockChoice==0 ? 0 : owned,missing=quantity-use;
            double? stockValue=use==0 ? 0 : source.Supply.StockUnit*use;
            if(missing==0 || source.Supply.PurchaseUnit.HasValue)
            {
                double cash=missing*(source.Supply.PurchaseUnit ?? 0);
                ConsiderEstimate(stockValue+cash,cash);
            }
            foreach(var recipe in source.Recipes)
            {
                int free=missing;
                foreach(var ingredient in recipe) free=Math.Min(free,ingredient.Supply.Available/ingredient.Count);
                for(int craftChoice=0;craftChoice<(free>0 && free<missing ? 2 : 1);craftChoice++)
                {
                    int crafts=craftChoice==0 ? missing : free;
                    if(crafts==0 || crafts<missing && !source.Supply.PurchaseUnit.HasValue) continue;
                    for(int ingredientChoice=0;ingredientChoice<2;ingredientChoice++)
                    {
                        double cash=(missing-crafts)*(source.Supply.PurchaseUnit ?? 0);
                        double? value=stockValue;
                        bool valid=true;
                        foreach(var ingredient in recipe)
                        {
                            long count=(long)ingredient.Count*crafts;
                            if(count>int.MaxValue){valid=false;break;}
                            int used=ingredientChoice==0 ? Math.Min((int)count,ingredient.Supply.Available) : 0;
                            long buy=count-used;
                            if(buy>0 && !ingredient.Supply.PurchaseUnit.HasValue){valid=false;break;}
                            cash+=buy*(ingredient.Supply.PurchaseUnit ?? 0);
                            if(used>0)value+=ingredient.Supply.StockUnit*used;
                        }
                        if(valid)ConsiderEstimate(value+cash,cash);
                    }
                }
            }
        }
        return (bestCost,bestCash);
    }

    private PetNutritionPlan? BuildFood(PetFood food,int quantity,IReadOnlyDictionary<long,BigInteger> available,CancellationToken token)
    {
        PetNutritionPlan? best=null;
        int owned=(int)BigInteger.Min(quantity,BigInteger.Max(0,available.GetValueOrDefault(food.Id)));
        foreach (int use in new[]{0,owned}.Distinct())
        {
            int missing=quantity-use;
            var stockUse=new Dictionary<long,int>();
            if (use>0) stockUse[food.Id]=use;
            double? ownedValue=OwnedValue(food.Id,food.MarketType,use);
            if (Buy(food.Id,food.MarketType,missing) is { } purchase)
                Consider(new([new(food,quantity,use,0,purchase.PurchasedQuantity)],
                    missing==0 ? [] : [new(food.Id,food.Name,food.MarketType,purchase,false)],[],purchase.TotalCost,ownedValue,stockUse),ref best);
            foreach (var recipe in _recipes[food.Id])
            {
                if (missing==0 || recipe.Ingredients.Count==0 || recipe.Ingredients.Any(i=>i.ItemId==food.Id || i.Count<=0)) continue;
                int free=(int)BigInteger.Min(missing,recipe.Ingredients.Min(i=>
                    BigInteger.Max(0,available.GetValueOrDefault(i.ItemId)-stockUse.GetValueOrDefault(i.ItemId))/i.Count));
                var quantities=new[]{free,missing}.Where(q=>q>0 && q<=missing).Distinct();
                foreach (int crafts in quantities)
                foreach (bool ingredientsFromStock in new[]{true,false})
                {
                    token.ThrowIfCancellationRequested();
                    var consumed=stockUse.ToDictionary();
                    var ingredientLines=new List<StockIngredientPlan>();
                    var shopping=new List<PetShoppingLine>();
                    long cash=0;
                    double? value=ownedValue;
                    bool valid=true;
                    foreach (var ingredient in recipe.Ingredients.GroupBy(i=>i.ItemId))
                    {
                        var first=ingredient.First();
                        long count=(long)ingredient.Sum(i=>i.Count)*crafts;
                        if (count>int.MaxValue) {valid=false;break;}
                        var have=BigInteger.Max(0,available.GetValueOrDefault(first.ItemId)-consumed.GetValueOrDefault(first.ItemId));
                        int used=ingredientsFromStock ? (int)BigInteger.Min(count,have) : 0;
                        int toBuy=(int)count-used;
                        var bought=Buy(first.ItemId,first.MarketType,toBuy);
                        if (bought is null) {valid=false;break;}
                        if (used>0) consumed[first.ItemId]=consumed.GetValueOrDefault(first.ItemId)+used;
                        var stockValue=OwnedValue(first.ItemId,first.MarketType,used);
                        value+=stockValue;cash=checked(cash+bought.TotalCost);
                        ingredientLines.Add(new(first.ItemId,first.Name,first.MarketType,(int)count,have,used,toBuy,bought,
                            stockValue is { } cost ? new(cost,false) : null,new(0,false)){IconId=first.IconId});
                        if (toBuy>0) shopping.Add(new(first.ItemId,first.Name,first.MarketType,bought,true));
                    }
                    if (!valid || Buy(food.Id,food.MarketType,missing-crafts) is not { } extra) continue;
                    cash=checked(cash+extra.TotalCost);
                    if (missing>crafts) shopping.Add(new(food.Id,food.Name,food.MarketType,extra,false));
                    Consider(new([new(food,quantity,use,crafts,extra.PurchasedQuantity)],shopping,
                        [new(recipe,crafts,ingredientLines)],cash,value,consumed),ref best);
                }
            }
        }
        return best;
    }

    private IReadOnlyDictionary<(long,int),MarketPriceObservation> Quotes(long id,MarketObjectType type)
    {
        if (!_quotes.TryGetValue((type,id),out var result))
            _quotes[(type,id)]=result=StockCraftPlanner.PricesFor(market,type).Where(pair=>pair.Key.DofusDbId==id &&
                pair.Value.Price>0 && !pair.Value.IsCleared && pair.Value.Quantity is 1 or 10 or 100 or 1000).ToDictionary();
        return result;
    }

    private double? OwnedValue(long id,MarketObjectType type,int quantity)
    {
        if(quantity==0) return 0;
        return prices.CalculateValue(id,quantity,Quotes(id,type))?.Value;
    }

    internal MarketPurchaseResult? Buy(long id,MarketObjectType type,int quantity)
    {
        if (quantity==0) return new(0,0,0,new Dictionary<int,int>());
        return CalculatePurchase(id,type,quantity);
    }

    private MarketPurchaseResult? CalculatePurchase(long id,MarketObjectType type,int quantity)
    {
        var quotes=Quotes(id,type).Values.OrderByDescending(quote=>quote.Quantity).ToArray();
        if (quotes.Length==0) return null;
        long best=long.MaxValue;int total=0;Dictionary<int,int>? selected=null;
        // Canonical lot sizes divide one another. For each size an optimal count
        // is zero, floor(remaining/size), or ceil(remaining/size): at most 3^4 leaves.
        Walk(0,quantity,0,0,[]);
        return selected is null ? null : new(best,quantity,total,selected,
            quotes.Where(quote=>selected.ContainsKey(quote.Quantity)).ToArray());
        void Walk(int index,int needed,long cost,long bought,Dictionary<int,int> lots)
        {
            if (cost>best) return;
            if (needed<=0)
            {
                if (bought>int.MaxValue) return;
                if (cost<best || (cost==best && bought<total)) {best=cost;total=(int)bought;selected=lots.ToDictionary();}
                return;
            }
            if (index>=quotes.Length) return;
            var quote=quotes[index];
            int floor=needed/quote.Quantity;
            foreach (int count in new[]{0,floor,floor+(needed%quote.Quantity==0 ? 0 : 1)}.Distinct())
            {
                if (count>0) lots[quote.Quantity]=count;else lots.Remove(quote.Quantity);
                if (count>0 && quote.Price>(long.MaxValue-cost)/count) continue;
                long units=(long)count*quote.Quantity;
                Walk(index+1,(int)Math.Max(0,needed-units),cost+count*quote.Price,bought+units,lots);
            }
            lots.Remove(quote.Quantity);
        }
    }

    private static void Consider(PetNutritionPlan? candidate,ref PetNutritionPlan? best)
    {
        if (candidate is null) return;
        if (best is null || (candidate.EconomicCost.HasValue && !best.EconomicCost.HasValue) ||
            candidate.EconomicCost<best.EconomicCost ||
            (candidate.EconomicCost==best.EconomicCost && candidate.CashRequired<best.CashRequired) ||
            (!candidate.EconomicCost.HasValue && !best.EconomicCost.HasValue && candidate.CashRequired<best.CashRequired)) best=candidate;
    }
}
