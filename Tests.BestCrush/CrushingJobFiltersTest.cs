using System.Reflection;
using System.Text.Json;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Models;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.BestCrush.Utils;
using ServerPage = BestCrush.Components.Pages.Server;

namespace Tests.BestCrush;

public sealed class CrushingJobFiltersTest
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Equipment[] Items = [
        new(100) { Name = "Amulette", Type = EquipmentType.Amulet, Level = 10 },
        new(200) { Name = "Anneau", Type = EquipmentType.Ring, Level = 20 },
        new(300) { Name = "Chapeau", Type = EquipmentType.Hat, Level = 30 },
        new(400) { Name = "Cape", Type = EquipmentType.Cloak, Level = 40 },
        new(500) { Name = "Bouclier", Type = EquipmentType.Shield, Level = 50 },
        new(600) { Name = "Trophée", Type = EquipmentType.Trophy, Level = 60 },
        new(700) { Name = "Outil A", Type = EquipmentType.Tool, Level = 70 },
        new(800) { Name = "Outil B", Type = EquipmentType.Tool, Level = 80 }];
    private static readonly CraftingJob[] Jobs = [new(16, "Bijoutier", [100, 200]), new(27, "Tailleur", [300, 400]),
        new(60, "Façonneur", [500, 600]), new(65, "Bricoleur", [700])];
    private static readonly EquipmentJobFilterCatalog Catalog = new(Jobs, Items);
    private static readonly Rune[] Runes = [new(1) { Name = "Rune Sa", Characteristic = Characteristic.Wisdom },
        new(2) { Name = "Rune Pa Sa", Characteristic = Characteristic.Wisdom },
        new(3) { Name = "Rune de chasse", Characteristic = Characteristic.Wisdom }];

    public sealed class Host : ComponentBase
    {
        public SearchItemsModel Model = new();
        public bool Disabled;
        public int? PlayerLevel;
        public IReadOnlyDictionary<string, int>? ProfessionLevels;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "form");
            builder.OpenComponent<SearchItemsForm>(1);
            builder.AddAttribute(2, "Model", Model);
            builder.AddAttribute(3, "JobCatalog", Catalog);
            builder.AddAttribute(4, "Disabled", Disabled);
            builder.AddAttribute(5, "Actions", (RenderFragment)(b =>
            {
                b.OpenElement(0, "button"); b.AddAttribute(1, "type", "submit");
                b.AddContent(2, "Analyser"); b.CloseElement();
            }));
            builder.AddAttribute(6, "AvailableRunes", Runes);
            builder.AddAttribute(7, "PlayerLevel", PlayerLevel);
            builder.AddAttribute(8, "ProfessionLevels", ProfessionLevels);
            builder.CloseComponent(); builder.CloseElement();
        }
        public void Render() => StateHasChanged();
    }

    private sealed class Capture : IComponentActivator
    {
        public Host Host = null!;
        public SearchItemsForm Form = null!;
        public EquipmentTypeFilter Types = null!;
        public JobFilter Job = null!;
        public List<VisualFilterChoice> Choices = [];
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is Host host) Host = host;
            if (component is SearchItemsForm form) Form = form;
            if (component is EquipmentTypeFilter types) Types = types;
            if (component is JobFilter job) Job = job;
            if (component is VisualFilterChoice choice) Choices.Add(choice);
            return component;
        }
    }

    private static long[] Filter(SearchItemsModel model)
    {
        var page = Activator.CreateInstance<ServerPage>();
        typeof(ServerPage).GetProperty("Model", Hidden)!.SetValue(page, model);
        typeof(ServerPage).GetField("_crushingJobCatalog", Hidden)!.SetValue(page, Catalog);
        return ((IEnumerable<Equipment>)typeof(ServerPage).GetMethod("FilterItems", Hidden)!.Invoke(page, [Items])!)
            .Select(x => x.DofusDbId).ToArray();
    }

    [Fact]
    public async Task JobSelectionNarrowsTypesAndAllTypesRemainWithinTheChosenProfession()
    {
        var capture = new Capture(); var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IComponentActivator>(capture);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Host>());
        // The equipment selectors are now inside the shared filter disclosure.
        await renderer.Dispatcher.InvokeAsync(() => {
            typeof(SearchItemsForm).GetMethod("ToggleAdvanced", Hidden)!.Invoke(capture.Form, null);
            typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(capture.Form, null);
        });
        Assert.Equal(Items.Select(x => x.DofusDbId), Filter(capture.Host.Model));
        async Task Job(long? id)
        {
            await renderer.Dispatcher.InvokeAsync(() => capture.Job.SelectedValueChanged.InvokeAsync(id?.ToString() ?? ""));
            await renderer.Dispatcher.InvokeAsync(capture.Host.Render);
        }
        await Job(16);
        Assert.Equal(new[] { EquipmentType.Amulet, EquipmentType.Ring }, capture.Types.AvailableTypes.Order());
        Assert.Equal(new long[] { 100, 200 }, Filter(capture.Host.Model));
        Assert.DoesNotContain("Tous du métier", System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        await renderer.Dispatcher.InvokeAsync(() => capture.Types.SelectedTypesChanged.InvokeAsync(new[] { EquipmentType.Ring }));
        Assert.Equal(new long[] { 200 }, Filter(capture.Host.Model));
        await Job(60); // The incompatible ring selection is cleared.
        Assert.Empty(capture.Host.Model.EquipmentType.GetSelectedTypes());
        Assert.Equal(new long[] { 500, 600 }, Filter(capture.Host.Model));
        Assert.Equal(new[] { EquipmentType.Shield, EquipmentType.Trophy }, capture.Types.AvailableTypes.Order());
        await Job(65); Assert.Equal(new long[] { 700 }, Filter(capture.Host.Model));
        Assert.DoesNotContain(800L, Filter(capture.Host.Model)); // Same type, but a different craft membership.
        await Job(null); Assert.Equal(20, capture.Types.AvailableTypes.Count);
        Assert.Equal(8, Filter(capture.Host.Model).Length);
        await renderer.Dispatcher.InvokeAsync(() => capture.Types.SelectedTypesChanged.InvokeAsync(new[] { EquipmentType.Hat, EquipmentType.Cloak }));
        Assert.Equal(new long[] { 300, 400 }, Filter(capture.Host.Model));
        await renderer.Dispatcher.InvokeAsync(() => typeof(EquipmentTypeFilter).GetMethod("Reset", Hidden)!.Invoke(capture.Types, null));
        Assert.Equal(8, Filter(capture.Host.Model).Length);
        string html = System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains("Filtres avancés", html); Assert.DoesNotContain("btn-amulet", html);
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            typeof(SearchItemsForm).GetMethod("ToggleAdvanced", Hidden)!.Invoke(capture.Form, null);
            typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(capture.Form, null);
        });
        html = System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.DoesNotContain("Runes / caractéristiques", html);
        await renderer.Dispatcher.InvokeAsync(() => {
            typeof(SearchItemsForm).GetMethod("ToggleAdvanced", Hidden)!.Invoke(capture.Form, null);
            typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(capture.Form, null);
        });
        html = System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains("Runes / caractéristiques", html); Assert.Contains("ROI min.", html);
        capture.Host.Model.SearchText = "Outil"; capture.Host.Model.LevelMin = 75;
        Assert.Equal(new long[] { 800 }, Filter(capture.Host.Model));
    }

    [Fact]
    public async Task ProfessionSwitchFollowsDetectedLevelsAndBoundsAndClearsAnInvalidSelection()
    {
        var capture = new Capture(); var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IComponentActivator>(capture);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Host>());
        await renderer.Dispatcher.InvokeAsync(() => {
            typeof(SearchItemsForm).GetMethod("ToggleAdvanced", Hidden)!.Invoke(capture.Form, null);
            typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(capture.Form, null);
        });
        async Task<string> SwitchHtml() => System.Text.RegularExpressions.Regex.Match(
            await renderer.Dispatcher.InvokeAsync(root.ToHtmlString), "<input id=\"crushing-respect-job-levels\"[^>]*>").Value;
        Assert.Contains("disabled", await SwitchHtml());
        await renderer.Dispatcher.InvokeAsync(() => {
            capture.Host.PlayerLevel = 39; capture.Host.ProfessionLevels = new Dictionary<string, int> { ["Bijoutier"] = 20 };
            capture.Host.Render();
        });
        Assert.DoesNotContain("disabled", await SwitchHtml());
        await renderer.Dispatcher.InvokeAsync(() => typeof(SearchItemsForm).GetMethod("ToggleProfessionLevels", Hidden)!.Invoke(capture.Form, [new ChangeEventArgs { Value = true }]));
        Assert.True(capture.Host.Model.RespectJobLevels);
        await renderer.Dispatcher.InvokeAsync(() => { capture.Host.Model.LevelMin = 39; capture.Host.Render(); });
        Assert.Contains("disabled", await SwitchHtml()); Assert.False(capture.Host.Model.RespectJobLevels);
        await renderer.Dispatcher.InvokeAsync(() => { capture.Host.Model.LevelMin = 38; capture.Host.Render(); });
        Assert.DoesNotContain("disabled", await SwitchHtml());
        await renderer.Dispatcher.InvokeAsync(() => { capture.Host.Model.LevelMax = 100; capture.Host.Render(); });
        Assert.Contains("disabled", await SwitchHtml());
        await renderer.Dispatcher.InvokeAsync(() => { capture.Host.Model.LevelMax = null; capture.Host.ProfessionLevels = null; capture.Host.Render(); });
        Assert.Contains("disabled", await SwitchHtml());
        string html = System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.DoesNotContain("Tous les métiers", html); Assert.DoesNotContain("Tous les types", html);
        Assert.Contains("img/runes/", html);
    }

    [Fact]
    public async Task DirectRuneChoicesKeepExactVariantsAndMatchModeAndRespectDisabledState()
    {
        var capture = new Capture(); var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IComponentActivator>(capture);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Host>());
        await renderer.Dispatcher.InvokeAsync(() => {
            typeof(SearchItemsForm).GetMethod("ToggleAdvanced", Hidden)!.Invoke(capture.Form, null);
            typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(capture.Form, null);
        });
        var normal = capture.Choices.Single(x => x.Label == "Sa");
        var pa = capture.Choices.Single(x => x.Label == "Pa Sa");
        Assert.Contains(capture.Choices, x => x.Label == "chasse");
        capture.Host.Model.RuneMatchMode = RuneFilterMatchMode.All;
        await renderer.Dispatcher.InvokeAsync(() => normal.Pressed.InvokeAsync());
        await renderer.Dispatcher.InvokeAsync(() => pa.Pressed.InvokeAsync());
        Assert.Equal(new long[] { 1, 2 }, capture.Host.Model.RuneFilters.Select(x => x.DofusDbId));
        Assert.Equal(new[] { "Rune Sa", "Rune Pa Sa" }, capture.Host.Model.RuneFilters.Select(x => x.RuneName));
        Assert.Equal(RuneFilterMatchMode.All, capture.Host.Model.RuneMatchMode);
        await renderer.Dispatcher.InvokeAsync(() => normal.Pressed.InvokeAsync());
        Assert.Equal(2, Assert.Single(capture.Host.Model.RuneFilters).DofusDbId);
        await renderer.Dispatcher.InvokeAsync(() => { capture.Host.Disabled = true; capture.Host.Render(); });
        await renderer.Dispatcher.InvokeAsync(() => pa.Pressed.InvokeAsync());
        Assert.Equal(2, Assert.Single(capture.Host.Model.RuneFilters).DofusDbId);
        string html = System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.DoesNotContain("Choisir une rune...", html);
        Assert.Contains("Runes à obtenir", html);
        Assert.DoesNotContain("Cliquez à nouveau", html);
        Assert.DoesNotContain("Rune Pa Sa", html);
    }

    [Fact]
    public async Task CraftMembershipComesFromPublicSkillsAndItsCacheSurvivesRestart()
    {
        string folder = Path.Combine(Path.GetTempPath(), "crush-job-types-" + Guid.NewGuid());
        try
        {
            using var http = new CraftCatalogFixture { Skills = [
                new { parentJobId = 1, isForgemagus = false, craftableItemIds = new long[] {100} },
                new { parentJobId = 16, isForgemagus = false, craftableItemIds = new long[] {100,200} },
                new { parentJobId = 27, isForgemagus = false, craftableItemIds = new long[] {300,400} },
                new { parentJobId = 44, isForgemagus = true, craftableItemIds = new long[] {100,300} },
                new { parentJobId = 65, isForgemagus = false, craftableItemIds = new long[] {900} }] };
            var service = new ActivityCatalogService(DofusDbClient.Production(), http, folder);
            var first = await service.GetCraftingJobsAsync(default);
            Assert.DoesNotContain(first, job => job.Id == 44);
            var catalog = new EquipmentJobFilterCatalog(first, Items);
            Assert.Equal(new long[] { 16, 27 }, catalog.Jobs.Select(x => x.Id));
            Assert.Equal(new[] { EquipmentType.Amulet, EquipmentType.Ring }, catalog.TypesFor(16).Order());
            int calls = http.Calls; http.Offline = true;
            var reopened = new ActivityCatalogService(DofusDbClient.Production(), http, folder);
            Assert.Equal(first.SelectMany(x => x.ItemIds), (await reopened.GetCraftingJobsAsync(default)).SelectMany(x => x.ItemIds));
            Assert.Equal(calls, http.Calls);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void SavedFiltersKeepProfessionAndAllLegacyTypeSelections()
    {
        var model = JsonSerializer.Deserialize<SearchItemsModel>("""{"EquipmentType":{"Ring":true},"LevelMax":100}""")!;
        Assert.Null(model.CraftJobId); Assert.Equal(new[] { EquipmentType.Ring }, model.EquipmentType.GetSelectedTypes());
        model.CraftJobId = 16;
        var clone = (SearchItemsModel)typeof(ServerPage).GetMethod("CloneSearchModel", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [model])!;
        Assert.Equal(16, clone.CraftJobId); Assert.True(clone.EquipmentType.Ring); Assert.Equal(100, clone.LevelMax);
        foreach (var type in EquipmentTypesFilter.SupportedTypes)
        {
            model.EquipmentType.SetSelectedTypes([type]);
            var roundtrip = JsonSerializer.Deserialize<SearchItemsModel>(JsonSerializer.Serialize(model))!;
            Assert.Equal(new[] { type }, roundtrip.EquipmentType.GetSelectedTypes());
        }
        Assert.DoesNotContain("SupportedTypes", JsonSerializer.Serialize(model));
        Assert.Empty(new EquipmentJobFilterCatalog([], Items).TypesFor(999));
        Assert.Empty(Filter(new SearchItemsModel { CraftJobId = 999 }));
    }
}
