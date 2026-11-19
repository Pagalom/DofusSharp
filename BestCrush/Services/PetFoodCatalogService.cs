using System.Numerics;
using System.Text.Json;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

internal sealed record PetFoodSeed(string Version,string SourceName,string SourceSha256,DateTimeOffset ObservedAtUtc,
    string Server,PetFoodSeedRow[] Records);
internal sealed record PetFoodSeedRow(long Id,string Name,long? IconId,MarketObjectType MarketType,double? Experience,
    double? SheetExperience,string ExperienceSource,bool Craftable,long? UnitPrice,string SheetName,int SheetRow);
public sealed record PetFoodDataRow(PetFoodDefinition Food,MarketPriceObservation? UnitQuote,double? UnitPrice,
    int? Quantity80,MarketPurchaseResult? Cost80,int? Quantity100,MarketPurchaseResult? Cost100)
{ public PetFoodEvidence? Evidence {get;init;} }

internal sealed class PetFoodCatalogService(IDbContextFactory<BestCrushDbContext> database,ActivityCatalogService catalog,
    IDataPriorityProvider priority,ILogger<PetFoodCatalogService> logger,CurrentDofusClientsState? clients=null)
{
    internal static readonly SemaphoreSlim Gate=new(1,1);
    internal static PetFoodSeed ReadSeed()
    {
        using var stream=typeof(PetFoodCatalogService).Assembly.GetManifestResourceStream("BestCrush.Data.pet-food-seed.json")!;
        return JsonSerializer.Deserialize<PetFoodSeed>(stream)!;
    }

    internal async Task<IReadOnlyList<PetFoodDefinition>> SynchronizeAsync(CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            await using var db=await database.CreateDbContextAsync(token);
            await ApplySeedAsync(db,ReadSeed(),token);
            var foods=await db.PetFoods.ToDictionaryAsync(food=>food.DofusDbId,token);
            try
            {
                foreach(var item in await catalog.GetPetFoodsAsync(token))
                {
                    if(!foods.TryGetValue(item.Id,out var food)) db.PetFoods.Add(foods[item.Id]=food=new(){DofusDbId=item.Id});
                    food.Name=item.Name;food.DofusDbIconId=item.IconId;food.MarketType=item.MarketType;
                    food.Craftable=item.Craftable;food.Experience=item.Experience;food.ExperienceSource="Catalogue client";
                }
            }
            catch(Exception ex) when(!token.IsCancellationRequested)
            {
                logger.LogWarning(ex,"Catalogue des nourritures indisponible : utilisation des données enregistrées.");
            }
            var resources=await db.Resources.ToDictionaryAsync(resource=>resource.DofusDbId,token);
            var equipmentIds=(await db.Equipments.Select(e=>e.DofusDbId).ToArrayAsync(token)).ToHashSet();
            foreach(var food in foods.Values.Where(food=>food.MarketType==MarketObjectType.Resource && !equipmentIds.Contains(food.DofusDbId)))
            {
                if(!resources.TryGetValue(food.DofusDbId,out var resource))
                    db.Resources.Add(resources[food.DofusDbId]=resource=new(food.DofusDbId){Name=food.Name,DofusDbIconId=food.DofusDbIconId});
                resource.PetExperience=food.Experience;
            }
            await db.SaveChangesAsync(token);
            return foods.Values.OrderBy(food=>food.Name).ToArray();
        }
        finally{Gate.Release();}
    }

    internal static async Task ApplySeedAsync(BestCrushDbContext db,PetFoodSeed seed,CancellationToken token)
    {
        if(await db.Upgrades.AnyAsync(u=>u.Kind==UpgradeKind.PetFoodSeed && u.NewVersion==seed.Version,token)) return;
        if(seed.Records.Any(r=>r.Id<=0 || string.IsNullOrWhiteSpace(r.Name) ||
            r.Experience is { } xp && (!double.IsFinite(xp) || xp<0)) || seed.Records.Select(r=>r.Id).Distinct().Count()!=seed.Records.Length)
            throw new InvalidDataException("Le catalogue initial de nourriture est invalide.");
        await using var transaction=await db.Database.BeginTransactionAsync(token);
        var foods=await db.PetFoods.ToDictionaryAsync(food=>food.DofusDbId,token);
        var latest=await db.MarketPriceObservations.Where(q=>q.ServerName==seed.Server)
            .GroupBy(q=>new{q.ObjectType,q.DofusDbId}).Select(g=>new{g.Key.ObjectType,g.Key.DofusDbId,At=g.Max(q=>q.ObservedAtUtc)})
            .ToDictionaryAsync(q=>(q.ObjectType,q.DofusDbId),q=>q.At,token);
        foreach(var row in seed.Records)
        {
            if(!foods.TryGetValue(row.Id,out var food)) db.PetFoods.Add(foods[row.Id]=food=new()
            {DofusDbId=row.Id,Name=row.Name,DofusDbIconId=row.IconId,MarketType=row.MarketType,Craftable=row.Craftable,
                Experience=row.Experience,ExperienceSource=row.ExperienceSource});
            food.SheetExperience=row.SheetExperience;
            if(row.UnitPrice is >0 && (!latest.TryGetValue((row.MarketType,row.Id),out var at) || at<seed.ObservedAtUtc.UtcDateTime))
                db.MarketPriceObservations.Add(new(){DofusDbId=row.Id,ObjectType=row.MarketType,ServerName=seed.Server,
                    Price=row.UnitPrice.Value,Quantity=1,Source=MarketPriceSource.Manual,ObservedAtUtc=seed.ObservedAtUtc.UtcDateTime});
        }
        db.Upgrades.Add(new(){Kind=UpgradeKind.PetFoodSeed,NewVersion=seed.Version,UpgradeDate=DateTime.UtcNow});
        await db.SaveChangesAsync(token);await transaction.CommitAsync(token);
    }

    internal async Task<PetFood[]> GetFoodsAsync(CancellationToken token)
    {
        var foods=await SynchronizeAsync(token);
        await using var db=await database.CreateDbContextAsync(token);
        var evidence=await PetFoodEvidenceReader.ReadAsync(db,foods,clients?.SelectedClient?.ClientBuild,token);
        return foods.Where(food=>food.Experience is >0 && evidence.GetValueOrDefault(food.DofusDbId) is not {Matches:false} and not {Consistent:false})
            .Select(food=>new PetFood(food.DofusDbId,food.Name,food.DofusDbIconId,food.MarketType,food.Experience!.Value,food.Craftable)).ToArray();
    }

    internal async Task<IReadOnlyList<PetFoodDataRow>> LoadTableAsync(string server,CancellationToken token)
    {
        var foods=await SynchronizeAsync(token);
        await using var db=await database.CreateDbContextAsync(token);
        var pricing=new MarketPriceService(db,priority);
        var evidence=await PetFoodEvidenceReader.ReadAsync(db,foods,clients?.SelectedClient?.ClientBuild,token);
        var market=new EquipmentProfitabilityContext
        {
            ResourcePrices=await pricing.GetLatestObservationsForServerAsync(MarketObjectType.Resource,server,token,excludeUnavailableLots:true),
            RunePrices=await pricing.GetLatestObservationsForServerAsync(MarketObjectType.Rune,server,token,excludeUnavailableLots:true),
            EquipmentPrices=new Dictionary<(long,int),MarketPriceObservation>(),Coefficients=[]
        };
        var planner=new PetFoodPlanner(pricing,[],[],new Dictionary<long,BigInteger>(),market);
        return foods.Select(food=>
        {
            var observation=evidence.GetValueOrDefault(food.DofusDbId);
            var row=BuildRow(food,planner,market) with {Evidence=observation};
            return observation is {Matches:false} or {Consistent:false}
                ? row with {Quantity80=null,Cost80=null,Quantity100=null,Cost100=null} : row;
        }).ToArray();
    }

    internal static PetFoodDataRow BuildRow(PetFoodDefinition food,PetFoodPlanner planner,EquipmentProfitabilityContext market)
    {
        var unit=MarketPriceService.CheapestUnitQuote(StockCraftPlanner.PricesFor(market,food.MarketType).Values
            .Where(q=>q.DofusDbId==food.DofusDbId && q.Quantity is 1 or 10 or 100 or 1000));
        int? Count(int level)=>food.Experience is >0 ? PetExperience.UnitsFor(food.Experience.Value,PetExperience.RequiredAtLevel(level)) : null;
        int? q80=Count(80),q100=Count(100);
        return new(food,unit,unit is null ? null : (double)unit.Price/unit.Quantity,q80,
            q80 is { } n80 ? planner.Buy(food.DofusDbId,food.MarketType,n80) : null,q100,
            q100 is { } n100 ? planner.Buy(food.DofusDbId,food.MarketType,n100) : null);
    }
}
