using System.Net;
using System.Reflection;
using BestCrush.Components.Pages;
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
    private sealed class Factory(TestDatabase database)
    {
        public int Count;
        public TaskCompletionSource? Started, Release;
        public BestCrushDbContext Create()
        {
            Interlocked.Increment(ref Count);
            var started = Interlocked.Exchange(ref Started, null);
            if (started is not null) { started.TrySetResult(); Release!.Task.GetAwaiter().GetResult(); }
            return database.CreateContext();
        }
    }
    private sealed class Capture : IComponentActivator
    {
        public ForgemagingWorkspace Workspace = null!;
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is ForgemagingWorkspace workspace) Workspace = workspace;
            return component;
        }
    }
    private static async Task Seed(TestDatabase database)
    {
        await using var db = database.CreateContext();
        var item = new Equipment(1) { Name = "Objet de test", Level = 200 };
        item.Characteristics.Add(new(item, Characteristic.Strength, 0, 20)); db.Equipments.Add(item);
        db.Runes.AddRange(new Rune(10) { Name = "Rune Fo", Characteristic = Characteristic.Strength },
            new Rune(11) { Name = "Rune Pa Fo", Characteristic = Characteristic.Strength },
            new Rune(12) { Name = "Rune Ra Fo", Characteristic = Characteristic.Strength });
        foreach (var server in new[] { "A", "B" })
        foreach (long id in new[] { 10L, 11L }) db.MarketPriceObservations.Add(new() { ObjectType = MarketObjectType.Rune,
            DofusDbId = id, ServerName = server, Price = server == "A" ? 100 : 1000, Quantity = 10,
            Source = MarketPriceSource.InGameAutomatic, ObservedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
    private static ServiceCollection Services(Factory factory, Capture capture, string path)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IComponentActivator>(capture);
        services.AddSingleton(new ForgemagingSessionStore(path)); services.AddSingleton<MarketDataChangeNotifier>();
        services.AddSingleton<IDataPriorityProvider>(new PriorityStub()); services.AddScoped(_ => factory.Create());
        services.AddScoped<MarketPriceService>(); services.AddScoped<ForgemagingDataService>(); return services;
    }

    [Fact]
    public async Task CostGuideUsesBackgroundReadsEditsLocallyAndRequiresActualNeutralResults()
    {
        using var database = new TestDatabase(); await Seed(database);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var factory = new Factory(database); var capture = new Capture();
            await using var provider = Services(factory, capture, path).BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ForgemagingWorkspace>(ParameterView.FromDictionary(new Dictionary<string, object?> { { "ServerName", "A" } })));
            await renderer.Dispatcher.InvokeAsync(async () => { Set(capture.Workspace, "_selectedItem", 1L); await Call(capture.Workspace, "SelectItemAsync"); Render(capture.Workspace); });
            Assert.Null(Field<ForgemagingSession>(capture.Workspace, "_session").Lines.Single().Current);
            Assert.Contains("Renseignez le jet actuel", WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
            int reads = factory.Count;
            await renderer.Dispatcher.InvokeAsync(async () => { await (Task)Invoke(capture.Workspace, "FillRollAsync", false)!; Render(capture.Workspace); });
            Assert.Equal(reads, factory.Count);
            var candidates = Field<IReadOnlyList<ForgemagingAdvice>>(capture.Workspace, "_advice");
            Assert.Equal(11, candidates[0].Rune.Id); Assert.Null(candidates.Single(a => a.Rune.Id == 12).Rune.UnitPrice);
            var coachingHtml = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            Assert.Contains("Prochaine action", coachingHtml); Assert.Contains("Rune Pa Fo", coachingHtml); Assert.Contains("Rune Ra Fo", coachingHtml);
            await renderer.Dispatcher.InvokeAsync(() => { Set(capture.Workspace, "_prepareCrushing", true); Invoke(capture.Workspace, "Recalculate"); Render(capture.Workspace); });
            Assert.Contains("Coefficient de concassage non relevé", WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
            Assert.Equal(reads, factory.Count);
            await renderer.Dispatcher.InvokeAsync(() => { Set(capture.Workspace, "_crushContext", new ForgemagingCrushContext(200, DateTime.UtcNow)); Invoke(capture.Workspace, "Recalculate"); Render(capture.Workspace); });
            Assert.Equal(1220, Field<ForgemagingCrushComparison>(capture.Workspace, "_crushComparison").PreparationCeiling);
            Assert.Contains("Marge disponible pour la FM", WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
            Assert.Equal(reads, factory.Count);
            await renderer.Dispatcher.InvokeAsync(() => {
                Field<ForgemagingSession>(capture.Workspace, "_session").Lines.Single().Current = 20;
                Invoke(capture.Workspace, "Recalculate");
            });
            Assert.Null(Field<ForgemagingCoachingPlan>(capture.Workspace, "_coaching").Next);
            Assert.Contains("Conservez le jet actuel", Field<ForgemagingCoachingPlan>(capture.Workspace, "_coaching").StopReason);
            await renderer.Dispatcher.InvokeAsync(() => {
                Field<ForgemagingSession>(capture.Workspace, "_session").Lines.Single().Current = 0;
                Set(capture.Workspace, "_prepareCrushing", false); Invoke(capture.Workspace, "Recalculate");
            });
            await renderer.Dispatcher.InvokeAsync(() => { Invoke(capture.Workspace, "Prepare", candidates[0]); Set(capture.Workspace, "_outcome", ForgemagingOutcome.Neutral); Invoke(capture.Workspace, "SetOutcome"); });
            await renderer.Dispatcher.InvokeAsync(() => Call(capture.Workspace, "RecordAttemptAsync"));
            Assert.Empty(Field<ForgemagingSession>(capture.Workspace, "_session").Attempts);
            await renderer.Dispatcher.InvokeAsync(async () => {
                Set(capture.Workspace, "_confirmedAfter", true); Set(capture.Workspace, "_attemptPrice", (double?)7);
                await Call(capture.Workspace, "RecordAttemptAsync"); Render(capture.Workspace);
            });
            var session = Field<ForgemagingSession>(capture.Workspace, "_session");
            Assert.Equal(3, session.Lines.Single().Current); Assert.Equal(7, session.KnownSpent);
            Assert.Equal(reads, factory.Count); Assert.Null(session.Sink);
            Assert.Equal(7, provider.GetRequiredService<ForgemagingSessionStore>().Get("A")!.KnownSpent);
            await renderer.Dispatcher.InvokeAsync(() => provider.GetRequiredService<MarketDataChangeNotifier>().Notify(MarketObjectType.Rune, 11, "A"));
            Assert.Equal(reads, factory.Count); Assert.True(Field<bool>(capture.Workspace, "_pricesDirty"));
            await renderer.Dispatcher.InvokeAsync(() => (Task)Invoke(capture.Workspace, "LoadItemAsync", true)!);
            Assert.Equal(7, Field<ForgemagingSession>(capture.Workspace, "_session").Attempts.Single().Rune.UnitPrice);
            Assert.Equal(3, Field<ForgemagingSession>(capture.Workspace, "_session").Lines.Single().Current);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ServerChangeCancelsObsoleteReadWithoutPublishingItsJetOrPrices()
    {
        using var database = new TestDatabase(); await Seed(database);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var factory = new Factory(database); var capture = new Capture();
        await using var provider = Services(factory, capture, path).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ForgemagingWorkspace>(ParameterView.FromDictionary(new Dictionary<string, object?> { { "ServerName", "A" } })));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Started = started; factory.Release = release; Task old = Task.CompletedTask;
        try
        {
            await renderer.Dispatcher.InvokeAsync((Action)(() => { Set(capture.Workspace, "_selectedItem", 1L); old = Call(capture.Workspace, "SelectItemAsync"); Render(capture.Workspace); }));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("spinner-border", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            await renderer.Dispatcher.InvokeAsync(async () => {
                typeof(ForgemagingWorkspace).GetProperty("ServerName")!.SetValue(capture.Workspace, "B");
                await Call(capture.Workspace, "OnParametersSetAsync");
                Set(capture.Workspace, "_selectedItem", 1L); await Call(capture.Workspace, "SelectItemAsync");
            });
            release.TrySetResult(); await old.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(Field<IReadOnlyList<ForgemagingRune>>(capture.Workspace, "_runes").Where(r => r.UnitPrice.HasValue), r => Assert.Equal(100, r.UnitPrice));
        }
        finally { release.TrySetResult(); if (File.Exists(path)) File.Delete(path); }
    }
    [Fact]
    public async Task WorkshopAutomaticallySelectsLiveModeAndManualChoiceSurvivesUpdatesUntilAnotherActivation()
    {
        using var database=new TestDatabase();await Seed(database);
        string folder=Path.Combine(Path.GetTempPath(),"fm-auto-"+Guid.NewGuid());
        string path=Path.Combine(folder,"sessions.json");
        var clients=new CurrentDofusClientsState();var window=new DofusClientWindowKey(111,1);
        clients.Update([new(new IntPtr(window.Handle),"Pagalom - Eniripsa - 3.7.13.17 - Release","Dofus",0,0,100,100){ProcessId=window.ProcessId}],window);
        var servers=new CurrentServerState();servers.SelectServer("A");
        var factory=new Factory(database);var capture=new Capture();var services=Services(factory,capture,path);
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
            string GameInput(string html)=>System.Text.RegularExpressions.Regex.Match(html,"<input[^>]*id=\"fm-tracking-game\"[^>]*>").Value;
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
            Assert.Contains("disabled",GameInput(await Html()));
            Assert.False(Field<bool>(capture.Workspace,"_gameTracking"));
            // A workshop description alone and another window are not sufficient evidence to arm capture.
            live.Observe(Wire("S→C","kch",description));
            live.Observe(Wire("C→S","jys",[..V(1,500),..V(3,1)]) with{CandidateWindow=new(999,9)});
            Assert.False(live.View("A").CanTrack);
            Deposit(); // no Start call, no click on the live radio
            await Until(()=>Field<bool>(capture.Workspace,"_gameTracking"));
            string html=await Html();Assert.DoesNotContain("disabled",GameInput(html));Assert.Contains("checked",GameInput(html));
            Assert.Equal(8,Field<ForgemagingSession>(capture.Workspace,"_session").Lines.Single().Current);
            Assert.Equal(5,store.Get("A")!.Lines.Single().Current);
            long activation=live.View("A").Activation;
            await renderer.Dispatcher.InvokeAsync(()=>((Task)Invoke(capture.Workspace,"SelectTrackingModeAsync",false)!));
            Assert.False(Field<bool>(capture.Workspace,"_gameTracking"));
            Assert.True(live.View("A").CanTrack); // manual choice keeps passive capture available
            Assert.Equal(5,Field<ForgemagingSession>(capture.Workspace,"_session").Lines.Single().Current);
            await renderer.Dispatcher.InvokeAsync(()=>Call(capture.Workspace,"SynchronizeTrackingAsync"));
            Assert.False(Field<bool>(capture.Workspace,"_gameTracking"));
            Assert.Equal(activation,live.View("A").Activation);
            await renderer.Dispatcher.InvokeAsync(()=>((Task)Invoke(capture.Workspace,"SelectTrackingModeAsync",true)!));
            Assert.True(Field<bool>(capture.Workspace,"_gameTracking"));
            live.Observe(Wire("S→C","kbr",[]));
            await Until(()=>!Field<bool>(capture.Workspace,"_gameTracking") &&
                Field<ForgemagingSession?>(capture.Workspace,"_session") is { GameRecordingId: null });
            Assert.Contains("disabled",GameInput(await Html()));
            Assert.Equal(5,Field<ForgemagingSession>(capture.Workspace,"_session").Lines.Single().Current);
            Deposit();await Until(()=>Field<bool>(capture.Workspace,"_gameTracking"));
            Assert.True(live.View("A").Activation>activation); // same item, new workshop activation
            servers.SelectServer("B");
            await Until(()=>!Field<bool>(capture.Workspace,"_gameTracking"));
            Assert.False(live.View("A").CanTrack);Assert.Contains("disabled",GameInput(await Html()));
            await renderer.Dispatcher.InvokeAsync(()=>((Task)Invoke(capture.Workspace,"SelectTrackingModeAsync",true)!));
            Assert.False(Field<bool>(capture.Workspace,"_gameTracking"));
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }

    private static object? Invoke(object instance, string name, params object?[] args) => instance.GetType().GetMethod(name, Hidden)!.Invoke(instance, args);
    private static Task Call(object instance, string name) => (Task)Invoke(instance, name)!;
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Hidden)!.GetValue(instance)!;
    private static void Set(object instance, string name, object? value) => instance.GetType().GetField(name, Hidden)!.SetValue(instance, value);
    private static void Render(object instance) => typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(instance, null);
}
