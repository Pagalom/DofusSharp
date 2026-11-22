using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using BestCrush.Components.Pages;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class PetProgressiveLoadingTest
{
    [Fact]
    public async Task LocalOverviewNeedsNoCatalogueDownloadAndPrioritizesInventoryThenBankIncludingUnknownFoods()
    {
        using var h=new Harness(cache:false);
        var report=await h.Service.LoadOverviewAsync(h.Window,"A",default);
        Assert.False(report.AnalysisComplete);Assert.False(report.OwnedAnalysisComplete);
        Assert.Equal("Familier possédé",Assert.Single(report.Owned).Species.Name);
        Assert.Null(report.Owned[0].Best);
        Assert.Single(report.OwnedChecks);
        var other=await h.Service.LoadOverviewAsync(h.Window,"B",default);
        Assert.Empty(other.Owned);Assert.False(other.InventoryKnown);Assert.False(other.BankKnown);
    }

    [Fact]
    public async Task PublishesInventoryBeforeSimulationAndPublishesOwnedAdviceBeforePurchases()
    {
        using var h=new Harness();
        var stages=new List<PetOpportunityReport>();
        var first=new TaskCompletionSource<PetOpportunityReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var load=h.Service.LoadAsync(h.Window,"A",default,async stage=>
        {
            stages.Add(stage);
            if(stages.Count==1) {first.SetResult(stage);await resume.Task;}
        });
        var overview=await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(load.IsCompleted);Assert.Null(Assert.Single(overview.Owned).Best);
        resume.SetResult();
        var result=(await load)!;
        Assert.Equal(2,stages.Count);
        Assert.True(stages[1].OwnedAnalysisComplete);Assert.False(stages[1].AnalysisComplete);
        Assert.Equal(23,Assert.Single(stages[1].Owned).Best!.Level);
        Assert.True(result.AnalysisComplete);Assert.True(result.OwnedAnalysisComplete);
        Assert.Equal(23,Assert.Single(result.Owned).Best!.Level);
        Assert.All(result.Checks,check=>Assert.Equal(100,check.ItemId));
        Assert.DoesNotContain(result.Checks,check=>check.ItemId is 10 or 20 or 30 or 40);
        Assert.NotNull(result.BestFoodValue);
        Assert.Empty(result.Purchases); // No market volume: no purchase advice is shown.
        Assert.Equal(result.Owned[0].Best,(await h.Service.LoadAsync(h.Window,"A",default))!.Owned[0].Best);
    }

    [Fact]
    public async Task CancellingAfterInventoryPublicationStopsBeforeAnyRemoteCatalogueRequest()
    {
        using var h=new Harness(cache:false);
        using var cancellation=new CancellationTokenSource();
        int published=0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>h.Service.LoadAsync(h.Window,"A",cancellation.Token,stage=>
        {
            Assert.Single(stage.Owned);published++;cancellation.Cancel();return Task.CompletedTask;
        }));
        Assert.Equal(1,published);
    }

    [Fact]
    public async Task PurchaseAdviceStillUsesObservedDemandAndCurrentNutritionAfterCaching()
    {
        using var h=new Harness();
        Assert.Empty((await h.Service.LoadAsync(h.Window,"A",default))!.Purchases);
        var now=DateTimeOffset.UtcNow;
        h.Sales.SaveHistory(new(new("A",MarketObjectType.Equipment,100),now,[],[new(1000,100,now.AddHours(-1))]));
        var report=(await h.Service.LoadAsync(h.Window,"A",default))!;
        var choice=Assert.Single(report.Purchases);
        Assert.Equal(23,choice.Best!.Level);Assert.True(choice.Best.Margin>0);Assert.True(choice.Best.RemainingListings>0);
        Assert.NotEmpty(report.OverlayPurchases);Assert.All(report.OverlayPurchases,p=>Assert.True(p.Best?.Margin>0));
        Assert.DoesNotContain(report.OwnedChecks,c=>c.Reason.Contains("Cours du marché"));
    }

    [Fact]
    public async Task OldPricesStayInCalculationsAndTriggerAnHdvReviewInsteadOfRemovingAdvice()
    {
        var old=DateTimeOffset.UtcNow.AddDays(-10);
        using var h=new Harness(petObservedAt:old);
        h.SetFoodPriceAge(old.UtcDateTime);
        var report=(await h.Service.LoadAsync(h.Window,"A",default))!;
        var pet=Assert.Single(report.Owned);
        Assert.Equal(23,pet.Best!.Level);Assert.Equal(18,pet.Best.Nutrition.CashRequired);
        Assert.NotNull(report.BestFoodValue);Assert.Equal(old.UtcDateTime,report.BestFoodValue.ObservedAtUtc);
        Assert.Equal(1796,Assert.Single(report.FoodPriceChecks).ItemId);
        Assert.Contains(1796,report.KnownFoodExperienceItems);
        Assert.Equal(old,Assert.Single(report.OwnedChecks).LastObserved);
        Assert.Contains(report.Checks,check=>check.ItemId==100);
        Assert.DoesNotContain(report.Checks,check=>check.ItemId==10); // Pet and food-price lists stay separate.
    }

    [Fact]
    public async Task FourDayOldFoodPriceRemainsUsableAndIsFlaggedForHdv()
    {
        using var h=new Harness();
        h.SetFoodPriceAge(DateTime.UtcNow.AddDays(-4));
        var report=(await h.Service.LoadAsync(h.Window,"A",default))!;
        Assert.Equal(23,Assert.Single(report.Owned).Best!.Level);
        Assert.Equal(1796,Assert.Single(report.FoodPriceChecks).ItemId);
        Assert.NotNull(report.BestFoodValue);
    }



    private sealed class PreparationCapture:IComponentActivator
    {
        public PetPreparationLists Lists=null!;
        public IComponent CreateInstance(Type type)
        {var component=(IComponent)Activator.CreateInstance(type)!;if(component is PetPreparationLists lists)Lists=lists;return component;}
    }









    [Fact]
    public void FoodPriceCopyPrioritizesKnownExperienceAndKeepsFiftyDistinctNames()
    {
        var now=DateTimeOffset.UtcNow;
        var checks=Enumerable.Range(1,70).Select(i=>new BreedingPriceCheck(i,$"Ressource {i:00}",now.AddDays(-10),"Ancien")).ToArray();
        var known=Enumerable.Range(61,10).Select(i=>(long)i).ToHashSet();
        var copied=PetOpportunityService.FoodPriceNames(checks.Concat([new(80,"RESSOURCE 61",null,"Manquant"),new(81," ",null,"Manquant")]),known);
        Assert.Equal(50,copied.Count);
        Assert.Equal(checks.Skip(60).Select(c=>c.Name),copied.Take(10));
        Assert.Equal("Ressource 01",copied[10]);Assert.Equal("Ressource 40",copied[^1]);
        Assert.Equal(50,copied.Distinct(StringComparer.CurrentCultureIgnoreCase).Count());
        Assert.Equal(new[]{"Ressource 65"},PetOpportunityService.FoodPriceNames(checks.Where(c=>c.ItemId==65),known));
        Assert.Empty(PetOpportunityService.FoodPriceNames([],known));
    }

    [Fact]
    public void NutritionCacheIgnoresUnrelatedPricesButInvalidatesFoodPricesStockXpAndRecipes()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        PetFood[] foods=[new(10,"Aliment",null,MarketObjectType.Resource,10,false)];
        var stock=new Dictionary<long,BigInteger>();
        var quote=new MarketPriceObservation{DofusDbId=10,ObjectType=MarketObjectType.Resource,ServerName="A",Quantity=1,Price=2,
            Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=DateTime.UtcNow};
        var quotes=new Dictionary<(long,int),MarketPriceObservation>{{(10,1),quote}};
        var market=new EquipmentProfitabilityContext{ResourcePrices=quotes,RunePrices=new Dictionary<(long,int),MarketPriceObservation>(),EquipmentPrices=new Dictionary<(long,int),MarketPriceObservation>(),Coefficients=[]};
        string key=PetNutritionCache.Fingerprint(foods,[],stock,market);
        var cache=new PetNutritionCache();
        var plan=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),foods,[],stock,market,cache).Calculate(90);
        Assert.Same(plan,new PetFoodPlanner(new(db,new TestDataPriorityProvider()),foods,[],stock,market,cache).Calculate(90));
        quotes[(999,1)]=new(){DofusDbId=999,ObjectType=MarketObjectType.Resource,ServerName="A",Price=100,Source=MarketPriceSource.Manual,ObservedAtUtc=DateTime.UtcNow};
        stock[999]=100;
        Assert.Equal(key,PetNutritionCache.Fingerprint(foods,[],stock,market));
        stock[10]=1;Assert.NotEqual(key,PetNutritionCache.Fingerprint(foods,[],stock,market));stock.Remove(10);
        Assert.NotEqual(key,PetNutritionCache.Fingerprint([foods[0] with{Experience=20}],[],stock,market));
        ProductCraftRecipe recipe=new(new(10,"Aliment",1,null,MarketObjectType.Resource,1,"Ressource","Métier",1),[new(20,"Ingrédient",MarketObjectType.Resource,1,null)]);
        Assert.NotEqual(key,PetNutritionCache.Fingerprint(foods,[recipe],stock,market));
        quotes[(10,1)]=new(){DofusDbId=10,ObjectType=MarketObjectType.Resource,ServerName="A",Price=20,Source=MarketPriceSource.Manual,ObservedAtUtc=DateTime.UtcNow};
        Assert.NotEqual(key,PetNutritionCache.Fingerprint(foods,[],stock,market));
        var updated=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),foods,[],stock,market,new()).Calculate(90);
        Assert.True(updated!.CashRequired>plan!.CashRequired);
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        var empty=new PetNutritionCache();
        Assert.ThrowsAny<OperationCanceledException>(()=>new PetFoodPlanner(new(db,new TestDataPriorityProvider()),foods,[],stock,market,empty).Calculate(95,canceled.Token));
        Assert.Equal(0,empty.Count);
    }

    private sealed class Harness:IDisposable
    {
        private readonly TestDatabase _db=new();
        private readonly string _folder=Path.Combine(Path.GetTempPath(),"pet-progressive-"+Guid.NewGuid());
        private readonly LiveStockService _stock;
        internal DofusClientWindowKey Window {get;}=new(111,1111);
        internal PetOpportunityService Service {get;}
        internal GlobalSalesRepository Sales {get;}
        internal void SetFoodPriceAge(DateTime at)
        {
            using var db=_db.CreateContext();
            foreach(var quote in db.MarketPriceObservations)db.Entry(quote).Property(q=>q.ObservedAtUtc).CurrentValue=at;
            db.SaveChanges();
        }
        internal Harness(bool cache=true,DateTimeOffset? petObservedAt=null)
        {
            Directory.CreateDirectory(_folder);
            void Cache(string key,object value)
            {
                string host=DofusDbClient.ProductionUri.Host.Replace('.','-');
                string file=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(host+"-"+key)))+".json";
                File.WriteAllText(Path.Combine(_folder,file),JsonSerializer.Serialize(new{UpdatedUtc=DateTime.UtcNow,Data=value}));
            }
            if(cache)
            {
                Cache("pet-species-v1",new[]{new PetSpecies(100,"Familier possédé",null,18)});
            }
            using(var db=_db.CreateContext())
            {
                db.Equipments.Add(new(100){Name="Familier possédé",Type=EquipmentType.Pet});
                db.Resources.AddRange(new Resource(20){Name="Inventaire"},new Resource(30){Name="Banque"},new Resource(40){Name="Autre"});
                db.MarketPriceObservations.Add(new(){ObjectType=MarketObjectType.Resource,DofusDbId=1796,ServerName="A",Price=2,Quantity=1,Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=DateTime.UtcNow});
                db.SaveChanges();
            }
            var clients=new CurrentDofusClientsState();clients.Update([new(new IntPtr(Window.Handle),"Test - Ecaflip - 3.7.14.18 - Release","Dofus",0,0,100,100){ProcessId=Window.ProcessId}],Window);
            _stock=new(new(Path.Combine(_folder,"stock.db")),clients);
            byte[] Item(ulong id,ulong uid,ulong quantity,params byte[][] effects)=>Message(Unsigned(3,quantity),Unsigned(4,id),Unsigned(5,uid),Message(effects.Select(e=>Bytes(7,e)).ToArray()));
            void Observe(string opcode,params byte[][] items)=>_stock.Observe(new("S→C",opcode,Message(Unsigned(opcode=="irp"?1:4,1),Message(items.Select(i=>Bytes(2,Bytes(1,i))).ToArray())),DateTime.UtcNow,new("A",1),1,ConnectionId:1,CandidateWindow:Window,SyntheticWindowForReplay:true));
            Observe("irl",Item(100,1,1,Message(Unsigned(10,2821),Bytes(9,Unsigned(1,18))),Message(Unsigned(10,2819),Bytes(9,Unsigned(2,1)))),Item(20,2,4));
            Observe("irp",Item(30,3,6));
            var sales=Sales=new GlobalSalesRepository(Path.Combine(_folder,"sales.db"));
            sales.SavePetPrices("A",new(100,[new(0,0,100,"normal",false),new(23,90,500,"normal",false)]),petObservedAt ?? DateTimeOffset.UtcNow);
            var servers=new CurrentServerState();
            var catalogue=new ActivityCatalogService(DofusDbClient.Production(),Moq.Mock.Of<IHttpClientFactory>(),_folder);
            Service=new(_db,new TestDataPriorityProvider(),catalogue,_stock,servers,new(servers),sales,NullLogger<PetOpportunityService>.Instance);
        }
        public void Dispose(){_stock.Dispose();_db.Dispose();Directory.Delete(_folder,true);}
    }
}
