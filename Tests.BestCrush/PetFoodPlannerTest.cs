using System.Numerics;
using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients.Models.Items;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class PetFoodPlannerTest
{
    private static MarketPriceObservation Quote(long id,int size,long price)=>new()
    {ObjectType=MarketObjectType.Resource,DofusDbId=id,ServerName="A",Price=price,Quantity=size,
        Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=DateTime.UtcNow};
    private static PetFood Food(long id,double xp)=>new(id,$"Nourriture {id}",null,MarketObjectType.Resource,xp,false);
    private static EquipmentProfitabilityContext Market(params MarketPriceObservation[] quotes)=>new()
    {ResourcePrices=quotes.ToDictionary(q=>(q.DofusDbId,q.Quantity)),RunePrices=new Dictionary<(long,int),MarketPriceObservation>(),
        EquipmentPrices=new Dictionary<(long,int),MarketPriceObservation>(),Coefficients=[]};

    [Fact]
    public void FastCanonicalLotPricingMatchesTheExistingExactPurchaseAlgorithm()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var service=new MarketPriceService(db,new TestDataPriorityProvider());
        var random=new Random(42);
        for(int iteration=0;iteration<120;iteration++)
        {
            var quotes=new[]{1,10,100,1000}.Where(_=>random.Next(4)>0)
                .Select(size=>Quote(10,size,random.Next(1,20000))).ToArray();
            var market=Market(quotes);
            var planner=new PetFoodPlanner(service,[Food(10,10)],[],new Dictionary<long,BigInteger>(),market);
            int needed=random.Next(1,12000);
            var expected=service.CalculateMinimumPurchaseCost(10,needed,market.ResourcePrices);
            var actual=planner.Buy(10,MarketObjectType.Resource,needed);
            Assert.Equal(expected?.TotalCost,actual?.TotalCost);
            Assert.Equal(expected?.PurchasedQuantity,actual?.PurchasedQuantity);
        }
    }

    [Fact]
    public void MixesOwnedFoodAndPurchasedFoodAndIncludesTheOwnedOpportunityCost()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,10),Food(20,100)],[],
            new Dictionary<long,BigInteger>{{10,2}},Market(Quote(10,1,5),Quote(20,1,25)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(120));
        Assert.True(plan.Experience>=120);
        Assert.Equal(25,plan.CashRequired);Assert.Equal(8,plan.StockValue);
        Assert.Equal(2,plan.StockConsumed[10]);Assert.Equal(2,plan.Meals.Count);
        Assert.Equal(20,Assert.Single(plan.Purchases).ItemId);
    }

    [Fact]
    public void DoesNotWasteExpensiveOwnedFoodWhenCheaperFoodCanBePurchased()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,10),Food(20,100)],[],
            new Dictionary<long,BigInteger>{{10,100}},Market(Quote(10,1,100),Quote(20,1,25)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(100));
        Assert.Empty(plan.StockConsumed);Assert.Equal(25,plan.CashRequired);
    }

    [Fact]
    public void IngredientStockCannotAlsoBeConsumedAsFoodWithinTheSamePlan()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var recipe=new ProductCraftRecipe(new(20,"Préparation",1,null,MarketObjectType.Resource,32,"Essence","Alchimiste",1),
            [new(10,"Ingrédient comestible",MarketObjectType.Resource,1,null),new(30,"Autre ingrédient",MarketObjectType.Resource,1,null)]);
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,50),Food(20,150)],[recipe],
            new Dictionary<long,BigInteger>{{10,1},{30,1}},Market(Quote(10,1,100),Quote(30,1,1)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(200));
        Assert.True(plan.Experience>=200);Assert.True(plan.StockConsumed.GetValueOrDefault(10)<=1);
        Assert.NotEmpty(plan.Crafts);
        Assert.True(plan.CashRequired>0); // One shared ingredient cannot provide both meals for free.
    }

    [Fact]
    public void OwnedPetCanBeKeptAtItsCurrentLevelWhenAdditionalXpReducesProfit()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,10)],[],new Dictionary<long,BigInteger>(),Market(Quote(10,1,1)));
        var species=new PetSpecies(100,"Familier",null,18);
        var owned=new OwnedPet(species,7,1,"bank",new(0,0,"normal",false,false));
        PetMarketOffer[] offers=[new(0,0,100,"normal",false),new(23,90,105,"normal",false)];
        var result=PetOpportunityService.Assess(species,owned,null,offers,planner,null,default);
        Assert.Equal(0,result.Best!.Level);Assert.Empty(result.Best.Nutrition.Meals);Assert.Equal(0,result.Best.Margin);
        Assert.Equal("bank",result.Owned!.Container);
    }

    [Fact]
    public void ChoosesTheMostProfitableObservedLevelInsteadOfAlwaysLevelOneHundred()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,10)],[],new Dictionary<long,BigInteger>(),Market(Quote(10,1,1)));
        var species=new PetSpecies(100,"Familier",null,18);
        PetMarketOffer[] offers=[new(0,0,100,"normal",false),new(23,90,300,"normal",false),new(44,924,320,"normal",false),
            new(100,179592,1_000_000,"legendary-other-power",true)];
        var result=PetOpportunityService.Assess(species,null,offers[0],offers,planner,null,default);
        Assert.Equal(23,result.Best!.Level);Assert.DoesNotContain(result.Options,option=>option.Level==100);
        Assert.Equal(190,result.Best.Margin);
    }

    [Fact]
    public void NeverValuesAnOvershotPetAtTheWrongObservedLevel()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,500),Food(20,101.25)],[],
            new Dictionary<long,BigInteger>(),Market(Quote(10,1,1),Quote(20,1,50)));
        var species=new PetSpecies(100,"Familier",null,18);
        PetMarketOffer[] offers=[new(0,0,100,"normal",false),new(23,90,300,"normal",false)];
        var result=PetOpportunityService.Assess(species,null,offers[0],offers,planner,null,default);
        Assert.Equal(23,result.Best!.Level);
        Assert.Equal(101,result.Best.Nutrition.Experience);
        Assert.Equal(20,Assert.Single(result.Best.Nutrition.Meals).Food.Id);
        Assert.Equal(23,PetExperience.LevelAt(result.Best.Nutrition.Experience));
    }

    [Fact]
    public void ReadsCurrentResourceYieldAndRejectsLegendaryCroquettesAsXpFood()
    {
        var resource=JsonSerializer.Deserialize<DofusDbItem>("""
            {"id":13942,"name":{"fr":"Bec de Dodox"},"typeId":47,"recyclingNuggets":10.125}
            """,JsonSerializerOptions.Web)!;
        Assert.Equal(101.25,ActivityCatalogService.ReadPetFood(resource,new HashSet<long>{47})!.Experience);
        var legendary=JsonSerializer.Deserialize<DofusDbItem>("""
            {"id":26994,"name":{"fr":"Croquette infusée"},"typeId":209,"possibleEffects":[{"effectId":2864,"value":1}]}
            """,JsonSerializerOptions.Web)!;
        Assert.Null(ActivityCatalogService.ReadPetFood(legendary,new HashSet<long>{47}));
    }
}
