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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class MarketAnalyticsWorkspaceTest
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private sealed class Gate
    {
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Factory(TestDatabase database) : IDbContextFactory<BestCrushDbContext>
    {
        public int Created;
        public Gate? Next;
        public BestCrushDbContext CreateDbContext()
        {
            Interlocked.Increment(ref Created);
            var gate=Interlocked.Exchange(ref Next,null);
            if(gate is not null){gate.Started.TrySetResult();gate.Release.Task.GetAwaiter().GetResult();}
            return database.CreateDbContext();
        }
    }
    private sealed class Capture : IComponentActivator
    {
        public AnalyticsWorkspace Workspace=null!;
        public IComponent CreateInstance(Type type)
        {
            var component=(IComponent)Activator.CreateInstance(type)!;
            if(component is AnalyticsWorkspace workspace)Workspace=workspace;
            return component;
        }
    }

    [Fact]
    public async Task DisplayChangesAndNewPriceNoticesDoNotReadAgainAndObsoleteServerReadsCannotPublish()
    {
        using var database=new TestDatabase();
        await using(var db=database.CreateContext())
        {
            for(int i=1;i<=12;i++)
            {
                db.Runes.Add(new Rune(i){Name=$"Rune {i:00}"});
                foreach(var server in new[]{"A","B"}) db.MarketPriceObservations.Add(new()
                {ObjectType=MarketObjectType.Rune,DofusDbId=i,ServerName=server,Price=server=="A"?111:222,Quantity=1,Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=DateTime.UtcNow.AddHours(-1)});
            }
            await db.SaveChangesAsync();
        }
        var factory=new Factory(database);var capture=new Capture();var changes=new MarketDataChangeNotifier();
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IComponentActivator>(capture);
        services.AddSingleton<IWorkspacePreferences>(new MarketAnalyticsPresetTest.MemoryPreferences());
        services.AddSingleton<MarketAnalyticsPresetService>();services.AddSingleton(changes);
        services.AddScoped(_=>factory.CreateDbContext());services.AddScoped<MarketAnalyticsService>();
        await using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<AnalyticsWorkspace>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"ServerName","A"}})));
        Assert.Contains("Mes courbes",WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        string initialHtml=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Matches("aria-pressed=\"true\"[^>]*>7 jours</button>",initialHtml);
        Assert.Matches("aria-pressed=\"true\"[^>]*>Base 100</button>",initialHtml);
        await renderer.Dispatcher.InvokeAsync(async()=>{await Call(capture.Workspace,"QuickAllRunesAsync");Render(capture.Workspace);});
        var original=Field<MarketAnalyticsComparison>(capture.Workspace,"_comparison");
        Assert.Equal(12,original.Groups.Single().Members.Count);Assert.Equal(2,factory.Created);
        Guid id=Field<List<MarketAnalyticsSeriesDefinition>>(capture.Workspace,"_series")[0].Id;
        await renderer.Dispatcher.InvokeAsync(()=>
        {
            Invoke(capture.Workspace,"ToggleVisibility",id,new ChangeEventArgs{Value=false});
            Invoke(capture.Workspace,"ShowDetails",id);Render(capture.Workspace);
        });
        Assert.Empty(Field<IReadOnlyList<MarketAnalyticsPlotSeries>>(capture.Workspace,"_visiblePlots"));
        string panelHtml=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("role=\"dialog\"",panelHtml);Assert.Contains("Fusionner en une courbe",panelHtml);
        Assert.Equal(2,factory.Created);
        await renderer.Dispatcher.InvokeAsync(async()=>
        {
            Invoke(capture.Workspace,"ToggleVisibility",id,new ChangeEventArgs{Value=true});
            Invoke(capture.Workspace,"OpenOptions");await Call(capture.Workspace,"ApplyOptionsAsync");Render(capture.Workspace);
        });
        Assert.Equal(2,factory.Created);Assert.Same(original,Field<MarketAnalyticsComparison>(capture.Workspace,"_comparison"));
        await renderer.Dispatcher.InvokeAsync(()=>
        {
            Invoke(capture.Workspace,"ToggleFusion",id,new ChangeEventArgs{Value=false});Render(capture.Workspace);
        });
        Assert.Equal(8,Field<IReadOnlyList<MarketAnalyticsPlotSeries>>(capture.Workspace,"_visiblePlots").Count);
        await renderer.Dispatcher.InvokeAsync(()=>
        {
            Invoke(capture.Workspace,"ChangePlotPage",1);
            typeof(AnalyticsWorkspace).GetField("_scale",Hidden)!.SetValue(capture.Workspace,MarketAnalyticsTransformation.Raw);
            Render(capture.Workspace);
        });
        Assert.Equal(4,Field<IReadOnlyList<MarketAnalyticsPlotSeries>>(capture.Workspace,"_visiblePlots").Count);
        Assert.Equal(2,factory.Created);Assert.Same(original,Field<MarketAnalyticsComparison>(capture.Workspace,"_comparison"));
        await renderer.Dispatcher.InvokeAsync(()=>changes.Notify(MarketObjectType.Rune,1,"A"));
        string html=WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains("Nouveaux relevés disponibles",html);Assert.Equal(2,factory.Created);

        var gate=new Gate();factory.Next=gate;Task oldRead=Task.CompletedTask;
        try
        {
            await renderer.Dispatcher.InvokeAsync((Action)(()=>{oldRead=Call(capture.Workspace,"RefreshAsync");Render(capture.Workspace);}));
            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(oldRead.IsCompleted);
            html=await renderer.Dispatcher.InvokeAsync(root.ToHtmlString).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("spinner-border",html);
            await renderer.Dispatcher.InvokeAsync(async()=>
            {
                typeof(AnalyticsWorkspace).GetProperty("ServerName")!.SetValue(capture.Workspace,"B");
                await Call(capture.Workspace,"OnParametersSetAsync");Render(capture.Workspace);
            });
            gate.Release.TrySetResult();await oldRead.WaitAsync(TimeSpan.FromSeconds(5));
            var current=Field<MarketAnalyticsComparison>(capture.Workspace,"_comparison");
            Assert.Equal("B",current.ServerName);Assert.All(current.Groups.Single().Members,m=>Assert.Equal(222,m.Samples.Last().Price));
            int contexts=factory.Created;
            await renderer.Dispatcher.InvokeAsync(()=>Call(capture.Workspace,"OnParametersSetAsync"));
            Assert.Equal(contexts,factory.Created);
            await renderer.Dispatcher.InvokeAsync(async()=>
            {
                var parent=Field<List<MarketAnalyticsSeriesDefinition>>(capture.Workspace,"_series")[0];
                parent.SourceMode=MarketAnalyticsSourceMode.Manual;parent.LotQuantity=10;parent.Aggregation=MarketAnalyticsAggregation.Mean;
                typeof(AnalyticsWorkspace).GetField("_inspectId",Hidden)!.SetValue(capture.Workspace,(Guid?)id);
                await (Task)Invoke(capture.Workspace,"IsolateAsync",new MarketAnalyticsMember(MarketObjectType.Rune,1,"Rune 01"))!;
                var isolated=Field<List<MarketAnalyticsSeriesDefinition>>(capture.Workspace,"_series")[1];
                Assert.Equal(MarketAnalyticsSourceMode.Manual,isolated.SourceMode);Assert.Equal(10,isolated.LotQuantity);Assert.Equal(MarketAnalyticsAggregation.Mean,isolated.Aggregation);
                Assert.Equal(MarketAnalyticsSelectionMode.Single,isolated.SelectionMode);
                await (Task)Invoke(capture.Workspace,"RemoveSeriesAsync",isolated.Id)!;
            });
            await renderer.Dispatcher.InvokeAsync(async()=>
            {
                Invoke(capture.Workspace,"BeginEdit",id);
                await (Task)Invoke(capture.Workspace,"RemoveSeriesAsync",id)!;
                Render(capture.Workspace);
            });
            Assert.Null(typeof(AnalyticsWorkspace).GetField("_draft",Hidden)!.GetValue(capture.Workspace));
            Assert.Empty(Field<List<MarketAnalyticsSeriesDefinition>>(capture.Workspace,"_series"));
            await renderer.Dispatcher.InvokeAsync(async()=>
            {
                Invoke(capture.Workspace,"BeginAdd");
                typeof(AnalyticsWorkspace).GetField("_builderSearch",Hidden)!.SetValue(capture.Workspace,"Rune 12");
                Invoke(capture.Workspace,"BuilderSearchChanged");
                Assert.Equal(12,Field<MarketAnalyticsSeriesDefinition>(capture.Workspace,"_draft").DofusDbId);
                await Call(capture.Workspace,"CommitDraftAsync");Render(capture.Workspace);
            });
            var added=Field<List<MarketAnalyticsSeriesDefinition>>(capture.Workspace,"_series").Single();
            Assert.Equal(12,added.DofusDbId);Assert.Equal("Rune 12",added.ItemName);
            await renderer.Dispatcher.InvokeAsync(async()=>
            {
                Invoke(capture.Workspace,"BeginAdd");
                typeof(AnalyticsWorkspace).GetField("_builderSearch",Hidden)!.SetValue(capture.Workspace,"Aucun objet correspondant");
                Invoke(capture.Workspace,"BuilderSearchChanged");
                Assert.Equal(0,Field<MarketAnalyticsSeriesDefinition>(capture.Workspace,"_draft").DofusDbId);
                await Call(capture.Workspace,"CommitDraftAsync");Render(capture.Workspace);
            });
            Assert.Single(Field<List<MarketAnalyticsSeriesDefinition>>(capture.Workspace,"_series"));
            Assert.Contains("Aucun objet ne correspond",WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
            int beforeSave=factory.Created;
            await renderer.Dispatcher.InvokeAsync(()=>
            {
                Invoke(capture.Workspace,"ClosePanel");
                Invoke(capture.Workspace,"ToggleVisibility",added.Id,new ChangeEventArgs{Value=false});
                Invoke(capture.Workspace,"RequestSave");Render(capture.Workspace);
            });
            Assert.Contains("Enregistrer l’analyse",WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
            await renderer.Dispatcher.InvokeAsync(()=>
            {
                typeof(AnalyticsWorkspace).GetField("_nameDraft",Hidden)!.SetValue(capture.Workspace,"Analyse de test");
                Invoke(capture.Workspace,"CompleteName");Render(capture.Workspace);
            });
            Assert.Equal(beforeSave,factory.Created);
            var saved=provider.GetRequiredService<MarketAnalyticsPresetService>().GetAll().Single();
            Assert.Equal("Analyse de test",saved.Name);Assert.False(saved.Series.Single().IsVisible);
            await renderer.Dispatcher.InvokeAsync(async()=>
            {
                Invoke(capture.Workspace,"NewAnalysis");
                await (Task)Invoke(capture.Workspace,"SelectPresetByIdAsync",(Guid?)saved.Id)!;Render(capture.Workspace);
            });
            Assert.False(Field<List<MarketAnalyticsSeriesDefinition>>(capture.Workspace,"_series").Single().IsVisible);
            Assert.Empty(Field<IReadOnlyList<MarketAnalyticsPlotSeries>>(capture.Workspace,"_visiblePlots"));
        }
        finally{gate.Release.TrySetResult();}
    }

    [Fact]
    public async Task HistoryLoadsOnDemandIncludesExcludedMembersAndCancelsWhenClosed()
    {
        using var database = new TestDatabase();
        await using (var db = database.CreateContext())
        {
            db.Runes.AddRange(new Rune(1) { Name = "Rune présente" }, new Rune(2) { Name = "Rune absente" });
            foreach (var (id, days) in new[] { (1L, -1), (2L, -90) })
                db.MarketPriceObservations.Add(new() { ObjectType = MarketObjectType.Rune, DofusDbId = id,
                    ServerName = "A", Price = 123, Quantity = 10, Source = MarketPriceSource.InGameAutomatic, ObservedAtUtc = DateTime.UtcNow.AddDays(days) });
            await db.SaveChangesAsync();
        }
        var factory = new Factory(database); var capture = new Capture();
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IComponentActivator>(capture);
        services.AddSingleton<IWorkspacePreferences>(new MarketAnalyticsPresetTest.MemoryPreferences());
        services.AddSingleton<MarketAnalyticsPresetService>(); services.AddSingleton<MarketDataChangeNotifier>();
        services.AddScoped(_ => factory.CreateDbContext()); services.AddScoped<MarketAnalyticsService>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<AnalyticsWorkspace>(ParameterView.FromDictionary(new Dictionary<string,object?> { { "ServerName", "A" } })));
        await renderer.Dispatcher.InvokeAsync(() => Call(capture.Workspace, "QuickAllRunesAsync"));
        var comparison = Field<MarketAnalyticsComparison>(capture.Workspace, "_comparison");
        var group = comparison.Groups.Single(); var excluded = group.Members.Single(m => m.Member.DofusDbId == 2);
        Assert.Null(excluded.ReferencePrice);
        int before = factory.Created;
        await renderer.Dispatcher.InvokeAsync(() => (Task)Invoke(capture.Workspace, "OpenDetailsAsync", group.DefinitionId)!);
        Assert.Equal(before, factory.Created);
        await renderer.Dispatcher.InvokeAsync(async () => { await (Task)Invoke(capture.Workspace, "OpenHistoryAsync", excluded.Member)!; Render(capture.Workspace); });
        Assert.Empty(Field<MarketAnalyticsHistoryPage>(capture.Workspace, "_history").Rows);
        Assert.Contains("Aucun relevé", WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        await renderer.Dispatcher.InvokeAsync(async () => {
            typeof(AnalyticsWorkspace).GetField("_historyAll", Hidden)!.SetValue(capture.Workspace, true);
            await Call(capture.Workspace, "ReloadHistoryAsync"); Render(capture.Workspace);
        });
        Assert.Equal(12.3, Field<MarketAnalyticsHistoryPage>(capture.Workspace, "_history").Rows.Single().UnitPrice);
        Assert.Same(comparison, Field<MarketAnalyticsComparison>(capture.Workspace, "_comparison"));
        var gate = new Gate(); factory.Next = gate; Task pending = Task.CompletedTask;
        try
        {
            await renderer.Dispatcher.InvokeAsync((Action)(() => { pending = Call(capture.Workspace, "ReloadHistoryAsync"); Render(capture.Workspace); }));
            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("Chargement des relevés", WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
            await renderer.Dispatcher.InvokeAsync(() => { Invoke(capture.Workspace, "ClosePanel"); Render(capture.Workspace); });
            gate.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(typeof(AnalyticsWorkspace).GetField("_history", Hidden)!.GetValue(capture.Workspace));
        }
        finally { gate.Release.TrySetResult(); }
        await renderer.Dispatcher.InvokeAsync(() => (Task)Invoke(capture.Workspace, "OpenDetailsAsync", group.DefinitionId)!);
        var switchGate = new Gate(); factory.Next = switchGate; Task previousItem = Task.CompletedTask;
        try
        {
            await renderer.Dispatcher.InvokeAsync((Action)(() => { previousItem = (Task)Invoke(capture.Workspace, "OpenHistoryAsync", excluded.Member)!; }));
            await switchGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await renderer.Dispatcher.InvokeAsync(() => (Task)Invoke(capture.Workspace, "OpenHistoryAsync", group.Members.Single(m => m.Member.DofusDbId == 1).Member)!);
            switchGate.Release.TrySetResult(); await previousItem.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, Field<MarketAnalyticsMember>(capture.Workspace, "_historyMember").DofusDbId);
            Assert.Single(Field<MarketAnalyticsHistoryPage>(capture.Workspace, "_history").Rows);
            Assert.Same(comparison, Field<MarketAnalyticsComparison>(capture.Workspace, "_comparison"));
        }
        finally { switchGate.Release.TrySetResult(); }
        await renderer.Dispatcher.InvokeAsync(async () => {
            var definition = Field<List<MarketAnalyticsSeriesDefinition>>(capture.Workspace, "_series").Single();
            definition.SelectionMode = MarketAnalyticsSelectionMode.Single; definition.DofusDbId = 1;
            await Call(capture.Workspace, "RefreshAsync");
            await (Task)Invoke(capture.Workspace, "OpenDetailsAsync", definition.Id)!;
        });
        Assert.True(Field<bool>(capture.Workspace, "_historyTab"));
        Assert.Single(Field<MarketAnalyticsHistoryPage>(capture.Workspace, "_history").Rows);
    }

    [Theory]
    [InlineData(MarketAnalyticsTransformation.Base100)]
    [InlineData(MarketAnalyticsTransformation.Raw)]
    public async Task ChartConnectsOnlyObservedIntervalsAndStopsAtTheLastObservation(MarketAnalyticsTransformation scale)
    {
        DateTime at=new(2026,10,1,8,0,0,DateTimeKind.Utc);
        var samples=new MarketAnalyticsSample[]
        {
            new(at.AddHours(-1),80,80,0,at.AddHours(-2),true),
            new(at,90,90,1,at,false),
            new(at.AddHours(1),110,110,1,at.AddHours(1),false),
            new(at.AddHours(2),110,110,0,at.AddHours(1),true),
            new(at.AddHours(3),null,null,0,null,false),
            new(at.AddHours(4),null,null,0,null,false),
            // New data for one group member can coexist with carried member prices.
            new(at.AddHours(5),130,130,1,at.AddHours(4),true),
            new(at.AddHours(6),130,130,0,at.AddHours(4),true),
            new(at.AddHours(7),null,null,0,null,false)
        };
        string html=WebUtility.HtmlDecode(await RenderChartAsync(samples,scale));
        Assert.Equal(3,Regex.Matches(html,"class=\"analytics-observation\"").Count);
        Assert.Equal(3,Regex.Matches(html,"class=\"analytics-hit\"").Count);
        var segments=Regex.Matches(html,"<polyline [^>]+>");
        Assert.Single(segments);
        var coordinates=Regex.Match(segments[0].Value,"points=\"([^\"]+)\"").Groups[1].Value.Split(' ');
        Assert.Equal(3,coordinates.Length);
        // 7h and 15h still bound the selected period, but the curve runs from 8h to 13h.
        Assert.True(double.Parse(coordinates[0].Split(',')[0],System.Globalization.CultureInfo.InvariantCulture)>95);
        Assert.True(double.Parse(coordinates[^1].Split(',')[0],System.Globalization.CultureInfo.InvariantCulture)<1070);
        Assert.DoesNotContain("stroke-dasharray",html);
        Assert.Equal(2,Regex.Matches(html,"class=\"analytics-link-hit\"").Count);
        Assert.Contains("Aucun relevé dans les créneaux intermédiaires",html);
        Assert.Contains("Aucun prix intermédiaire calculé",html);
        Assert.DoesNotContain("NaN",html);Assert.DoesNotContain("Infinity",html);
    }

    [Theory]
    [InlineData(MarketAnalyticsTransformation.Base100)]
    [InlineData(MarketAnalyticsTransformation.Raw)]
    public async Task ChartWithOnlyProlongedPricesDoesNotInventACurve(MarketAnalyticsTransformation scale)
    {
        DateTime at=new(2026,10,1,0,0,0,DateTimeKind.Utc);
        var samples=Enumerable.Range(0,3).Select(i=>new MarketAnalyticsSample(at.AddHours(i),10,100,0,at.AddHours(-1),true)).ToArray();
        string html=await RenderChartAsync(samples,scale);
        Assert.DoesNotContain("class=\"analytics-observation\"",html);
        Assert.DoesNotContain("<polyline ",html);
        Assert.DoesNotContain("class=\"analytics-hit\"",html);
        Assert.Contains("Aucun point comparable",html);
    }

    [Fact]
    public async Task ChartExplainsMissingGroupValuesWithoutClaimingThereWereNoReadings()
    {
        DateTime at=new(2026,10,1,0,0,0,DateTimeKind.Utc);
        var samples=new MarketAnalyticsSample[]
        {
            new(at,10,100,1,at,false),
            new(at.AddHours(1),null,null,1,at.AddHours(1),false),
            new(at.AddHours(2),12,120,1,at.AddHours(2),false)
        };
        string html=WebUtility.HtmlDecode(await RenderChartAsync(samples,MarketAnalyticsTransformation.Base100));
        Assert.Equal(2,Regex.Matches(html,"class=\"analytics-observation\"").Count);
        Assert.Contains("Données insuffisantes pour afficher un point",html);
        Assert.DoesNotContain("Aucun relevé dans les créneaux intermédiaires",html);
    }

    private static async Task<string> RenderChartAsync(IReadOnlyList<MarketAnalyticsSample> samples,MarketAnalyticsTransformation scale)
    {
        var services=new ServiceCollection();services.AddLogging();await using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var series=new MarketAnalyticsPlotSeries("one",Guid.NewGuid(),"Objet",samples);
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<MarketAnalyticsChart>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Series",new[]{series}},{"Scale",scale}})));
        return await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
    }

    private static object? Invoke(object target,string method,params object?[] args)=>target.GetType().GetMethod(method,Hidden)!.Invoke(target,args);
    private static Task Call(object target,string method)=>(Task)Invoke(target,method)!;
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Hidden)!.GetValue(target)!;
    private static void Render(object target)=>typeof(ComponentBase).GetMethod("StateHasChanged",Hidden)!.Invoke(target,null);
}
