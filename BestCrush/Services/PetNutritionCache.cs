using System.Collections.Concurrent;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

// Only completed plans are shared. Cancellation never stores a partial result.
internal sealed class PetNutritionCache
{
    private sealed record Entry(PetNutritionPlan? Plan);
    private readonly ConcurrentDictionary<(int Needed,int Maximum),Entry> _plans = new();
    internal int Count => _plans.Count;
    internal bool TryGet((int,int) key,out PetNutritionPlan? plan)
    {
        bool found=_plans.TryGetValue(key,out var entry); plan=entry?.Plan; return found;
    }
    internal PetNutritionPlan? Store((int,int) key,PetNutritionPlan? plan)
    {
        if (_plans.Count<2048) _plans.TryAdd(key,new(plan));
        return plan;
    }
    internal static string Fingerprint(IReadOnlyList<PetFood> foods,IReadOnlyList<ProductCraftRecipe> recipes,
        IReadOnlyDictionary<long,BigInteger> stock,EquipmentProfitabilityContext market)
    {
        var relevant=foods.Select(f=>f.Id).Concat(recipes.SelectMany(r=>r.Ingredients).Select(i=>i.ItemId)).ToHashSet();
        var data=new
        {
            Foods=foods.OrderBy(f=>f.Id),
            Recipes=recipes.OrderBy(r=>r.Product.DofusDbId),
            Stock=stock.Where(s=>relevant.Contains(s.Key) && s.Value>0).OrderBy(s=>s.Key).Select(s=>new{s.Key,Quantity=s.Value.ToString()}),
            Quotes=market.ResourcePrices.Values.Concat(market.RunePrices.Values).Concat(market.EquipmentPrices.Values)
                .Where(q=>relevant.Contains(q.DofusDbId)).OrderBy(q=>q.ObjectType).ThenBy(q=>q.DofusDbId).ThenBy(q=>q.Quantity)
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(data)));
    }
}
