using System.Numerics;
using BestCrush.Domain.Models;
using BestCrush.Network.Protocol;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Services;

public sealed record PetFoodTest(PetFoodDefinition Food,BigInteger Inventory,BigInteger Bank,bool Recheck);

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
            bool stale=quote is null || now-quote.ObservedAtUtc>TimeSpan.FromHours(72);
            bool missing=owned.Where(p=>p.Species.Id==pet.Species.Id).Any(p=>p.Reading is { } reading &&
                !(quote?.Snapshot.Offers ?? []).Any(o=>o.Level==reading.Level && o.Variant==reading.Variant));
            bool higher=owned.Where(p=>p.Species.Id==pet.Species.Id).Any(p=>p.Reading is {Level:<100} reading &&
                !(quote?.Snapshot.Offers ?? []).Any(o=>o.Level>reading.Level && o.Variant==reading.Variant));
            if(stale || missing || higher || !histories.Contains(pet.Species.Id))
                checks.Add(new(pet.Species.Id,pet.Species.Name,quote?.ObservedAtUtc,stale ? "Actualiser les offres par niveau" :
                    missing ? "Relever le prix au niveau actuel" : higher ? "Comparer les niveaux supérieurs" : "Relever le Cours du marché"));
        }
        var definitions=await db.PetFoods.AsNoTracking().ToDictionaryAsync(f=>f.DofusDbId,token);
        // Every resource is a possible food, even if it has no entry or XP in the food catalogue yet.
        foreach(var resource in await db.Resources.AsNoTracking().ToArrayAsync(token))
            definitions.TryAdd(resource.DofusDbId,new(){DofusDbId=resource.DofusDbId,Name=resource.Name,DofusDbIconId=resource.DofusDbIconId,
                MarketType=MarketObjectType.Resource,Experience=resource.PetExperience});
        foreach(var rune in await db.Runes.AsNoTracking().ToArrayAsync(token))
            definitions.TryAdd(rune.DofusDbId,new(){DofusDbId=rune.DofusDbId,Name=rune.Name,DofusDbIconId=rune.DofusDbIconId,MarketType=MarketObjectType.Rune});
        string? build=clients?.Clients.FirstOrDefault(c=>c.Key==window)?.ClientBuild;
        var evidence=await PetFoodEvidenceReader.ReadAsync(db,definitions.Values.ToArray(),build,token);
        var tests=FoodTests(definitions.Values,evidence,inventory,bank);
        var rows=owned.Select(p=>new PetAssessment(p.Species,p,null,null,[],null,null)).ToArray();
        return new(server,rows,[],checks,inventory,bank,containers.Any(c=>c.Container=="inventory"),containers.Any(c=>c.Container=="bank"),
            professions.ForWindow(window,server) is {Count:>0},true,definitions.Count,0)
        {Stock=source,BudgetKamas=settings?.ProductionBudgetKamas is >0 and var budget ? budget : null,
            OwnedChecks=checks,FoodTests=tests,RelevantPriceItems=species.Keys.Concat(definitions.Keys).ToHashSet(),AnalysisComplete=false,OwnedAnalysisComplete=false};
    }

    internal static IReadOnlyList<PetFoodTest> FoodTests(IEnumerable<PetFoodDefinition> definitions,
        IReadOnlyDictionary<long,PetFoodEvidence> evidence,IReadOnlyDictionary<long,BigInteger> inventory,IReadOnlyDictionary<long,BigInteger> bank)=>
        definitions.Where(f=>evidence.GetValueOrDefault(f.DofusDbId) is not {Consistent:true,Matches:true})
            .Select(f=>new PetFoodTest(f,inventory.GetValueOrDefault(f.DofusDbId),bank.GetValueOrDefault(f.DofusDbId),evidence.ContainsKey(f.DofusDbId)))
            .OrderBy(t=>t.Inventory>0 ? 0 : t.Bank>0 ? 1 : 2)
            .ThenByDescending(t=>t.Recheck).ThenBy(t=>t.Food.Experience is >0 ? 1 : 0)
            .ThenBy(t=>t.Food.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
}
