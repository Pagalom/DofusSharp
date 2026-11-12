using System.Reflection;
using System.Text.RegularExpressions;
using BestCrush.Components.Pure;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class ActivityOpportunityTest
{
    private sealed class PublicCatalog : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls;
        public bool Offline;
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if(Offline)throw new HttpRequestException("Offline fixture");
            string route=request.RequestUri!.AbsolutePath.Trim('/');
            string query=WebUtility.UrlDecode(request.RequestUri.Query);
            object[] data=route switch
            {
                "jobs" => [new{id=2,name=new{fr="Bûcheron"}},new{id=11,name=new{fr="Sculpteur"}}],
                "skills" => [new{id=6,parentJobId=2,gatheredRessourceItem=1,levelMin=10,
                    item=new{id=1,name=new{fr="Bois"},level=1,typeId=38,dropMonsterIds=new[]{10}}}],
                "recipes" => [new{id=100,resultId=100,jobId=11,resultLevel=20}],
                "dungeons" => [new{id=1,name=new{fr="Donjon de test"},bosses=new[]{147,148,149}}],
                "monsters" => [
                    new{id=147,name=new{fr="Boss capturable"},isBoss=true,isMiniBoss=false,soulCaptureForbidden=false,grades=new[]{new{level=40}}},
                    new{id=148,name=new{fr="Archimonstre"},isBoss=true,isMiniBoss=true,soulCaptureForbidden=false,grades=new[]{new{level=40}}},
                    new{id=149,name=new{fr="Capture interdite"},isBoss=true,isMiniBoss=false,soulCaptureForbidden=true,grades=new[]{new{level=40}}}],
                "items" when query.Contains("typeId=83") => [new{id=9686,name=new{fr="Petite pierre"},typeId=83,
                    possibleEffects=new[]{new{effectId=705,value=50,diceNum=100}}}],
                "items" when query.Contains("iconId") && !query.Contains("dropMonsterIds") => [new { id = 1, iconId = 9001 }],

                "items" => [new{id=1,name=new{fr="Bois"},level=1,typeId=38,dropMonsterIds=new[]{10}},
                    new{id=2,name=new{fr="Écorce"},level=10,typeId=15,dropMonsterIds=Array.Empty<int>()}],
                _ => throw new InvalidOperationException("Unexpected route "+route)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(
                JsonSerializer.Serialize(new{total=data.Length,limit=50,skip=0,data}),Encoding.UTF8,"application/json")});
        }
    }
    private sealed class Harness : IDisposable
    {
        public readonly TestDatabase Database=new();
        public readonly string Folder=Path.Combine(Path.GetTempPath(),"activity-tests-"+Guid.NewGuid());
        public readonly PublicCatalog Http=new();
        public readonly MarketDataChangeNotifier Changes=new();
        public GlobalSalesRepository Sales {get;}
        public ActivityCatalogService Catalog {get;}
        public ActivityOpportunityService Scanner {get;}
        public Harness()
        {
            Sales=new(Path.Combine(Folder,"sales.db"));
            Catalog=new(DofusDbClient.Production(),Http,Path.Combine(Folder,"catalog"));
            Scanner=new(Database,new TestDataPriorityProvider(),Sales,Catalog,Changes);
        }
        public async Task SeedAsync()
        {
            using var db=Database.CreateContext();
            var wood=new Resource(1){Name="Bois",Level=1,DofusDbIconId=9001};var bark=new Resource(2){Name="Écorce",Level=10,DofusDbIconId=9002};
            var equipment=new Equipment(100){Name="Bâton rentable",Level=20,DofusDbIconId=9100};
            equipment.Recipe.Add(new(equipment,wood,2));equipment.Recipe.Add(new(equipment,bark,1));
            db.Equipments.Add(equipment);
            db.MarketPriceObservations.AddRange(Price(1,1,5,server:"Tylezia",at:DateTime.UtcNow),
                Price(1,10,100,server:"Tylezia",at:DateTime.UtcNow),Price(2,1,20,server:"Tylezia",at:DateTime.UtcNow),
                Price(100,1,1000,type:MarketObjectType.Equipment,server:"Tylezia",at:DateTime.UtcNow),
                Price(9686,1,200,server:"Tylezia",at:DateTime.UtcNow));
            await db.SaveChangesAsync();
            var now=DateTimeOffset.UtcNow;
            Sales.SaveHistory(new(new("Tylezia",MarketObjectType.Resource,1),now,
                [new(12,50,now.AddHours(-2)),new(12,50,now.AddHours(-1))],[new(12,700,now.AddHours(-1))]));
        }
        public static LiveStockSource Stock(string server="Tylezia",ulong quantity=20)=>new("current-Pagalom","Pagalom",server,DateTime.UtcNow,
            [new("inventory",true,false,DateTime.UtcNow,[new(1,1,quantity,null,null,null)])]);
        public void Dispose(){Scanner.Dispose();Database.Dispose();Http.Dispose();if(Directory.Exists(Folder))Directory.Delete(Folder,true);}
    }

    [Fact]
    public async Task TradingKeepsOldQuotesAsIndicationsAndExcludesWithdrawnLots()    {        using var h = new Harness();        await h.SeedAsync();        const long itemId = 8383;        var now = DateTime.UtcNow;        var oldQuote = now.AddHours(-13);        using (var db = h.Database.CreateContext())        {            db.Resources.Add(new Resource(itemId) { Name = "Peau de Moon" });            db.MarketPriceObservations.AddRange(                Price(itemId, 1, 8_884, server: "Tylezia", at: oldQuote),                Price(itemId, 10, 164_998, server: "Tylezia", at: oldQuote),                Price(itemId, 1000, 2_500_000, server: "Tylezia", at: now.AddDays(-3)),                Price(itemId, 1000, 0, server: "Tylezia", at: oldQuote, cleared: true),                Price(itemId, 100, 300_000, source: MarketPriceSource.Manual, server: "Tylezia", at: now.AddMinutes(-2)),                Price(itemId, 100, 0, server: "Tylezia", at: now.AddMinutes(-1), cleared: true));            await db.SaveChangesAsync();        }        var report = await h.Scanner.ScanAsync("achat-vente", "Tylezia", null, 1, default);        var offer = Assert.Single(report.Candidates, row => row.ItemId == itemId);        Assert.Equal("lots-Resource-8383-1-10", offer.Id);        Assert.Equal(76_157d, offer.Profitability.Gain);        Assert.Equal(oldQuote, offer.PriceUpdatedUtc);        Assert.Single(ActivityRanking.Select([offer], new(), now));        Assert.Empty(ActivityRanking.Select([offer], new(ExcludeOldPrices: true), now));        Assert.Contains(offer.MissingData, detail => detail.StartsWith("Historique global"));    }    [Fact]
    public async Task EquipmentOffersAreExcludedFromLotRecomposition()
    {
        using var h = new Harness();
        await h.SeedAsync();
        var now = DateTimeOffset.UtcNow;
        using (var db = h.Database.CreateContext())
        {
            db.MarketPriceObservations.Add(Price(100, 10, 15_000, type: MarketObjectType.Equipment,
                server: "Tylezia", at: now.UtcDateTime));
            await db.SaveChangesAsync();
        }
        h.Sales.SaveHistory(new(new("Tylezia", MarketObjectType.Equipment, 100), now,
            [new(1800, 20, now.AddHours(-2)), new(1800, 20, now.AddHours(-1))],
            [new(1800, 40, now.AddDays(-1))]));

        var report = await h.Scanner.ScanAsync("achat-vente", "Tylezia", null, 1, default);
        Assert.Contains(report.Candidates, row => row.Id == "history-Equipment-100-1");
        Assert.DoesNotContain(report.Candidates, row => row.Id.StartsWith("lots-Equipment-"));
        Assert.Contains(report.Candidates, row => row.Id.StartsWith("lots-Resource-"));
    }

    [Fact]    public async Task HistoricalTradeComparesIdenticalLotSizesAndUsesLatestDetailedSale()
    {
        using var h = new Harness();
        await h.SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var lastReached = now.AddDays(-2);
        h.Sales.SaveHistory(new(new("Tylezia", MarketObjectType.Resource, 1), now,
            [new(19, 50, now.AddHours(-1))],
            [new(2, 2, now.AddDays(-3)), new(20, 100, lastReached), new(21, 1, now.AddHours(-1))]));

        var report = await h.Scanner.ScanAsync("achat-vente", "Tylezia", null, 1, default);
        Assert.DoesNotContain(report.Candidates, candidate => candidate.Id == "history-Resource-1-10");
        var row = Assert.Single(report.Candidates, candidate => candidate.Id == "history-Resource-1-1");
        var comparison = Assert.IsType<HistoricalPriceComparison>(row.HistoricalPrice);
        Assert.Equal(5d, comparison.CurrentLotPrice);
        Assert.Equal(20d, comparison.MedianLotPrice);
        Assert.Equal(19d, row.Revenue);
        Assert.Equal(14d, row.Profitability.Gain);
        Assert.Equal(lastReached, comparison.LastAtOrAboveMedianUtc);

        ActivityFilters filters = new(MinimumMedianDiscount: 75, MaximumMedianAgeDays: 2);
        Assert.Single(ActivityRanking.Select([row], filters, now.UtcDateTime));
        Assert.Empty(ActivityRanking.Select([row], filters with { MaximumMedianAgeDays = 1 }, now.UtcDateTime));
        Assert.Empty(ActivityRanking.Select([row], filters with { MinimumMedianDiscount = 76 }, now.UtcDateTime));
    }

    [Fact]
    public async Task HistoricalTradeRejectsTheEninfernoOfferAboveTheMedian()
    {
        using var h = new Harness();
        await h.SeedAsync();
        const long itemId = 25180;
        var now = DateTimeOffset.UtcNow;
        using (var db = h.Database.CreateContext())
        {
            db.Resources.Add(new Resource(itemId) { Name = "Aile d’Eninferno" });
            db.MarketPriceObservations.Add(Price(itemId, 1, 982, server: "Tylezia", at: now.UtcDateTime));
            await db.SaveChangesAsync();
        }

        h.Sales.SaveHistory(new(new("Tylezia", MarketObjectType.Resource, itemId), now,
            [new(1_283, 100, now.AddHours(-1))],
            [new(817, 99, now.AddDays(-6).AddHours(-1)), new(959, 220, now.AddDays(-6)),
             new(2_302, 406, now.AddDays(-5)), new(661, 40, now.AddDays(-4)),
             new(854, 35, now.AddDays(-3)), new(927, 905, now.AddDays(-2)),
             new(1_043, 230, now.AddDays(-1)), new(1_283, 100, now.AddHours(-1))]));

        var report = await h.Scanner.ScanAsync("achat-vente", "Tylezia", null, 1, default);
        Assert.DoesNotContain(report.Candidates, candidate => candidate.ItemId == itemId);
    }

    [Fact]
    public async Task TradingUsesHourlyDemandForResaleFormatAndTotalQuantity()
    {
        using var h = new Harness();
        await h.SeedAsync();
        using (var db = h.Database.CreateContext())
        {
            var at = DateTime.UtcNow;
            db.Resources.Add(new Resource(8789) { Name = "Chaussette du Kimbo" });
            db.MarketPriceObservations.AddRange(
                Price(8789, 1, 130_000, server: "Tylezia", at: at),
                Price(8789, 10, 1_189_990, server: "Tylezia", at: at),
                Price(8789, 100, 11_899_900, server: "Tylezia", at: at),
                Price(8789, 1000, 118_999_000, server: "Tylezia", at: at));
            await db.SaveChangesAsync();
        }

        var pendingReport = await h.Scanner.ScanAsync("achat-vente", "Tylezia", null, 1, default);
        var provisional = pendingReport.Candidates.Where(row => row.ItemId == 8789).ToArray();
        Assert.Equal(3, provisional.Length);
        Assert.All(provisional, row =>
        {
            Assert.Equal(129_999d, row.TradingLots?.SellPrice);
            Assert.True(row.Profitability.Gain > 0);
            Assert.Null(row.Profitability.MarketDailyGainCapacity);
            Assert.Contains(row.MissingData, info => info.StartsWith("Historique global"));
        });
        var bestGain = ActivityRanking.Select(provisional, new(Sort: "gain"), DateTime.UtcNow)[0];
        Assert.Equal(1_000, bestGain.Quantity);
        Assert.Equal(11_000_000d, bestGain.Profitability.Gain);

        var now = DateTimeOffset.UtcNow;
        var intervals = new (long Units, long Price)[]
        {
            (6, 129330), (11, 121816), (12, 123656), (10, 146999), (14, 130927),
            (3, 145996), (1, 145994), (1, 145991), (1, 145987), (1, 145988),
            (1, 145986), (5, 145985), (1, 145000), (2, 121229), (1, 121229),
            (1, 121229), (53, 125137), (1, 145982), (15, 131926)
        };
        h.Sales.SaveHistory(new(new("Tylezia", MarketObjectType.Resource, 8789), now,
            intervals.Select((point, index) => new GlobalSalePoint(point.Price, point.Units, now.AddHours(index - 19))).ToArray(),
            [new(129_912, 531, now.AddDays(-1))]));

        var report = await h.Scanner.ScanAsync("achat-vente", "Tylezia", null, 1, default);
        var repacked = Assert.Single(report.Candidates, row => row.ItemId == 8789);
        Assert.Equal("Recomposer des lots", repacked.Strategy);
        Assert.Equal(10, repacked.Quantity);
        Assert.Equal(1, repacked.TradingLots?.SellQuantity);
        Assert.Equal(54d, repacked.Profitability.Kinetics.UnitsPerDay);
        Assert.True(repacked.UsesResaleVolumeCeiling);
        Assert.Equal(repacked.Profitability.Gain!.Value * 54 / 10, repacked.Profitability.MarketDailyGainCapacity!.Value, 6);
        Assert.Equal(129_999d, repacked.TradingLots?.SellPrice);
        Assert.Equal(1_299_990d, repacked.Revenue);
    }

    [Fact]
    public async Task TradingRequiresSeveralHoursAtTheProposedLotPrice()
    {
        using var h = new Harness();
        await h.SeedAsync();
        var now = DateTimeOffset.UtcNow;
        h.Sales.SaveHistory(new(new("Tylezia", MarketObjectType.Resource, 1), now,
            [new(5, 50, now.AddHours(-3)), new(5, 50, now.AddHours(-2)), new(10, 50, now.AddHours(-1))],
            [new(5, 150, now.AddDays(-1))]));
        var report = await h.Scanner.ScanAsync("achat-vente", "Tylezia", null, 1, default);
        Assert.DoesNotContain(report.Candidates, row => row.ItemId == 1);
    }

    [Fact]
    public async Task TradingDiscardsAbnormalLotPricesFromRecommendationsAndStockValuation()
    {
        using var h = new Harness();
        await h.SeedAsync();
        var at = DateTime.UtcNow.AddSeconds(1);
        using (var db = h.Database.CreateContext())
        {
            db.MarketPriceObservations.AddRange(
                Price(1, 1, 100, server: "Tylezia", at: at),
                Price(1, 10, 997, server: "Tylezia", at: at),
                Price(1, 100, 5_000_000, server: "Tylezia", at: at),
                Price(1, 1000, 67_999, server: "Tylezia", at: at),
                Price(2, 10, 100_000, server: "Tylezia", at: at),
                Price(2, 100, 2_000, server: "Tylezia", at: at));
            await db.SaveChangesAsync();
        }

        var now = DateTimeOffset.UtcNow;
        h.Sales.SaveHistory(new(new("Tylezia", MarketObjectType.Resource, 1), now,
            [new(100, 500, now.AddHours(-2)), new(100, 500, now.AddHours(-1))], [new(100, 1000, now.AddDays(-1))]));
        var report = await h.Scanner.ScanAsync("achat-vente", "Tylezia", Harness.Stock(quantity: 100), 1, default);
        Assert.DoesNotContain(report.Candidates, row =>
            row.TradingLots?.BuyPrice == 5_000_000 || row.TradingLots?.SellPrice == 5_000_000);
        Assert.Equal(31_601d, Assert.Single(report.Candidates, row => row.Id == "lots-Resource-1-1000-10").Profitability.Gain);
        Assert.Equal(9_960d, Assert.Single(report.Candidates, row => row.Id == "owned-Resource-1").Revenue);
        Assert.DoesNotContain(report.Candidates, row => row.ItemId == 2);
    }

    [Fact]
    public async Task BuySellComputesLotsHistoricalDiscountAndCraftNoticeAndReusesReport()
    {
        using var h=new Harness();await h.SeedAsync();var stock=Harness.Stock();
        var report=await h.Scanner.ScanAsync("achat-vente","Tylezia",stock,100,default);
        var lot=Assert.Single(report.Candidates,x=>x.Id=="lots-Resource-1-1-10");
        Assert.Equal(10,lot.Quantity);Assert.Equal(50d,lot.Cost);Assert.Equal(99d,lot.Revenue);Assert.Equal(49d,lot.Profitability.Gain);
        Assert.True(lot.Profitability.Kinetics.HasMeasuredDemand);
        Assert.Equal(9001,lot.IconId);
        Assert.Equal(new TradingLotPresentation(1,5,10,99),lot.TradingLots);
        var historical=Assert.Single(report.Candidates,x=>x.Id=="history-Resource-1-10");
        Assert.Equal(119d,historical.Revenue);Assert.Equal(19d,historical.Profitability.Gain);
        Assert.Equal(new TradingLotPresentation(10,100,10,119,true),historical.TradingLots);
        var owned=Assert.Single(report.Candidates,x=>x.Strategy=="Revendre mon stock");
        Assert.Equal(20,owned.Quantity);Assert.Equal(198d,owned.Revenue);
        Assert.Equal(new StockResaleTarget[] { new(1, 11, true), new(10, 119, true) }, owned.StockResaleTargets);
        var craft=Assert.Single(owned.Crafts);Assert.Equal("Bâton rentable",craft.Name);
        Assert.Contains("2 × Bois",craft.Recipe);Assert.Contains("1 × Écorce",craft.Recipe);
        Assert.Equal(9100,craft.IconId);
        Assert.Equal(new long?[]{9001,9002},craft.Ingredients.OrderBy(x=>x.ItemId).Select(x=>x.IconId));
        Assert.Equal(new[]{2,1},craft.Ingredients.OrderBy(x=>x.ItemId).Select(x=>x.Quantity));
        Assert.Equal(970d,craft.AdditionalGain);Assert.Equal("Sculpteur",craft.Job);Assert.Equal(20,craft.RequiredLevel);
        int calls=h.Http.Calls;
        Assert.Same(report,await h.Scanner.ScanAsync("achat-vente","Tylezia",stock,100,default));Assert.Equal(calls,h.Http.Calls);
        h.Changes.Notify(MarketObjectType.Resource,1,"Kourial");
        Assert.Same(report,await h.Scanner.ScanAsync("achat-vente","Tylezia",stock,100,default));
        h.Changes.Notify(MarketObjectType.Resource,1,"Tylezia");
        Assert.NotSame(report,await h.Scanner.ScanAsync("achat-vente","Tylezia",stock,100,default));
    }

    [Theory]
    [InlineData(40UL)]
    [InlineData(350UL)]
    [InlineData(25000UL)]
    [InlineData(3000000000UL)]
    public async Task TradingValuesEntireStockWithoutScenarioVolumeLimit(ulong quantity)
    {
        using var h=new Harness();await h.SeedAsync();
        var stock=Harness.Stock(quantity:quantity);
        var report=await h.Scanner.ScanAsync("achat-vente","Tylezia",stock,100,default);
        var owned=Assert.Single(report.Candidates,x=>x.Strategy=="Revendre mon stock");
        Assert.Equal((long)quantity,owned.Quantity);
        Assert.Equal((double)(quantity / 10) * 99,owned.Revenue);
        Assert.Equal(owned.Revenue,owned.Profitability.Gain);
        Assert.Same(report,await h.Scanner.ScanAsync("achat-vente","Tylezia",stock,1,default));
        Assert.Same(report,await h.Scanner.ScanAsync("achat-vente","Tylezia",stock,25000,default));
        var opportunities=await h.Scanner.ScanAsync("","Tylezia",stock,10,default);
        Assert.Equal(10,Assert.Single(opportunities.Candidates,x=>x.Strategy=="Revendre mon stock").Quantity);
    }

    [Fact]
    public async Task ForeignOrIncompleteStocksCannotAppearInResaleButMarketAnalysisStillWorks()
    {
        using var h=new Harness();await h.SeedAsync();
        var report=await h.Scanner.ScanAsync("achat-vente","Tylezia",Harness.Stock("Kourial"),100,default);
        Assert.Null(report.StockSourceId);Assert.DoesNotContain(report.Candidates,x=>x.Strategy=="Revendre mon stock");
        Assert.Contains(report.Candidates,x=>x.Strategy=="Recomposer des lots");
        Assert.Contains(report.Notices,x=>x.Contains("Stocks indisponibles"));
        var unknown=Harness.Stock() with{Containers=[new("inventory",true,false,DateTime.UtcNow,[new(999,1,3,null,null,null)])]};
        var unknownReport=await h.Scanner.ScanAsync("achat-vente","Tylezia",unknown,100,default);
        var unknownRow=Assert.Single(unknownReport.Candidates,x=>x.Strategy=="Revendre mon stock");
        Assert.Equal(999,unknownRow.ItemId);Assert.Equal(3,unknownRow.Quantity);Assert.Null(unknownRow.Revenue);
    }

    [Fact]
    public async Task FarmingMapsActualHarvestFieldAndMonsterSourcesWithoutAssumingLootOrTime()
    {
        using var h=new Harness();await h.SeedAsync();
        var report=await h.Scanner.ScanAsync("farming","Tylezia",Harness.Stock(),100,default);
        Assert.DoesNotContain(report.Notices,x=>x.Contains("indisponible"));
        var harvest=Assert.Single(report.Candidates,x=>x.Strategy=="Récolte");
        Assert.Equal("Bûcheron",harvest.Job);Assert.Equal(10,harvest.RequiredJobLevel);Assert.Equal(1000d,harvest.Revenue);
        Assert.Single(harvest.Crafts);Assert.NotNull(harvest.PriceUpdatedUtc);
        var combat=Assert.Single(report.Candidates,x=>x.Strategy=="Ressource de combat");
        Assert.Equal(new long[]{10},combat.MonsterIds);Assert.Equal(harvest.Revenue,combat.Revenue);
        Assert.Contains(combat.Details,x=>x.Contains("ne représente pas le gain d’un combat"));
        Assert.Contains(combat.Details,x=>x.Contains("aucun temps"));
        var transformed=Assert.Single(report.Candidates,x=>x.Strategy=="Récolte puis craft");
        Assert.Equal(100,transformed.Quantity);Assert.Equal(50000d,transformed.Revenue);
        Assert.Equal(1000d,transformed.Cost);Assert.Equal(49000d,transformed.Profitability.Gain);
        Assert.Equal("Bûcheron",transformed.Job);Assert.Equal("Sculpteur",transformed.CraftJob);
        var filtered=ActivityRanking.Select([transformed],new(RespectJobs:true),DateTime.UtcNow,
            new Dictionary<string,int>{{"Bûcheron",100},{"Sculpteur",1}});
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task FarmingRepairsMissingIconIdsAndReusesThemOffline()
    {
        using var h = new Harness();
        await h.SeedAsync();
        await using (var db = h.Database.CreateContext())
        {
            db.Resources.Single(resource => resource.DofusDbId == 1).DofusDbIconId = null;
            await db.SaveChangesAsync();
        }

        var report = await h.Scanner.ScanAsync("farming", "Tylezia", null, 100, default);
        Assert.Equal(9001, Assert.Single(report.Candidates, row => row.Strategy == "Récolte").IconId);
        Assert.Equal(9001, Assert.Single(report.Candidates, row => row.Strategy == "Ressource de combat").IconId);

        h.Http.Offline = true;
        int calls = h.Http.Calls;
        var catalog = new ActivityCatalogService(DofusDbClient.Production(), h.Http, Path.Combine(h.Folder, "catalog"));
        using var offlineScanner = new ActivityOpportunityService(h.Database, new TestDataPriorityProvider(), h.Sales, catalog, h.Changes);
        var offlineReport = await offlineScanner.ScanAsync("farming", "Tylezia", null, 100, default);
        Assert.Equal(9001, Assert.Single(offlineReport.Candidates, row => row.Strategy == "Récolte").IconId);
        Assert.Equal(calls, h.Http.Calls);
    }

    [Fact]
    public async Task SoulsExcludeArchmonstersAndForbiddenCapturesAndKeepPartialPricesAndServerScopes()
    {
        using var h=new Harness();await h.SeedAsync();
        h.Sales.SaveSoulQuote(new("Tylezia",147,9000,DateTimeOffset.UtcNow));
        var report=await h.Scanner.ScanAsync("pierres-ame","Tylezia",null,1,default);
        var boss=Assert.Single(report.Candidates);Assert.Equal("Boss capturable",boss.Name);
        Assert.Equal(200d,boss.Cost);Assert.Equal(9000d,boss.Revenue);Assert.Equal(8800d,boss.Profitability.Gain);
        Assert.False(boss.Complete);Assert.False(boss.Profitability.CanCompareKinetics);
        Assert.Contains(boss.MissingData,x=>x.Contains("cinétique inconnue"));
        var otherServer=await h.Scanner.ScanAsync("pierres-ame","Kourial",null,1,default);
        var partial=Assert.Single(otherServer.Candidates);Assert.Null(partial.Cost);Assert.Null(partial.Revenue);
        Assert.Null(partial.Profitability.Gain);Assert.Contains(partial.MissingData,x=>x.Contains("pierre d’âme vide"));
        h.Sales.SaveSoulQuote(new("Kourial",147,7000,DateTimeOffset.UtcNow));
        var knownSale=Assert.Single((await h.Scanner.ScanAsync("pierres-ame","Kourial",null,1,default)).Candidates);
        Assert.Equal(7000d,knownSale.Revenue);Assert.Null(knownSale.Profitability.Gain);
    }

    [Fact]
    public async Task AutomaticSoulPricesInvalidateAnalysisAndUseTheLowestBossOffer()
    {
        using var h=new Harness();await h.SeedAsync();
        var before=await h.Scanner.ScanAsync("pierres-ame","Tylezia",null,1,default);
        Assert.Null(Assert.Single(before.Candidates).Revenue);
        h.Sales.SaveSoulMarket("Tylezia",new(33713,[new(1,147,553,new ulong[]{72990,0,0,0}),
            new(2,147,553,new ulong[]{135000,0,0,0})]),DateTimeOffset.UtcNow);
        var after=await h.Scanner.ScanAsync("pierres-ame","Tylezia",null,1,default);
        Assert.NotSame(before,after);var four=Assert.Single(after.Candidates);
        Assert.Equal(72990d,four.Revenue);Assert.Equal(72790d,four.Profitability.Gain);
        Assert.DoesNotContain(four.Details,x=>x.Contains("butin"));

    }

    [Fact]
    public async Task PublicCatalogCanBeReadFromDiskWithoutNetworkAfterRestart()
    {
        using var h=new Harness();
        var first=await h.Catalog.GetHarvestsAsync(default);Assert.Single(first);
        h.Http.Offline=true;
        var freshInstance=new ActivityCatalogService(DofusDbClient.Production(),h.Http,Path.Combine(h.Folder,"catalog"));
        var cached=await freshInstance.GetHarvestsAsync(default);
        Assert.Equal(first.Single().Item.Id,cached.Single().Item.Id);
        Assert.Equal(first.Single().Item.Name,cached.Single().Item.Name);
        Assert.Equal(first.Single().Item.DropMonsterIds,cached.Single().Item.DropMonsterIds);
    }

    private sealed class TradeSettings : IBestCrushSettingsProvider
    {
        public bool EquipmentCaptureEnabled => true;
        public bool RuneCaptureEnabled => true;
        public bool ResourceCaptureEnabled => true;
        public bool CoefficientCaptureEnabled => true;
        public CrushYieldEstimationMode CrushYieldEstimationMode => CrushYieldEstimationMode.Conservative;
        public double TargetRoiPercent => 15;
        public double CrushValueDiscountPercent => 0;
    }

    private sealed class ViewCapture : IComponentActivator

    {
        public ActivityOpportunities? View;
        public TradingStrategies? Strategies;
        public ServiceFilters? Filters;
        public readonly List<ServiceSearch> Searches=[];
        public readonly List<ActivityRecommendationRow> Rows=[];
        public readonly List<FilterChip> Chips=[];
        public readonly List<VisualFilterChoice> Choices=[];
        public IComponent CreateInstance(Type type)
        {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is ActivityOpportunities view)View=view;
            if(component is TradingStrategies strategies)Strategies=strategies;
            if(component is ServiceFilters filters)Filters=filters;
            if(component is ServiceSearch search)Searches.Add(search);
            if(component is ActivityRecommendationRow row)Rows.Add(row);
            if(component is FilterChip chip)Chips.Add(chip);
            if(component is VisualFilterChoice choice)Choices.Add(choice);
            return component;
        }
    }

    [Fact]
    public async Task ActualViewCapsAfterLocalFilteringWithoutAnotherCalculationOrHiddenDetails()
    {
        using var h=new Harness();
        using(var db=h.Database.CreateContext())
        {
            for(int i=1;i<=150;i++)
            {
                db.Resources.Add(new Resource(1000+i){Name=(i<=100 ? "Autre" : "Cible")+i.ToString("D3")});
                db.MarketPriceObservations.AddRange(Price(1000+i,1,1,server:"Tylezia",at:DateTime.UtcNow),
                    Price(1000+i,10,20,server:"Tylezia",at:DateTime.UtcNow));
            }
            await db.SaveChangesAsync();
        }
        var now = DateTimeOffset.UtcNow;
        for (int i = 1; i <= 150; i++)
        {
            h.Sales.SaveHistory(new(new("Tylezia", MarketObjectType.Resource, 1000 + i), now,
                [new(2, 20, now.AddHours(-2)), new(2, 20, now.AddHours(-1))],
                [new(1, 40, now.AddDays(-1))]));
        }
        var clients=new CurrentDofusClientsState();var state=new CurrentServerState();
        var filter=new ServerViewFilterState();filter.BrowseServer("Tylezia");
        var stocks=new LiveStockService(new LiveStockRepository(Path.Combine(h.Folder,"stocks.db")),clients);
        var capture=new ViewCapture();var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(h.Scanner);services.AddSingleton(h.Sales);services.AddSingleton(h.Catalog);
        services.AddSingleton(stocks);services.AddSingleton(clients);services.AddSingleton(filter);services.AddSingleton(h.Changes);
        services.AddSingleton(new CurrentWindowProfessionsState(state));
        services.AddSingleton<IBestCrushSettingsProvider>(new TradeSettings());
        var preferences = new MarketAnalyticsPresetTest.MemoryPreferences();
        services.AddSingleton<IWorkspacePreferences>(preferences);
        services.AddSingleton(new StockOpportunityService(null!,null!,null!,[]));
        services.AddSingleton<IComponentActivator>(capture);
        await using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<ActivityOpportunities>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"Module","achat-vente"}})));
        string html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        string[] Names(string markup) => Regex.Matches(WebUtility.HtmlDecode(markup),
            @"<button[^>]*data-bs-title=""Copier le nom de l’objet""[^>]*aria-label=""Copier ((?:Autre|Cible)\d+)""")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(100,Names(html).Length);Assert.All(Names(html),x=>Assert.StartsWith("Autre",x));
        Assert.DoesNotContain("trade-route",html);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? report=typeof(ActivityOpportunities).GetField("_report",flags)!.GetValue(capture.View);
        Assert.Contains("trading-card",html);
        Assert.DoesNotContain("table-striped",html);
        Assert.DoesNotContain("Affiner les propositions",html); // lazy advanced panel
        var initialInputs=typeof(ActivityOpportunities).GetField("_lastInputs",flags)!.GetValue(capture.View);
        var history=Assert.Single(capture.Choices,x=>x.Label=="Prix sous l’historique");
        await renderer.Dispatcher.InvokeAsync(()=>history.Pressed.InvokeAsync());
        html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Empty(Names(html));
        Assert.True(history.Selected);
        Assert.Single(capture.Chips,x=>x.Label=="Prix sous l’historique");
        await renderer.Dispatcher.InvokeAsync(()=>history.Pressed.InvokeAsync()); // reclick clears
        Assert.False(history.Selected);
        Assert.Equal(100,Names(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)).Length);
        await renderer.Dispatcher.InvokeAsync(()=>history.Pressed.InvokeAsync());
        await renderer.Dispatcher.InvokeAsync(()=>capture.Chips.Last(x=>x.Label=="Prix sous l’historique").Removed.InvokeAsync());
        Assert.False(history.Selected);
        await renderer.Dispatcher.InvokeAsync(async()=>{
            await (Task)typeof(ServiceFilters).GetMethod("ToggleAsync",flags)!.Invoke(capture.Filters,null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged",flags)!.Invoke(capture.Filters,null);
        });
        html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.DoesNotContain("Quantité de mon stock par objet",html);
        Assert.DoesNotContain("Niveau maximum",html);
        Assert.Contains("Budget achats",html);
        Assert.Contains("bi-sliders", html);
        Assert.DoesNotContain("Affiner les propositions", html);
        var equipment = Assert.Single(capture.Choices, choice => choice.Label == "Équipements");
        var resources = Assert.Single(capture.Choices, choice => choice.Label == "Ressources");
        await renderer.Dispatcher.InvokeAsync(() => equipment.Pressed.InvokeAsync());
        Assert.Empty(Names(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        await renderer.Dispatcher.InvokeAsync(() => resources.Pressed.InvokeAsync());
        Assert.Equal(100, Names(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)).Length);
        await renderer.Dispatcher.InvokeAsync(() => resources.Pressed.InvokeAsync());
        Assert.False(resources.Selected);
        Assert.Single(Regex.Matches(html, @"<input[^>]*role=""switch""[^>]*\bdisabled\b[^>]*>"));
        await renderer.Dispatcher.InvokeAsync(()=>{
            typeof(ActivityOpportunities).GetField("_budget",flags)!.SetValue(capture.View,0d);
            typeof(ComponentBase).GetMethod("StateHasChanged",flags)!.Invoke(capture.View,null);
        });
        Assert.Empty(Names(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        await renderer.Dispatcher.InvokeAsync(()=>capture.Filters!.Reset.InvokeAsync());
        Assert.Equal(100,Names(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)).Length);
        await renderer.Dispatcher.InvokeAsync(()=>capture.Searches.Single().ValueChanged.InvokeAsync("Cible"));
        html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Equal(50,Names(html).Length);Assert.All(Names(html),x=>Assert.StartsWith("Cible",x));
        var row=capture.Rows.Last(x=>x.Row.Name=="Cible101");
        await renderer.Dispatcher.InvokeAsync(async()=>{
            await (Task)typeof(ActivityRecommendationRow).GetMethod("ToggleAsync",flags)!.Invoke(row,null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged",flags)!.Invoke(row,null);
        });
        Assert.Contains("trade-route",await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        await renderer.Dispatcher.InvokeAsync(async()=>{
            await (Task)typeof(ActivityRecommendationRow).GetMethod("ToggleAsync",flags)!.Invoke(row,null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged",flags)!.Invoke(row,null);
        });
        Assert.DoesNotContain("trade-route",await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        await renderer.Dispatcher.InvokeAsync(() => row.TradeHiddenChanged.InvokeAsync(true));
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.DoesNotContain("Cible101", Names(html));
        Assert.Contains("Afficher les masqu", html);
        Assert.Contains("bi-eye-slash", html);
        Assert.DoesNotContain(">Afficher les masqu", html);
        Assert.Contains(row.Row.Id, preferences.GetString("Trading.Hidden.Tylezia"));
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            typeof(ActivityOpportunities).GetMethod("ToggleShowHidden", flags)!.Invoke(capture.View, null);
            typeof(ComponentBase).GetMethod("StateHasChanged", flags)!.Invoke(capture.View, null);
        });
        var restored = capture.Rows.Last(x => x.Row.Name == "Cible101");
        Assert.True(restored.TradeHidden);
        await renderer.Dispatcher.InvokeAsync(() => restored.TradeHiddenChanged.InvokeAsync(false));
        Assert.Null(preferences.GetString("Trading.Hidden.Tylezia"));
        await renderer.Dispatcher.InvokeAsync(()=>{
            typeof(ActivityOpportunities).GetField("_sort",flags)!.SetValue(capture.View,"cost");
            typeof(ComponentBase).GetMethod("StateHasChanged",flags)!.Invoke(capture.View,null);
        });
        Assert.Same(initialInputs,typeof(ActivityOpportunities).GetField("_lastInputs",flags)!.GetValue(capture.View));
        Assert.Same(report,typeof(ActivityOpportunities).GetField("_report",flags)!.GetValue(capture.View));
        Assert.Equal(0,h.Http.Calls);
        var openRow = capture.Rows.Last(x => x.Row.Name == "Cible101");
        await renderer.Dispatcher.InvokeAsync(() => openRow.TradeExpandedChanged.InvokeAsync(true));

        var original = Assert.IsType<ActivityReport>(report);
        var updated = original with { Candidates = original.Candidates.Where(x => x.Id != openRow.Row.Id).ToArray() };
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            typeof(ActivityOpportunities).GetField("_report", flags)!.SetValue(capture.View, updated);
            typeof(ComponentBase).GetMethod("StateHasChanged", flags)!.Invoke(capture.View, null);
        });
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("Cible101", Names(html));
        Assert.Contains("Ne plus acheter", html);
        Assert.Contains("data-dismiss-on-scroll", html);
        Assert.DoesNotContain("trade-route", html);

        await renderer.Dispatcher.InvokeAsync(() => openRow.TradeExpandedChanged.InvokeAsync(false));
        Assert.DoesNotContain("Cible101", Names(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));

        await renderer.DisposeAsync();
        await using var reopened = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await reopened.Dispatcher.InvokeAsync(() => reopened.RenderComponentAsync<ActivityOpportunities>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["Module"] = "pierres-ame" })));
        Assert.Equal("", capture.Searches.Last().Value);
        await reopened.Dispatcher.InvokeAsync(() => reopened.RenderComponentAsync<ActivityOpportunities>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["Module"] = "achat-vente" })));
        Assert.Equal("Cible", capture.Searches.Last().Value);
    }



    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TradingKeepsCraftNoticeRecipeAndLazyDetailInBothLayouts(bool card)
    {
        using var h=new Harness();await h.SeedAsync();
        var report=await h.Scanner.ScanAsync("achat-vente","Tylezia",Harness.Stock(),100,default);
        var row=Assert.Single(report.Candidates,x=>x.Strategy=="Revendre mon stock");
        var capture=new ViewCapture();var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton(h.Catalog);services.AddSingleton<IComponentActivator>(capture);
        await using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<ActivityRecommendationRow>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"Row",row},{"CardLayout",card}})));
        string html=WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains("Cette ressource peut rapporter plus",html);
        Assert.Contains("title=\"2 × Bois",html);
        Assert.Contains("/modules/craft?item=100",html);
        Assert.Contains("Bâton rentable",html);
        Assert.DoesNotContain("gain potentiel par craft",html);
        if(card) Assert.DoesNotContain("Indice :",html);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        await renderer.Dispatcher.InvokeAsync(async()=>{
            await (Task)typeof(ActivityRecommendationRow).GetMethod("ToggleAsync",flags)!.Invoke(capture.Rows.Single(),null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged",flags)!.Invoke(capture.Rows.Single(),null);
        });
        html=WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains(card ? "Gain du craft" : "gain potentiel par craft",html);
        if (card)
        {
            Assert.Contains("Coût d’acquisition inconnu".ToLowerInvariant(), html.ToLowerInvariant());
            Assert.DoesNotContain("Ventes globales :",html);
            Assert.DoesNotContain("Gain brut estimé",html);
            Assert.Contains("Copier Bois",html);
            Assert.Contains("Copier Bâton rentable",html);
        }
    }

    [Fact]
    public async Task BossDemandDrivesKineticsAndRankingWithoutLeakingForeignServerHistory()
    {
        using var h=new Harness();await h.SeedAsync();var now=DateTimeOffset.UtcNow;
        h.Sales.SaveSoulQuote(new("Tylezia",147,9000,now));
        h.Sales.SaveHistory(new(new("Kourial",MarketObjectType.Resource,33713,GlobalSalesRepository.BossHistoryVariant(147)),now,[],[new(8000,999,now.AddHours(-1))]));
        var before=Assert.Single((await h.Scanner.ScanAsync("pierres-ame","Tylezia",null,1,default)).Candidates);
        Assert.False(before.Profitability.CanCompareKinetics);
        h.Sales.SaveHistory(new(new("Tylezia",MarketObjectType.Resource,33713,GlobalSalesRepository.BossHistoryVariant(147)),now,[],[new(8000,7,now.AddHours(-1))]));
        var row=Assert.Single((await h.Scanner.ScanAsync("pierres-ame","Tylezia",null,1,default)).Candidates);
        Assert.True(row.Profitability.CanCompareKinetics);Assert.Equal(1d,row.Profitability.Kinetics.UnitsPerDay);
        Assert.Equal(8800d,row.Profitability.Gain);Assert.Equal(8800d,row.Profitability.MarketDailyGainCapacity);
        Assert.True(row.Complete);
        Assert.Single(ActivityRanking.Select([row],new(CompleteOnly:true),DateTime.UtcNow));
        Assert.DoesNotContain(row.Details,x=>x.Contains("butin"));
    }

    [Fact]
    public async Task BulkEmptyStoneUsesUnitCostAndWholeLotBudgetWhileOwnedStoneKeepsItsEconomicCost()
    {
        using var h=new Harness();await h.SeedAsync();
        using(var db=h.Database.CreateContext())
        {
            db.MarketPriceObservations.RemoveRange(db.MarketPriceObservations.Where(x=>x.DofusDbId==9686));
            db.MarketPriceObservations.Add(Price(9686,10,1500,server:"Tylezia",at:DateTime.UtcNow));await db.SaveChangesAsync();
        }
        h.Sales.SaveSoulQuote(new("Tylezia",147,9000,DateTimeOffset.UtcNow));
        var row=Assert.Single((await h.Scanner.ScanAsync("pierres-ame","Tylezia",null,1,default)).Candidates);
        Assert.Equal(150d,row.Cost);Assert.Equal(1500d,row.CashRequired);Assert.Equal(8850d,row.Profitability.Gain);
        Assert.Contains(row.Details,x=>x.Contains("lot entier"));
        var owned=Harness.Stock() with{Containers=[new("inventory",true,false,DateTime.UtcNow,[new(9686,1,1,null,null,null)])]};
        var ownedRow=Assert.Single((await h.Scanner.ScanAsync("pierres-ame","Tylezia",owned,1,default)).Candidates);
        Assert.Equal(0d,ownedRow.CashRequired);Assert.Equal(150d,ownedRow.Cost);Assert.Equal(8850d,ownedRow.Profitability.Gain);
    }
}
