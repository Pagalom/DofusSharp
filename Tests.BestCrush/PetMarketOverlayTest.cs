using System.Net;
using System.Numerics;
using System.Reflection;
using BestCrush.Components.Overlays;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.BestCrush;

public sealed class PetMarketOverlayTest
{
    private static PetOpportunityReport Report()=>new("Kourial",[],[],[],new Dictionary<long,BigInteger>(),new Dictionary<long,BigInteger>(),true,true,true,true,0,0);

    [Fact]
    public void TestQueueUsesOnlyInventoryResourcesUnknownFirstThenOlderThanAWeekAndCapsAtFifty()
    {
        var now=DateTime.UtcNow;
        var foods=Enumerable.Range(1,65).Select(i=>new PetFoodDefinition{DofusDbId=i,Name=$"Aliment {i:00}",MarketType=MarketObjectType.Resource}).ToList();
        foods.Add(new(){DofusDbId=100,Name="Rune",MarketType=MarketObjectType.Rune});
        var stock=foods.ToDictionary(f=>f.DofusDbId,_=>(BigInteger)1);stock[65]=0;
        foods[0].ExperienceObservedAtUtc=now;foods[1].ExperienceObservedAtUtc=now.AddDays(-7);
        foods[2].ExperienceObservedAtUtc=now.AddDays(-8);foods[3].ExperienceObservedAtUtc=now.AddDays(-9);
        var queue=PetOpportunityService.FoodReviews(foods,stock,now);
        Assert.Equal(50,queue.Count);Assert.All(queue,row=>Assert.Null(row.LastObservedAtUtc));
        Assert.DoesNotContain(queue,row=>row.Food.DofusDbId is 1 or 2 or 65 or 100);
        var small=PetOpportunityService.FoodReviews(foods.Take(5),stock,now);
        Assert.Equal(new long[]{5,4,3},small.Select(row=>row.Food.DofusDbId));
    }

    [Fact]
    public void BestFoodUsesXpPerKamaAndKeepsOldPricesWhileRespectingTheBudget()
    {
        PetFood[] foods=[new(1,"Pas cher",null,MarketObjectType.Resource,1,false),new(2,"Rentable",null,MarketObjectType.Resource,100,false)];
        var now=DateTime.UtcNow;
        MarketPriceObservation Quote(long id,int quantity,long price,DateTime at)=>new(){DofusDbId=id,ObjectType=MarketObjectType.Resource,Quantity=quantity,Price=price,ObservedAtUtc=at,ServerName="Kourial",Source=MarketPriceSource.InGameAutomatic};
        var quotes=new Dictionary<(long,int),MarketPriceObservation>{{(1,1),Quote(1,1,1,now)},{(2,10),Quote(2,10,100,now.AddHours(-2))},{(2,100),Quote(2,100,1,now.AddDays(-4))}};
        var context=new EquipmentProfitabilityContext{ResourcePrices=quotes,RunePrices=new Dictionary<(long,int),MarketPriceObservation>(),EquipmentPrices=new Dictionary<(long,int),MarketPriceObservation>(),Coefficients=[]};
        var best=PetOpportunityService.BestFoodValue(foods,context)!;
        Assert.Equal(2,best.Food.Id);Assert.Equal(10_000,best.ExperiencePerKama);Assert.Equal(100,best.LotQuantity);
        Assert.Equal(now.AddDays(-4),best.ObservedAtUtc);
        Assert.Null(PetOpportunityService.BestFoodValue(foods,context,0));
    }

    [Fact]
    public async Task ShowsPurchaseNutritionAndOneBestFoodAndOpensAnalysisInsteadOfCopying()
    {
        var food=new PetFood(20,"Bec test",null,MarketObjectType.Resource,100,false);
        var nutrition=new PetNutritionPlan([new(food,4,1,0,3)],[],[],30,10,new Dictionary<long,int>{{20,1}});
        var plan=new PetSalePlan(80,1000,nutrition,860,2,true);
        var purchase=new PetAssessment(new(100,"À acheter",null,18),null,new(1,0,100,"normal",false),null,[plan],plan,null);
        var owned=new PetAssessment(new(101,"Déjà possédé",null,18),null,null,null,[],null,null);
        var report=Report() with {Owned=[owned],Purchases=[purchase],BestFoodValue=new(food,100,10),
            OverlayFoodReviews=[new(new(){DofusDbId=30,Name="À tester",MarketType=MarketObjectType.Resource},1,null)]};
        using var provider=new ServiceCollection().AddLogging().AddSingleton(new ItemsService(null!,null!,null!,null!,null!)).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<PetMarketOverlay>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Report",report},{"ServerKnown",true}})));
        var html=await renderer.Dispatcher.InvokeAsync(()=>WebUtility.HtmlDecode(root.ToHtmlString()));
        Assert.Contains("À acheter",html);Assert.Contains("Plus rentables · niv. 1",html);Assert.Contains("Niv. 1 → 80",html);
        Assert.Contains("Bec test",html);Assert.Contains("× 4",html);Assert.Contains("Analyser les ressources à tester",html);
        Assert.DoesNotContain("Déjà possédé",html);Assert.DoesNotContain("Synchronisez",html);
        Assert.Contains("title=\"Prix familier (0)\"",html);Assert.Contains("title=\"Test XP (0)\"",html);Assert.Contains("title=\"Prix ressources (0)\"",html);
        Assert.Contains("Copier les prix des familiers à actualiser",html);Assert.Contains("Copier les prix des ressources à actualiser",html);
        Assert.Contains("Utiliser mes stocks",html);Assert.Contains("Aliment précédent",html);Assert.Contains("Aliment suivant",html);
    }

    [Fact]
    public void PriceReviewsExpireAtSeventyTwoHoursAndRankByLastCalculatedMargin()
    {
        var now=DateTimeOffset.UtcNow;
        var species=Enumerable.Range(1,5).Select(i=>new PetSpecies(i,$"Familier {i}",null,18)).ToArray();
        PetAssessment Plan(int id,double margin)
        {
            var nutrition=new PetNutritionPlan([],[],[],0,0,new Dictionary<long,int>());
            var plan=new PetSalePlan(100,1000,nutrition,margin,1,true);
            return new(species[id-1],null,new(1,0,100,"normal",false),null,[plan],plan,null);
        }
        var observed=new Dictionary<long,DateTimeOffset>{{1,now.AddHours(-72)},{2,now.AddDays(-5)},{3,now.AddHours(-71)},{4,now.AddDays(-6)}};
        var checks=PetOpportunityService.PetPriceReviews(species,observed,[Plan(1,50),Plan(2,500),Plan(3,900)],now.UtcDateTime);
        Assert.Equal(new long[]{2,1,5,4},checks.Select(c=>c.ItemId));
        Assert.DoesNotContain(checks,c=>c.ItemId==3);Assert.Null(checks[2].LastObserved);
    }

    [Fact]
    public async Task ThreeCopyActionsSharePageQueuesAndFoodAnalysisContainsExactlyTheSameFiftyResources()
    {
        var now=DateTime.UtcNow;
        var foods=Enumerable.Range(1,65).Select(i=>new PetFoodTest(new(){DofusDbId=i,Name=$"Aliment {i:00}",MarketType=MarketObjectType.Resource},i==65?1:0,0,false)).ToArray();
        var checks=Enumerable.Range(1,65).Select(i=>new BreedingPriceCheck(i,$"Aliment {i:00}",DateTimeOffset.UtcNow.AddDays(-5),"Ancien")).ToArray();
        var known=new HashSet<long>{64,65};
        var report=Report() with {FoodTests=foods,FoodPriceChecks=checks,KnownFoodExperienceItems=known,PetPriceChecks=[new(100,"Familier prioritaire",null,"Ancien")]};
        string[] copied=[];int opened=0;
        var component=new PetMarketOverlay{Report=report,CopyRequested=EventCallback.Factory.Create<IEnumerable<string>>(this,(IEnumerable<string> names)=>copied=names.ToArray()),
            AnalysisRequested=EventCallback.Factory.Create(this,()=>opened++)};
        async Task Click(string method)=>await (Task)typeof(PetMarketOverlay).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(component,null)!;
        await Click("CopyFoodTests");Assert.Equal(PetOpportunityService.FoodTestNames(foods,now),copied);Assert.Equal(1,opened);
        Assert.Equal(copied,PetOpportunityService.FoodAnalysisFoods(foods,now).Select(f=>f.Name));Assert.Equal("Aliment 65",copied[0]);Assert.Equal(50,copied.Length);
        await Click("CopyResourcePrices");Assert.Equal(PetOpportunityService.FoodPriceNames(checks,known),copied);Assert.Equal(50,copied.Length);Assert.Equal("Aliment 64",copied[0]);
        await Click("CopyPetPrices");Assert.Equal(new[]{"Familier prioritaire"},copied);Assert.Equal(1,opened);
    }

    [Fact]
    public void PetPublicationUsesPlayedServerEvenWhenBrowsingAnotherServer()
    {
        var window=new DofusClientWindowKey(111,1111);var clients=new CurrentDofusClientsState();
        clients.Update([new(new IntPtr(111),"Alpha - Ecaflip - 3.7.14.18 - Release","Dofus",0,0,100,100){ProcessId=1111}],window);
        var attribution=new WindowServerAttributionState();attribution.Reset(1);
        attribution.ObserveGame(new("S→C","jzs",[],DateTime.UtcNow,null,1,ConnectionId:1,CandidateWindow:window,SyntheticWindowForReplay:true),true);
        attribution.AssignPlayedServer(clients.SelectedClient!,"Kourial");
        var servers=new CurrentServerState(attribution,true);servers.SelectServer("Brial");
        using var provider=new ServiceCollection().BuildServiceProvider();
        var data=new OverlayWorkspaceService(provider.GetRequiredService<IServiceScopeFactory>(),servers,clients,null!,null!,new(),NullLogger<OverlayWorkspaceService>.Instance);
        var report=Report();data.PublishPets(report);Assert.Same(report,data.PetReport);
        data.PublishPets(report with {Server="Brial"});Assert.Same(report,data.PetReport);
    }
}
