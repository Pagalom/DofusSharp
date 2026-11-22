using System.Numerics;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Services;

public sealed record PetFoodValue(PetFood Food,long LotPrice,int LotQuantity)
{
    public DateTime? ObservedAtUtc {get;init;}
    public double UnitPrice => (double)LotPrice/LotQuantity;
    public double ExperiencePerKama => Food.Experience/UnitPrice;
}

internal sealed partial class PetOpportunityService
{
    internal async Task<PetOpportunityReport> LoadOverviewAsync(DofusClientWindowKey? window,string server,CancellationToken token)
    {
        var source=window is { } key ? liveStock.ForWindow(key) : null;
        var containers=string.Equals(source?.ServerName,server,StringComparison.OrdinalIgnoreCase)
            ? source!.Containers.Where(c=>c.Complete && !c.Stale && c.Container is "inventory" or "bank").ToArray() : [];
        Dictionary<long,BigInteger> Stock(string container)=>containers.Where(c=>c.Container==container).SelectMany(c=>c.Items)
            .Where(i=>i.ItemId<=long.MaxValue).GroupBy(i=>(long)i.ItemId)
            .ToDictionary(g=>g.Key,g=>g.Aggregate(BigInteger.Zero,(sum,i)=>sum+i.Quantity));
        var inventory=Stock("inventory");var bank=Stock("bank");
        await using var db=await database.CreateDbContextAsync(token);
        var species=(await catalog.GetKnownPetSpeciesAsync(token)).ToDictionary(s=>s.Id);
        foreach(var item in await db.Equipments.AsNoTracking().Where(e=>e.Type==EquipmentType.Pet || e.Type==EquipmentType.Petsmount).ToArrayAsync(token))
            species.TryAdd(item.DofusDbId,new(item.DofusDbId,item.Name,item.DofusDbIconId,item.Type==EquipmentType.Pet ? 18 : 121));
        var owned=containers.SelectMany(c=>c.Items.Where(i=>i.ItemId<=long.MaxValue && species.ContainsKey((long)i.ItemId))
            .Select(i=>new OwnedPet(species[(long)i.ItemId],i.Uid,i.Quantity,c.Container,
                PetMarketDecoder.Read(i.EffectWire,(ulong)species[(long)i.ItemId].TypeId)))).ToArray();
        var now=DateTimeOffset.UtcNow;
        var quotes=history.ReadPetPrices(server,now).ToDictionary(q=>q.Snapshot.ItemId);
        var histories=history.ReadHistories(server).Where(h=>h.Market.ObjectType==MarketObjectType.Equipment && h.Market.Variant.Length==0 &&
            now-h.ObservedAtUtc<=BreedingMarketDemand.Lifetime && h.ObservedAtUtc<=now).Select(h=>h.Market.ItemId).ToHashSet();
        var checks=new List<BreedingPriceCheck>();
        foreach(var pet in owned.DistinctBy(p=>p.Species.Id))
        {
            quotes.TryGetValue(pet.Species.Id,out var quote);
            bool stale=quote is null || PetDataFreshness.Price(quote.ObservedAtUtc.UtcDateTime,now.UtcDateTime)==DataFreshness.Stale;
            bool missing=owned.Where(p=>p.Species.Id==pet.Species.Id).Any(p=>p.Reading is { } reading &&
                !(quote?.Snapshot.Offers ?? []).Any(o=>o.Level==reading.Level && o.Variant==reading.Variant));
            bool higher=owned.Where(p=>p.Species.Id==pet.Species.Id).Any(p=>p.Reading is {Level:<100} reading &&
                !(quote?.Snapshot.Offers ?? []).Any(o=>o.Level>reading.Level && o.Variant==reading.Variant));
            if(stale || missing || higher || !histories.Contains(pet.Species.Id))
                checks.Add(new(pet.Species.Id,pet.Species.Name,quote?.ObservedAtUtc,stale ? "Actualiser les offres par niveau" :
                    missing ? "Relever le prix au niveau actuel" : higher ? "Comparer les niveaux supérieurs" : "Relever le Cours du marché"));
        }
        var definitions=FixedPetFoodCatalog.All;
        var rows=owned.Select(p=>new PetAssessment(p.Species,p,null,null,[],null,null)).ToArray();
        return new(server,rows,[],checks,inventory,bank,containers.Any(c=>c.Container=="inventory"),containers.Any(c=>c.Container=="bank"),
            professions.ForWindow(window,server) is {Count:>0},true,definitions.Count,0)
        {Stock=source,BudgetKamas=settings?.ProductionBudgetKamas is >0 and var budget ? budget : null,
            OwnedChecks=checks,PetPriceChecks=PetPriceReviews(owned.Select(p=>p.Species),quotes.ToDictionary(q=>q.Key,q=>q.Value.ObservedAtUtc),[],now.UtcDateTime),
            RelevantPriceItems=species.Keys.Concat(definitions.Keys).ToHashSet(),AnalysisComplete=false,OwnedAnalysisComplete=false};
    }

    internal static IReadOnlyList<BreedingPriceCheck> PetPriceReviews(IEnumerable<PetSpecies> species,
        IReadOnlyDictionary<long,DateTimeOffset> observed,IEnumerable<PetAssessment> plans,DateTime now)
    {
        var margins=plans.Where(p=>p.Best?.Margin is {} margin && double.IsFinite(margin))
            .GroupBy(p=>p.Species.Id).ToDictionary(g=>g.Key,g=>g.Max(p=>p.Best!.Margin!.Value));
        return species.DistinctBy(p=>p.Id).Where(p=>!observed.TryGetValue(p.Id,out var at) ||
                PetDataFreshness.Price(at.UtcDateTime,now)==DataFreshness.Stale)
            .Select(p=>new BreedingPriceCheck(p.Id,p.Name,observed.TryGetValue(p.Id,out var at)?at:null,"Actualiser les offres par niveau"))
            .OrderByDescending(p=>margins.GetValueOrDefault(p.ItemId,double.NegativeInfinity))
            .ThenBy(p=>p.LastObserved).ThenBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static PetFoodValue? BestFoodValue(IEnumerable<PetFood> foods,EquipmentProfitabilityContext market,long? budget=null) =>
        foods.Where(food=>double.IsFinite(food.Experience) && food.Experience>0)
            .SelectMany(food=>StockCraftPlanner.PricesFor(market,food.MarketType).Values.Where(quote=>quote.DofusDbId==food.Id &&
                quote.Quantity is 1 or 10 or 100 or 1000 && quote.Price>0 && !quote.IsCleared &&
                quote.ObservedAtUtc<=DateTime.UtcNow &&
                (budget is null || quote.Price<=budget))
                .Select(quote=>new PetFoodValue(food,quote.Price,quote.Quantity){ObservedAtUtc=quote.ObservedAtUtc}))
            .OrderByDescending(row=>row.ExperiencePerKama).ThenBy(row=>row.LotPrice).ThenBy(row=>row.Food.Id).FirstOrDefault();

    internal static IReadOnlyList<string> FoodPriceNames(IEnumerable<BreedingPriceCheck> checks,IReadOnlySet<long> knownExperienceItems)=>
        checks.OrderByDescending(check=>knownExperienceItems.Contains(check.ItemId))
            .Select(check=>check.Name).Where(name=>!string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).Take(50).ToArray();

}
