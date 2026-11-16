using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BestCrush.Components.Pages;
using BestCrush.Components.Pure;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class ForgemagingWorkspaceTest
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class PriorityStub : IDataPriorityProvider { public DataPriority Priority { get; set; } = DataPriority.InGameAutomatic; }
    private sealed class Capture : IComponentActivator
    {
        public ForgemagingWorkspace Workspace = null!;
        public CrushingItemDetail CrushingDetail = null!;
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is ForgemagingWorkspace workspace) Workspace = workspace;
            if (component is CrushingItemDetail detail) CrushingDetail = detail;
            return component;
        }
    }
    private static async Task Seed(TestDatabase database)
    {
        await using var db = database.CreateContext();
        var item = new Equipment(1) { Name = "Objet de test", Level = 200 };
        item.Characteristics.Add(new(item, Characteristic.Strength, 0, 20)); db.Equipments.Add(item);
        db.Runes.AddRange(new Rune(10) { Name = "Rune Fo", Characteristic = Characteristic.Strength, DofusDbIconId = 78043 },
            new Rune(11) { Name = "Rune Pa Fo", Characteristic = Characteristic.Strength },
            new Rune(12) { Name = "Rune Ra Fo", Characteristic = Characteristic.Strength });
        foreach (var server in new[] { "A", "B" })
        foreach (long id in new[] { 10L, 11L }) db.MarketPriceObservations.Add(new() { ObjectType = MarketObjectType.Rune,
            DofusDbId = id, ServerName = server, Price = server == "A" ? 100 : 1000, Quantity = 10,
            Source = MarketPriceSource.InGameAutomatic, ObservedAtUtc = DateTime.UtcNow });
        foreach (var server in new[] { "A", "B" })
            db.MarketPriceObservations.Add(Utils.EconomicTestData.Price(10, 1, server == "A" ? 11 : 101,
                server: server, type: MarketObjectType.Rune, at: DateTime.UtcNow));
        await db.SaveChangesAsync();
    }
    private static ServiceCollection Services(TestDatabase database, Capture capture, string path)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IComponentActivator>(capture);
        services.AddSingleton(new ForgemagingSessionStore(path)); services.AddSingleton<MarketDataChangeNotifier>();
        services.AddSingleton<IDataPriorityProvider>(new PriorityStub()); services.AddScoped(_ => database.CreateContext());
        services.AddScoped<MarketPriceService>(); services.AddScoped<ForgemagingDataService>(); return services;
    }

    [Fact]
    public async Task CrushingDetailLocksOwnedRollAndOnlyShowsSimulationControlsForCatalogue()
    {
        using var database = new TestDatabase();
        await Seed(database);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var capture = new Capture();
        var services = Services(database, capture, path);
        services.AddScoped<CoefficientService>();
        await using (var db = database.CreateContext())
        {
            db.CoefficientObservations.Add(new() { DofusDbId = 1, ServerName = "A", Source = CoefficientSource.InGameAutomatic,
                ObservedAtUtc = DateTime.UtcNow, CoefficientPercent = 200 });
            db.MarketPriceObservations.Add(Utils.EconomicTestData.Price(1, 1, 100,
                server: "A", type: MarketObjectType.Equipment, at: DateTime.UtcNow));
            db.MarketPriceObservations.Add(new() { ObjectType = MarketObjectType.Rune,
                DofusDbId = 10, ServerName = "A", Price = 999, Quantity = 1,
                Source = MarketPriceSource.Manual, ObservedAtUtc = DateTime.UtcNow.AddSeconds(1) });
            await db.SaveChangesAsync();
        }
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var item = new Equipment(1) { Name = "Objet", Level = 200 };
        item.Characteristics.Add(new(item, Characteristic.Strength, 0, 20));
        var owned = new OwnedForgemagingEquipment("instance", item, "inventory", 1,
            [new() { Characteristic = Characteristic.Strength, Current = 19, Minimum = 0, Maximum = 20 }],
            false, [], null, null, true, true, 0, 0, false);
        async Task<string> RenderDetailAsync(OwnedForgemagingEquipment? instance)
        {
            var parameters = new Dictionary<string, object?> { ["Equipment"] = item, ["ServerName"] = "A", ["Owned"] = instance };
            var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CrushingItemDetail>(
                ParameterView.FromDictionary(parameters)));
            return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        }
        string catalogue = await RenderDetailAsync(null);
        Assert.Contains("Jet minimum", catalogue);
        Assert.Contains("Jet moyen", catalogue);
        Assert.Contains("Jet maximum", catalogue);
        Assert.Contains("+ Exo", catalogue);
        Assert.Matches("Prix de vente conseillé</dt>\\s*<dd[^>]*>99 K", catalogue);
        Assert.Contains("Total kamas sans focus", catalogue);
        Assert.Contains("img/runes/78043.png", catalogue);
        Assert.Contains(">10 K</td>", catalogue);
        Assert.DoesNotContain(">998 K</td>", catalogue);
        string stock = await RenderDetailAsync(owned);
        Assert.Contains(">19</strong>", stock);
        Assert.DoesNotContain("Jet maximum", stock);
        Assert.DoesNotContain("+ Exo", stock);
        Assert.DoesNotContain("Jet de simulation", stock);

        var cardParameters = new Dictionary<string, object?>
        {
            ["Equipment"] = item, ["ServerName"] = "A", ["InitiallyExpanded"] = true,
            ["Identity"] = (RenderFragment)(builder => builder.AddContent(0, item.Name)),
            ["RankedSummary"] = new CrushingResultSummary(450, 350, 100)
        };
        var card = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CrushingResultCard>(
            ParameterView.FromDictionary(cardParameters)));
        async Task<string> CardHtml() => WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(card.ToHtmlString));
        var before = await CardHtml();
        Assert.Single(Regex.Matches(before, ">Gain estimé<"));
        Assert.Single(Regex.Matches(before, ">Rentabilité<"));
        Assert.Single(Regex.Matches(before, ">Valeur des runes<"));
        Assert.Single(Regex.Matches(before, ">Coefficient<"));
        Assert.Contains("514 K</strong>", before);
        Assert.DoesNotContain("350 K</strong>", before);

        var setPreset = typeof(CrushingItemDetail).GetMethod("SetPreset", Hidden)!;
        var maximum = Enum.Parse(setPreset.GetParameters().Single().ParameterType, "Maximum");
        await renderer.Dispatcher.InvokeAsync(() => (Task)setPreset.Invoke(capture.CrushingDetail, [maximum])!);
        var after = await CardHtml();
        Assert.Contains($"{1208:N0} K</strong>", after);
        Assert.Contains($"{1108:N0} K</strong>", after);
        Assert.Contains($"{1108:N1} %</strong>", after);
    }

    [Fact]
    public async Task WorkshopUsesOnlyObservedRollAndRefreshesPricesWithoutManualAction()
    {
        using var database=new TestDatabase();await Seed(database);
        string folder=Path.Combine(Path.GetTempPath(),"fm-auto-"+Guid.NewGuid());
        string path=Path.Combine(folder,"sessions.json");
        var clients=new CurrentDofusClientsState();var window=new DofusClientWindowKey(111,1);
        clients.Update([new(new IntPtr(window.Handle),"Pagalom - Eniripsa - 3.7.13.17 - Release","Dofus",0,0,100,100){ProcessId=window.ProcessId}],window);
        var servers=new CurrentServerState();servers.SelectServer("A");
        var capture=new Capture();var services=Services(database,capture,path);
        services.AddSingleton(clients);services.AddSingleton(servers);services.AddSingleton<LiveDofusClientNetworkBindings>();
        services.AddSingleton(new ActivityCatalogService(Moq.Mock.Of<DofusSharp.DofusDb.ApiClients.IDofusDbClientsFactory>(),
            Moq.Mock.Of<IHttpClientFactory>(),folder));
        services.AddSingleton<LiveForgemagingService>();
        try
        {
            await using var provider=services.BuildServiceProvider();
            var live=provider.GetRequiredService<LiveForgemagingService>();
            // Fixed validated effect metadata; the test never contacts the public catalogue.
            typeof(LiveForgemagingService).GetField("_effects",Hidden)!.SetValue(live,
                new Dictionary<ulong,ForgemagingEffectDefinition>{{118,new(118,Characteristic.Strength,1)}});
            var store=provider.GetRequiredService<ForgemagingSessionStore>();
            store.Save("A",new(){ItemId=1,ItemName="Manuel",Level=200,Sink=4,
                Lines=[new(){Characteristic=Characteristic.Strength,Minimum=0,Maximum=20,Current=5,Target=20}]});
            await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
            var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<ForgemagingWorkspace>(
                ParameterView.FromDictionary(new Dictionary<string,object?>{{"ServerName","A"}})));
            async Task<string> Html()=>await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            async Task Until(Func<bool> condition)
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
                while(!await renderer.Dispatcher.InvokeAsync(condition))await Task.Delay(10,timeout.Token);
            }
            DofusWireMessage Wire(string direction,string key,byte[] body)=>new(direction,key,body,DateTime.UtcNow,
                servers.GetCaptureLease(),1,ConnectionId:1,CandidateWindow:window,SyntheticWindowForReplay:true);
            byte[] V(int field,ulong value) { List<byte> bytes=[(byte)(field*8)];do{bytes.Add((byte)((value&127)|(value>=128?128UL:0)));value>>=7;}while(value>0);return bytes.ToArray(); }
            byte[] B(int field,byte[] bytes)=>[(byte)(field*8+2),(byte)bytes.Length,..bytes];
            byte[] item=[..V(3,1),..V(4,1),..V(5,500),..B(7,[..V(5,8),..V(10,118)])];
            byte[] description=[21,0,0,0,0,..B(3,[..B(1,item),..V(4,63)])];
            void Deposit()
            {
                live.Observe(Wire("C→S","jys",[..V(1,500),..V(3,1)]));
                live.Observe(Wire("S→C","kch",description));
            }
            Assert.Null(Field<ForgemagingSession?>(capture.Workspace, "_session"));
            string standby = WebUtility.HtmlDecode(await Html());
            Assert.DoesNotContain("Suivi manuel", standby);
            Assert.DoesNotContain("Manuel", standby);
            live.Observe(Wire("S→C","kch",description));
            live.Observe(Wire("C→S","jys",[..V(1,500),..V(3,1)]) with { CandidateWindow = new(999, 9) });
            Assert.False(live.View("A").CanTrack);
            Deposit();
            await Until(() => Field<ForgemagingSession?>(capture.Workspace, "_session") is not null);
            Assert.Equal(8, Field<ForgemagingSession>(capture.Workspace, "_session").Lines.Single().Current);
            Assert.Equal(5, store.Get("A")!.Lines.Single().Current);
            var html = WebUtility.HtmlDecode(await Html());
            Assert.Contains("Prochaine étape", html);
            Assert.Contains("data-bs-title=\"Rune Fo · +1 Force\"", html);
            Assert.Contains("/servers/A?item=1&owned=true&instance=" + ForgemagedEquipmentStock.InstanceKey("A", 1, 500), html);
            Assert.DoesNotContain("Actualiser les données", html);
            Assert.DoesNotContain("Détail du jet", html);
            Assert.DoesNotContain("Jet actuel :", html);
            Assert.Contains("Objectif : Force", html);

            await renderer.Dispatcher.InvokeAsync(() =>
            {
                var workspace = capture.Workspace;
                typeof(ForgemagingWorkspace).GetField("_prepareCrushing", Hidden)!.SetValue(workspace, true);
                typeof(ForgemagingWorkspace).GetMethod("Recalculate", Hidden)!.Invoke(workspace, null);
                typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(workspace, null);
            });
            string preparation = WebUtility.HtmlDecode(await Html());
            Assert.DoesNotContain("Objectif : Force", preparation);
            Assert.DoesNotContain("+ Exo", preparation);
            Assert.Contains("Prochaine étape", preparation);
            Assert.Contains("Il manque des données pour conseiller une rune.", preparation);
            await using (var db = database.CreateContext())
            {
                db.MarketPriceObservations.Add(Utils.EconomicTestData.Price(11, 1, 4,
                    server: "A", type: MarketObjectType.Rune, at: DateTime.UtcNow));
                await db.SaveChangesAsync();
            }
            provider.GetRequiredService<MarketDataChangeNotifier>().Notify(MarketObjectType.Rune, 11, "A");
            await Until(() => Field<IReadOnlyList<ForgemagingRune>>(capture.Workspace, "_runes").Any(rune => rune.Id == 11 && rune.UnitPrice == 4));
            live.Observe(Wire("S→C", "kbr", []));
            await Until(() => Field<ForgemagingSession?>(capture.Workspace, "_session") is null);
            Assert.DoesNotContain("Manuel", WebUtility.HtmlDecode(await Html()));
            Deposit();
            await Until(() => Field<ForgemagingSession?>(capture.Workspace, "_session") is not null);
            servers.SelectServer("B");
            await Until(() => Field<ForgemagingSession?>(capture.Workspace, "_session") is null);
            Assert.False(live.View("A").CanTrack);
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }

    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Hidden)!.GetValue(instance)!;

}
