using System.Reflection;
using System.Text.RegularExpressions;
using BestCrush.Components.Pages;
using BestCrush.Components.Pure;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class DataWorkspacePerformanceTest
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Gate
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class GatedFactory(TestDatabase database) : IDbContextFactory<BestCrushDbContext>
    {
        public Gate? Next;
        public BestCrushDbContext CreateDbContext()
        {
            var gate = Interlocked.Exchange(ref Next, null);
            if (gate is not null)
            {
                gate.Started.TrySetResult();
                // Simulates SQLite doing a synchronous read despite its async API.
                gate.Release.Task.GetAwaiter().GetResult();
            }
            return database.CreateDbContext();
        }
    }
    private sealed class Capture : IComponentActivator
    {
        public ResourcePrices Resources = null!;
        public object Pager = null!;
        public Probe Body = null!;
        public ActivityHost Host = null!;
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is ResourcePrices resources) Resources = resources;
            if (component is Probe body) Body = body;
            if (component is ActivityHost host) Host = host;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PagedDataTable<>)) Pager = component;
            return component;
        }
    }

    [Fact]
    public async Task ResourceReadsLeaveRendererResponsiveBoundRowsAndRejectAnObsoleteServer()
    {
        using var database = new TestDatabase();
        await using (var db = database.CreateContext())
        {
            db.Resources.AddRange(Enumerable.Range(0, 1201).Select(id => new Resource(id + 1) { Name = $"Resource{id:0000}" }));
            foreach (var server in new[] { "A", "B" })
                db.MarketPriceObservations.Add(new MarketPriceObservation
                {
                    ObjectType = MarketObjectType.Resource, DofusDbId = 1200, Quantity = 1,
                    Price = server == "A" ? 111 : 222, ServerName = server,
                    Source = MarketPriceSource.Manual, ObservedAtUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }
        await using (var db = database.CreateContext())
        {
            db.MarketPriceObservations.Add(new MarketPriceObservation
            {
                ObjectType = MarketObjectType.Resource, DofusDbId = 1200, Quantity = 1,
                Price = 333, ServerName = "B", Source = MarketPriceSource.InGameAutomatic,
                ObservedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var factory = new GatedFactory(database);
        var capture = new Capture();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(capture);
        services.AddSingleton<IDbContextFactory<BestCrushDbContext>>(factory);
        services.AddScoped(_ => factory.CreateDbContext());
        services.AddScoped(sp => new ItemsService(sp.GetRequiredService<BestCrushDbContext>(), null!, null!, null!, null!));
        services.AddSingleton<IDataPriorityProvider, TestDataPriorityProvider>();
        services.AddScoped<MarketPriceService>();
        services.AddSingleton<MarketDataChangeNotifier>();
        services.AddSingleton<ServerViewFilterState>();
        services.AddSingleton<CurrentDofusClientsState>();
        string folder = Path.Combine(Path.GetTempPath(), "bestcrush-data-loading-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        services.AddSingleton(new LiveStockRepository(Path.Combine(folder, "stocks.db")));
        services.AddSingleton<LiveStockService>();
        var gate = new Gate();
        try
        {
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ResourcePrices>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["ServerName"] = "A" })));
            string html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Equal(51, Regex.Matches(html, "<tr(?:[ >])").Count); // Header + 50 rows, not 1,201 controls.
            Assert.Contains("Resource0000", html);
            Assert.DoesNotContain("Resource0050", html);
            await renderer.Dispatcher.InvokeAsync(() =>
            {
                capture.Pager.GetType().GetMethod("Next", Hidden)!.Invoke(capture.Pager, null);
                Render(capture.Pager);
            });
            html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Contains("Resource0050", html);
            Assert.DoesNotContain("Resource0000", html);

            factory.Next = gate;
            Task refresh = Task.CompletedTask;
            await renderer.Dispatcher.InvokeAsync((Action)(() => refresh = Invoke(capture.Resources, "RefreshAsync")));
            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(refresh.IsCompleted);
            html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("spinner-border", html);
            // The user can still filter the cached table while SQLite is blocked.
            await renderer.Dispatcher.InvokeAsync(() =>
            {
                typeof(ResourcePrices).GetField("_searchText", Hidden)!.SetValue(capture.Resources, "Resource1199");
                Render(capture.Resources);
            });
            html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Contains("Resource1199", html);
            Assert.DoesNotContain("Resource0050", html);
            Assert.Equal(2, Regex.Matches(html, "<tr(?:[ >])").Count);

            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                SetParameter(capture.Resources, "IsActive", false);
                await Invoke(capture.Resources, "OnParametersSetAsync");
                SetParameter(capture.Resources, "ServerName", "B");
                SetParameter(capture.Resources, "IsActive", true);
                await Invoke(capture.Resources, "OnParametersSetAsync");
            });
            html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Contains("value=\"222\"", html);
            Assert.DoesNotContain("value=\"111\"", html);
            gate.Release.TrySetResult();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Contains("value=\"222\"", html);
            Assert.DoesNotContain("spinner-border", html);
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                provider.GetRequiredService<IDataPriorityProvider>().Priority = DataPriority.InGameAutomatic;
                SetParameter(capture.Resources, "Priority", DataPriority.InGameAutomatic);
                await Invoke(capture.Resources, "OnParametersSetAsync");
            });
            html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
            Assert.Contains("value=\"333\"", html);
            Assert.DoesNotContain("value=\"222\"", html);

        }
        finally
        {
            gate.Release.TrySetResult();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task ValueCatalogPreservesCoverageWithoutMaterializingRecipesAndCharacteristics()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        var resource = new Resource(1) { Name = "Resource" };
        var craftable = new Equipment(10) { Name = "Craftable", Level = 25, Type = EquipmentType.Amulet };
        craftable.Recipe.Add(new(craftable, resource, 5));
        craftable.Characteristics.Add(new(craftable, Characteristic.Strength, 1, 10));
        db.AddRange(craftable, new Equipment(20) { Name = "Without recipe" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new ItemsService(db, null!, null!, null!, null!);
        var light = Assert.Single(await service.GetEquipmentValueCatalogAsync());
        Assert.Equal(10, light.DofusDbId);
        Assert.Equal(25, light.Level);
        Assert.Equal(EquipmentType.Amulet, light.Type);
        Assert.Empty(light.Recipe);
        Assert.Empty(light.Characteristics);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(new long[] { 10, 20 }, (await service.GetEquipmentValueCatalogAsync(craftableOnly: false)).Select(item => item.DofusDbId));
    }

    [Fact]
    public async Task FailedBackgroundReadCanBeRetriedAndDisposalDiscardsLateCompletion()
    {
        using var load = new BackgroundDataLoad<int>();
        Assert.True(await load.RunAsync(_ => Task.FromResult(10)));
        Assert.False(await load.RunAsync(_ => throw new IOException("read failed")));
        Assert.NotNull(load.Error);
        Assert.Equal(10, load.Value);
        Assert.True(await load.RunAsync(_ => Task.FromResult(20)));
        Assert.Null(load.Error);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = load.RunAsync(_ => { started.TrySetResult(); return release.Task; });
        await started.Task;
        load.Dispose();
        release.SetResult(30);
        Assert.False(await pending);
        Assert.Equal(20, load.Value);
    }

    public sealed class Probe : ComponentBase
    {
        [Parameter] public string? Label { get; set; }
        public int Renders;
        protected override void BuildRenderTree(RenderTreeBuilder builder) { Renders++; builder.AddContent(0, Label); }
    }
    public sealed class ActivityHost : ComponentBase
    {
        public string Route = "activity-A";
        public int OtherState;
        private RenderFragment _body = null!;
        protected override void OnInitialized() => _body = builder =>
        {
            builder.OpenComponent<Probe>(0); builder.AddAttribute(1, "Label", Route); builder.CloseComponent();
        };
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<PreservedActivity>(0); builder.AddAttribute(1, "Route", Route);
            builder.AddAttribute(2, "ChildContent", _body); builder.CloseComponent(); builder.AddContent(3, OtherState);
        }
    }
    [Fact]
    public async Task ShellChangesDoNotRerenderTheActivityButRealNavigationStillDoes()
    {
        var capture = new Capture();
        var services = new ServiceCollection().AddLogging().AddSingleton<IComponentActivator>(capture);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ActivityHost>());
        int before = capture.Body.Renders;
        await renderer.Dispatcher.InvokeAsync(() => { capture.Host.OtherState++; Render(capture.Host); });
        Assert.Equal(before, capture.Body.Renders);
        await renderer.Dispatcher.InvokeAsync(() => { capture.Host.Route = "activity-B"; Render(capture.Host); });
        Assert.Contains("activity-B", await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.True(capture.Body.Renders > before);
    }

    private static Task Invoke(object component, string method) =>
        (Task)component.GetType().GetMethod(method, Hidden)!.Invoke(component, null)!;
    private static void Render(object component) => typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(component, null);
    private static void SetParameter(object component, string name, object value) => component.GetType().GetProperty(name)!.SetValue(component, value);
}
