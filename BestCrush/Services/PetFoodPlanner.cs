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
/// Compares every quoted food, accessible craft, stock prefix and mixed-food finishing route.
/// Stock is shared inside a plan, never duplicated between its recipe and its meals.
/// Alternatives for different pets are independent simulations, not stock reservations.
/// </summary>
internal sealed class PetFoodPlanner(MarketPriceService prices, IReadOnlyList<PetFood> foods,
    IReadOnlyList<ProductCraftRecipe> recipes, IReadOnlyDictionary<long,BigInteger> stock,
    EquipmentProfitabilityContext market)
{
    private readonly Dictionary<(int,int),PetNutritionPlan?> _cache=[];
    private readonly Dictionary<(MarketObjectType,long),IReadOnlyDictionary<(long,int),MarketPriceObservation>> _quotes=[];
    private readonly Dictionary<(MarketObjectType,long,int),MarketPurchaseResult?> _purchases=[];
    private readonly Dictionary<(MarketObjectType,long,int),double?> _values=[];
    private readonly Dictionary<(long,int,BigInteger,string),PetNutritionPlan?> _foodPlans=[];
    private readonly Dictionary<long,double> _lowerUnitCosts=[];
    private PetFood[]? _orderedFoods;
    private readonly ILookup<long,ProductCraftRecipe> _recipes=recipes.ToLookup(recipe=>recipe.Product.DofusDbId);
    private readonly Dictionary<long,long[]> _ingredients=recipes.GroupBy(recipe=>recipe.Product.DofusDbId)
        .ToDictionary(group=>group.Key,group=>group.SelectMany(recipe=>recipe.Ingredients).Select(i=>i.ItemId).Distinct().Order().ToArray());
    private readonly PetFood[] _foods=foods.Where(food=>stock.GetValueOrDefault(food.Id)>0 ||
        StockCraftPlanner.PricesFor(market,food.MarketType).Keys.Any(key=>key.DofusDbId==food.Id) ||
        recipes.Any(recipe=>recipe.Product.DofusDbId==food.Id)).ToArray();

    internal PetNutritionPlan? Calculate(int needed,CancellationToken token=default,int maximumExperience=int.MaxValue)
    {
        if (needed<=0) return PetNutritionPlan.Empty;
        if (maximumExperience<needed) return null;
        if (_cache.TryGetValue((needed,maximumExperience),out var cached)) return cached;
        PetNutritionPlan? best=Finish(needed,stock,token,maximumExperience);
        var prefix=PetNutritionPlan.Empty;
        var remainingStock=stock.ToDictionary();
        var available=_foods.Where(food=>stock.GetValueOrDefault(food.Id)>0)
            .Select(food=>(Food:food, Value:OwnedValue(food.Id,food.MarketType,1)))
            .OrderBy(entry=>entry.Value.HasValue ? entry.Value/entry.Food.Experience : double.MaxValue).ToArray();
        foreach (var entry in available)
        {
            token.ThrowIfCancellationRequested();
            if(best?.EconomicCost.HasValue==true && !entry.Value.HasValue) break;
            int remainder=needed-prefix.Experience;
            if (remainder<=0) { Consider(prefix,ref best);break; }
            if (PetExperience.UnitsFor(entry.Food.Experience,remainder) is not { } units) continue;
            units=(int)BigInteger.Min(units,remainingStock.GetValueOrDefault(entry.Food.Id));
            int allowed=maximumExperience-prefix.Experience;
            if (PetExperience.MealExperience(entry.Food.Experience,units)>allowed)
                units=(int)Math.Min(units,Math.Floor(allowed/entry.Food.Experience));
            if (units==0 || PetExperience.MealExperience(entry.Food.Experience,units)==0) continue;
            var use=new PetNutritionPlan([new(entry.Food,units,units,0,0)],[],[],0,
                OwnedValue(entry.Food.Id,entry.Food.MarketType,units),new Dictionary<long,int>{{entry.Food.Id,units}});
            prefix=Join(prefix,use);
            if(best?.EconomicCost is { } ceiling && prefix.EconomicCost>=ceiling) break;
            remainingStock[entry.Food.Id]-=units;
            var tail=Finish(needed-prefix.Experience,remainingStock,token,maximumExperience-prefix.Experience);
            if (tail is not null && Join(prefix,tail) is { } joined && joined.Experience<=maximumExperience) Consider(joined,ref best);
        }

        // Whole HDV lots can make two foods cheaper than either food alone.
        var starters=_foods.Select(food=>(Food:food,Quote:Quotes(food.Id,food.MarketType).Values
                .MinBy(quote=>(double)quote.Price/quote.Quantity)))
            .Where(entry=>entry.Quote is not null)
            .OrderBy(entry=>(double)entry.Quote!.Price/entry.Quote.Quantity/entry.Food.Experience).Take(8);
        foreach (var entry in starters)
        foreach (int quantity in Quotes(entry.Food.Id,entry.Food.MarketType).Values.Select(quote=>quote.Quantity).Distinct())
        {
            int xp=PetExperience.MealExperience(entry.Food.Experience,quantity);
            if (xp<=0 || xp>=needed) continue;
            var start=ForFood(entry.Food,xp,stock,token,maximumExperience);
            if (start is null || (best?.EconomicCost is { } current && start.EconomicCost>=current)) continue;
            var after=stock.ToDictionary();
            foreach (var used in start.StockConsumed) after[used.Key]-=used.Value;
            var tail=Finish(needed-start.Experience,after,token,maximumExperience-start.Experience);
            if (tail is not null && Join(start,tail) is { } joined && joined.Experience<=maximumExperience) Consider(joined,ref best);
        }
        return _cache[(needed,maximumExperience)]=best;
    }

    private PetNutritionPlan? Finish(int needed,IReadOnlyDictionary<long,BigInteger> available,CancellationToken token,int maximumExperience)
    {
        if (needed<=0) return PetNutritionPlan.Empty;
        PetNutritionPlan? best=null;
        _orderedFoods ??= _foods.OrderBy(food=>LowerUnitCost(food)/food.Experience).ToArray();
        foreach (var food in _orderedFoods)
        {
            token.ThrowIfCancellationRequested();
            if(best?.EconomicCost is { } ceiling && PetExperience.UnitsFor(food.Experience,needed) is { } units &&
                units*LowerUnitCost(food)>ceiling) continue;
            Consider(ForFood(food,needed,available,token,maximumExperience),ref best);
        }
        return best;
    }

    // Fractional purchases and unlimited use of existing stocks give a lower
    // bound, not a feasible plan. It is used only to rule out costlier routes.
    private double LowerUnitCost(PetFood food)
    {
        if(_lowerUnitCosts.TryGetValue(food.Id,out double cost)) return cost;
        double Ingredient(long id,MarketObjectType type)
        {
            var quotes=Quotes(id,type).Values.ToArray();
            double purchase=quotes.Select(q=>(double)q.Price/q.Quantity).DefaultIfEmpty(double.PositiveInfinity).Min();
            if(stock.GetValueOrDefault(id)<=0) return purchase;
            double resale=quotes.Select(q=>(double)Math.Max(1,q.Price-1)/q.Quantity).DefaultIfEmpty(0).Min();
            return Math.Min(purchase,resale);
        }
        cost=Ingredient(food.Id,food.MarketType);
        foreach(var recipe in _recipes[food.Id])
            cost=Math.Min(cost,recipe.Ingredients.Sum(i=>i.Count*Ingredient(i.ItemId,i.MarketType)));
        return _lowerUnitCosts[food.Id]=cost;
    }

    private PetNutritionPlan? ForFood(PetFood food,int needed,IReadOnlyDictionary<long,BigInteger> available,CancellationToken token,int maximumExperience)
    {
        if (PetExperience.UnitsFor(food.Experience,needed) is not { } quantity) return null;
        if (PetExperience.MealExperience(food.Experience,quantity)>maximumExperience) return null;
        // A stock-prefix simulation changes only a few resources. Reuse a food plan
        // when neither its stock nor the stock of any of its ingredients changed.
        string ingredients=_ingredients.TryGetValue(food.Id,out var ids)
            ? string.Join(',',ids.Select(id=>available.GetValueOrDefault(id))) : "";
        var key=(food.Id,quantity,BigInteger.Min(quantity,available.GetValueOrDefault(food.Id)),ingredients);
        if(_foodPlans.TryGetValue(key,out var cached)) return cached;
        return _foodPlans[key]=BuildFood(food,quantity,available,token);
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
                var quantities=new[]{1,10,100,1000,free,missing}.Where(q=>q>0 && q<=missing).Distinct();
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
        var key=(type,id,quantity);
        if(!_values.TryGetValue(key,out var value))
            _values[key]=value=prices.CalculateValue(id,quantity,Quotes(id,type))?.Value;
        return value;
    }

    internal MarketPurchaseResult? Buy(long id,MarketObjectType type,int quantity)
    {
        if (quantity==0) return new(0,0,0,new Dictionary<int,int>());
        var key=(type,id,quantity);
        if(_purchases.TryGetValue(key,out var cached)) return cached;
        return _purchases[key]=CalculatePurchase(id,type,quantity);
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
    private PetNutritionPlan Join(PetNutritionPlan first,PetNutritionPlan second)
    {
        var purchases=first.Purchases.Concat(second.Purchases).GroupBy(line=>(line.Type,line.ItemId))
            .Select(group=>new PetShoppingLine(group.Key.ItemId,group.First().Name,group.Key.Type,
                Buy(group.Key.ItemId,group.Key.Type,checked(group.Sum(line=>line.Purchase.RequiredQuantity)))!,group.Any(line=>line.Ingredient)))
            .ToArray();
        return new(first.Meals.Concat(second.Meals).ToArray(),purchases,
            first.Crafts.Concat(second.Crafts).ToArray(),purchases.Sum(line=>line.Purchase.TotalCost),
            first.StockValue+second.StockValue,first.StockConsumed.Concat(second.StockConsumed)
                .GroupBy(pair=>pair.Key).ToDictionary(group=>group.Key,group=>group.Sum(pair=>pair.Value)));
    }
}
