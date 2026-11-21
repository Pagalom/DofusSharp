using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

public sealed class LivePetFeedingService(IDbContextFactory<BestCrushDbContext> database,CurrentDofusClientsState clients,
    CurrentServerState servers,LiveDofusClientNetworkBindings bindings,ILogger<LivePetFeedingService> logger)
{
    private readonly object _sync=new();
    private long _epoch=-1,_generation;
    private sealed record Flow(DofusClientWindowKey Window,NetworkCaptureLease Lease,PetFeedingTracker Tracker);
    private readonly Dictionary<long,Flow> _flows=[];
    public event Action<string>? Changed;
    public event Action<long>? CoefficientUpdated;
    internal async Task ObserveAsync(DofusWireMessage message,CancellationToken token)
    {
        if(message.Key!="irb" && !LiveStockProtocol.IsStockOpcode(message.Key)) return;
        var client=clients.Clients.FirstOrDefault(c=>c.Key==message.CandidateWindow);
        if(message.ConnectionId<=0 || client is not {HasCharacterTitle:true,ClientBuild:{Length:>0}} ||
            message.CaptureLease is not { } lease || !servers.IsCaptureLeaseActive(lease) ||
            (!message.SyntheticWindowForReplay && !bindings.IsCurrentConnection(message.CaptureEpoch,message.ConnectionId,client.Key))) return;
        BestCrush.Network.Protocol.PetFeedingObservation? meal;
        long generation;
        lock(_sync)
        {
            if(_epoch!=message.CaptureEpoch){_flows.Clear();_epoch=message.CaptureEpoch;_generation++;}
            if(!_flows.TryGetValue(message.ConnectionId,out var flow))
            {
                if(_flows.Count>=128) return;
                _flows[message.ConnectionId]=flow=new(client.Key,lease,new());
            }
            if(flow.Window!=client.Key || flow.Lease!=lease) return;
            meal=flow.Tracker.Observe(message.Direction,message.Key,message.Body,message.ObservedAtUtc);
            generation=_generation;
        }
        if(meal is null) return;
        bool saved=false;
        await PetFoodCatalogService.Gate.WaitAsync(token);
        try
        {
            lock(_sync){if(generation!=_generation || !servers.IsCaptureLeaseActive(lease)) return;}
            await using var db=await database.CreateDbContextAsync(token);
            saved=await SaveAsync(db,meal,token);
        }
        catch(Exception ex) when(!token.IsCancellationRequested){logger.LogWarning(ex,"Could not update a pet food XP coefficient.");}
        finally{PetFoodCatalogService.Gate.Release();}
        if(saved){Changed?.Invoke(lease.ServerName);CoefficientUpdated?.Invoke(meal.FoodItemId);}
    }
    internal static async Task<bool> SaveAsync(BestCrushDbContext db,BestCrush.Network.Protocol.PetFeedingObservation meal,
        CancellationToken token)
    {
        if(meal.Quantity<=0 || meal.FoodItemId<=0 || meal.PetItemId<=0 || meal.ExperienceBefore<0 ||
            meal.ExperienceAfter<meal.ExperienceBefore || meal.ExperienceAfter>=PetExperience.RequiredAtLevel(100)) return false;
        double coefficient=(double)(meal.ExperienceAfter-meal.ExperienceBefore)/meal.Quantity;
        return await SaveCoefficientAsync(db,meal.FoodItemId,coefficient,meal.ObservedAtUtc,null,MarketPriceSource.InGameAutomatic,token);
    }
    internal async Task<bool> RecordAsync(PetFoodDefinition definition,int quantity,long experience,CancellationToken token=default)
    {
        if(definition.DofusDbId<=0 || definition.MarketType!=MarketObjectType.Resource || quantity<=0 || experience<0)return false;
        bool saved;
        await PetFoodCatalogService.Gate.WaitAsync(token);
        try
        {
            await using var db=await database.CreateDbContextAsync(token);
            saved=await SaveCoefficientAsync(db,definition.DofusDbId,(double)experience/quantity,DateTime.UtcNow,definition,MarketPriceSource.Manual,token);
        }
        finally{PetFoodCatalogService.Gate.Release();}
        if(saved){Changed?.Invoke("");CoefficientUpdated?.Invoke(definition.DofusDbId);}
        return saved;
    }
    private static async Task<bool> SaveCoefficientAsync(BestCrushDbContext db,long itemId,double coefficient,
        DateTime observedAt,PetFoodDefinition? definition,MarketPriceSource origin,CancellationToken token)
    {
        var food=await db.PetFoods.FirstOrDefaultAsync(f=>f.DofusDbId==itemId,token);
        if(food?.ExperienceObservedAtUtc is {} last && (last>observedAt || last==observedAt && food.Experience==coefficient))return false;
        var resource=await db.Resources.FirstOrDefaultAsync(r=>r.DofusDbId==itemId,token);
        if(food is null)
        {
            var rune=await db.Runes.FirstOrDefaultAsync(r=>r.DofusDbId==itemId,token);
            var equipment=resource is null && rune is null ? await db.Equipments.FirstOrDefaultAsync(e=>e.DofusDbId==itemId,token) : null;
            db.PetFoods.Add(food=new(){DofusDbId=itemId,Name=rune?.Name ?? resource?.Name ?? equipment?.Name ?? definition?.Name ?? $"Objet #{itemId}",
                DofusDbIconId=rune?.DofusDbIconId ?? resource?.DofusDbIconId ?? equipment?.DofusDbIconId ?? definition?.DofusDbIconId,
                MarketType=rune is not null ? MarketObjectType.Rune : equipment is not null ? MarketObjectType.Equipment : MarketObjectType.Resource});
        }
        food.Experience=coefficient;food.ExperienceObservedAtUtc=observedAt;food.ExperienceOrigin=origin;
        food.ExperienceSource=origin==MarketPriceSource.Manual ? "Manuel" : "Relevé en jeu";
        if(food.MarketType==MarketObjectType.Resource)
        {
            if(resource is null)db.Resources.Add(resource=new(itemId){Name=food.Name,DofusDbIconId=food.DofusDbIconId});
            resource.PetExperience=coefficient;
        }
        await db.SaveChangesAsync(token);return true;
    }
    public void ClearLive(){lock(_sync){_flows.Clear();_generation++;}}
}
