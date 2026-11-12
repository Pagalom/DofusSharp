using System.Numerics;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using FluentAssertions;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class StockOpportunityTest : IDisposable
{
    private readonly BestCrushDbContext _db = new(new Microsoft.EntityFrameworkCore.DbContextOptions<BestCrushDbContext>());
    private readonly MarketPriceService _prices;
    private readonly StockCraftPlanner _planner;
    private static readonly DateTime Now = DateTime.UtcNow;
    public StockOpportunityTest()
    {
        _prices = new(_db,new TestDataPriorityProvider());
        _planner = new(_prices);
    }
    public void Dispose()=>_db.Dispose();
    private static Equipment Recipe(int count=3)
    {
        Equipment equipment=new(100){Name="Équipement",Level=100};
        equipment.Recipe.Add(new(equipment,new Resource(1){Name="Bois"},count));
        return equipment;
    }
    private static EquipmentProfitabilityContext Market(params MarketPriceObservation[] resourcePrices)=>new()
    {
        ResourcePrices=Prices(resourcePrices), EquipmentPrices=Prices(Price(100,1,100,type:MarketObjectType.Equipment,at:Now)),
        RunePrices=Prices(),Coefficients=[]
    };
    private StockCraftPlan Plan(int count=3,int available=3)=>_planner.Calculate(Recipe(count),1,
        new Dictionary<long,BigInteger>{{1,available}},Market(Price(1,1,10,at:Now)));

    [Fact]
    public async Task OwnedMaterialsReduceCashButAreNeverFreeInProfit()
    {
        var plan=Plan();
        plan.CraftableNow.Should().Be(new BigInteger(1));
        plan.CashRequired.Should().Be(0);
        plan.OwnedValue.Should().Be(30);
        plan.EconomicCost.Should().Be(30);
        var rows=await new CraftOpportunityProvider(_prices).EvaluateAsync(new(Server,[plan],Market(Price(1,1,10,at:Now))),default);
        rows.Single().Benefit.Should().Be(70);
        rows.Single().RoiPercent.Should().BeApproximately(233.3333333,0.0001);
    }
    [Fact]
    public void GroupsDuplicateIngredientsAndScalesBatchBeforeConsumingStock()
    {
        var equipment=Recipe(2);
        equipment.Recipe.Add(new(equipment,new Resource(1){Name="Bois"},3));
        var plan=_planner.Calculate(equipment,3,new Dictionary<long,BigInteger>{{1,11}},Market(Price(1,1,10,at:Now)));
        plan.CraftableNow.Should().Be(new BigInteger(2));
        var line=plan.Ingredients.Single();
        line.Required.Should().Be(15);line.Used.Should().Be(11);line.Missing.Should().Be(4);
        plan.CashRequired.Should().Be(40);plan.OwnedValue.Should().Be(110);plan.EconomicCost.Should().Be(150);
    }
    [Fact]
    public void FullLotsAreInCashAndTheirLeftoversRetainConservativeValue()
    {
        var plan=_planner.Calculate(Recipe(2),1,new Dictionary<long,BigInteger>{{1,1}},Market(Price(1,10,100,at:Now)));
        plan.CashRequired.Should().Be(100);plan.OwnedValue.Should().Be(10);
        plan.Ingredients.Single().Purchase!.SurplusQuantity.Should().Be(9);
        plan.SurplusCredit.Should().Be(90);plan.EconomicCost.Should().Be(20);
    }
    [Fact]
    public void ExpensiveSinglesCannotTurnCheapLotLeftoversIntoArtificialProfit()
    {
        var plan=_planner.Calculate(Recipe(1),1,new Dictionary<long,BigInteger>(),
            Market(Price(1,1,1000,at:Now),Price(1,10,100,at:Now)));
        plan.SurplusCredit.Should().Be(90);plan.EconomicCost.Should().Be(10);
    }
    [Fact]
    public async Task MissingOwnedMaterialPriceDoesNotBecomeFreeOrRankAsProfit()
    {
        var plan=_planner.Calculate(Recipe(),1,new Dictionary<long,BigInteger>{{1,3}},Market());
        plan.CashRequired.Should().Be(0);plan.EconomicCost.Should().BeNull();
        var rows=await new CraftOpportunityProvider(_prices).EvaluateAsync(new(Server,[plan],Market()),default);
        rows.Single().Revenue.Should().Be(100);rows.Single().Benefit.Should().BeNull();
        OpportunityRanking.Select(rows,Now,0,0,null,false,"gain").Should().BeEmpty();
    }
    [Fact]
    public void EquipmentIngredientsUseTheirOwnMarketAndParticipateInCraftableLimit()
    {
        var equipment=Recipe(2);
        equipment.EquipmentRecipe.Add(new(equipment,new Equipment(3){Name="Anneau"},2));
        var market=Market(Price(1,1,10,at:Now));
        market=new(){ResourcePrices=market.ResourcePrices,RunePrices=market.RunePrices,Coefficients=[],
            EquipmentPrices=Prices(Price(3,1,50,type:MarketObjectType.Equipment,at:Now),Price(3,10,1,type:MarketObjectType.Equipment,at:Now))};
        var plan=_planner.Calculate(equipment,2,new Dictionary<long,BigInteger>{{1,100},{3,3}},market);
        plan.CraftableNow.Should().Be(new BigInteger(1));plan.CashRequired.Should().Be(50);
        plan.Ingredients.Single(x=>x.ItemId==3).Purchase!.PurchasedQuantity.Should().Be(1);
    }
    [Fact]
    public void HugeStockDoesNotOverflowAndRequestedBatchRemainsBounded()
    {
        var plan=_planner.Calculate(Recipe(1),2,new Dictionary<long,BigInteger>{{1,BigInteger.Parse("184467440737095516160")}},Market(Price(1,1,10,at:Now)));
        plan.Ingredients.Single().Used.Should().Be(2);(plan.CraftableNow > ulong.MaxValue).Should().BeTrue();
        Action invalid=()=>_planner.Calculate(Recipe(),1001,new Dictionary<long,BigInteger>(),Market());
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }
    [Fact]
    public void TwoPlansAreAlternativesAndDoNotReserveOrMutateSharedStock()
    {
        var stock=new Dictionary<long,BigInteger>{{1,3}};
        var market=Market(Price(1,1,10,at:Now));
        _planner.Calculate(Recipe(),1,stock,market).CraftableNow.Should().Be(new BigInteger(1));
        _planner.Calculate(Recipe(),1,stock,market).CraftableNow.Should().Be(new BigInteger(1));
        stock[1].Should().Be(new BigInteger(3));
    }
    [Fact]
    public void RankingCombinesOriginsAndAppliesBudgetRoiFreshnessAndCompleteness()
    {
        var plan=Plan();
        var recent=new OpportunityEvidence(plan.Observations.ToArray());
        var old=new OpportunityEvidence([Price(1,1,10,at:Now.AddHours(-13))]);
        EconomicOpportunity Row(string origin,double revenue)=>new(origin,"Test",Server,plan,revenue,recent,[]);
        var craft=Row("craft",100);var crush=Row("concassage",200);
        var stale=Row("craft",1000) with{Evidence=old};
        var incomplete=Row("concassage",2000) with{MissingData=["Rune manquante"]};
        var rows=OpportunityRanking.Select([craft,crush,stale,incomplete],Now,0,100,0,true,"gain");
        rows.Should().Equal(crush,craft);
        rows[0].SourceUrl.Should().Contain("?stocks=true&item=100");
        var costly=crush with{Plan=Plan(3,1)};
        OpportunityRanking.Select([costly],Now,0,0,10,true,"gain").Should().BeEmpty();
    }
    [Fact]
    public void CoefficientAgeCountsEvenWhenEveryPriceIsFresh()
    {
        var evidence=new OpportunityEvidence([Price(1,1,10,at:Now)],new CoefficientObservation
        {DofusDbId=100,ServerName=Server,CoefficientPercent=100,Source=CoefficientSource.InGameAutomatic,ObservedAtUtc=Now.AddHours(-13)});
        evidence.IsStale(Now).Should().BeTrue();
    }
    [Fact]
    public void SynchronizedInventoryIsRequiredButUnsynchronizedBankIsOptional()
    {
        var inventory=new LiveStockContainer("inventory",true,false,Now,[]);
        var bank=new LiveStockContainer("bank",true,false,Now,[]);
        LiveStockSource source=new("scope","Pagalom",Server,Now,[inventory,bank]);
        StockOpportunityService.CanUse(source,Server).Should().BeTrue();
        StockOpportunityService.CanUse(source,"Autre serveur").Should().BeFalse();
        StockOpportunityService.CanUse(null,Server).Should().BeFalse();
        StockOpportunityService.CanUse(source with{Containers=[inventory]},Server).Should().BeTrue();
        StockOpportunityService.CanUse(source with{Containers=[inventory,bank with{Stale=true}]},Server).Should().BeTrue();
        StockOpportunityService.HasSynchronizedBank(source).Should().BeTrue();
        StockOpportunityService.HasSynchronizedBank(source with{Containers=[inventory]}).Should().BeFalse();
        StockOpportunityService.HasSynchronizedBank(source with{Containers=[inventory,bank with{Stale=true}]}).Should().BeFalse();
        StockOpportunityService.CanUse(source with{Containers=[bank]},Server).Should().BeFalse();
        StockOpportunityService.CanUse(source with{Containers=[inventory with{Stale=true},bank]},Server).Should().BeFalse();
        StockOpportunityService.CanUse(source with{Containers=[inventory with{Complete=false},bank]},Server).Should().BeFalse();
    }
    [Fact]
    public async Task MissingCrushingCoefficientDoesNotFetchExternalDataOrInventRevenue()
    {
        var rows=await new CrushingOpportunityProvider(null!,_prices,null!).EvaluateAsync(new(Server,[Plan()],Market()),default);
        rows.Single().Revenue.Should().BeNull();rows.Single().Evidence.Coefficient.Should().BeNull();
        rows.Single().MissingData.Should().ContainSingle();
    }
}
