using System.Net;
using System.Reflection;
using BestCrush.Components.Pages;
using BestCrush.Domain.Models;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class RoutineWorkspaceTest
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Capture : IComponentActivator
    {
        public RoutineWorkspace Workspace = null!;
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is RoutineWorkspace workspace) Workspace = workspace;
            return component;
        }
    }
    [Fact]
    public async Task EditingIsExclusiveAndKeepsOrderUntilClosedAndCreationAndDeletionPersist()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var capture = new Capture(); var services = new ServiceCollection(); services.AddLogging();
        var store = new WeeklyRoutineStore(path);
        services.AddSingleton<IComponentActivator>(capture); services.AddSingleton(store);
        try
        {
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<RoutineWorkspace>());
            object? Field(string name) => typeof(RoutineWorkspace).GetField(name, Hidden)!.GetValue(capture.Workspace);
            void Set(string name, object value) => typeof(RoutineWorkspace).GetField(name, Hidden)!.SetValue(capture.Workspace, value);
            async Task Invoke(string name, params object[] args) => await renderer.Dispatcher.InvokeAsync(async () => {
                await (Task)typeof(RoutineWorkspace).GetMethod(name, Hidden)!.Invoke(capture.Workspace, args)!;
                typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(capture.Workspace, null);
            });
            Guid[] Order() => ((IEnumerable<RoutineActivity>)typeof(RoutineWorkspace).GetProperty("DisplayedActivities", Hidden)!.GetValue(capture.Workspace)!).Select(a => a.Id).ToArray();
            async Task<string> Html() => WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            var routine = (WeeklyRoutine)Field("_routine")!;
            var first = routine.Activities[0]; var second = routine.Activities[1];
            var order = Order();
            Assert.DoesNotContain("Titre de l’activité", await Html());
            await Invoke("ToggleEditorAsync", first.Id);
            await Invoke("SetPriorityAsync", first.Id, 3);
            Assert.Equal(order, Order()); Assert.Equal(3, store.Read().Activities[0].Priority);
            first.Title = "Titre corrigé"; first.Notes = "Description corrigée";
            await Invoke("SaveAsync");
            Assert.Equal("Titre corrigé", store.Read().Activities[0].Title);
            Assert.Equal("Description corrigée", store.Read().Activities[0].Notes);
            await Invoke("ToggleEditorAsync", second.Id);
            Assert.Equal(second.Id, (Guid?)Field("_editingId"));
            Assert.True(Array.IndexOf(Order(), first.Id) > Array.IndexOf(Order(), second.Id));
            string html = await Html();
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(html, "aria-label=\"Titre de l’activité\"").Count);
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(html, "aria-label=\"Description de l’activité\"").Count);
            Assert.DoesNotContain("Mes notes", html); Assert.DoesNotContain("Niveau du personnage", html);
            await Invoke("ToggleEditorAsync", second.Id);
            Assert.DoesNotContain("Titre de l’activité", await Html());
            Set("_newTitle", "Nouvel objectif"); Set("_newPriority", 1);
            await Invoke("AddAsync");
            var created = routine.Activities.Last();
            Assert.Equal(1, created.Priority); Assert.Equal(1, store.Read().Activities.Last().Priority);
            await Invoke("ToggleAsync", created.Id, (DateOnly)Field("_today")!);
            await Invoke("ToggleEditorAsync", created.Id);
            await Invoke("DeleteActivityAsync", created.Id);
            Assert.DoesNotContain(created.Id, Order());
            var saved = store.Read(); Assert.True(saved.Activities.Single(a => a.Id == created.Id).Deleted);
            Assert.Single(saved.Completions, c => c.ActivityId == created.Id);
            await Invoke("UndoDeleteAsync");
            Assert.Contains(created.Id, Order()); Assert.False(store.Read().Activities.Single(a => a.Id == created.Id).Deleted);
            Assert.Single(store.Read().Completions, c => c.ActivityId == created.Id);
            // A deleted objective remains visible on the date of its historical completion.
            await Invoke("ToggleEditorAsync", created.Id); await Invoke("DeleteActivityAsync", created.Id);
            await renderer.Dispatcher.InvokeAsync(() => {
                typeof(RoutineWorkspace).GetMethod("UpdateDate", Hidden)!.Invoke(capture.Workspace, [((DateOnly)Field("_today")!).AddDays(1)]);
                typeof(RoutineWorkspace).GetMethod("MoveDay", Hidden)!.Invoke(capture.Workspace, [-1]);
            });
            Assert.Contains(created.Id, Order());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task RoutineRendersWithoutNetworkOrGameIdentityAndKeepsChecksAfterReload()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var capture = new Capture(); var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IComponentActivator>(capture); services.AddSingleton(new WeeklyRoutineStore(path));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<RoutineWorkspace>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ServerName"] = "Kourial" })));
            var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            Assert.DoesNotContain("À faire en premier", html); Assert.Contains("Valoriser les ressources accumulées", html);
            Assert.Contains("Capturer des âmes de boss", html); Assert.Contains("href=\"/modules/pierres-ame\"", html);
            Assert.Contains("Gain à renseigner", html); Assert.Contains("/servers/Kourial", html);
            Assert.DoesNotContain("Planning personnel",html);Assert.DoesNotContain("cette semaine",html);
            Assert.DoesNotContain("routine-days",html);Assert.DoesNotContain("id=\"routine-settings\"",html);
            Assert.Contains("routine-personalize",html);Assert.DoesNotContain("Fait aujourd’hui",html);Assert.Contains("routine-completion",html);Assert.Contains("routine-priority-1",html);
            var routine = (WeeklyRoutine)typeof(RoutineWorkspace).GetField("_routine", Hidden)!.GetValue(capture.Workspace)!;
            var today = (DateOnly)typeof(RoutineWorkspace).GetField("_today", Hidden)!.GetValue(capture.Workspace)!;
            await renderer.Dispatcher.InvokeAsync(async () => {
                await (Task)typeof(RoutineWorkspace).GetMethod("SetPriorityAsync", Hidden)!.Invoke(capture.Workspace, [routine.Activities[0].Id, 3])!;
            });
            Assert.Equal(3, routine.Activities[0].Priority);
            Assert.Equal(3, provider.GetRequiredService<WeeklyRoutineStore>().Read().Activities[0].Priority);
            await renderer.Dispatcher.InvokeAsync(async () => {
                await (Task)typeof(RoutineWorkspace).GetMethod("SetPriorityAsync", Hidden)!.Invoke(capture.Workspace, [routine.Activities[0].Id, 1])!;
            });
            await renderer.Dispatcher.InvokeAsync(async () => {
                await (Task)typeof(RoutineWorkspace).GetMethod("ToggleAsync", Hidden)!.Invoke(capture.Workspace, [routine.Activities[0].Id, today])!;
                typeof(ComponentBase).GetMethod("StateHasChanged", Hidden)!.Invoke(capture.Workspace, null);
            });
            Assert.Single(provider.GetRequiredService<WeeklyRoutineStore>().Read().Completions);
            html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            Assert.Contains("Vérifier les missions de guilde", html);
            Assert.Contains("1 / 7", html);
            routine.Activities[0].EstimatedNetKamas = 100;
            routine.Activities[1].EstimatedNetKamas = -20;
            Assert.Equal(-20L, (long?)typeof(RoutineWorkspace).GetProperty("RemainingEstimate", Hidden)!.GetValue(capture.Workspace));
            await renderer.Dispatcher.InvokeAsync(()=>{
                typeof(RoutineWorkspace).GetMethod("UpdateDate",Hidden)!.Invoke(capture.Workspace,[today.AddDays(1)]);
                typeof(ComponentBase).GetMethod("StateHasChanged",Hidden)!.Invoke(capture.Workspace,null);
            });
            html=WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            Assert.Contains("0 / 7",html);
            Assert.Equal(80L,(long?)typeof(RoutineWorkspace).GetProperty("RemainingEstimate",Hidden)!.GetValue(capture.Workspace));
            Assert.Single(provider.GetRequiredService<WeeklyRoutineStore>().Read().Completions);
            await renderer.Dispatcher.InvokeAsync(()=>{
                typeof(RoutineWorkspace).GetMethod("MoveDay",Hidden)!.Invoke(capture.Workspace,[-1]);
                typeof(ComponentBase).GetMethod("StateHasChanged",Hidden)!.Invoke(capture.Workspace,null);
            });
            html=WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            Assert.Contains("1 / 7",html);Assert.Contains("Jour passé",html);
            await renderer.Dispatcher.InvokeAsync(()=>{
                typeof(RoutineWorkspace).GetField("_settingsOpen",Hidden)!.SetValue(capture.Workspace,true);
                typeof(ComponentBase).GetMethod("StateHasChanged",Hidden)!.Invoke(capture.Workspace,null);
            });
            html=WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            Assert.Contains("id=\"routine-settings\"",html);Assert.DoesNotContain("Début du planning",html);Assert.DoesNotContain("Fréquence",html);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
