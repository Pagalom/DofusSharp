using System.Net;
using System.Reflection;
using BestCrush.Components.Overlays;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class RuneRankingOverlayTest
{
    private static Rune Rune(long id,string? name=null)=>new(id){Name=name??$"Rune Fo {id}",Characteristic=Characteristic.Strength};
    private static MarketPriceObservation Quote(long id,long price,int lot=1)=>new(){DofusDbId=id,Price=price,Quantity=lot,ObjectType=MarketObjectType.Rune,ServerName="Kourial",Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=DateTime.UtcNow};
    private static IReadOnlyList<RuneRankingEntry> Rows()=>OverlayWorkspaceService.RankRunes(Enumerable.Range(1,8).Select(i=>Rune(i)),
        Enumerable.Range(1,8).ToDictionary(i=>((long)i,1),i=>Quote(i,900-i*100)));

    [Fact]
    public void RankingUsesOnlyKamasPerWeightAndTheSameLotReferenceAsTheDataTable()
    {
        var rows=OverlayWorkspaceService.RankRunes([Rune(1,"Rune Ra Fo"),Rune(2,"Rune Fo"),Rune(3)],
            new Dictionary<(long,int),MarketPriceObservation>{{(1,1),Quote(1,1000)},{(2,1),Quote(2,300)},{(2,100),Quote(2,20000,100)}});
        Assert.Equal(new long[]{2,1,3},rows.Select(r=>r.Rune.DofusDbId));
        Assert.Equal(200,rows[0].Ratio);Assert.Equal(100,rows[1].Ratio);Assert.Null(rows[2].Ratio);
        Assert.Equal(RuneMarketRanking.Ratio(1,300,null,20000,null),rows[0].Ratio);
        Assert.Equal(100,rows[0].Reference!.Value.Lot);
    }

    [Fact]
    public void HiddenRunesSurviveRepricingAndRestartAndRemainScopedToTheServer()
    {
        string path=Path.Combine(Path.GetTempPath(),"rune-ranking-"+Guid.NewGuid()+".json");
        try
        {
            var preferences=new RuneRankingPreferences(path);preferences.Hide("Kourial",1);preferences.Hide("Brial",2);
            var updated=OverlayWorkspaceService.RankRunes([Rune(1),Rune(2)],new Dictionary<(long,int),MarketPriceObservation>{{(1,1),Quote(1,99999)},{(2,1),Quote(2,10)}});
            var reopened=new RuneRankingPreferences(path);
            Assert.Equal(2,Assert.Single(updated.Where(r=>!reopened.Hidden("Kourial").Contains(r.Rune.DofusDbId))).Rune.DofusDbId);
            Assert.Contains(1,reopened.Hidden("kourial"));Assert.DoesNotContain(1,reopened.Hidden("Brial"));
            reopened.Restore("Kourial",1);Assert.Empty(reopened.Hidden("Kourial"));
            reopened.Hide("Kourial",1);reopened.Reset("Kourial");Assert.Empty(new RuneRankingPreferences(path).Hidden("Kourial"));
            Assert.Contains(2,new RuneRankingPreferences(path).Hidden("Brial"));
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }

    private sealed class Capture:IComponentActivator
    {
        public RuneRankingOverlay Overlay=null!;
        public IComponent CreateInstance(Type type)
        {var c=(IComponent)Activator.CreateInstance(type)!;if(c is RuneRankingOverlay overlay)Overlay=overlay;return c;}
    }

    [Fact]
    public async Task ShowsFiveThenBackfillsHiddenRowsSupportsIndividualRestoreAndReset()
    {
        string path=Path.Combine(Path.GetTempPath(),"rune-ranking-ui-"+Guid.NewGuid()+".json");
        try
        {
            var capture=new Capture();var preferences=new RuneRankingPreferences(path);
            using var provider=new ServiceCollection().AddLogging().AddSingleton(preferences).AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
            await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
            var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<RuneRankingOverlay>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Rows",Rows()},{"Server","Kourial"}})));
            async Task<string> Html()=>WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            async Task Click(string method,params object[] args)=>await renderer.Dispatcher.InvokeAsync(()=>
            {typeof(RuneRankingOverlay).GetMethod(method,flags)!.Invoke(capture.Overlay,args);typeof(ComponentBase).GetMethod("StateHasChanged",flags)!.Invoke(capture.Overlay,null);});
            string html=await Html();Assert.Contains("Masquer Rune Fo 5",html);Assert.DoesNotContain("Masquer Rune Fo 6",html);
            await Click("Hide",1L);html=await Html();Assert.DoesNotContain("Masquer Rune Fo 1",html);Assert.Contains("Masquer Rune Fo 6",html);
            await renderer.Dispatcher.InvokeAsync(()=>{typeof(RuneRankingOverlay).GetField("_showHidden",flags)!.SetValue(capture.Overlay,true);typeof(ComponentBase).GetMethod("StateHasChanged",flags)!.Invoke(capture.Overlay,null);});
            Assert.Contains("Réafficher Rune Fo 1",await Html());
            await Click("Restore",1L);Assert.Contains("Masquer Rune Fo 1",await Html());
            await Click("Hide",1L);await Click("Reset");html=await Html();
            Assert.Contains("Masquer Rune Fo 1",html);Assert.DoesNotContain("Masquer Rune Fo 6",html);Assert.Empty(preferences.Hidden("Kourial"));
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
}
