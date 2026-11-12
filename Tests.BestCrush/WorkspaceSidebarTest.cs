using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BestCrush.Components;
using BestCrush.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class WorkspaceSidebarTest
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Capture : IComponentActivator
    {
        public WorkspaceSidebar Sidebar = null!;
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is WorkspaceSidebar sidebar) Sidebar = sidebar;
            return component;
        }
    }
    private static ServiceCollection Services(Capture capture)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IComponentActivator>(capture); return services;
    }

    [Theory]
    [InlineData("Kourial", "/servers/Kourial")]
    [InlineData(null, "/modules/concassage")]
    public async Task AllElevenServicesRemainDirectlyReachableWithExactlyOneActiveService(string? server, string crushRoute)
    {
        var capture = new Capture(); await using var provider = Services(capture).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<WorkspaceSidebar>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["ServerName"] = server, ["ActiveModule"] = "craft" })));
        string html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Equal(11, Regex.Matches(html, "<a ").Count);
        Assert.Equal(1, Regex.Matches(html, "aria-current=\"page\"").Count);
        Assert.Contains($"href=\"{crushRoute}\"", html);
        foreach (var module in WorkspaceModules.All) Assert.Contains($"aria-label=\"{module.Name}\"", html);
        Assert.Equal(11, Regex.Matches(html, "<i class=\"bi bi-").Count);
        Assert.DoesNotContain("bi-circle", html);
        Assert.True(html.IndexOf("href=\"/modules/routine\"", StringComparison.Ordinal) < html.IndexOf("href=\"/modules/craft\"", StringComparison.Ordinal));
        Assert.True(html.IndexOf("href=\"/modules/analyses\"", StringComparison.Ordinal) > html.IndexOf("href=\"/modules/forgemagie\"", StringComparison.Ordinal));
        Assert.Contains("Au quotidien", html); Assert.Contains("workspace-nav-tools", html);
    }

    [Theory]
    [InlineData(WorkspaceDataTab.Equipment)]
    [InlineData(WorkspaceDataTab.Runes)]
    [InlineData(WorkspaceDataTab.Resources)]
    [InlineData(WorkspaceDataTab.Coefficients)]
    [InlineData(WorkspaceDataTab.History)]
    public async Task DataNavigationHasFiveChoicesAndCallsTheExistingSelectionFlow(WorkspaceDataTab selected)
    {
        var capture = new Capture(); await using var provider = Services(capture).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        WorkspaceDataTab? requested = null;
        var callback = EventCallback.Factory.Create<WorkspaceDataTab>(capture, (WorkspaceDataTab tab) => requested = tab);
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<WorkspaceSidebar>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["DataOpen"] = true, ["DataTab"] = selected, ["OnDataTabSelected"] = callback })));
        string html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Equal(5, Regex.Matches(html, "class=\"workspace-nav-item").Count);
        Assert.Equal(5, Regex.Matches(html, "<i class=\"bi bi-").Count);
        Assert.DoesNotContain("bi-circle", html);
        Assert.Equal(1, Regex.Matches(html, "aria-current=\"page\"").Count);
        Assert.DoesNotContain("href=\"/modules/routine\"", html);
        await renderer.Dispatcher.InvokeAsync(async () => {
            typeof(WorkspaceSidebar).GetField("_expanded", Hidden)!.SetValue(capture.Sidebar, true);
            await (Task)typeof(WorkspaceSidebar).GetMethod("ChooseDataAsync", Hidden)!.Invoke(capture.Sidebar, [WorkspaceDataTab.History])!;
        });
        Assert.Equal(WorkspaceDataTab.History, requested);
        Assert.False((bool)typeof(WorkspaceSidebar).GetField("_expanded", Hidden)!.GetValue(capture.Sidebar)!);
    }

    [Fact]
    public async Task KeyboardEscapeClosesTheCompactMenuWithoutSelectingAnotherService()
    {
        var capture = new Capture(); await using var provider = Services(capture).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<WorkspaceSidebar>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ActiveModule"] = "forgemagie" })));
        await renderer.Dispatcher.InvokeAsync(() => {
            typeof(WorkspaceSidebar).GetField("_expanded", Hidden)!.SetValue(capture.Sidebar, true);
            typeof(WorkspaceSidebar).GetMethod("HandleKey", Hidden)!.Invoke(capture.Sidebar, [new KeyboardEventArgs { Key = "Escape" }]);
            typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(capture.Sidebar, null);
        });
        Assert.False((bool)typeof(WorkspaceSidebar).GetField("_expanded", Hidden)!.GetValue(capture.Sidebar)!);
        Assert.Contains("Menu · Forgemagie", WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
    }
}
