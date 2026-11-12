using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class ActivityRankingTest
{
    private static readonly DateTimeOffset Now=DateTimeOffset.UtcNow;
    private static ActivityRecommendation Row(string id,double gain,long? daily=10,string? job=null,int? level=null)
    {
        var key=new SalesMarketKey("Tylezia",MarketObjectType.Resource,1);
        var observation=daily is { } units ? new GlobalSalesObservation(key,Now.AddDays(-1),Now,units,Now) : null;
        var value=SalesKineticsCalculator.EvaluateProfitability(key,10,10+gain,10,observation,Now,TimeSpan.FromDays(2));
        return new(id,"farming","Récolte",1,id,20,MarketObjectType.Resource,10,10+gain,10,value,
            daily is null ? ["Historique manquant"] : [],[]) {Job=job,RequiredJobLevel=level,PriceUpdatedUtc=Now.UtcDateTime};
    }

    [Fact]
    public void KineticsCanRankAFasterSmallerMarginAboveASlowLargerMarginButNeverPromotesALoss()
    {
        var fast=Row("Rapide",50,100);var slow=Row("Lent",100,1);
        var unknown=Row("À vérifier",500,null);var loss=Row("Perte",-5,1000);
        var rows=ActivityRanking.Select([loss,slow,unknown,fast],new(),Now.UtcDateTime);
        Assert.Equal(new[]{"Rapide","Lent","À vérifier","Perte"},rows.Select(x=>x.Id));
        Assert.Equal("À vérifier",ActivityRanking.Select(rows,new(Sort:"gain"),Now.UtcDateTime).First().Id);
    }

    [Fact]
    public void PurchaseTargetDemotesLowerReturnsAndRoundsKamaThresholds()
    {
        var below = Row("Gros gain", 100) with { Cost = 1000, Revenue = 1100, TradingLots = new(1, 1000, 1, 1100) };
        var atTarget = Row("Objectif atteint", 10) with { Cost = 100, Revenue = 115, TradingLots = new(1, 100, 1, 115) };
        var rows = ActivityRanking.Select([below, atTarget], new(Sort: "gain", TradingTargetRoi: 15), Now.UtcDateTime);

        Assert.Equal(new[] { atTarget, below }, rows);
        Assert.Equal(new TradingPriceTargets(12_109, 11_497), TradingPriceTargets.Calculate(9_997, 13_926, 15));
        Assert.Equal(new TradingPriceTargets(13_925, 9_998), TradingPriceTargets.Calculate(9_997, 13_926, 0));
    }

    [Theory]
    [InlineData(null, null, 3)]
    [InlineData(MarketObjectType.Equipment, null, 1)]
    [InlineData(MarketObjectType.Resource, null, 1)]
    [InlineData(MarketObjectType.Rune, null, 1)]
    [InlineData(MarketObjectType.Equipment, MarketObjectType.Rune, 2)]
    public void TradingCategoriesCanBeCombined(MarketObjectType? first, MarketObjectType? second, int count)
    {
        var candidates = new[]
        {
            Row("Chapeau", 10) with { ObjectType = MarketObjectType.Equipment },
            Row("Bois", 10),
            Row("Rune Sa", 10) with { ObjectType = MarketObjectType.Rune }
        };
        var categories = new[] { first, second }.OfType<MarketObjectType>().ToArray();
        var rows = ActivityRanking.Select(candidates, new(TradingCategories: categories), Now.UtcDateTime);

        Assert.Equal(count, rows.Count);
        if (categories.Length > 0)
            Assert.Equal(categories.Order(), rows.Select(row => row.ObjectType).Order());
    }

    [Fact]
    public void JobFilterKeepsUnknownLevelsButHidesKnownInaccessibleHarvests()
    {
        var wood=Row("Bois",5,job:"Bûcheron",level:40);
        var ore=Row("Minerai",5,job:"Mineur",level:60);
        var unknown=Row("Inconnu",5,job:"Paysan",level:100);
        var filters=new ActivityFilters(RespectJobs:true);
        Assert.Equal(3,ActivityRanking.Select([wood,ore,unknown],filters,Now.UtcDateTime).Count);
        var rows=ActivityRanking.Select([wood,ore,unknown],filters,Now.UtcDateTime,new Dictionary<string,int>{{"Bûcheron",50},{"Mineur",20}});
        Assert.Contains(wood,rows);Assert.Contains(unknown,rows);Assert.DoesNotContain(ore,rows);
    }

    [Fact]
    public void FarmingSeparatesBothSourcesOfOneResourceAndLimitsOnlyHarvestsByJob()
    {
        var harvest = Row("Chêne récolté", 100, job: "Bûcheron", level: 40) with { MonsterIds = [12] };
        var combat = Row("Chêne en combat", 100) with { MonsterIds = [12, 45], CraftJob = "Sculpteur", RequiredCraftLevel = 100 };
        var otherMonster = Row("Autre loot", 100) with { MonsterIds = [10] };
        var ore = Row("Minerai", 100, job: "Mineur", level: 20);
        var candidates = new[] { harvest, combat, otherMonster, ore };
        var levels = new Dictionary<string, int> { ["Bûcheron"] = 20, ["Mineur"] = 25, ["Sculpteur"] = 10 };
        var harvestFilter = new ActivityFilters(FarmSource: FarmingSource.Harvest, HarvestJob: "Bûcheron");

        Assert.Equal(harvest, Assert.Single(ActivityRanking.Select(candidates, harvestFilter, Now.UtcDateTime, levels)));
        Assert.Empty(ActivityRanking.Select(candidates, harvestFilter with { RespectHarvestLevels = true }, Now.UtcDateTime, levels));
        Assert.Equal(combat, Assert.Single(ActivityRanking.Select(candidates,
            new(Search: "chene", FarmSource: FarmingSource.Combat, RespectHarvestLevels: true,
                CombatSelection: new([new(45, "Monstre", 30)], [])), Now.UtcDateTime, levels)));
    }

    [Fact]
    public void FarmingCombinesSeveralZonesAndMonstersAndSortsByTheirMatchingSources()
    {
        CombatSource[] monsters = [new(1, "Bouftou", 20), new(2, "Arakne", 5), new(3, "Tofu", 10), new(4, "Dragon", 100)];
        FarmingZone[] zones = [new(12, "Champs", null, [1]), new(13, "Bois", null, [2]), new(14, "Autre zone", null, [1, 4])];
        var catalogue = new FarmingCombatCatalog(monsters, zones);
        var combat = new FarmingCombatSelection([monsters[2], monsters[0]], [zones[0], zones[1]]);
        var loot1 = Row("Premier loot", 100) with { MonsterIds = [1] };
        var loot2 = Row("Deuxième loot", 20) with { MonsterIds = [2] };
        var loot3 = Row("Troisième loot", 50) with { MonsterIds = [3] };
        var other = Row("Autre loot", 30) with { MonsterIds = [4] };
        var harvest = Row("Récolte", 30, job: "Paysan", level: 1) with { MonsterIds = [1] };
        var candidates = new[] { loot1, loot2, loot3, other, harvest };
        var filter = new ActivityFilters(Sort: "zone", FarmSource: FarmingSource.Combat,
            CombatSelection: combat, CombatCatalog: catalogue);

        Assert.Equal(new[] { loot2, loot1, loot3 }, ActivityRanking.Select(candidates, filter, Now.UtcDateTime));
        Assert.Equal(new[] { loot2, loot1, loot3 }, ActivityRanking.Select(candidates, filter with { Sort = "monster" }, Now.UtcDateTime));
        Assert.Equal("Champs", catalogue.ZoneNameFor(loot1.MonsterIds, combat));
        Assert.Equal("Autre zone", catalogue.ZoneNameFor(loot1.MonsterIds, FarmingCombatSelection.Empty));
    }
    [Fact]
    public void FiltersPrecedeTop100AndUnknownPriceOrDemandIsNotSilentlyMadeComplete()
    {
        var rows=Enumerable.Range(1,220).Select(i=>Row((i<=120 ? "Autre" : "Cible")+i,i,i)).ToArray();
        var matching=ActivityRanking.Select(rows,new(Search:"Cible",Sort:"gain"),Now.UtcDateTime).Take(100).ToArray();
        Assert.Equal(100,matching.Length);Assert.Equal("Cible220",matching.First().Id);Assert.Equal("Cible121",matching.Last().Id);
        var incomplete=Row("Historique absent",500,null);
        Assert.Empty(ActivityRanking.Select([incomplete],new(CompleteOnly:true),Now.UtcDateTime));
        Assert.Single(ActivityRanking.Select([incomplete],new(),Now.UtcDateTime));
        var old=Row("Ancien",500) with{PriceUpdatedUtc=Now.UtcDateTime.AddDays(-1)};
        Assert.Empty(ActivityRanking.Select([old],new(ExcludeOldPrices:true),Now.UtcDateTime));
        Assert.Empty(ActivityRanking.Select([old],new(Budget:5),Now.UtcDateTime));
        var owned=Row("Craft sur stock",50) with{Module="craft",CashRequired=0};
        Assert.Single(ActivityRanking.Select([owned],new(Budget:0,ReadyOnly:true,Origin:"craft"),Now.UtcDateTime));
        Assert.Empty(ActivityRanking.Select([owned],new(Origin:"achat-vente"),Now.UtcDateTime));
        Assert.Empty(ActivityRanking.Select([owned],new(MinimumRoi:1000),Now.UtcDateTime));
    }
}
