using System.Net;
using System.Reflection;
using BestCrush.Components.Overlays;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.BestCrush;

public sealed class OverlayBreedingPresentationTest
{
    [Theory]
    [InlineData(30,false)]
    [InlineData(600,false)]
    [InlineData(601,true)]
    public void OldLabelStartsAfterTenMinutesAndNeverHidesTheForecast(int elapsedSeconds,bool old)
    {
        var observed=DateTime.UtcNow;
        var mount=new PaddockMountReading(Guid.NewGuid(),96,0,0,0){Endurance=10000,Love=10000,Sex=BreedingMountSex.Male,Fertility=BreedingMountFertility.Fertile,SpeciesId=1};
        var reading=new LivePaddockReading(new([new(PaddockGauge.Maturity,true,100000)],[mount]),observed,1,1,1);
        var species=new Dictionary<int,BreedingSpecies>{{1,new(1,"Dragodinde de test",1,2)}};
        var card=OverlayBreedingPresentation.Enclosure(reading,species,new Dictionary<Guid,int>(),observed.AddSeconds(elapsedSeconds));
        Assert.Equal(old,card.OldReading);Assert.NotNull(card.Deadline);Assert.NotNull(Assert.Single(card.Mounts).Deadline);
        Assert.Empty(card.Lines);Assert.Equal("1 montures",card.Subtitle);
        Assert.Equal(2,card.Mounts[0].Generation);
    }

    [Theory]
    [InlineData(3601,"1 h 01 min")]
    [InlineData(300,"0 h 05 min")]
    [InlineData(0,"0 h 00 min")]
    [InlineData(-30,"0 h 00 min")]
    public void CountdownUsesHoursAndMinutesInsteadOfClockTime(int seconds,string expected)
    {
        var now=DateTime.UtcNow;Assert.Equal(expected,OverlayBreedingPresentation.Countdown(now.AddSeconds(seconds),now));
    }

    [Fact]
    public async Task MainCardOnlyShowsClickableCountAndPopupPutsAllIndicatorsOnOneRow()
    {
        var now=DateTime.UtcNow;
        var mount=new PaddockMountReading(Guid.NewGuid(),96,0,-200,10000)
            {Endurance=20000,Love=5000,Sex=BreedingMountSex.Male,Fertility=BreedingMountFertility.Fertile,SpeciesId=1};
        var row=new OverlayMountReading("Dragodinde Dorée",mount,2,now.AddHours(1));
        var card=new OverlayActionCard("Enclos 1","9 montures",[],[])
            {Mounts=[row],OldReading=true,Deadline=now.AddHours(1),TimerLabel="Prochaine étape"};
        using var bootstrap=new ServiceCollection().BuildServiceProvider();
        var data=new OverlayWorkspaceService(bootstrap.GetRequiredService<IServiceScopeFactory>(),new(),new(),null!,null!,new(),NullLogger<OverlayWorkspaceService>.Instance);
        var modules=(System.Collections.Concurrent.ConcurrentDictionary<WorkspaceOverlayKind,OverlayModuleView>)typeof(OverlayWorkspaceService)
            .GetField("_modules",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(data)!;
        modules[WorkspaceOverlayKind.Breeding]=new("Élevage",[card],"","elevage");
        var host=new OverlayWindowsService(data,null!);
        var windows=(Dictionary<string,OverlayWindowView>)typeof(OverlayWindowsService).GetField("_windows",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)!;
        windows.Add("main",new(){Id="main",Kind=WorkspaceOverlayKind.Breeding,Title="Élevage"});
        windows.Add("popup",new(){Id="popup",Kind=WorkspaceOverlayKind.Popup,Title="Enclos 1",Popup=new("mounts:test","Enclos 1",[]){Mounts=[row]}});
        using var provider=new ServiceCollection().AddLogging().AddSingleton(data).AddSingleton(host).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        async Task<string> Render(string id)=>await renderer.Dispatcher.InvokeAsync(async()=>WebUtility.HtmlDecode((await renderer.RenderComponentAsync<OverlayRoot>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"WindowId",id}}))).ToHtmlString()));
        string main=await Render("main");Assert.Contains("mount-count",main);Assert.Contains("9 montures",main);
        Assert.DoesNotContain("9 éléments",main);Assert.DoesNotContain("Dragodinde Dorée",main);Assert.Contains("1 h 00 min",main);
        Assert.Contains("Relevé de plus de 10 minutes",main);
        string popup=await Render("popup");Assert.Contains("Dragodinde Dorée",popup);Assert.Contains("bi-emoji-frown-fill",popup);
        Assert.Contains("class=\"overlay-mount-name\"",popup);Assert.Contains("Copier : Dragodinde Dorée",popup);
        Assert.True(popup.IndexOf("class=\"countdown\"",StringComparison.Ordinal)<popup.IndexOf("class=\"overlay-mount-needs\"",StringComparison.Ordinal));
        Assert.Contains("need-endurance is-full",popup);Assert.Contains("need-maturity is-partial",popup);Assert.Contains("need-love is-partial",popup);
        Assert.Contains("bi-gender-male",popup);Assert.Contains("Niv. 96",popup);Assert.Contains("G2",popup);Assert.Contains("bi-egg-fill",popup);
        Assert.Contains("mount-needs-training",popup);
    }

    [Fact]
    public async Task ClickingAMountNameCopiesOnlyItsSpeciesName()
    {
        IEnumerable<string>? copied=null;
        var component=new OverlayMountRow();
        typeof(OverlayMountRow).GetProperty(nameof(OverlayMountRow.Mount))!.SetValue(component,
            new OverlayMountReading("Dragodinde Amande",new(Guid.NewGuid(),96,0,100,10000),1,DateTime.UtcNow.AddHours(1)));
        typeof(OverlayMountRow).GetProperty(nameof(OverlayMountRow.CopyRequested))!.SetValue(component,
            EventCallback.Factory.Create<IEnumerable<string>>(new object(),names=>copied=names.ToArray()));
        await (Task)typeof(OverlayMountRow).GetMethod("CopyName",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(component,null)!;
        Assert.Equal(new[]{"Dragodinde Amande"},copied);
    }
}
