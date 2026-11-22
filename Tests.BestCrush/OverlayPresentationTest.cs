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
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.BestCrush;

public sealed class OverlayPresentationTest
{
    private static OverlayWorkspaceService Data(IServiceScopeFactory scopes)=>new(scopes,new CurrentServerState(),new CurrentDofusClientsState(),
        null!,null!,new MarketDataChangeNotifier(),NullLogger<OverlayWorkspaceService>.Instance);
    private static void RegisterWindow(OverlayWindowsService host,OverlayWindowView window)=>
        ((Dictionary<string,OverlayWindowView>)typeof(OverlayWindowsService).GetField("_windows",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(host)!).Add(window.Id,window);
    [Fact] public async Task ControlExposesAllBusinessOverlaysIncludingRuneRanking()
    {
        var services=new ServiceCollection().AddLogging();using var bootstrap=services.BuildServiceProvider();
        var data=Data(bootstrap.GetRequiredService<IServiceScopeFactory>());var host=new OverlayWindowsService(data,null!);
        RegisterWindow(host,new(){Id="control",Kind=WorkspaceOverlayKind.Control,Title="Panneau de contrôle"});
        services.AddSingleton(data);services.AddSingleton(host);using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>{
            var root=await renderer.RenderComponentAsync<OverlayRoot>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"WindowId","control"}}));
            return WebUtility.HtmlDecode(root.ToHtmlString());});
        foreach(var label in new[]{"Rentabilité","Résultat du concassage","Forgemagie","Élevage","Familiers","Classement runes","Parcours"})Assert.Contains($"aria-label=\"{label}\"",html);
        Assert.DoesNotContain("Mise à jour marché",html);
    }
    [Fact] public void RecallingResultIsIndependentFromNewItemFocusAndLiveRepricing()
    {
        using var provider=new ServiceCollection().BuildServiceProvider();var data=Data(provider.GetRequiredService<IServiceScopeFactory>());
        var history=new CrushHistorySession("TEST",DateTime.UtcNow,DateTime.UtcNow,500,5,475);
        history.Equipments.Add(new(history,1,null,"Bottes mémorisées",200,CoefficientSource.InGameAutomatic,1));
        history.Runes.Add(new(history,10,null,"Rune Fo",5,500));
        data.RecallCrush(history);
        var recalled=data.Module(WorkspaceOverlayKind.Crush);
        data.PublishCrush(new(false,0,0,null,null,1,[new("Rune Ine",10,900,[])],900,5,855,null),"Autre objet","TEST");
        Assert.Same(recalled,data.Module(WorkspaceOverlayKind.Crush));
        Assert.Equal("Bottes mémorisées",recalled.Cards.Single().Title);
        Assert.Equal("Rune Fo",recalled.Cards.Single().Lines.Single().Name);
        data.BeginCrush();
        data.PublishCrush(new(false,0,0,null,null,1,[new("Rune Ine",10,900,[])],900,5,855,null),"Autre objet","TEST");
        Assert.Equal("Autre objet",data.Module(WorkspaceOverlayKind.Crush).Cards.Single().Title);
    }
    [Fact] public async Task PopupShowsTotalAndUnitPricesAndOnlyOneFocusAtATime()
    {
        var services=new ServiceCollection().AddLogging();using var bootstrap=services.BuildServiceProvider();
        var data=Data(bootstrap.GetRequiredService<IServiceScopeFactory>());var host=new OverlayWindowsService(data,null!);
        RegisterWindow(host,new(){Id="popup",Kind=WorkspaceOverlayKind.Popup,Title="Craft",Popup=new("popup","Craft",[new("Bois test",24,300,10,"30 achetés · 6 conservés")])});
        services.AddSingleton(data);services.AddSingleton(host);using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>{
            var root=await renderer.RenderComponentAsync<OverlayRoot>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"WindowId","popup"}}));
            return WebUtility.HtmlDecode(root.ToHtmlString());});
        Assert.Contains("300 K",html);Assert.Contains(10d.ToString("N2")+" K / u",html);Assert.Contains("30 achetés",html);
        Assert.Contains("Épingler",html);Assert.DoesNotContain("Copier la liste",html);
        Assert.Equal(8,System.Text.RegularExpressions.Regex.Matches(html,"class=\"resize-edge ").Count);
    }
    private sealed class CaptureActivator:IComponentActivator
    {
        public global::BestCrush.Components.Pure.OverlayFocusButton? Focus;
        public IComponent CreateInstance(Type type)
        {
            var value=(IComponent)Activator.CreateInstance(type)!;
            if(value is global::BestCrush.Components.Pure.OverlayFocusButton button)Focus=button;
            return value;
        }
    }
    [Fact] public async Task EquipmentHistoryFocusRetainsTheRealServerAndItem()
    {
        var activator=new CaptureActivator();
        using var provider=new ServiceCollection().AddLogging().AddSingleton(new ItemsService(null!,null!,null!,null!,null!))
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var item=new Equipment(100){Name="Objet test",Level=100,Type=EquipmentType.Ring};
        var row=new ItemHistoryRow{ObservationId=Guid.NewGuid(),DofusDbId=100,Name=item.Name,Level=100,Type=item.Type,
            DataKind=ItemHistoryDataKind.Price,Quantity=1,Value=725652,MarketSource=MarketPriceSource.InGameAutomatic,
            IsCleared=false,ObservedAtUtc=DateTime.UtcNow,SourceSortOrder=0,DataKindSortOrder=0};
        await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<global::BestCrush.Components.Pure.HistoryObservationTable>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"ServerName","Kourial"},{"IsEquipment",true},
                {"ItemRows",new[]{row}},{"Items",new Dictionary<long,IItem>{{100,item}}}})));
        Assert.NotNull(activator.Focus);Assert.Equal("Kourial",activator.Focus.ServerName);Assert.Same(item,activator.Focus.Item);
    }

    [Fact] public void RuneComparisonMatchesStableIdsAcrossSeparateDatabaseReads()
    {
        var normalRune=new Rune(10){Name="Rune Fo",Characteristic=Characteristic.Strength};
        var focusedRune=new Rune(10){Name="Rune Fo",Characteristic=Characteristic.Strength};
        var item=new Equipment(100){Name="Objet test"};
        var coefficient=new CoefficientObservation{DofusDbId=100,ServerName="TEST",CoefficientPercent=100,
            ObservedAtUtc=DateTime.UtcNow,Source=CoefficientSource.InGameAutomatic};
        var scenario=new EquipmentProfitabilityScenario(item,coefficient,null,new([]),null,null,null,null,null,
            new Dictionary<Rune,double>{{normalRune,12.5}},new Dictionary<Rune,MarketValueResult>{{normalRune,new(1250,false)}});
        Assert.NotSame(normalRune,focusedRune);
        Assert.Equal(12.5,OverlayRoot.RuneQuantity(scenario,focusedRune.DofusDbId));
        Assert.Equal(1250,OverlayRoot.RunePrice(scenario,focusedRune.DofusDbId)?.Value);
    }

    [Fact] public async Task UnknownCoefficientStillListsPotentialRunesAndTheirKnownUnitPrices()
    {
        var services=new ServiceCollection().AddLogging();using var bootstrap=services.BuildServiceProvider();
        var data=Data(bootstrap.GetRequiredService<IServiceScopeFactory>());var host=new OverlayWindowsService(data,null!);
        var item=new Equipment(100){Name="Objet sans coefficient"};
        var rune=new Rune(10){Name="Rune Fo",Characteristic=Characteristic.Strength};
        var crush=new EquipmentProfitabilityResult(item,null,null,new([]),[],[]){PotentialRunes=[rune],
            PotentialRuneUnitValues=new Dictionary<Rune,MarketValueResult>{{rune,new(79,false)}}};
        var snapshot=new ProfitabilityOverlaySnapshot(item,"TEST","Jet moyen",crush,145,null,null,[],[],[]);
        RegisterWindow(host,new(){Id="runes",Kind=WorkspaceOverlayKind.Popup,Title="Comparer les focus",Popup=new("runes","Comparer les focus",[],snapshot)});
        services.AddSingleton(data);services.AddSingleton(host);using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>WebUtility.HtmlDecode((await renderer.RenderComponentAsync<OverlayRoot>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"WindowId","runes"}}))).ToHtmlString()));
        Assert.Contains("Rune Fo",html);Assert.Contains(79d.ToString("N2")+" K / u",html);
        Assert.Contains("Coefficient non relevé",html);Assert.Contains("disabled",html);
        Assert.Null(snapshot.GrossRuneValue(null));Assert.Null(snapshot.Gain(OverlayRoute.PurchaseCrush,null));
    }
    [Fact] public void RuneListMergesUnpricedAndCalculatedRunesByStableIdentity()
    {
        var item=new Equipment(100);var rune=new Rune(10){Name="Rune Fo",Characteristic=Characteristic.Strength};
        var copy=new Rune(10){Name="Rune Fo",Characteristic=Characteristic.Strength};
        var scenario=new EquipmentProfitabilityScenario(item,null!,null,new([]),null,null,null,null,Characteristic.Strength,
            new Dictionary<Rune,double>{{copy,10}},new Dictionary<Rune,MarketValueResult>());
        var crush=new EquipmentProfitabilityResult(item,null,null,new([]),[scenario],[]){PotentialRunes=[rune]};
        var snapshot=new ProfitabilityOverlaySnapshot(item,"TEST","Jet moyen",crush,null,null,null,[],[],[]);
        Assert.Single(OverlayRoot.RuneChoices(snapshot));
    }

    [Fact] public void MainOverlaysStayVisibleOutsideTheGameButRespectExplicitHiding()
    {
        foreach(var kind in Enum.GetValues<WorkspaceOverlayKind>().Where(k=>k!=WorkspaceOverlayKind.Popup))
        {
            var view=new OverlayWindowView{Id=kind.ToString(),Kind=kind,Title=kind.ToString(),RequestedVisible=true};
            Assert.True(view.ContextVisible(false));
            view.RequestedVisible=false;
            Assert.False(view.ContextVisible(false));Assert.False(view.ContextVisible(true));
        }
        var popup=new OverlayWindowView{Id="popup",Kind=WorkspaceOverlayKind.Popup,Title="Détail",RequestedVisible=true};
        Assert.False(popup.ContextVisible(false));Assert.True(popup.ContextVisible(true));
        popup.Pinned=true;Assert.True(popup.ContextVisible(false));
        popup.RequestedVisible=false;Assert.False(popup.ContextVisible(false));
    }

    [Fact]
    public async Task StandardTotalHasSelectionBorderAndCoefficientAppearsOnlyInCrushingHeader()
    {
        var services=new ServiceCollection().AddLogging();using var bootstrap=services.BuildServiceProvider();
        var data=Data(bootstrap.GetRequiredService<IServiceScopeFactory>());var host=new OverlayWindowsService(data,null!);
        var item=new Equipment(30828){Name="Bounihimée",Level=1};
        var rune=new Rune(10057){Name="Rune de chasse",Characteristic=Characteristic.Hunting};
        var coefficient=new CoefficientObservation{DofusDbId=item.DofusDbId,ServerName="Kourial",CoefficientPercent=23.41,
            ObservedAtUtc=DateTime.UtcNow,Source=CoefficientSource.InGameAutomatic};
        EquipmentProfitabilityScenario Scenario(Characteristic? focus,double amount)=>new(item,coefficient,null,new([]),null,null,null,null,focus,
            new Dictionary<Rune,double>{{rune,.05}},new Dictionary<Rune,MarketValueResult>{{rune,new(amount,false)}}){AdjustedRuneValue=amount};
        var normal=Scenario(null,537);var focused=Scenario(Characteristic.Hunting,838);
        var snapshot=new ProfitabilityOverlaySnapshot(item,"Kourial","Jet réel",new(item,coefficient,null,new([]),[normal,focused],[]),897,null,null,[],[],[]){FocusId=Guid.NewGuid()};
        typeof(OverlayWorkspaceService).GetProperty(nameof(OverlayWorkspaceService.Profitability))!.SetValue(data,snapshot);
        data.Selection.SelectFocus(null);data.Selection.SelectRoute(OverlayRoute.PurchaseCrush);
        RegisterWindow(host,new(){Id="profit",Kind=WorkspaceOverlayKind.Profitability,Title="Rentabilité"});
        RegisterWindow(host,new(){Id="runes",Kind=WorkspaceOverlayKind.Popup,Title="Comparer les focus",Popup=new("runes","Comparer les focus",[],snapshot)});
        services.AddSingleton(data);services.AddSingleton(host);using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        async Task<string> Render(string id)=>await renderer.Dispatcher.InvokeAsync(async()=>WebUtility.HtmlDecode((await renderer.RenderComponentAsync<OverlayRoot>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"WindowId",id}}))).ToHtmlString()));
        var popup=await Render("runes");
        Assert.Matches("Total sans focus</td><td><button class=\"value-button[^\"]*selected",popup);
        var crush=await Render("profit");
        Assert.Contains("Niv. 1 ·",crush);Assert.Contains("Jet réel",crush);Assert.DoesNotContain("class=\"roll-selector\"",crush);
        Assert.DoesNotContain("Choisir un exemplaire en inventaire",crush);
        Assert.DoesNotContain("crushed-marker",crush);
        typeof(OverlayWorkspaceService).GetProperty(nameof(OverlayWorkspaceService.Profitability))!.SetValue(data,snapshot with {WasCrushed=true});
        var destroyed=await Render("profit");
        Assert.Contains("bi-hammer crushed-marker",destroyed);Assert.Contains("title=\"Concassé\"",destroyed);
        Assert.Contains(coefficient.CoefficientPercent.ToString("N2")+"%",crush);
        Assert.DoesNotContain("Coefficient relevé",crush);
        data.Selection.SelectRoute(OverlayRoute.PurchaseSale);
        var sale=await Render("profit");
        Assert.DoesNotContain(coefficient.CoefficientPercent.ToString("N2")+"%",sale);
    }

    [Fact]
    public async Task OwnedItemHoverIncludesTheWholeActualRollIconsAndNoFakePrices()
    {
        var item=new Equipment(1){Name="Exemplaire test"};
        var roll=new ForgemagingLine[]{new(){Characteristic=Characteristic.Strength,Current=7,Minimum=1,Maximum=6},
            new(){Characteristic=Characteristic.Chance,Current=-2,Minimum=-5,Maximum=-1},
            new(){Characteristic=Characteristic.Ap,Current=1}};
        var owned=ForgemagedEquipmentStock.Project("instance",item,"inventory",1,DateTime.UtcNow,roll,null,null,new(null,null));
        var popup=OverlayRoot.OwnedRoll(owned);
        Assert.Equal(3,popup.Lines.Count);
        Assert.Equal(new double?[]{7,-2,1},popup.Lines.Select(line=>line.Quantity));
        Assert.All(popup.Lines,line=>{Assert.True(line.IsStatistic);Assert.Null(line.Total);Assert.Null(line.Unit);});
        Assert.Equal("Over",popup.Lines[0].Detail);Assert.Equal("Exo",popup.Lines[2].Detail);
        Assert.Equal(roll.Select(line=>line.Characteristic),popup.Lines.Select(line=>line.Stat!.Value));
        var services=new ServiceCollection().AddLogging();using var bootstrap=services.BuildServiceProvider();
        var data=Data(bootstrap.GetRequiredService<IServiceScopeFactory>());var host=new OverlayWindowsService(data,null!);
        RegisterWindow(host,new(){Id="roll",Kind=WorkspaceOverlayKind.Popup,Title=popup.Title,Popup=popup});
        services.AddSingleton(data);services.AddSingleton(host);using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>WebUtility.HtmlDecode((await renderer.RenderComponentAsync<OverlayRoot>(
            ParameterView.FromDictionary(new Dictionary<string,object?>{{"WindowId","roll"}}))).ToHtmlString()));
        Assert.Contains("bi-diamond-fill earth",html);Assert.Contains("bi-droplet-fill water",html);
        Assert.Contains("bi-lightning-charge-fill",html);Assert.Contains("owned-roll-stat over",html);Assert.Contains("owned-roll-stat exotic",html);
        Assert.DoesNotContain("K / u",html);
    }

}
