using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class SoulMigrationAndStockStatusTest
{
    [Fact]
    public async Task SoulCardKeepsKnownResaleWhenTheEmptyStoneHasNoPrice()
    {
        var now = DateTime.UtcNow;
        var capture = new ActivityRecommendation("soul-147", "pierres-ame", "Capturer puis revendre", 147,
            "Boss capturable", 40, MarketObjectType.Resource, 1, 9000, null,
            new(9000, null, null, null, new(SalesKineticsStatus.Missing, null, null, null), null),
            ["Prix de la pierre d’âme vide manquant.", "Historique des ventes manquant."], [])
        {
            SoulCapture = new("Antre d'Été", new(9686, "Petite pierre d’âme", 50), false, false)
        };
        Assert.Equal(capture, Assert.Single(ActivityRanking.Select([capture], new(Search: "antre d'ete"), now)));

        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<SoulCaptureResult>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Row"] = capture, ["InitiallyExpanded"] = true }));
            return System.Net.WebUtility.HtmlDecode(view.ToHtmlString());
        });

        Assert.Contains("Copier 9000", html);
        Assert.Contains("Petite pierre d’âme", html);
        Assert.Contains("Capacité 50", html);
        Assert.Contains("Prix de la pierre vide", html);
        Assert.DoesNotContain(">Prix de la pierre pleine<", html);
        Assert.DoesNotContain("Indice :", html);
    }

    [Theory]
    [InlineData(false, 120, true)]
    [InlineData(true, 120, false)]
    [InlineData(false, 0, false)]
    public async Task TradeCraftIngredientsShowRequiredAndKnownStockQuantities(bool staleBank, int bankQuantity, bool showBank)
    {
        var now = DateTime.UtcNow;
        var stock = new LiveStockSource("live", "Pagalom", "Tylezia", now,
        [
            new("inventory", true, false, now, [new(42, 1, 10, null, null, null), new(42, 2, 14, null, null, null)]),
            new("bank", true, staleBank, now, [new(42, 3, (ulong)bankQuantity, null, null, null)])
        ]);
        var row = new ActivityRecommendation("owned", "achat-vente", "Revendre mon stock", 12,
            "Queue du Royalmouth", 120, MarketObjectType.Resource, 10, 1000, 0,
            new(1000, 0, 1000, null, new(SalesKineticsStatus.Missing, null, null, null), null), [], []);
        CraftAlternative[] crafts = [new(91, "Coiffe de Bouflouth", "6 × Œil", 1000)
        {
            Ingredients = [new(42, "Œil", 6, null)]
        }];
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<TradingResultDetail>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Stock"] = stock, ["Row"] = row, ["Crafts"] = crafts }));
            return System.Net.WebUtility.HtmlDecode(view.ToHtmlString());
        });

        Assert.Contains("Œil\nQuantité requise : 6\nInventaire : 24", html);
        Assert.Contains("data-bs-title=\"Inventaire : 24\"", html);
        if (showBank)
        {
            Assert.Contains("Inventaire : 24\nBanque : 120", html);
            Assert.Contains("data-bs-title=\"Banque : 120\"", html);
        }
        else Assert.DoesNotContain("Banque :", html);
    }

    [Fact]
    public void UpgradePreservesLatestSnapshotsMinimumPricesManualPriorityHistoryAndIsIdempotent()
    {
        string folder=Path.Combine(Path.GetTempPath(),"soul-upgrade-"+Guid.NewGuid());
        Directory.CreateDirectory(folder);string path=Path.Combine(folder,"sales.db");
        try
        {
            var initial=new GlobalSalesRepository(path);var now=DateTimeOffset.UtcNow;
            initial.SaveHistory(new(new("Tylezia",MarketObjectType.Resource,33713,"boss:173:all-slots"),now,[],[new(72990,75,now.AddHours(-1))]));
            using(var db=new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();using var command=db.CreateCommand();command.CommandText="""
                    DROP TABLE soul_market_prices;
                    DROP TABLE soul_capture_quotes;
                    CREATE TABLE soul_market_prices(server TEXT,item_id INTEGER,boss_id INTEGER,loot_slots INTEGER,lot_quantity INTEGER,lot_price INTEGER,group_id INTEGER,observed_at TEXT,PRIMARY KEY(server,item_id,loot_slots,lot_quantity));
                    CREATE TABLE soul_capture_quotes(server TEXT,boss_id INTEGER,loot_slots INTEGER,price INTEGER,observed_at TEXT,PRIMARY KEY(server,boss_id,loot_slots));
                    INSERT INTO soul_market_prices VALUES
                    ('TYLEZIA',33713,173,4,1,72990,553,'2026-10-29T12:00:00.0000000+00:00'),
                    ('TYLEZIA',33713,173,8,1,135000,553,'2026-10-29T12:00:00.0000000+00:00'),
                    ('TYLEZIA',33713,173,7,1,100,553,'2026-10-28T12:00:00.0000000+00:00'),
                    ('KOURIAL',33713,173,4,1,999,553,'2026-10-29T12:00:00.0000000+00:00');
                    INSERT INTO soul_capture_quotes VALUES
                    ('TYLEZIA',173,4,70000,'2026-10-29T11:00:00.0000000+00:00'),
                    ('TYLEZIA',173,8,130000,'2026-10-29T10:00:00.0000000+00:00');
                    """;command.ExecuteNonQuery();
            }
            var migrated=new GlobalSalesRepository(path);
            Assert.Equal(72990d,Assert.Single(migrated.ReadSoulQuotes("Tylezia")).Price);
            Assert.Equal(999d,Assert.Single(migrated.ReadSoulQuotes("Kourial")).Price);
            var history=Assert.Single(migrated.ReadHistories("Tylezia"));
            Assert.Equal("boss:173",history.Market.Variant);Assert.Equal(75,history.Period(7).UnitsSold);
            Assert.NotNull(migrated.ReadLatest(history.Market,now));
            using(var db=new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();using var command=db.CreateCommand();command.CommandText="PRAGMA table_info(soul_market_prices)";
                using var columns=command.ExecuteReader();while(columns.Read())Assert.NotEqual("loot_slots",columns.GetString(1));
            }
            migrated.SaveSoulQuote(new("Tylezia",173,71000,now.AddSeconds(1)));
            var reopened=new GlobalSalesRepository(path);
            var manual=Assert.Single(reopened.ReadSoulQuotes("Tylezia"));Assert.Equal(71000d,manual.Price);Assert.False(manual.IsAutomatic);
            Assert.Single(reopened.ReadHistories("Tylezia"));
        }
        finally{Directory.Delete(folder,true);}
    }

    [Fact]
    public async Task HeaderStockIndicatorsFollowEachWindowAndClearForAnEmptySynchronizedBank()
    {
        string folder=Path.Combine(Path.GetTempPath(),"stock-banner-"+Guid.NewGuid());
        try
        {
            var a=new DofusClientWindowKey(111,1111);var b=new DofusClientWindowKey(222,2222);
            var clients=new CurrentDofusClientsState();
            clients.Update([new(new IntPtr(a.Handle),"Pagalom - Eniripsa - 3.7.12.12 - Release","Dofus",0,0,100,100){ProcessId=a.ProcessId},
                new(new IntPtr(b.Handle),"Autre - Iop - 3.7.12.12 - Release","Dofus",0,0,100,100){ProcessId=b.ProcessId}],a);
            var stock=new LiveStockService(new LiveStockRepository(Path.Combine(folder,"stock.db")),clients);
            var bindings=new LiveDofusClientNetworkBindings();var services=new ServiceCollection();services.AddLogging();
            services.AddSingleton(stock);services.AddSingleton(clients);services.AddSingleton(bindings);
            await using var provider=services.BuildServiceProvider();
            await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
            var selected=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<StockStatus>());
            var viewA=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<StockStatus>(ParameterView.FromDictionary(
                new Dictionary<string,object?>{{"Window",a}})));
            var viewB=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<StockStatus>(ParameterView.FromDictionary(
                new Dictionary<string,object?>{{"Window",b}})));
            async Task<string> Html(Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent view)=>
                System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(view.ToHtmlString));
            const string inventory="data-stock-container=\"inventory\"",bank="data-stock-container=\"bank\"";
            void Observe(string opcode,byte[] body,DofusClientWindowKey window,long connection=1)=>stock.Observe(new("S→C",opcode,body,DateTime.UtcNow,new NetworkCaptureLease("Tylezia",1),1,
                ConnectionId:connection,CandidateWindow:window,SyntheticWindowForReplay:true));
            string initial=await Html(selected);
            Assert.Contains(inventory,initial);Assert.Contains(bank,initial);
            Assert.Contains("<svg",initial);
            Assert.DoesNotContain("Voir les stocks",initial);Assert.DoesNotContain("en attente du relevé",initial);
            Assert.Contains("role=\"tooltip\"",initial);Assert.DoesNotContain("title=",initial);
            await renderer.Dispatcher.InvokeAsync(()=>Observe("irl",Message(Unsigned(4,1),Bytes(2,Bytes(1,Message(Unsigned(3,3),Unsigned(4,1),Unsigned(5,10))))),a));
            Assert.DoesNotContain(inventory,await Html(viewA));Assert.Contains(bank,await Html(viewA));
            Assert.Contains(inventory,await Html(viewB));Assert.Contains(bank,await Html(viewB));
            await renderer.Dispatcher.InvokeAsync(()=>clients.SelectWindow(b));
            Assert.Contains(inventory,await Html(selected));Assert.Contains(bank,await Html(selected));
            Assert.DoesNotContain(inventory,await Html(viewA)); // selecting B must not change A’s indicators
            await renderer.Dispatcher.InvokeAsync(()=>Observe("irp",[0],a));Assert.Contains(bank,await Html(viewA));
            await renderer.Dispatcher.InvokeAsync(()=>Observe("irp",[],a));
            Assert.DoesNotContain(bank,await Html(viewA));Assert.DoesNotContain(inventory,await Html(viewA));
            await renderer.Dispatcher.InvokeAsync(()=>Observe("irp",[],b,2));
            Assert.Contains(inventory,await Html(viewB));Assert.DoesNotContain(bank,await Html(viewB));
            await renderer.Dispatcher.InvokeAsync(()=>Observe("irl",[0],a)); // partial inventory needs resync
            Assert.Contains(inventory,await Html(viewA));Assert.DoesNotContain(bank,await Html(viewA));
            await renderer.Dispatcher.InvokeAsync(stock.ClearLive);
            Assert.Contains(inventory,await Html(selected));Assert.Contains(bank,await Html(selected));
            await renderer.Dispatcher.InvokeAsync(()=>clients.Update([],null));
            Assert.Null(stock.ForWindow(a));Assert.Null(stock.ForWindow(b));
            Assert.DoesNotContain(inventory,await Html(viewA));Assert.DoesNotContain(bank,await Html(viewA));
            Assert.DoesNotContain(inventory,await Html(selected));Assert.DoesNotContain(bank,await Html(selected));
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
