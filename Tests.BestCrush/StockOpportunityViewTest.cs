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

public sealed class StockOpportunityViewTest
{
    private sealed class CountingSource : IOpportunityProvider
    {
        public string Origin => "craft";
        public int Calls;
        public Task<IReadOnlyList<EconomicOpportunity>> EvaluateAsync(OpportunityScanContext context, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult<IReadOnlyList<EconomicOpportunity>>([]);
        }
    }

    [Fact]
    public async Task WindowFocusPreservesReportButStockPricesAndTargetChangesRefreshIt()
    {
        string folder=Path.Combine(Path.GetTempPath(),"opportunity-view-"+Guid.NewGuid());
        try
        {
            using TestDatabase database=new();using var db=database.CreateContext();
            var prices=new MarketPriceService(db,new TestDataPriorityProvider());
            var items=new ItemsService(db,null!,null!,null!,null!);
            var profitability=new EquipmentProfitabilityService(null!,null!,
                new CoefficientService(db,new TestDataPriorityProvider()),prices,null!,null!);
            var provider=new CountingSource();
            var scanner=new StockOpportunityService(items,profitability,new StockCraftPlanner(prices),[provider]);
            var a=new DofusClientWindowKey(111,1111);var b=new DofusClientWindowKey(222,2222);
            DofusWindowInfo Window(DofusClientWindowKey key,string title)=>new(new IntPtr(key.Handle),title,"Dofus",0,0,100,100){ProcessId=key.ProcessId};
            var windows=new[]{Window(a,"Pagalom - Eniripsa - 3.7.12.12 - Release"),Window(b,"Autre - Iop - 3.7.12.12 - Release")};
            var clients=new CurrentDofusClientsState();clients.Update(windows,a);
            var stock=new LiveStockService(new LiveStockRepository(Path.Combine(folder,"stock.db")),clients);
            var at=DateTime.UtcNow;
            void Inventory(ulong quantity)=>stock.Observe(new("S→C","irl",
                Message(Unsigned(4,1),Bytes(2,Bytes(1,Message(Unsigned(3,quantity),Unsigned(4,1),Unsigned(5,10))))),
                at,new NetworkCaptureLease("Tylezia",1),1,ConnectionId:1,CandidateWindow:a,SyntheticWindowForReplay:true));
            Inventory(3);
            var filter=new ServerViewFilterState();filter.FollowWindow(a,"Tylezia");
            var changes=new MarketDataChangeNotifier();
            var services=new ServiceCollection();services.AddLogging();
            services.AddSingleton(scanner);services.AddSingleton(stock);services.AddSingleton(clients);
            services.AddSingleton(filter);services.AddSingleton(changes);
            CraftCatalogFixture.Register(services,folder);
            await using var container=services.BuildServiceProvider();
            await using var renderer=new HtmlRenderer(container,container.GetRequiredService<ILoggerFactory>());
            var view=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<StockOpportunities>(
                ParameterView.FromDictionary(new Dictionary<string,object?>{{"OriginFilter","craft"}})));
            Assert.Equal(1,provider.Calls);
            string html=await renderer.Dispatcher.InvokeAsync(view.ToHtmlString);
            Assert.Contains("recettes liées",System.Net.WebUtility.HtmlDecode(html));
            Assert.DoesNotContain("Banque non synchronisée",System.Net.WebUtility.HtmlDecode(html));
            Assert.False(StockOpportunityService.HasSynchronizedBank(stock.Selected));
            Assert.Contains("Rechercher parmi les recettes",System.Net.WebUtility.HtmlDecode(html));
            Assert.DoesNotContain("Inventaire seul",System.Net.WebUtility.HtmlDecode(html));

            // BestCrush -> same Dofus window -> BestCrush; titles/foreground
            // notifications and same-server manual browsing must keep the report.
            await renderer.Dispatcher.InvokeAsync(()=>
            {
                clients.Update(windows);clients.Update(windows,a);clients.Update(windows);
                filter.BrowseServer("tylezia");filter.FollowWindow(a,"Tylezia");
            });
            await Task.Delay(850);
            Assert.Equal(1,provider.Calls);
            Assert.Equal(html,await renderer.Dispatcher.InvokeAsync(view.ToHtmlString));

            // A real stock update, even at the same observation timestamp,
            // must replace the report. Another server's prices must not.
            await renderer.Dispatcher.InvokeAsync(()=>Inventory(4));
            await Task.Delay(850);Assert.Equal(2,provider.Calls);
            await renderer.Dispatcher.InvokeAsync(()=>changes.Notify(MarketObjectType.Resource,1,"Kourial"));
            await Task.Delay(850);Assert.Equal(2,provider.Calls);
            await renderer.Dispatcher.InvokeAsync(()=>changes.Notify(MarketObjectType.Resource,1,"Tylezia"));
            await Task.Delay(850);Assert.Equal(3,provider.Calls);

            // Receiving even an empty synchronized bank automatically includes
            // it and removes the warning without blocking or leaving the view.
            await renderer.Dispatcher.InvokeAsync(()=>stock.Observe(new("S→C","irp",[],at,
                new NetworkCaptureLease("Tylezia",1),1,ConnectionId:1,CandidateWindow:a,SyntheticWindowForReplay:true)));
            await Task.Delay(850);Assert.Equal(4,provider.Calls);
            string synchronized=System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(view.ToHtmlString));
            Assert.True(StockOpportunityService.HasSynchronizedBank(stock.Selected));
            Assert.Contains("Rechercher parmi les recettes",synchronized);
            Assert.DoesNotContain("Banque non synchronisée",synchronized);

            // An actual target change invalidates the previous character's
            // report immediately, even if the new character has no stock yet.
            await renderer.Dispatcher.InvokeAsync(()=>clients.SelectWindow(b));
            await Task.Delay(850);
            Assert.Contains("Le calcul nécessite",System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(view.ToHtmlString)));
            Assert.Equal(4,provider.Calls);
            await renderer.Dispatcher.InvokeAsync(()=>clients.SelectWindow(a));
            await Task.Delay(850);Assert.Equal(4,provider.Calls);
            // Returning to an unchanged character reuses its valid cached report.
            Assert.True(StockOpportunityService.HasSynchronizedBank(stock.Selected));
            Assert.Contains("Rechercher parmi les recettes",System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(view.ToHtmlString)));
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }
}
