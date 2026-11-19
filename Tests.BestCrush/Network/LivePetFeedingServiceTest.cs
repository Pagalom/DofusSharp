using BestCrush.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Network.PetFeedingTrackerTest;

namespace Tests.BestCrush.Network;

public sealed class LivePetFeedingServiceTest
{
    [Theory]
    [InlineData("window")]
    [InlineData("connection")]
    [InlineData("lease")]
    [InlineData("unverified")]
    [InlineData("revoked")]
    [InlineData("cleared")]
    public async Task MealRequiresOneVerifiedFlowAndAnActiveLease(string invalid)
    {
        using var database=new TestDatabase();
        DofusClientWindowKey one=new(111,1),two=new(222,2);
        var clients=new CurrentDofusClientsState();
        clients.Update([
            new DofusWindowInfo(new(111),"One - Ecaflip - 3.7.14.18 - Release","Dofus",0,0,100,100){ProcessId=1},
            new DofusWindowInfo(new(222),"Two - Ecaflip - 3.7.14.18 - Release","Dofus",0,0,100,100){ProcessId=2}],one);
        var servers=new CurrentServerState();servers.SelectServer("Kourial");
        var service=new LivePetFeedingService(database,clients,servers,new(),NullLogger<LivePetFeedingService>.Instance);
        DofusWireMessage Wire(string key,byte[] body)=>new(key=="irb" ? "C→S":"S→C",key,body,DateTime.UtcNow,
            servers.GetCaptureLease(),1,ConnectionId:1,CandidateWindow:one,SyntheticWindowForReplay:true);
        await service.ObserveAsync(Wire("irl",Snapshot()),default);
        var request=Wire("irb",Request());
        request=invalid switch
        {
            "window"=>request with{CandidateWindow=two},
            "connection"=>request with{ConnectionId=2},
            "lease"=>request with{CaptureLease=null},
            "unverified"=>request with{SyntheticWindowForReplay=false},
            _=>request
        };
        await service.ObserveAsync(request,default);
        var consumed=Wire("irx",Quantity(9));var updated=Wire("iqy",UpdatedPet());
        if(invalid=="revoked") servers.SelectServer("Tylezia");
        if(invalid=="cleared") service.ClearLive();
        await service.ObserveAsync(consumed,default);await service.ObserveAsync(updated,default);
        using var db=database.CreateContext();Assert.Empty(await db.PetFeedingObservations.ToArrayAsync());
        service.ClearLive();
        await service.ObserveAsync(Wire("irl",Snapshot()),default);
        await service.ObserveAsync(Wire("irb",Request()),default);
        await service.ObserveAsync(Wire("irx",Quantity(9)),default);
        await service.ObserveAsync(Wire("iqy",UpdatedPet()),default);
        var meal=Assert.Single(await db.PetFeedingObservations.ToArrayAsync());
        Assert.Equal("3.7.14.18",meal.ClientBuild);Assert.Equal(101,meal.ExperienceAfter);
    }
}
