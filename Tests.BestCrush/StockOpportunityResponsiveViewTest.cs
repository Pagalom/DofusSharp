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

public sealed class StockOpportunityResponsiveViewTest
{
    private sealed class Gate
    {
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
    }
    private sealed class Source : IOpportunityProvider
    {
        public string Origin=>"craft";
        public int Calls;
        private Gate? _next;
        private readonly IReadOnlyList<EconomicOpportunity> _rows=new CraftPerformanceProbeTest.Source().Rows;
        public Gate HoldNext(){var gate=new Gate();Volatile.Write(ref _next,gate);return gate;}
        public async Task<IReadOnlyList<EconomicOpportunity>> EvaluateAsync(OpportunityScanContext context,CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            var gate=Interlocked.Exchange(ref _next,null);
            if(gate is not null){gate.Token=token;gate.Started.TrySetResult();await gate.Release.Task.WaitAsync(token);}
            return _rows;
        }
    }
    [Fact]
    public async Task RefreshKeepsRowsAndFiltersResponsiveCoalescesPricesAndHidesAnotherCharactersReport()
    {
        string folder=Path.Combine(Path.GetTempPath(),"craft-responsive-"+Guid.NewGuid());
        try
        {
            using TestDatabase database=new();using var db=database.CreateContext();var priority=new TestDataPriorityProvider();
            var prices=new MarketPriceService(db,priority);var provider=new Source();var notifier=new MarketDataChangeNotifier();
            using var scanner=new StockOpportunityService(new ItemsService(db,null!,null!,null!,null!),
                new EquipmentProfitabilityService(null!,null!,new CoefficientService(db,priority),prices,null!,null!),
                new StockCraftPlanner(prices),[provider],notifier);
            var a=new DofusClientWindowKey(111,1111);var b=new DofusClientWindowKey(222,2222);var clients=new CurrentDofusClientsState();
            DofusWindowInfo Window(DofusClientWindowKey key,string title)=>new(new IntPtr(key.Handle),title,"Dofus",0,0,100,100){ProcessId=key.ProcessId};
            clients.Update([Window(a,"Pagalom - Eniripsa - 3.7.12.12 - Release"),Window(b,"Autre - Iop - 3.7.12.12 - Release")],a);
            var stocks=new LiveStockService(new LiveStockRepository(Path.Combine(folder,"stock.db")),clients);
            stocks.Observe(new("S→C","irl",Message(Unsigned(4,1),Bytes(2,Bytes(1,Message(Unsigned(3,1),Unsigned(4,1),Unsigned(5,10))))),
                DateTime.UtcNow,new NetworkCaptureLease("Tylezia",1),1,ConnectionId:1,CandidateWindow:a,SyntheticWindowForReplay:true));
            var filter=new ServerViewFilterState();filter.FollowWindow(a,"Tylezia");var capture=new CraftPerformanceProbeTest.Capture();
            var services=new ServiceCollection();services.AddLogging();services.AddSingleton(scanner);services.AddSingleton(stocks);
            services.AddSingleton(clients);services.AddSingleton(filter);services.AddSingleton(notifier);services.AddSingleton<IComponentActivator>(capture);
            CraftCatalogFixture.Register(services,folder);
            await using var container=services.BuildServiceProvider();await using var renderer=new HtmlRenderer(container,container.GetRequiredService<ILoggerFactory>());
            var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<StockOpportunities>(
                ParameterView.FromDictionary(new Dictionary<string,object?>{{"OriginFilter","craft"}})));
            Task refresh=Task.CompletedTask;
            async Task BeginRefresh()
            {
                await renderer.Dispatcher.InvokeAsync((Action)(()=>{
                    refresh=(Task)typeof(StockOpportunities).GetMethod("RefreshAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.View,null)!;
                }));
            }
            var gate=provider.HoldNext();await BeginRefresh();await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var html=System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            Assert.Contains("spinner-border",html);Assert.Contains("aria-busy=\"true\"",html);
            Assert.Equal(100,Regex.Matches(html,"<strong data-result-name[^>]*>").Count);Assert.Contains("Recette220",html);
            // The worker is deliberately blocked, yet the renderer can apply a filter.
            await renderer.Dispatcher.InvokeAsync(()=>{
                typeof(StockOpportunities).GetField("_search",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(capture.View,"Recette219");
                typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(capture.View,null);
            });
            html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Single(Regex.Matches(html,"<strong data-result-name[^>]*>"));Assert.Contains("Recette219",html);
            gate.Release.TrySetResult();await refresh;Assert.Equal(2,provider.Calls);

            await renderer.Dispatcher.InvokeAsync(()=>notifier.Notify(MarketObjectType.Rune,500,"Tylezia"));
            await Task.Delay(400);Assert.Equal(2,provider.Calls);

            await renderer.Dispatcher.InvokeAsync(()=>{
                for(int i=0;i<100;i++)notifier.Notify(MarketObjectType.Resource,i,"Tylezia");
            });
            await Task.Delay(750);Assert.Equal(3,provider.Calls);
            html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);Assert.Contains("Recette219",html);
            Assert.DoesNotContain("spinner-border",html);

            // Updates arriving during an already running market batch must not be lost.
            var trailingGate=provider.HoldNext();
            await renderer.Dispatcher.InvokeAsync(()=>notifier.Notify(MarketObjectType.Resource,1,"Tylezia"));
            await trailingGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await renderer.Dispatcher.InvokeAsync(()=>notifier.Notify(MarketObjectType.Resource,2,"Tylezia"));
            trailingGate.Release.TrySetResult();
            await Task.Delay(750);Assert.Equal(5,provider.Calls);
            html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);Assert.DoesNotContain("spinner-border",html);

            var cancellationGate=provider.HoldNext();await BeginRefresh();await cancellationGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await renderer.Dispatcher.InvokeAsync(()=>clients.SelectWindow(b));
            html=System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            Assert.Empty(Regex.Matches(html,"<strong data-result-name[^>]*>"));Assert.Contains("Le calcul nécessite",html);
            await refresh;Assert.True(cancellationGate.Token.IsCancellationRequested);
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
