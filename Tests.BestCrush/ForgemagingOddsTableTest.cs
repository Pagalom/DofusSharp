using System.Net;
using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class ForgemagingOddsTableTest
{
    [Fact]
    public async Task LiveTableShowsAllThreeOddsImagesAndZeroExoWithoutObjectivesOrHistory()
    {
        var session=new ForgemagingSession{Level=100,Lines=[new(){Characteristic=Characteristic.Strength,Current=0,Target=0}]};
        var runes=new ForgemagingRune[]{new(1,"Rune Fo",Characteristic.Strength,1,10){IconId=100},
            new(2,"Rune Pa Fo",Characteristic.Strength,3,20){IconId=101},new(3,"Rune Ra Fo",Characteristic.Strength,10,40){IconId=102}};
        using var provider=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>WebUtility.HtmlDecode((await renderer.RenderComponentAsync<ForgemagingOddsTable>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"Session",session},{"Runes",runes}}))).ToHtmlString()));
        foreach(var rune in runes)
        {
            var odds=ForgemagingPlanner.Evaluate(100,session.Lines,rune);
            Assert.Contains($"img/runes/{rune.IconId}.png",html);
            Assert.Contains($"Succès critique {odds.Critical:0.#} %, succès neutre {odds.Neutral:0.#} %, échec critique {odds.Failure:0.#} %",html);
        }
        Assert.Contains("class=\"sc\"",html);Assert.Contains("class=\"sn\"",html);Assert.Contains("class=\"ec\"",html);
        Assert.DoesNotContain("Objectif",html);Assert.DoesNotContain("Historique",html);Assert.DoesNotContain("type=\"number\"",html);
        Assert.Contains("+ Exo",html);Assert.Contains("rune-exo-picker",html);
        Assert.Contains("Supprimer l’objectif : Force",html);
        Assert.Equal(0,session.Lines[0].Current);
    }

    [Fact]
    public async Task OverlayHidesRemovalForAnExoThatWasGainedThenLost()
    {
        var stat=Characteristic.Chance;
        var session=new ForgemagingSession{Lines=[new(){Characteristic=stat,Current=0,Target=1}],Attempts=[
            new(){Rune=new(1,"Rune Cha",stat,1,10),Before=[new(){Characteristic=stat,Current=0}],After=[new(){Characteristic=stat,Current=1}]},
            new(){Rune=new(2,"Rune Fo",Characteristic.Strength,1,10),Before=[new(){Characteristic=stat,Current=1}],After=[new(){Characteristic=stat,Current=0}]}]};
        using var provider=new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>(await renderer.RenderComponentAsync<ForgemagingOddsTable>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"Session",session}}))).ToHtmlString());
        Assert.DoesNotContain("fm-overlay-remove-goal",html);
    }
}
