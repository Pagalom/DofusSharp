using System.Reflection;
using System.Net;
using System.Text.RegularExpressions;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Models;
using BestCrush.Network.Protocol;
using static Tests.BestCrush.Network.NetworkPayload;
using BestCrush.Services;
using DofusSharp.Dofocus.ApiClients;
using DofusSharp.Dofocus.ApiClients.Models.Items;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Utils.EconomicTestData;
using ServerPage = BestCrush.Components.Pages.Server;

namespace Tests.BestCrush;

public sealed class CrushingAnalysisFlowTest
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Get<T>(ServerPage page, string name) => (T)typeof(ServerPage).GetField(name, Hidden)!.GetValue(page)!;
    private static Task Analyze(ServerPage page) => (Task)typeof(ServerPage).GetMethod("AnalyzeAsync", Hidden)!.Invoke(page, null)!;

    // Keep the real page and render tree, replacing only MAUI startup/preferences.
    public sealed class TestPage : ServerPage
    {
        public TestPage() => typeof(ServerPage).GetProperty("Model", Hidden)!.SetValue(this,
            new SearchItemsModel { SearchText = "Cible", LevelMax = 100 });
        protected override Task OnInitializedAsync()
        {
            typeof(ServerPage).GetMethod("InitializeMarketTracking", Hidden)!.Invoke(this, null);
            return Task.CompletedTask;
        }
        protected override Task OnParametersSetAsync() => Task.CompletedTask;
    }

    private sealed class Activator : IComponentActivator
    {
        public TestPage? Page;
        public global::BestCrush.Components.Pure.SearchItemsForm? Form;
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)System.Activator.CreateInstance(type)!;
            if (component is TestPage page) Page = page;
            if (component is global::BestCrush.Components.Pure.SearchItemsForm form) Form = form;
            return component;
        }
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://localhost/", "http://localhost/servers/TEST_SERVER");
    }

    private sealed class Settings : IBestCrushSettingsProvider
    {
        public bool EquipmentCaptureEnabled => false;
        public bool RuneCaptureEnabled => false;
        public bool ResourceCaptureEnabled => false;
        public bool CoefficientCaptureEnabled => false;
        public CrushYieldEstimationMode CrushYieldEstimationMode => CrushYieldEstimationMode.Average;
        public double TargetRoiPercent => 20;
        public double CrushValueDiscountPercent => 0;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public readonly TestDatabase Database = new();
        public readonly Mock<IDofocusItemsClient> Remote = new(MockBehavior.Strict);
        public readonly Activator Activator = new();
        public readonly MarketDataChangeNotifier Changes = new();
        private readonly string folder = Path.Combine(Path.GetTempPath(), "crush-flow-" + Guid.NewGuid());
        public readonly CraftCatalogFixture CatalogHttp = new() { Skills = [
            new { parentJobId = 16, isForgemagus = false, craftableItemIds = new long[] { 100 } }] };
        public readonly ServiceProvider Services;
        public readonly HtmlRenderer Renderer;
        public Harness()
        {
            var factory = new Mock<IDofocusClientFactory>();
            factory.Setup(x => x.Items()).Returns(Remote.Object);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Changes);
            services.AddSingleton<IComponentActivator>(Activator);
            services.AddSingleton<IDbContextFactory<BestCrushDbContext>>(Database);
            services.AddScoped(_ => Database.CreateDbContext());
            services.AddScoped(sp => new ItemsService(sp.GetRequiredService<BestCrushDbContext>(), null!, factory.Object, null!, null!));
            services.AddSingleton<IDataPriorityProvider, TestDataPriorityProvider>();
            services.AddSingleton<IBestCrushSettingsProvider, Settings>();
            services.AddScoped<MarketPriceService>();
            services.AddScoped<CoefficientService>();
            services.AddScoped<CraftCostService>();
            services.AddScoped<EquipmentProfitabilityService>();
            services.AddScoped<StockCraftPlanner>();
            services.AddSingleton<CrushService>();
            services.AddSingleton<RunesService>();
            services.AddSingleton<BestCrushSettingsService>();
            services.AddSingleton(new OverlayService(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!));
            services.AddSingleton(new CurrentServerState());
            services.AddSingleton<CurrentDofusClientsState>();
            services.AddSingleton(new LiveStockRepository(Path.Combine(folder, "stock.db")));
            services.AddSingleton<LiveStockService>();
            services.AddSingleton<NavigationManager, Navigation>();
            CraftCatalogFixture.Register(services, folder, fixture: CatalogHttp);
            Services = services.BuildServiceProvider();
            Renderer = new HtmlRenderer(Services, Services.GetRequiredService<ILoggerFactory>());
        }

        public async Task SeedAsync()
        {
            using var db = Database.CreateContext();
            var resource = new Resource(1) { Name = "Bois" };
            foreach (var (id, name, level) in new[] { (100, "Cible A", 10), (200, "Cible B", 20), (300, "Hors filtre", 10), (400, "Cible niveau exclu", 200) })
            {
                var item = new Equipment(id) { Name = name, Level = level, Type = EquipmentType.Amulet };
                // EF orders the included graph by its GUID key. Keep this fixture
                // deterministic so the delayed coefficient is the second item.
                typeof(Equipment).GetProperty(nameof(Equipment.Id))!.SetValue(item,
                    Guid.Parse($"{id:x8}-0000-0000-0000-000000000001"));
                item.Recipe.Add(new(item, resource, 2));
                item.Characteristics.Add(new(item, Characteristic.Strength, 10, 10));
                db.Equipments.Add(item);
            }
            db.Runes.Add(new Rune(10) { Name = "Rune Fo", Characteristic = Characteristic.Strength });
            db.MarketPriceObservations.AddRange(Price(1, 1, 10), Price(10, 1, 100, type: MarketObjectType.Rune),
                Price(100, 1, 100, type: MarketObjectType.Equipment));
            db.CoefficientObservations.Add(new() { DofusDbId = 100, ServerName = Server, CoefficientPercent = 100,
                Source = CoefficientSource.Manual, ObservedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Renderer.DisposeAsync();
            await Services.DisposeAsync();
            Database.Dispose();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpandedResultsUseSynchronizedStocksOnActualServer(bool hasRunePrices)
    {
        await using var h = new Harness(); await h.SeedAsync();
        if (!hasRunePrices)
        {
            using var db = h.Database.CreateContext();
            db.MarketPriceObservations.RemoveRange(db.MarketPriceObservations.Where(x => x.ObjectType == MarketObjectType.Rune));
            await db.SaveChangesAsync();
        }
        var clients = h.Services.GetRequiredService<CurrentDofusClientsState>();
        var key = new DofusClientWindowKey(111, 1111);
        clients.Update([new(new IntPtr(key.Handle), "Pagalom - Eniripsa - 3.7.12.12 - Release", "Dofus", 0, 0, 100, 100) { ProcessId = key.ProcessId }], key);
        var stocks = h.Services.GetRequiredService<LiveStockService>();
        void Observe(string opcode, byte[] body) => stocks.Observe(new("S→C", opcode, body,
            DateTime.UtcNow, new NetworkCaptureLease(Server, 1), 1, ConnectionId: 1, CandidateWindow: key, SyntheticWindowForReplay: true));
        Observe("irl", Message(Unsigned(4, 1),
            Bytes(2, Bytes(1, Message(Unsigned(3, 3), Unsigned(4, 100), Unsigned(5, 10)))),
            Bytes(2, Bytes(1, Message(Unsigned(3, 6), Unsigned(4, 1), Unsigned(5, 11))))));
        Observe("irp", Message(Unsigned(1, 1)));
        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.RenderComponentAsync<TestPage>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ServerName"] = Server })));
        var page = h.Activator.Page!;
        typeof(ServerPage).GetProperty("StockItemId")!.SetValue(page, 100L);
        ((SearchItemsModel)typeof(ServerPage).GetProperty("Model", Hidden)!.GetValue(page)!).SearchText = "Cible A";
        await h.Renderer.Dispatcher.InvokeAsync(() => Analyze(page));
        string html = WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains("data-bs-title=\"Inventaire : 3\"", html);
        Assert.Contains("6 disponibles", html);
        Assert.Contains("Inventaire : 6", html);
        Assert.Contains("Banque : 0", html);
        Assert.DoesNotContain("non synchronisé", html);
        Assert.DoesNotContain("Quantités du client ciblé", html);
        h.Remote.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarketUpdatesRefreshOnlyRelatedRowsIncludingMissingRunesAndCoefficientsWithoutRemoteRequests()
    {
        await using var h = new Harness(); await h.SeedAsync();
        using (var db = h.Database.CreateContext())
        {
            db.CoefficientObservations.Add(new() { DofusDbId = 200, ServerName = Server, CoefficientPercent = 100,
                Source = CoefficientSource.Manual, ObservedAtUtc = DateTime.UtcNow });
            db.MarketPriceObservations.Add(Price(200, 1, 120, type: MarketObjectType.Equipment));
            await db.SaveChangesAsync();
        }
        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.RenderComponentAsync<TestPage>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ServerName"] = Server })));
        await h.Renderer.Dispatcher.InvokeAsync(() => Analyze(h.Activator.Page!));
        var page = h.Activator.Page!;
        Dictionary<long, EquipmentProfitabilityResult> Calculations() => Get<Dictionary<long, EquipmentProfitabilityResult>>(page, "_baseCalculations");
        async Task Notify(MarketObjectType type, long id, string? server = null)
        {
            await h.Renderer.Dispatcher.InvokeAsync(() => h.Changes.Notify(type, id, server ?? Server, 1));
            await Get<Task>(page, "_marketRefreshTask").WaitAsync(TimeSpan.FromSeconds(15));
        }
        async Task PriceUpdate(long id, long price, MarketObjectType type)
        {
            using var db = h.Database.CreateContext();
            db.MarketPriceObservations.Add(Price(id, 1, price, type: type, at: DateTime.UtcNow)); await db.SaveChangesAsync();
        }
        var originalA = Calculations()[100]; var originalB = Calculations()[200];
        var idle = Get<Task>(page, "_marketRefreshTask");
        await Notify(MarketObjectType.Resource, 999); await Notify(MarketObjectType.Rune, 10, "OTHER_SERVER");
        Assert.Same(idle, Get<Task>(page, "_marketRefreshTask")); Assert.Same(originalA, Calculations()[100]);
        await PriceUpdate(100, 300, MarketObjectType.Equipment); await Notify(MarketObjectType.Equipment, 100);
        Assert.Equal(300, Calculations()[100].EquipmentCost!.Price); Assert.Same(originalB, Calculations()[200]);
        Assert.Equal(originalA.Scenarios[0].PurchaseBenefit - 200, Calculations()[100].Scenarios[0].PurchaseBenefit);
        await PriceUpdate(1, 40, MarketObjectType.Resource); await Notify(MarketObjectType.Resource, 1);
        Assert.Equal(80, Calculations()[100].CraftCost.TotalCost); Assert.Equal(80, Calculations()[200].CraftCost.TotalCost);
        var beforeRune = Calculations()[100].Scenarios[0].AdjustedRuneValue;
        await PriceUpdate(10, 200, MarketObjectType.Rune); await Notify(MarketObjectType.Rune, 10);
        Assert.Equal(beforeRune * 2, Calculations()[100].Scenarios[0].AdjustedRuneValue, 5);
        using (var db = h.Database.CreateContext())
        {
            db.MarketPriceObservations.RemoveRange(db.MarketPriceObservations.Where(x => x.DofusDbId == 10));
            await db.SaveChangesAsync();
        }
        await Notify(MarketObjectType.Rune, 10); Assert.Empty(Calculations()[100].Scenarios);
        Assert.Contains("Données absentes", WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        await PriceUpdate(10, 100, MarketObjectType.Rune); await Notify(MarketObjectType.Rune, 10);
        Assert.NotEmpty(Calculations()[100].Scenarios);
        using (var db = h.Database.CreateContext())
        {
            await new CoefficientService(db, new TestDataPriorityProvider()).AddObservationAsync(100, Server, 200, CoefficientSource.Manual);
        }
        await Notify(MarketObjectType.Equipment, 100); Assert.Equal(200, Calculations()[100].Coefficient!.CoefficientPercent);
        // Simulate a price changed while the activity page is away: catch up from the journal on return.
        var handler = (EventHandler<MarketDataChangedEventArgs>)Delegate.CreateDelegate(typeof(EventHandler<MarketDataChangedEventArgs>), page,
            typeof(ServerPage).GetMethod("OnCrushingMarketChanged", Hidden)!);
        h.Changes.Changed -= handler;
        await PriceUpdate(100, 450, MarketObjectType.Equipment);
        h.Changes.Notify(MarketObjectType.Equipment, 100, Server, 1);
        await h.Renderer.Dispatcher.InvokeAsync(() => typeof(ServerPage).GetMethod("CatchUpMarketChanges", Hidden)!.Invoke(page, null));
        await Get<Task>(page, "_marketRefreshTask").WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(450, Calculations()[100].EquipmentCost!.Price);
        h.Changes.Changed += handler;
        using (var db = h.Database.CreateContext())
        {
            db.CoefficientObservations.RemoveRange(db.CoefficientObservations.Where(x => x.DofusDbId == 100));
            await db.SaveChangesAsync();
        }
        await Notify(MarketObjectType.Equipment, 100);
        Assert.Null(Calculations()[100].Coefficient);
        Assert.Contains("Coefficient de brisage indisponible", Calculations()[100].MissingData);
        h.Remote.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OneSubmitFiltersAndCalculatesProgressivelyWithoutBlockingOrDuplicatingTheScan()
    {
        await using var h = new Harness();
        await h.SeedAsync();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Remote.Setup(x => x.GetItemAsync(200, It.IsAny<CancellationToken>())).Returns(async () =>
        {
            requested.TrySetResult();
            await release.Task;
            throw new HttpRequestException("Missing remote coefficient fixture");
        });
        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.RenderComponentAsync<TestPage>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ServerName"] = Server })));
        await h.Renderer.Dispatcher.InvokeAsync(() => {
            typeof(global::BestCrush.Components.Pure.SearchItemsForm).GetMethod("ToggleAdvanced", Hidden)!.Invoke(h.Activator.Form, null);
            typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(h.Activator.Form, null);
        });
        Assert.Contains("Runes / caractéristiques", WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        Task? scan = null;
        await h.Renderer.Dispatcher.InvokeAsync((Action)(() => scan = Analyze(h.Activator.Page!)));
        try
        {
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var html = WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            Assert.Matches(@"Analyse…\s*1\s*/\s*2", html);
            Assert.DoesNotContain("Runes / caractéristiques", html);
            Assert.Contains("Cible A", html);
            Assert.DoesNotContain("Aucun équipement", html);
            Assert.DoesNotContain("Cible B", html);
            Assert.DoesNotContain("Hors filtre", html);
            Assert.Matches(@"type=""submit""[^>]*disabled", html);
            Assert.DoesNotContain("Rechercher les objets", html);
            Assert.DoesNotContain("Trouver le Best Crush", html);
            Assert.Contains("Stock personnel", html);
            Assert.Contains("value=\"Cible\"", html);
            Assert.DoesNotContain("value=\"Model.SearchText\"", html);
            // A second submission during the outstanding remote lookup is ignored.
            await h.Renderer.Dispatcher.InvokeAsync(() => Analyze(h.Activator.Page!));
            Assert.Equal(1, Get<int>(h.Activator.Page!, "_processedItems"));
        }
        finally
        {
            release.TrySetResult();
            if (scan is not null) await scan.WaitAsync(TimeSpan.FromSeconds(15));
        }
        string done = WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains("Analyser", done);
        Assert.Contains("Cible A", done);
        Assert.Contains("Cible B", done);
        Assert.Equal(2, Get<int>(h.Activator.Page!, "_processedItems"));
        Assert.False(Get<bool>(h.Activator.Page!, "_searching"));
        // Typing narrows the whole list immediately, without selecting an item or rescanning.
        async Task TypeSearch(string text) => await h.Renderer.Dispatcher.InvokeAsync(() =>
            (Task)typeof(global::BestCrush.Components.Pure.SearchItemsForm).GetMethod("ChangeSearchAsync", Hidden)!.Invoke(h.Activator.Form, [text])!);
        await TypeSearch("Cible A");
        string narrowed = WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains("Cible A", narrowed); Assert.DoesNotContain("Cible B", narrowed);
        Assert.Equal(2, Get<int>(h.Activator.Page!, "_processedItems"));
        await TypeSearch("Cible");
        Assert.Contains("Cible B", WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        h.Remote.Verify(x => x.GetItemAsync(200, It.IsAny<CancellationToken>()), Times.Once);
        h.Remote.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LosingDetectedProfessionLevelsClearsTheFilteredResultsAndStockProjectionCache()
    {
        await using var h = new Harness(); await h.SeedAsync();
        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.RenderComponentAsync<TestPage>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ServerName"] = Server })));
        var model = (SearchItemsModel)typeof(ServerPage).GetProperty("Model", Hidden)!.GetValue(h.Activator.Page)!;
        model.SearchText = "Cible A";
        await h.Renderer.Dispatcher.InvokeAsync(() => Analyze(h.Activator.Page!));
        Assert.NotEmpty((System.Collections.IDictionary)typeof(ServerPage).GetField("_baseCalculations", Hidden)!.GetValue(h.Activator.Page)!);
        await h.Renderer.Dispatcher.InvokeAsync(() => {
            // Simulate an enabled filter whose live owner has just disappeared.
            model.RespectJobLevels = true;
            typeof(ServerPage).GetField("_professionInputsKey", Hidden)!.SetValue(h.Activator.Page, "previous-live-session");
            typeof(ServerPage).GetMethod("ProfessionInputsChanged", Hidden)!.Invoke(h.Activator.Page, null);
        });
        Assert.False(model.RespectJobLevels);
        Assert.Null(typeof(ServerPage).GetField("_allItems", Hidden)!.GetValue(h.Activator.Page));
        Assert.Null(typeof(ServerPage).GetField("_rankedItems", Hidden)!.GetValue(h.Activator.Page));
        Assert.Empty((System.Collections.IDictionary)typeof(ServerPage).GetField("_baseCalculations", Hidden)!.GetValue(h.Activator.Page)!);
        Assert.Contains("Les niveaux du personnage ont changé", WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        h.Remote.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmptyFilterEndsAnalysisWithAnActionableMessageAndNoRemoteLookup()
    {
        await using var h = new Harness();
        await h.SeedAsync();
        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.RenderComponentAsync<TestPage>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ServerName"] = Server })));
        typeof(ServerPage).GetProperty("Model", Hidden)!.SetValue(h.Activator.Page, new SearchItemsModel { SearchText = "Inexistant" });
        await h.Renderer.Dispatcher.InvokeAsync(() => Analyze(h.Activator.Page!));
        string html = WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Single(Regex.Matches(html, "Aucun équipement"));
        Assert.Contains("Aucun équipement ne correspond à ces filtres", html);
        Assert.Contains("Analyser", html);
        Assert.DoesNotMatch(@"type=""submit""[^>]*disabled", html);
        h.Remote.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChosenProfessionLimitsTheActualScanBeforeCoefficientLookups()
    {
        await using var h = new Harness(); await h.SeedAsync();
        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.RenderComponentAsync<TestPage>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["ServerName"] = Server })));
        await h.Renderer.Dispatcher.InvokeAsync(() => (Task)typeof(ServerPage).GetMethod("LoadCrushingJobsAsync", Hidden)!.Invoke(h.Activator.Page, null)!);
        var model = (SearchItemsModel)typeof(ServerPage).GetProperty("Model", Hidden)!.GetValue(h.Activator.Page)!;
        model.CraftJobId = 16;
        await h.Renderer.Dispatcher.InvokeAsync(() => Analyze(h.Activator.Page!));
        Assert.Equal(1, Get<int>(h.Activator.Page!, "_processedItems"));
        string html = WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains("Cible A", html); Assert.DoesNotContain("Cible B", html);
        Assert.DoesNotContain("Réessayer", html);
        model.CraftJobId = 999;
        await h.Renderer.Dispatcher.InvokeAsync(() => typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(h.Activator.Page, null));
        html = await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Matches(@"type=""submit""[^>]*disabled", html);
        model.CraftJobId = null;
        await h.Renderer.Dispatcher.InvokeAsync(() => (Task)typeof(global::BestCrush.Components.Pure.SearchItemsForm).GetMethod("JobChanged", Hidden)!.Invoke(h.Activator.Form, null)!);
        html = await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.DoesNotMatch(@"type=""submit""[^>]*disabled", html);
        h.Remote.VerifyNoOtherCalls();
    }
}
