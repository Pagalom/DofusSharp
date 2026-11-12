using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.BestCrush.Utils;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush;

public sealed class CraftRecipeFiltersTest
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly DofusClientWindowKey A = new(111, 1111), B = new(222, 2222);
    private static string[] Names(string html) => Regex.Matches(html, @"<strong data-result-name[^>]*>([^<]+)</strong>")
        .Select(x => System.Net.WebUtility.HtmlDecode(x.Groups[1].Value)).ToArray();

    private sealed class Harness : IAsyncDisposable
    {
        public readonly string Folder = Path.Combine(Path.GetTempPath(), "craft-recipe-filters-" + Guid.NewGuid());
        public readonly TestDatabase Database = new();
        public readonly CraftPerformanceProbeTest.Source Provider = new();
        public readonly CraftPerformanceProbeTest.Capture Capture = new();
        public readonly CurrentDofusClientsState Clients = new();
        public readonly ServerViewFilterState Server = new();
        public readonly CurrentWindowProfessionsState Professions;
        public readonly WindowServerAttributionState Attribution = new();
        public readonly CurrentServerState NetworkServer;
        public readonly NetworkCaptureLease LeaseA, LeaseB;
        public readonly CraftCatalogFixture Http;
        public readonly ServiceProvider Services;
        public readonly HtmlRenderer Renderer;
        public StockOpportunities View => Capture.View!;
        public Harness(bool offline = false, bool hold = false)
        {
            Attribution.Reset(3);
            NetworkServer = new(Attribution, automaticRoutingEnabled: true);
            Professions = new(NetworkServer);
            LeaseA = Connect(A, 21, 101);
            LeaseB = Connect(B, 31, 202);
            DofusWindowInfo Window(DofusClientWindowKey key) => new(new IntPtr(key.Handle),
                "Pagalom - Eniripsa - 3.7.12.12 - Release", "Dofus", 0, 0, 100, 100) { ProcessId = key.ProcessId };
            Clients.Update([Window(A), Window(B)], A);
            Server.FollowWindow(A, "Tylezia");
            var stocks = new LiveStockService(new LiveStockRepository(Path.Combine(Folder, "stock.db")), Clients);
            byte[] inventory = Message(Unsigned(4, 1), Bytes(2, Bytes(1,
                Message(Unsigned(3, 1), Unsigned(4, 1), Unsigned(5, 10)))));
            foreach (var key in new[] { A, B })
                stocks.Observe(new("S→C", "irl", inventory, DateTime.UtcNow,
                    new NetworkCaptureLease("Tylezia", 1), 1, ConnectionId: key == A ? 21 : 31,
                    CandidateWindow: key, SyntheticWindowForReplay: true));
            Provider.Rows = Provider.Rows.Select(row =>
            {
                int id = (int)row.Plan.Product.DofusDbId;
                var product = (Equipment)row.Plan.Product;
                product.Type = id <= 120 ? (id % 2 == 0 ? EquipmentType.Ring : EquipmentType.Amulet) : EquipmentType.Hat;
                product.Level = id;
                return row;
            }).ToArray();
            Http = new() { Offline = offline, Recipes = Enumerable.Range(1, 220).Where(i => i != 7)
                .Select(i => new CraftCatalogFixture.Recipe(i, i <= 120 ? 16 : 27, i == 9 ? null : i)).ToArray(),
                HoldRecipes = hold ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null };
            var db = Database.CreateContext();
            var priority = new TestDataPriorityProvider();
            var prices = new MarketPriceService(db, priority);
            var scanner = new StockOpportunityService(new ItemsService(db, null!, null!, null!, null!),
                new EquipmentProfitabilityService(null!, null!, new CoefficientService(db, priority), prices, null!, null!),
                new StockCraftPlanner(prices), [Provider]);
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton(scanner); services.AddSingleton(stocks); services.AddSingleton(Clients);
            services.AddSingleton(Server); services.AddSingleton(new MarketDataChangeNotifier());
            services.AddSingleton<IComponentActivator>(Capture);
            CraftCatalogFixture.Register(services, Folder, Professions, Http);
            Services = services.BuildServiceProvider();
            Renderer = new HtmlRenderer(Services, Services.GetRequiredService<ILoggerFactory>());
        }

        private NetworkCaptureLease Connect(DofusClientWindowKey window, long connection, ulong character)
        {
            var row = Message(Unsigned(3, character), Bytes(4, Message(Unsigned(3, 38), Bytes(6, Encoding.UTF8.GetBytes("Pagalom")))));
            Attribution.ObserveSelection(window, 3, connection - 1, 293, DateTime.UtcNow);
            Attribution.ObserveGame(new("S→C", "ksc", Bytes(2, row), DateTime.UtcNow, null, 3,
                ConnectionId: connection, CandidateWindow: window, SyntheticWindowForReplay: true), true);
            Attribution.ObserveGame(new("S→C", "ksb", Bytes(1, Bytes(1, row)), DateTime.UtcNow, null, 3,
                ConnectionId: connection, CandidateWindow: window, SyntheticWindowForReplay: true), true);
            return NetworkServer.GetGameCaptureLease(window, 3, connection)!.Value;
        }

        public void Levels(DofusClientWindowKey key, int jeweller)
        {
            var body = Message(ProfessionPublicJobIdReference.ReferenceTwenty.Select(id => Bytes(1, Message(
                Unsigned(1, id == 16 ? (ulong)jeweller : 1UL), Unsigned(2, 0), Unsigned(5, id)))).ToArray());
            var lease = key == A ? LeaseA : LeaseB;
            Assert.True(Professions.Observe(new("S→C", "ipz", body, DateTime.UtcNow, lease, 3,
                ConnectionId: lease.ConnectionId, CandidateWindow: key, SyntheticWindowForReplay: true)));
        }

        public async Task Change(params (string Field, object Value)[] changes) => await Renderer.Dispatcher.InvokeAsync(() =>
        {
            foreach (var (field, value) in changes) typeof(StockOpportunities).GetField(field, Hidden)!.SetValue(View, value);
            typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(View, null);
        });

        public async ValueTask DisposeAsync()
        {
            Http.HoldRecipes?.TrySetResult();
            await Renderer.DisposeAsync(); await Services.DisposeAsync(); Database.Dispose(); Http.Dispose();
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        }
    }

    [Fact]
    public async Task JobsAndMultipleTypesFilterAllRecipesBeforeTop100WithoutRescanning()
    {
        await using var h = new Harness();
        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.RenderComponentAsync<StockOpportunities>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["OriginFilter"] = "craft" })));
        async Task<string> Html() => System.Net.WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        string html = await Html();
        Assert.Equal(100, Names(html).Length); Assert.Contains("Recette220", Names(html));
        await h.Renderer.Dispatcher.InvokeAsync(() => (Task)typeof(ServiceFilters).GetMethod("ToggleAsync", Hidden)!.Invoke(h.Capture.Components.OfType<ServiceFilters>().Single(), null)!);
        await h.Renderer.Dispatcher.InvokeAsync(() => typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(h.Capture.Components.OfType<ServiceFilters>().Single(), null));
        html = await Html();
        Assert.Contains("Métier de fabrication", html); Assert.DoesNotContain("Tous les métiers", html);
        Assert.Contains("visual-filter-choice", html);
        int requests = h.Http.Calls;
        var jobPicker = h.Capture.Components.OfType<JobFilter>().Single();
        await h.Renderer.Dispatcher.InvokeAsync(() => jobPicker.SelectedValueChanged.InvokeAsync("Bijoutier")); html = await Html();
        // 1..120 were all outside the initial global top 100; 7 has no recipe metadata.
        Assert.Equal(Enumerable.Range(1, 120).Where(i => i != 7).Reverse().Take(100).Select(i => $"Recette{i:D3}"), Names(html));
        Assert.Contains("Bijoutier ·", html); Assert.DoesNotContain("Recette220", Names(html));
        VisualFilterChoice Choice(EquipmentType type) => h.Capture.Components.OfType<VisualFilterChoice>()
            .Last(choice => choice.Label == type.ToDisplayName());
        await h.Renderer.Dispatcher.InvokeAsync(() => Choice(EquipmentType.Ring).Pressed.InvokeAsync());
        Assert.Equal(Enumerable.Range(1, 120).Where(i => i % 2 == 0).Reverse().Select(i => $"Recette{i:D3}"), Names(await Html()));
        await h.Renderer.Dispatcher.InvokeAsync(() => Choice(EquipmentType.Amulet).Pressed.InvokeAsync());
        Assert.Equal(100, Names(await Html()).Length);
        // Changing profession resets incompatible type selections.
        await h.Renderer.Dispatcher.InvokeAsync(() => jobPicker.SelectedValueChanged.InvokeAsync("Tailleur"));
        await h.Change(); Assert.Contains("Recette220", Names(await Html()));
        Assert.Equal(1, h.Provider.Calls); Assert.Equal(requests, h.Http.Calls);
    }

    [Fact]
    public async Task OtherTypesAreCollapsedAndKeepTheirSelectionWhenHidden()
    {
        await using var h = new Harness();
        var template = h.Provider.Rows[0];
        var alloy = new CraftedProduct(749, "Bakélélite", 80, null, MarketObjectType.Resource,
            40, "Alliage", "Mineur", 80);
        var questItem = new CraftedProduct(50000, "Objet de quête", 10, null, MarketObjectType.Resource,
            999, "Ressource de quête", "Base", 10);
        h.Provider.Rows = [template, template with { Plan = template.Plan with { Product = alloy } },
            template with { Plan = template.Plan with { Product = questItem } }];

        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.RenderComponentAsync<StockOpportunities>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["OriginFilter"] = "craft" })));
        async Task<string> Html() => System.Net.WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        await h.Change(("_filtersOpen", true));

        var collapsed = await Html();
        Assert.Contains(">Amulette</span>", collapsed);
        Assert.Contains("Voir plus…", collapsed);
        Assert.DoesNotContain(">Alliage</span>", collapsed);
        Assert.DoesNotContain(">Ressource de quête</span>", collapsed);
        int scans = h.Provider.Calls;

        await h.Change(("_showOtherCraftTypes", true));
        var expanded = await Html();
        Assert.Contains("Voir moins", expanded);
        Assert.Contains(">Alliage</span>", expanded);
        Assert.Contains(">Ressource de quête</span>", expanded);
        var other = Assert.Single(h.Capture.Components.OfType<VisualFilterChoice>(),
            choice => choice.Label == "Alliage");
        await h.Renderer.Dispatcher.InvokeAsync(() => other.Pressed.InvokeAsync());
        await h.Change(("_showOtherCraftTypes", false));

        Assert.Equal(new[] { "Bakélélite" }, Names(await Html()));
        Assert.Equal(scans, h.Provider.Calls);
    }

    [Fact]
    public async Task LevelFilterUsesSelectedWindowsVerifiedLevelsAndUpdatesWithoutRecalculation()
    {
        await using var h = new Harness(); h.Levels(A, 20); h.Levels(B, 100);
        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.RenderComponentAsync<StockOpportunities>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["OriginFilter"] = "craft" })));
        async Task<string[]> Rows() => Names(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        await h.Change(("_craftJob", "Bijoutier"), ("_respectCraftJobs", true));
        Assert.Equal(Enumerable.Range(1, 20).Where(i => i is not 7 and not 9).Reverse().Select(i => $"Recette{i:D3}"), await Rows());
        int calls = h.Provider.Calls, http = h.Http.Calls;
        await h.Renderer.Dispatcher.InvokeAsync(() => h.Levels(A, 30));
        Assert.Equal(28, (await Rows()).Length); Assert.Contains("Recette030", await Rows());
        await h.Renderer.Dispatcher.InvokeAsync(() => h.Clients.SelectWindow(B));
        await Task.Delay(750);
        Assert.Equal(98, (await Rows()).Length); Assert.Contains("Recette100", await Rows());
        Assert.DoesNotContain("Recette101", await Rows());
        await h.Change(("_respectCraftJobs", false)); Assert.Equal(100, (await Rows()).Length);
        Assert.Equal(calls + 1, h.Provider.Calls); Assert.Equal(http, h.Http.Calls);
    }

    [Fact]
    public async Task MissingLevelsAndRecipeMetadataAreExplicitAndDoNotInventCraftAccess()
    {
        await using var h = new Harness(offline: true);
        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.RenderComponentAsync<StockOpportunities>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["OriginFilter"] = "craft" })));
        async Task<string> Html() => System.Net.WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Equal(100, Names(await Html()).Length);
        Assert.Contains("Le filtre par type reste disponible", await Html());
        await h.Change(("_craftTypes", new[] { EquipmentType.Ring.ToDofusDbItemTypeId() })); Assert.Equal(60, Names(await Html()).Length);
        await h.Change(("_respectCraftJobs", true)); Assert.Empty(Names(await Html()));
        Assert.Contains("Niveaux de métier non synchronisés", await Html());
        h.Http.Offline = false;
        await h.Renderer.Dispatcher.InvokeAsync(() => (Task)typeof(StockOpportunities).GetMethod("RetryCraftMetadataAsync", Hidden)!.Invoke(h.View, null)!);
        Assert.DoesNotContain("Le filtre par type reste disponible", await Html());
        await h.Renderer.Dispatcher.InvokeAsync(() => h.Levels(A, 20));
        Assert.Equal(10, Names(await Html()).Length); Assert.Equal(1, h.Provider.Calls);
    }

    [Fact]
    public async Task RecipeMetadataLoadsInBackgroundWhileTypeFiltersRemainResponsive()
    {
        await using var h = new Harness(hold: true);
        var root = await h.Renderer.Dispatcher.InvokeAsync(() => h.Renderer.BeginRenderingComponent<StockOpportunities>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["OriginFilter"] = "craft" })));
        try
        {
            await h.Http.RecipeRequest.Task.WaitAsync(TimeSpan.FromSeconds(10));
            async Task<string> Html() => System.Net.WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            Assert.Contains("Chargement des métiers", await Html());
            Assert.Equal(100, Names(await Html()).Length);
            await h.Change(("_craftTypes", new[] { EquipmentType.Ring.ToDofusDbItemTypeId() }));
            Assert.Equal(60, Names(await Html()).Length);
            Assert.Equal(1, h.Provider.Calls);
        }
        finally { h.Http.HoldRecipes!.TrySetResult(); await root.QuiescenceTask.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Contains("Bijoutier", System.Net.WebUtility.HtmlDecode(await h.Renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        Assert.Equal(1, h.Provider.Calls);
    }

    [Fact]
    public async Task RecipeCatalogIsCachedInMemoryAndOnDiskIncludingMissingRecipes()
    {
        string folder = Path.Combine(Path.GetTempPath(), "recipe-cache-" + Guid.NewGuid());
        try
        {
            using var http = new CraftCatalogFixture { Recipes = [new(100, 16, 50)] };
            var catalog = new ActivityCatalogService(DofusDbClient.Production(), http, folder);
            var first = await catalog.GetRecipeProfessionsAsync([101, 100], default);
            Assert.Equal(new RecipeProfession(100, "Bijoutier", 50), Assert.Single(first).Value);
            int calls = http.Calls;
            await catalog.GetRecipeProfessionsAsync([100, 101, 100], default); Assert.Equal(calls, http.Calls);
            http.Offline = true;
            var reopened = new ActivityCatalogService(DofusDbClient.Production(), http, folder);
            Assert.Equal(first[100], (await reopened.GetRecipeProfessionsAsync([100, 101], default))[100]);
            Assert.Equal(calls, http.Calls);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
