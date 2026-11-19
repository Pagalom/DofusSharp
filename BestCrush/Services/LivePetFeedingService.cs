using System.Security.Cryptography;
using System.Text;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StoredMeal=BestCrush.Domain.Models.PetFeedingObservation;

namespace BestCrush.Services;

public sealed class LivePetFeedingService(IDbContextFactory<BestCrushDbContext> database,CurrentDofusClientsState clients,
    CurrentServerState servers,LiveDofusClientNetworkBindings bindings,ILogger<LivePetFeedingService> logger)
{
    private readonly string _run=Guid.NewGuid().ToString("N");
    private readonly object _sync=new();
    private long _epoch=-1,_generation;
    private sealed record Flow(DofusClientWindowKey Window,NetworkCaptureLease Lease,PetFeedingTracker Tracker);
    private readonly Dictionary<long,Flow> _flows=[];
    public event Action<string>? Changed;
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
        string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{_run}:{message.CaptureEpoch}:{message.ConnectionId}:{meal.Sequence}")));
        bool saved=false;
        await PetFoodCatalogService.Gate.WaitAsync(token);
        try
        {
            lock(_sync){if(generation!=_generation || !servers.IsCaptureLeaseActive(lease)) return;}
            await using var db=await database.CreateDbContextAsync(token);
            saved=await SaveAsync(db,meal,id,client.ClientBuild,token);
        }
        catch(Exception ex) when(!token.IsCancellationRequested){logger.LogWarning(ex,"Could not record a pet feeding observation.");}
        finally{PetFoodCatalogService.Gate.Release();}
        if(saved) Changed?.Invoke(lease.ServerName);
    }
    internal static async Task<bool> SaveAsync(BestCrushDbContext db,BestCrush.Network.Protocol.PetFeedingObservation meal,
        string id,string build,CancellationToken token)
    {
        if(meal.Quantity<=0 || meal.FoodItemId<=0 || meal.PetItemId<=0 || meal.ExperienceBefore<0 ||
            meal.ExperienceAfter<meal.ExperienceBefore || meal.ExperienceAfter>=PetExperience.RequiredAtLevel(100) ||
            string.IsNullOrWhiteSpace(build) || build.Length>64 || id.Length!=64) return false;
        if(await db.PetFeedingObservations.AnyAsync(m=>m.Id==id,token)) return false;
        if(!await db.PetFoods.AnyAsync(f=>f.DofusDbId==meal.FoodItemId,token))
        {
            var rune=await db.Runes.FirstOrDefaultAsync(r=>r.DofusDbId==meal.FoodItemId,token);
            var resource=await db.Resources.FirstOrDefaultAsync(r=>r.DofusDbId==meal.FoodItemId,token);
            db.PetFoods.Add(new(){DofusDbId=meal.FoodItemId,Name=rune?.Name ?? resource?.Name ?? $"Objet #{meal.FoodItemId}",
                DofusDbIconId=rune?.DofusDbIconId ?? resource?.DofusDbIconId,
                MarketType=rune is null ? MarketObjectType.Resource : MarketObjectType.Rune,ExperienceSource="Repas observé"});
        }
        db.PetFeedingObservations.Add(new StoredMeal{Id=id,FoodItemId=meal.FoodItemId,PetItemId=meal.PetItemId,
            Quantity=meal.Quantity,ExperienceBefore=meal.ExperienceBefore,ExperienceAfter=meal.ExperienceAfter,
            ObservedAtUtc=meal.ObservedAtUtc,ClientBuild=build});
        await db.SaveChangesAsync(token);return true;
    }
    public void ClearLive(){lock(_sync){_flows.Clear();_generation++;}}
}
