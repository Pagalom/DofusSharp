using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using FluentAssertions;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class StockOpportunityScanTest
{
    private static LiveStockSource Source()=>new("eni-session","Pagalom",Server,ObservedAt,
        [new("inventory",true,false,ObservedAt,[new(1,10,3,null,null,null)]),
         new("bank",true,false,ObservedAt,[new(1,20,3,null,null,null)])]);

    [Fact]
    public async Task CraftIncludesAlloysEvenWithoutTheirIngredientsInStock()
    {
        using TestDatabase database = new();
        using var db = database.CreateContext();
        db.MarketPriceObservations.AddRange(Price(441, 10, 343), Price(442, 10, 1743),
            Price(443, 10, 1897), Price(445, 10, 4800), Price(749, 1, 6783));
        await db.SaveChangesAsync();

        using var http = new AlloyCatalog();
        var cache = Directory.CreateTempSubdirectory("bestcrush-alloy-");
        try
        {
            var catalog = new ActivityCatalogService(DofusDbClient.Production(), http, cache.FullName);
            var priority = new TestDataPriorityProvider();
            var prices = new MarketPriceService(db, priority);
            var items = new ItemsService(db, null!, null!, null!, null!);
            var profitability = new EquipmentProfitabilityService(null!, null!, new CoefficientService(db, priority),
                prices, null!, null!);
            var scanner = new StockOpportunityService(items, profitability, new StockCraftPlanner(prices),
                [new CraftOpportunityProvider(prices)], activityCatalog: catalog);

            var report = await scanner.ScanAsync(Source(), Server, 1, default, originFilter: "craft");

            var candidate = Assert.Single(report.Candidates);
            var product = Assert.IsType<CraftedProduct>(candidate.Plan.Product);
            product.Name.Should().Be("Bakélélite");
            product.TypeName.Should().Be("Alliage");
            product.Job.Should().Be("Mineur");
            candidate.Plan.CraftableNow.Should().Be(System.Numerics.BigInteger.Zero);
            candidate.Plan.CashRequired.Should().Be(8783);
            candidate.Revenue.Should().Be(6783);
            candidate.Benefit.Should().Be(-2000);
        }
        finally
        {
            cache.Delete(recursive: true);
        }
    }

    private sealed class AlloyCatalog : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath.Trim('/');
            string data = path switch
            {
                "jobs" => """{"id":24,"name":{"fr":"Mineur"}}""",
                "recipes" => """
                    {"id":749,"resultId":749,"resultTypeId":40,"resultLevel":80,
                    "ingredientIds":[441,442,443,445],"quantities":[10,10,10,10],"jobId":24,
                    "result":{"id":749,"name":{"fr":"Bakélélite"},"iconId":40487},
                    "resultType":{"id":40,"name":{"fr":"Alliage"}},
                    "ingredients":[{"id":441,"name":{"fr":"Cuivre"}},{"id":442,"name":{"fr":"Bronze"}},
                    {"id":443,"name":{"fr":"Cobalt"}},{"id":445,"name":{"fr":"Manganèse"}}]}
                    """,
                _ => throw new InvalidOperationException($"Catalogue inattendu : {path}")
            };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"total":1,"limit":50,"skip":0,"data":[{{data}}]}""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
    [Fact]
    public async Task ScannerJoinsContainersAndPublishesBothModulesWithoutWritingMarketData()
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        Equipment equipment=new(100){Name="Objet",Level=10};
        equipment.Recipe.Add(new(equipment,new Resource(1){Name="Bois"},3));
        db.Equipments.Add(equipment);
        db.MarketPriceObservations.AddRange(Price(1,1,10),Price(100,1,100,type:MarketObjectType.Equipment));
        await db.SaveChangesAsync();
        var priority=new TestDataPriorityProvider();var prices=new MarketPriceService(db,priority);
        var items=new ItemsService(db,null!,null!,null!,null!);
        var profitability=new EquipmentProfitabilityService(items,new CrushService(database),new CoefficientService(db,priority),prices,new CraftCostService(prices),null!);
        IOpportunityProvider[] providers=[new CraftOpportunityProvider(prices),new CrushingOpportunityProvider(profitability,prices,null!)];
        var service=new StockOpportunityService(items,profitability,new StockCraftPlanner(prices),providers);
        var report=await service.ScanAsync(Source(),Server,2,default);
        report.SourceId.Should().Be("eni-session");report.RecipeCount.Should().Be(1);
        report.IncludesBank.Should().BeTrue();
        report.Candidates.Should().HaveCount(2);
        var craft=report.Candidates.Single(x=>x.Origin=="craft");
        craft.Plan.Ingredients.Single().Available.Should().Be(new System.Numerics.BigInteger(6));
        craft.Plan.CashRequired.Should().Be(0);craft.Plan.OwnedValue.Should().Be(60);craft.Benefit.Should().Be(140);
        report.Candidates.Single(x=>x.Origin=="concassage").Revenue.Should().BeNull();
        db.MarketPriceObservations.Count().Should().Be(2);db.CoefficientObservations.Count().Should().Be(0);
    }
    [Theory]
    [InlineData(0)] // bank absent
    [InlineData(1)] // retained rows in a stale bank
    [InlineData(2)] // retained rows in a partial bank
    public async Task InventoryOnlyScanNeverUsesUnsynchronizedBankRows(int bankState)
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        Equipment equipment=new(100){Name="Objet",Level=10};
        equipment.Recipe.Add(new(equipment,new Resource(1){Name="Bois"},3));
        Equipment bankOnly=new(200){Name="Recette banque",Level=10};
        bankOnly.Recipe.Add(new(bankOnly,new Resource(2){Name="Banque seule"},1));
        db.Equipments.AddRange(equipment,bankOnly);
        db.MarketPriceObservations.AddRange(Price(1,1,10),Price(100,1,100,type:MarketObjectType.Equipment));
        await db.SaveChangesAsync();
        var priority=new TestDataPriorityProvider();var prices=new MarketPriceService(db,priority);
        var items=new ItemsService(db,null!,null!,null!,null!);
        var profitability=new EquipmentProfitabilityService(null!,null!,new CoefficientService(db,priority),prices,null!,null!);
        var service=new StockOpportunityService(items,profitability,new StockCraftPlanner(prices),[new CraftOpportunityProvider(prices)]);
        var inventory=Source().Containers.Single(x=>x.Container=="inventory");
        var bank=new LiveStockContainer("bank",bankState!=2,bankState==1,ObservedAt,
            [new(1,20,999,null,null,null),new(2,21,999,null,null,null)]);
        var source=Source() with{Containers=bankState==0 ? [inventory] : [inventory,bank]};
        var report=await service.ScanAsync(source,Server,2,default);
        report.IncludesBank.Should().BeFalse();report.RecipeCount.Should().Be(1);
        var craft=report.Candidates.Single();
        craft.Plan.Ingredients.Single().Available.Should().Be(new System.Numerics.BigInteger(3));
        craft.Plan.Ingredients.Single().Missing.Should().Be(3);
        craft.Plan.CashRequired.Should().Be(30);craft.Plan.OwnedValue.Should().Be(30);craft.Benefit.Should().Be(140);
        if(bankState!=0) source.Containers.Single(x=>x.Container=="bank").Items.Should().HaveCount(2);
    }

    private sealed class SlowSource : IOpportunityProvider
    {
        private int _active;
        public string Origin=>"test";
        public async Task<IReadOnlyList<EconomicOpportunity>> EvaluateAsync(OpportunityScanContext context,CancellationToken token)
        {
            if(Interlocked.Increment(ref _active)!=1) throw new InvalidOperationException("Scans concurrent with shared context.");
            try { await Task.Delay(100,token); return []; }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
    [Fact]
    public async Task SwitchingModulesSerializesScansUsingTheSameScopedContext()
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        var priority=new TestDataPriorityProvider();var prices=new MarketPriceService(db,priority);
        var items=new ItemsService(db,null!,null!,null!,null!);
        var profitability=new EquipmentProfitabilityService(null!,null!,new CoefficientService(db,priority),prices,null!,null!);
        var service=new StockOpportunityService(items,profitability,new StockCraftPlanner(prices),[new SlowSource()]);
        var reports=await Task.WhenAll(service.ScanAsync(Source(),Server,1,default),service.ScanAsync(Source(),Server,1,default));
        reports.Should().HaveCount(2);
    }
    [Fact]
    public async Task RejectedSourceDoesNotAccessCatalogueOrMarkets()
    {
        var service=new StockOpportunityService(null!,null!,null!,[]);
        Func<Task> scan=()=>service.ScanAsync(Source(),"OTHER_SERVER",1,default);
        await scan.Should().ThrowAsync<InvalidOperationException>();
    }
    [Fact]
    public async Task CancelledScanDoesNotPublishResults()
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        var items=new ItemsService(db,null!,null!,null!,null!);
        var service=new StockOpportunityService(items,null!,null!,[]);
        using CancellationTokenSource cancellation=new();cancellation.Cancel();
        Func<Task> scan=()=>service.ScanAsync(Source(),Server,1,cancellation.Token);
        await scan.Should().ThrowAsync<OperationCanceledException>();
    }
}
