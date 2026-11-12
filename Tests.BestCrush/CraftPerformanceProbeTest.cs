using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
using Xunit.Abstractions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class CraftPerformanceProbeTest(ITestOutputHelper output)
{
    public sealed class Source : IOpportunityProvider
    {
        public string Origin => "craft";
        public int Calls;
        public IReadOnlyList<EconomicOpportunity> Rows = Enumerable.Range(1,220).Select(i => {
            var at=DateTime.UtcNow;
            var observations=Enumerable.Range(1,25).Select(j=>new MarketPriceObservation{
                DofusDbId=j,Price=10,Quantity=1,ServerName="Tylezia",ObjectType=MarketObjectType.Resource,
                Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=at}).ToArray();
            var ingredients=Enumerable.Range(1,8).Select(j=>new StockIngredientPlan(j,"Ingredient "+j,
                MarketObjectType.Resource,2,1,1,1,new(10,1,1,new Dictionary<int,int>{{1,1}},observations.Take(1).ToArray()),
                new(20,false,observations.Take(1).ToArray()),new(0,false))).ToArray();
            var plan=new StockCraftPlan(new Equipment(i){Name="Recette"+i.ToString("D3")},1,0,ingredients);
            return new EconomicOpportunity("craft","Fabriquer puis revendre","Tylezia",plan,240+i,new(observations),[]);
        }).ToArray();
        public Task<IReadOnlyList<EconomicOpportunity>> EvaluateAsync(OpportunityScanContext context,CancellationToken token)
        {Calls++;return Task.FromResult(Rows);}
    }
    public sealed class Capture : IComponentActivator
    {
        public StockOpportunities? View;
        public List<IComponent> Components=[];
        public IComponent CreateInstance(Type type)
        {
            var c=(IComponent)Activator.CreateInstance(type)!;
            Components.Add(c);
            if(c is StockOpportunities view)View=view;
            return c;
        }
    }
    [Fact]
    public async Task MeasureInitialAndRepeatedCraftRendering()
    {
        string folder=Path.Combine(Path.GetTempPath(),"craft-perf-"+Guid.NewGuid());
        try
        {
            using TestDatabase database=new();using var db=database.CreateContext();
            var prices=new MarketPriceService(db,new TestDataPriorityProvider());var provider=new Source();
            var scanner=new StockOpportunityService(new ItemsService(db,null!,null!,null!,null!),
                new EquipmentProfitabilityService(null!,null!,new CoefficientService(db,new TestDataPriorityProvider()),prices,null!,null!),
                new StockCraftPlanner(prices),[provider]);
            var key=new DofusClientWindowKey(111,1111);var clients=new CurrentDofusClientsState();
            clients.Update([new(new IntPtr(key.Handle),"Pagalom - Eniripsa - 3.7.12.12 - Release","Dofus",0,0,100,100){ProcessId=key.ProcessId}],key);
            var stocks=new LiveStockService(new LiveStockRepository(Path.Combine(folder,"stock.db")),clients);
            stocks.Observe(new("S→C","irl",Message(Unsigned(4,1),Bytes(2,Bytes(1,Message(Unsigned(3,1),Unsigned(4,1),Unsigned(5,10))))),
                DateTime.UtcNow,new NetworkCaptureLease("Tylezia",1),1,ConnectionId:1,CandidateWindow:key,SyntheticWindowForReplay:true));
            var filter=new ServerViewFilterState();filter.FollowWindow(key,"Tylezia");var capture=new Capture();
            var services=new ServiceCollection();services.AddLogging();services.AddSingleton(scanner);services.AddSingleton(stocks);
            services.AddSingleton(clients);services.AddSingleton(filter);services.AddSingleton(new MarketDataChangeNotifier());
            services.AddSingleton<IComponentActivator>(capture);
            CraftCatalogFixture.Register(services,folder);
            await using var container=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(container,container.GetRequiredService<ILoggerFactory>());
            var timer=Stopwatch.StartNew();
            var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<StockOpportunities>(
                ParameterView.FromDictionary(new Dictionary<string,object?>{{"OriginFilter","craft"}})));
            timer.Stop();double initialMs=timer.Elapsed.TotalMilliseconds;
            string html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Equal(100,Regex.Matches(html,"<strong data-result-name[^>]*>").Count);
            var samples=new List<double>();
            for(int i=0;i<9;i++)
            {
                timer.Restart();await renderer.Dispatcher.InvokeAsync(()=>{
                    typeof(StockOpportunities).GetField("_search",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(capture.View,i%2==0 ? "Recette" : "");
                    typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.View,null);
                });timer.Stop();if(i>0)samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            var measurement=new {initialRenderMs=initialMs,medianLocalRenderMs=samples[samples.Count/2],htmlBytes=Encoding.UTF8.GetByteCount(html),
                ingredientDetails=Regex.Matches(html,"Ingredient ").Count,priceDetails=Regex.Matches(html,"Prix #").Count,providerCalls=provider.Calls};
            string json=JsonSerializer.Serialize(measurement,new JsonSerializerOptions{WriteIndented=true});output.WriteLine(json);
            if(Environment.GetEnvironmentVariable("BESTCRUSH_PERF_OUTPUT") is {Length:>0} path)await File.WriteAllTextAsync(path,json);
            Assert.Equal(1,provider.Calls);
            Assert.Equal(0,measurement.ingredientDetails);Assert.Equal(0,measurement.priceDetails);

            var first=capture.Components.OfType<ServiceResult>().First();
            async Task Toggle()
            {
                await renderer.Dispatcher.InvokeAsync(()=>{
                    typeof(ServiceResult).GetMethod("Toggle",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(first,null);
                    typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(first,null);
                });
            }
            await Toggle();html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Equal(8,Regex.Matches(html,"class=\"craft-ingredient\"").Count);Assert.DoesNotContain("Prix #",html);
            // An unrelated parent render preserves the open detail and keeps the other 99 unbuilt.
            await renderer.Dispatcher.InvokeAsync(()=>typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.View,null));
            html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Equal(8,Regex.Matches(html,"class=\"craft-ingredient\"").Count);
            await Toggle();html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.DoesNotContain("Ingredient ",html);Assert.DoesNotContain("Prix #",html);

            // A link targeting one item still opens that item's detail automatically.
            await renderer.Dispatcher.InvokeAsync(()=>{
                typeof(StockOpportunities).GetProperty("ItemId")!.SetValue(capture.View,(long?)220);
                typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.View,null);
            });
            html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Single(Regex.Matches(html,"<strong data-result-name[^>]*>"));Assert.Equal(8,Regex.Matches(html,"class=\"craft-ingredient\"").Count);
            Assert.Equal(1,provider.Calls);
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
