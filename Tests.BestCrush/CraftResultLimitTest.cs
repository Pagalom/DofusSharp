using System.Reflection;
using System.Text.RegularExpressions;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class CraftResultLimitTest
{
    private sealed class Source : IOpportunityProvider
    {
        public string Origin => "craft";
        public int Calls;
        public IReadOnlyList<EconomicOpportunity> Rows = Enumerable.Range(1,220).Select(i => {
            int cash = i * 37 % 250 + 1;
            var item = new Equipment(i) { Name = (i <= 120 ? "Cible" : "Recette") + i.ToString("D3") };
            var plan = new StockCraftPlan(item,1,0,[new(1,"Bois",MarketObjectType.Resource,1,0,0,1,
                new(cash,1,1,new Dictionary<int,int>{{1,1}}),new(0,false),new(0,false))]);
            return new EconomicOpportunity("craft","Fabriquer puis revendre","Tylezia",plan,cash+i,new([]),[]);
        }).ToArray();
        public Task<IReadOnlyList<EconomicOpportunity>> EvaluateAsync(OpportunityScanContext context,CancellationToken token)
        { Calls++; return Task.FromResult(Rows); }
    }
    private sealed class Capture : IComponentActivator
    {
        public StockOpportunities? View;
        public IComponent CreateInstance(Type type)
        {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is StockOpportunities view)View=view;
            return component;
        }
    }
    private static string[] Names(string html) => Regex.Matches(html,@"<strong data-result-name[^>]*>([^<]+)</strong>")
        .Select(x=>System.Net.WebUtility.HtmlDecode(x.Groups[1].Value)).ToArray();

    [Fact]
    public async Task CraftRendersOnlyTop100AfterFiltersAndSortWithoutLoadMoreOrRescanning()
    {
        string folder=Path.Combine(Path.GetTempPath(),"craft-limit-"+Guid.NewGuid());
        try
        {
            using TestDatabase database=new();using var db=database.CreateContext();
            var prices=new MarketPriceService(db,new TestDataPriorityProvider());
            var provider=new Source();
            var scanner=new StockOpportunityService(new ItemsService(db,null!,null!,null!,null!),
                new EquipmentProfitabilityService(null!,null!,new CoefficientService(db,new TestDataPriorityProvider()),prices,null!,null!),
                new StockCraftPlanner(prices),[provider]);
            var key=new DofusClientWindowKey(111,1111);var clients=new CurrentDofusClientsState();
            clients.Update([new(new IntPtr(key.Handle),"Pagalom - Eniripsa - 3.7.12.12 - Release","Dofus",0,0,100,100){ProcessId=key.ProcessId}],key);
            var stocks=new LiveStockService(new LiveStockRepository(Path.Combine(folder,"stock.db")),clients);
            stocks.Observe(new("S→C","irl",Message(Unsigned(4,1),Bytes(2,Bytes(1,Message(Unsigned(3,1),Unsigned(4,1),Unsigned(5,10))))),
                DateTime.UtcNow,new NetworkCaptureLease("Tylezia",1),1,ConnectionId:1,CandidateWindow:key,SyntheticWindowForReplay:true));
            var filter=new ServerViewFilterState();filter.FollowWindow(key,"Tylezia");
            var capture=new Capture();var services=new ServiceCollection();services.AddLogging();
            services.AddSingleton(scanner);services.AddSingleton(stocks);services.AddSingleton(clients);
            services.AddSingleton(filter);services.AddSingleton(new MarketDataChangeNotifier());services.AddSingleton<IComponentActivator>(capture);
            CraftCatalogFixture.Register(services,folder);
            await using var container=services.BuildServiceProvider();
            await using var renderer=new HtmlRenderer(container,container.GetRequiredService<ILoggerFactory>());
            var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<StockOpportunities>(
                ParameterView.FromDictionary(new Dictionary<string,object?>{{"OriginFilter","craft"}})));
            string html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Equal(Enumerable.Range(121,100).Reverse().Select(i=>"Recette"+i.ToString("D3")),Names(html));
            Assert.DoesNotContain("Afficher 100 résultats de plus",html);

            async Task<string> Change(params (string Field,object Value)[] changes)
            {
                await renderer.Dispatcher.InvokeAsync(()=>{
                    foreach(var (field,value) in changes)typeof(StockOpportunities).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(capture.View,value);
                    typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.View,null);
                });
                return await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            }
            // These recipes were outside the first top 100. Filtering must happen first.
            html=await Change(("_search","Cible"),("_limit",500));
            Assert.Equal(Enumerable.Range(21,100).Reverse().Select(i=>"Cible"+i.ToString("D3")),Names(html));
            Assert.DoesNotContain("Afficher 100 résultats de plus",html);
            // Exercise the alternate ranking branch and the user's budget + sort.
            html=await Change(("_positiveOnly",true),("_sort","cash"),("_budget",100d));
            var expected=provider.Rows.Where(x=>x.Plan.Product.Name.StartsWith("Cible") && x.Plan.CashRequired<=100)
                .OrderBy(x=>x.Plan.CashRequired).ThenBy(x=>x.Plan.Product.Name).Take(100).Select(x=>x.Plan.Product.Name);
            Assert.Equal(expected,Names(html));
            Assert.Equal(1,provider.Calls);
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
