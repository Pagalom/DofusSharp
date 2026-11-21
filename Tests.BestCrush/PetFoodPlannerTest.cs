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
    public void SelectsOneFoodByCostPerXpInsteadOfSearchingForACheaperMixture()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,10),Food(20,100)],[],
            new Dictionary<long,BigInteger>{{10,2}},Market(Quote(10,1,5),Quote(20,1,25)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(120));
        Assert.True(plan.Experience>=120);
        Assert.Equal(50,plan.CashRequired);Assert.Equal(0,plan.StockValue);
        Assert.Empty(plan.StockConsumed);Assert.Equal(20,Assert.Single(plan.Meals).Food.Id);
        Assert.Equal(20,Assert.Single(plan.Purchases).ItemId);
    }

    [Fact]
    public void UsesOwnedUnitsOfTheSelectedFoodThenPurchasesTheRemainder()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,10)],[],
            new Dictionary<long,BigInteger>{{10,2}},Market(Quote(10,1,5)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(50));
        var meal=Assert.Single(plan.Meals);
        Assert.Equal(5,meal.Quantity);Assert.Equal(2,meal.FromStock);Assert.Equal(3,meal.Bought);
        Assert.Equal(8,plan.StockValue);Assert.Equal(15,plan.CashRequired);
    }

    [Fact]
    public void ComparesXpPerKamaRatherThanTheLowestResourceUnitPrice()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,1),Food(20,100)],[],
            new Dictionary<long,BigInteger>(),Market(Quote(10,1,1),Quote(20,1,25)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(100));
        Assert.Equal(20,Assert.Single(plan.Meals).Food.Id);Assert.Equal(25,plan.CashRequired);
    }

    [Fact]
    public void DoesNotRankASmallCheapStockAsAnUnlimitedSource()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,10),Food(20,10)],[],
            new Dictionary<long,BigInteger>{{10,1}},Market(Quote(10,1,2),Quote(20,1,1)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(1000));
        Assert.Equal(20,Assert.Single(plan.Meals).Food.Id);Assert.Equal(100,plan.CashRequired);
    }

    [Fact]
    public void CheapestRatioCanRequireABiggerLotWithoutStartingAMixtureSearch()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,10),Food(20,10)],[],
            new Dictionary<long,BigInteger>(),Market(Quote(10,100,100),Quote(20,1,2)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(10));
        var meal=Assert.Single(plan.Meals);
        Assert.Equal(10,meal.Food.Id);Assert.Equal(1,meal.Quantity);Assert.Equal(100,meal.Bought);
        Assert.Equal(100,plan.CashRequired);
    }

    [Fact]
    public void CanCraftTheSelectedFoodFromStockAndPurchaseTheRemainingUnits()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var recipe=new ProductCraftRecipe(new(20,"Préparation",1,null,MarketObjectType.Resource,32,"Essence","Alchimiste",1),
            [new(30,"Ingrédient",MarketObjectType.Resource,2,null)]);
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(20,10)],[recipe],
            new Dictionary<long,BigInteger>{{30,4}},Market(Quote(20,1,10),Quote(30,1,6)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(50));
        var meal=Assert.Single(plan.Meals);
        Assert.Equal(2,meal.Crafted);Assert.Equal(3,meal.Bought);Assert.Equal(4,plan.StockConsumed[30]);
        Assert.Equal(30,plan.CashRequired);Assert.Equal(20,plan.StockValue);
    }

    [Fact]
    public void PricesMissingIngredientsWhenCraftingHasTheBestXpRatio()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var recipe=new ProductCraftRecipe(new(20,"Préparation",1,null,MarketObjectType.Resource,32,"Essence","Alchimiste",1),
            [new(30,"Ingrédient",MarketObjectType.Resource,2,null)]);
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,10),Food(20,100)],[recipe],
            new Dictionary<long,BigInteger>{{30,2}},Market(Quote(10,1,5),Quote(20,1,60),Quote(30,1,10)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(200));
        Assert.Equal(20,Assert.Single(plan.Meals).Food.Id);Assert.Equal(2,Assert.Single(plan.Crafts).Quantity);
        Assert.Equal(2,plan.StockConsumed[30]);Assert.Equal(18,plan.StockValue);Assert.Equal(20,plan.CashRequired);
        Assert.Equal(30,Assert.Single(plan.Purchases).ItemId);
    }

    [Fact]
    public void MissingPricesNeverTurnOwnedFoodIntoFreeProfit()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,100),Food(20,100)],[],
            new Dictionary<long,BigInteger>{{10,100}},Market(Quote(20,1,10)));
        var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(100));
        Assert.Equal(20,Assert.Single(plan.Meals).Food.Id);Assert.Equal(10,plan.EconomicCost);
        var unknown=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[Food(10,100)],[],
            new Dictionary<long,BigInteger>{{10,100}},Market()).Calculate(100);
        Assert.NotNull(unknown);Assert.Null(unknown.EconomicCost);
    }

    [Fact]
    public void ManyFoodsAndTargetLevelsDoNotAccumulateIntermediateMealCombinations()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var foods=Enumerable.Range(1,1200).Select(id=>Food(id,100)).ToArray();
        var market=Market(foods.Select(food=>Quote(food.Id,1,food.Id+10)).ToArray());
        var stock=foods.ToDictionary(food=>food.Id,_=>new BigInteger(50));
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),foods,[],stock,market);
        planner.Calculate(100); // Initialize source prices before measuring target growth.
        long start=GC.GetAllocatedBytesForCurrentThread();
        for(int needed=101;needed<1101;needed++)
        {
            var plan=Assert.IsType<PetNutritionPlan>(planner.Calculate(needed));
            Assert.Single(plan.Meals);Assert.True(plan.Experience>=needed);
        }
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread()-start,0,64L*1024*1024);
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
