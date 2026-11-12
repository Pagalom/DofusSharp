using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Models;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class StockCrushingToggleTest
{
    private static Equipment Recipe(long id=100, long ingredient=1, int required=10)
    {
        Equipment item=new(id){Name="Objet "+id,Level=100,Type=EquipmentType.Amulet};
        item.Recipe.Add(new(item,new Resource(ingredient){Name="Ressource "+ingredient},required));
        return item;
    }
    private static EquipmentProfitabilityContext Market(params MarketPriceObservation[] prices)=>new(){
        ResourcePrices=Prices(prices),EquipmentPrices=Prices(),RunePrices=Prices(),Coefficients=[]};
    private static EquipmentProfitabilityScenario Scenario(Equipment item,CraftCostResult cost,double revenue=900)=>new(
        item,new(){DofusDbId=item.DofusDbId,ServerName=Server,CoefficientPercent=100,
            Source=CoefficientSource.Manual,ObservedAtUtc=DateTime.UtcNow},null,cost,null,null,
        revenue-cost.TotalCost,cost.TotalCost>0 ? (revenue-cost.TotalCost)/cost.TotalCost : null,
        null,new Dictionary<Rune,double>(),new Dictionary<Rune,MarketValueResult>()){
            AdjustedRuneValue=revenue,TargetRoiPercent=20,MaximumCostAtTargetRoi=revenue/1.2};

    [Fact]
    public void RemainingPurchasesDriveCashGainWithoutChangingRuneValueOrMarketScenario()
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        var prices=new MarketPriceService(db,new TestDataPriorityProvider());
        var market=Market(Price(1,1,100));var item=Recipe();
        var full=new CraftCostService(prices).Calculate(item,market.ResourcePrices,market.EquipmentPrices);
        var plan=new StockCraftPlanner(prices).Calculate(item,1,new Dictionary<long,BigInteger>{{1,6}},market);
        var original=Scenario(item,full);var adjusted=StockCrushingProjection.Apply(original,plan);
        Assert.Equal(1000,original.CraftCost.TotalCost);Assert.Equal(-100,original.CraftBenefit);
        Assert.Equal(400,adjusted.CraftCost.TotalCost);Assert.Equal(600,plan.OwnedValue);
        Assert.Equal(500,adjusted.CraftBenefit);Assert.Equal(1.25,adjusted.CraftYield);
        Assert.Equal(original.AdjustedRuneValue,adjusted.AdjustedRuneValue);
        Assert.Same(original.Runes,adjusted.Runes);Assert.Same(original.RuneValues,adjusted.RuneValues);
    }

    [Fact]
    public void RemainingLotsAreRecalculatedRatherThanSubtractingStockValuation()
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        var prices=new MarketPriceService(db,new TestDataPriorityProvider());
        var market=Market(Price(1,10,1000));var item=Recipe();
        var plan=new StockCraftPlanner(prices).Calculate(item,1,new Dictionary<long,BigInteger>{{1,6}},market);
        var cost=StockCrushingProjection.RemainingCost(plan);
        Assert.Equal(600,plan.OwnedValue);Assert.Equal(1000,cost.TotalCost);
        Assert.Equal(10,cost.Resources.Single().PurchasedQuantity);
        Assert.Equal(6,cost.Resources.Single().SurplusQuantity);
    }

    [Fact]
    public void FullyOwnedUnpricedIngredientsAllowKnownCashGainAndDoNotInventPercentageRoi()
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        var prices=new MarketPriceService(db,new TestDataPriorityProvider());var item=Recipe();var market=Market();
        var full=new CraftCostService(prices).Calculate(item,market.ResourcePrices,market.EquipmentPrices);
        var plan=new StockCraftPlanner(prices).Calculate(item,1,new Dictionary<long,BigInteger>{{1,100}},market);
        var adjusted=StockCrushingProjection.Apply(Scenario(item,full),plan);
        Assert.Null(plan.OwnedValue);Assert.Null(full.TotalCost);
        Assert.Equal(10,plan.Ingredients.Single().Used);
        Assert.Equal(0,adjusted.CraftCost.TotalCost);Assert.Equal(900,adjusted.CraftBenefit);
        Assert.Null(adjusted.CraftYield);Assert.Equal(ProfitabilityState.TargetReached,adjusted.CraftState);
    }

    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Set(object view,string name,object? value)
    {
        var field=view.GetType().GetField(name,Hidden);
        if(field is not null)field.SetValue(view,value);
        else view.GetType().GetProperty(name,Hidden|BindingFlags.Public)!.SetValue(view,value);
    }
    private static T Get<T>(object view,string name)=> (T)(view.GetType().GetField(name,Hidden)?.GetValue(view)
        ?? view.GetType().GetProperty(name,Hidden|BindingFlags.Public)!.GetValue(view))!;
    private static object? Call(object view,string name,params object?[] args)=>view.GetType().GetMethod(name,Hidden)!.Invoke(view,args);
    private static object[] Rows(object view)=>Get<IEnumerable>(view,"_rankedItems").Cast<object>().ToArray();
    private static long Id(object row)=>Get<Equipment>(row,"Equipment").DofusDbId;

    [Fact]
    public async Task RepeatedToggleRestoresFilteredCandidatesReordersAndNeverTouchesPlannerOrExternalServices()
    {
        string folder=Path.Combine(Path.GetTempPath(),"crush-toggle-"+Guid.NewGuid());
        try
        {
            using TestDatabase database=new();using var db=database.CreateContext();
            var prices=new MarketPriceService(db,new TestDataPriorityProvider());
            var clients=new CurrentDofusClientsState();var key=new DofusClientWindowKey(111,1111);
            clients.Update([new(new IntPtr(key.Handle),"Pagalom - Eniripsa - 3.7.12.12 - Release","Dofus",0,0,100,100){ProcessId=key.ProcessId}],key);
            var stocks=new LiveStockService(new LiveStockRepository(Path.Combine(folder,"stock.db")),clients);
            stocks.Observe(new("S→C","irl",Message(Unsigned(4,1),Bytes(2,Bytes(1,Message(Unsigned(3,6),Unsigned(4,1),Unsigned(5,10))))),
                DateTime.UtcNow,new NetworkCaptureLease(Server,1),1,ConnectionId:1,CandidateWindow:key,SyntheticWindowForReplay:true));
            var view=new global::BestCrush.Components.Pages.Server{ServerName=Server};
            Set(view,"Stocks",stocks);Set(view,"StockPlanner",new StockCraftPlanner(prices));
            Set(view,"Clients",clients);Set(view,"Model",new SearchItemsModel{ProfitabilityMode=ProfitabilityFilterMode.Craft,MinimumBenefit=0});
            var market=Market(Price(1,1,100),Price(2,1,100));Set(view,"_resourcePrices",market.ResourcePrices);
            Call(view,"SetStockInputs",stocks.Selected);
            // A is excluded on market cost, but is the best opportunity with stock.
            var a=Recipe();var b=Recipe(200,2,6);var craft=new CraftCostService(prices);
            foreach(var item in new[]{a,b})
            {
                var full=craft.Calculate(item,market.ResourcePrices,market.EquipmentPrices);
                var scenario=Scenario(item,full);
                Call(view,"CacheCalculation",new EquipmentProfitabilityResult(item,scenario.Coefficient,null,full,[scenario],[]));
            }
            // Initialize the private typed lists without depending on their private element type.
            foreach(string field in new[]{"_rankedItems","_unrankedItems"})
                Set(view,field,Activator.CreateInstance(view.GetType().GetField(field,Hidden)!.FieldType));
            Call(view,"RebuildVisibleCalculations");
            Assert.Equal(new long[]{200},Rows(view).Select(Id));
            var prepared=Get<object>(view,"_stockResults");
            // Any attempt to recalculate/reload during a toggle now fails immediately.
            Set(view,"StockPlanner",null);Set(view,"EquipmentProfitabilityService",null);Set(view,"ItemsService",null);
            var timer=Stopwatch.StartNew();
            for(int i=0;i<100;i++)
            {
                Call(view,"ToggleStocks",new ChangeEventArgs{Value=true});
                var ordered=((IEnumerable)Call(view,"OrderResults",Get<object>(view,"_rankedItems"))!).Cast<object>().ToArray();
                Assert.Equal(new long[]{100,200},ordered.Select(Id));
                Assert.Equal(500,Get<double?>(ordered[0],"CraftBenefit"));
                Call(view,"ToggleStocks",new ChangeEventArgs{Value=false});
                Assert.Equal(new long[]{200},Rows(view).Select(Id));
            }
            timer.Stop();Assert.Same(prepared,Get<object>(view,"_stockResults"));
            Assert.True(timer.Elapsed<TimeSpan.FromSeconds(2),$"200 local toggles took {timer.Elapsed}");
            await (Task)Call(view,"PrepareStocksAsync",false)!;
            Assert.Same(prepared,Get<object>(view,"_stockResults"));
            // Another target immediately stops the prepared character's stock from being applied.
            clients.Update([]);
            Call(view,"ToggleStocks",new ChangeEventArgs{Value=true});
            Assert.False(Get<bool>(view,"UseStockCalculation"));
            Assert.Equal(new long[]{200},Rows(view).Select(Id));
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,true)]
    public void IncompleteOrStaleBankCannotReduceTheMainScreenCost(bool complete,bool stale)
    {
        var at=DateTime.UtcNow;
        var source=new LiveStockSource("source","Pagalom",Server,at,[
            new("inventory",true,false,at,[new(1,10,6,null,null,null)]),
            new("bank",complete,stale,at,[new(1,20,999,null,null,null)])]);
        var method=typeof(global::BestCrush.Components.Pages.Server).GetMethod("Quantities",BindingFlags.Static|BindingFlags.NonPublic)!;
        var quantities=(IReadOnlyDictionary<long,BigInteger>)method.Invoke(null,[source,Server])!;
        Assert.Equal(new BigInteger(6),quantities[1]);
        var other=(IReadOnlyDictionary<long,BigInteger>)method.Invoke(null,[source,"OTHER_SERVER"])!;
        Assert.Empty(other);
    }

    [Fact]
    public async Task CashDisplayShowsRemainingCostOwnedValueAndNoPercentageForZeroPurchases()
    {
        string folder=Path.Combine(Path.GetTempPath(),"crush-render-"+Guid.NewGuid());
        try
        {
            using TestDatabase database=new();using var db=database.CreateContext();
            var prices=new MarketPriceService(db,new TestDataPriorityProvider());var item=Recipe();
            var plan=new StockCraftPlanner(prices).Calculate(item,1,new Dictionary<long,BigInteger>{{1,10}},Market(Price(1,1,100)));
            var cost=StockCrushingProjection.RemainingCost(plan);var clients=new CurrentDofusClientsState();
            var stocks=new LiveStockService(new LiveStockRepository(Path.Combine(folder,"stock.db")),clients);
            var services=new ServiceCollection();services.AddLogging();services.AddSingleton(stocks);services.AddSingleton(clients);services.AddSingleton(new ItemsService(null!,null!,null!,null!,null!));
            await using var container=services.BuildServiceProvider();
            await using var renderer=new HtmlRenderer(container,container.GetRequiredService<ILoggerFactory>());
            var summary=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<CraftCostSummary>(
                ParameterView.FromDictionary(new Dictionary<string,object?>{{"Result",cost},{"StockPlan",plan},{"StockServerName",Server}})));
            string html=System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(summary.ToHtmlString));
            Assert.Contains("Reste à payer (craft)",html);Assert.Contains("Valeur des ingrédients possédés utilisés",html);
            Assert.Contains("0 k",html);
            var line=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<CrushResultLine>(ParameterView.FromDictionary(new Dictionary<string,object?>{
                {"Equipment",item},{"CraftCost",cost},{"NoImage",true},
                {"Runes",new Dictionary<Rune,double>()},{"RuneValues",new Dictionary<Rune,MarketValueResult>()},
                {"AdjustedRuneValue",900d}})));
            html=System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(line.ToHtmlString));
            Assert.Contains("Valeur de revente",html);Assert.DoesNotContain("Bénéfice",html);Assert.DoesNotContain("Rafraîchir",html);Assert.DoesNotContain("Niv.",html);
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }

    private sealed class Settings : IBestCrushSettingsProvider
    {
        public bool EquipmentCaptureEnabled=>false;
        public bool RuneCaptureEnabled=>false;
        public bool ResourceCaptureEnabled=>false;
        public bool CoefficientCaptureEnabled=>false;
        public CrushYieldEstimationMode CrushYieldEstimationMode=>CrushYieldEstimationMode.Average;
        public double TargetRoiPercent=>20;
        public double CrushValueDiscountPercent=>0;
    }

    [Fact]
    public async Task MainScreenCanPrepareRuneScenariosWhenStocksCoverUnpricedRecipe()
    {
        using TestDatabase database=new();using var db=database.CreateContext();
        db.Runes.Add(new Rune(10){Name="Rune Fo",Characteristic=Characteristic.Strength});await db.SaveChangesAsync();
        var prices=new MarketPriceService(db,new TestDataPriorityProvider());
        var profitability=new EquipmentProfitabilityService(null!,new CrushService(database),
            new CoefficientService(db,new TestDataPriorityProvider()),prices,new CraftCostService(prices),new Settings());
        var item=Recipe();item.Characteristics.Add(new(item,Characteristic.Strength,10,10));
        var market=new EquipmentProfitabilityContext{
            ResourcePrices=Prices(),EquipmentPrices=Prices(),RunePrices=Prices(Price(10,1,10,type:MarketObjectType.Rune)),
            Coefficients=new(){{100,Scenario(item,new([])).Coefficient}}};
        var normal=await profitability.CalculateAsync(item,Server,market,includeThresholds:false);
        Assert.Empty(normal.Scenarios);
        var prepared=await profitability.CalculateAsync(item,Server,market,includeThresholds:false,includeUnpricedScenarios:true);
        Assert.NotEmpty(prepared.Scenarios);
        var plan=new StockCraftPlanner(prices).Calculate(item,1,new Dictionary<long,BigInteger>{{1,10}},market);
        var adjusted=StockCrushingProjection.Apply(prepared.Scenarios[0],plan);
        Assert.True(adjusted.CraftBenefit>0);Assert.Equal(0,adjusted.CraftCost.TotalCost);
    }
}
