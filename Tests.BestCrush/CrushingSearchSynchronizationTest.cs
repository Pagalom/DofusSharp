using System.Reflection;
using BestCrush.Domain.Models;
using BestCrush.Models;
using BestCrush.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ServerPage=BestCrush.Components.Pages.Server;

namespace Tests.BestCrush;

public sealed class CrushingSearchSynchronizationTest
{
    [Fact] public void ModuleAndOverlayExcludeTheSameNegativeOrZeroRuneLinesBeforeAnalysis()
    {
        var definitions=new[]{("Casque du Chafer",-50,-50),("Choipo Podlard",-20,-20),("Coiffe du Koulosse",-100,-100),
            ("Couronne du Wa Wabbit",-30,-30),("Dora",-25,-25),("Sans bonus",0,0),("Bonus positif",1,20)};
        var items=definitions.Select((row,index)=>
        {
            var item=new Equipment(index+1){Name=row.Item1,Level=100,Type=EquipmentType.Hat};
            item.Characteristics.Add(new(item,Characteristic.Chance,row.Item2,row.Item3));return item;
        }).ToArray();
        var rune=new Rune(10){Name="Rune Cha",Characteristic=Characteristic.Chance};
        var model=new SearchItemsModel();model.RuneFilters.Add(new(){DofusDbId=10,Characteristic=Characteristic.Chance,RuneName=rune.Name});
        var page=new ServerPage{ServerName="Kourial"};const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(ServerPage).GetProperty("Model",hidden)!.SetValue(page,model);
        var module=((IEnumerable<Equipment>)typeof(ServerPage).GetMethod("FilterItemsCore",hidden)!.Invoke(page,[items,true])!).ToArray();
        var overlay=new OverlayEquipmentSearchState();overlay.Load(items,[rune]);overlay.RuneIds.Add(10);overlay.Apply();
        Assert.Equal("Bonus positif",Assert.Single(module).Name);
        Assert.Equal(module.Select(e=>e.DofusDbId),overlay.Results.Select(e=>e.DofusDbId));
    }
    private static SearchItemsModel Filters(string text="Danathor")
    {
        var model=new SearchItemsModel{SearchText=text,LevelMin=150,LevelMax=200,CraftJobId=27,RuneMatchMode=RuneFilterMatchMode.All};
        model.EquipmentType.SetSelectedTypes([EquipmentType.Hat,EquipmentType.Cloak]);
        model.RuneFilters.Add(new(){DofusDbId=10,Characteristic=Characteristic.Wisdom,RuneName="Rune Sa"});return model;
    }
    private static OverlayWorkspaceService Data(IServiceProvider services,CurrentServerState servers) => new(services.GetRequiredService<IServiceScopeFactory>(),servers,new(),null!,null!,new(),NullLogger<OverlayWorkspaceService>.Instance);
    [Fact] public void CommonFiltersCopyBothWaysWithoutSharingMutableListsOrChangingModuleEconomics()
    {
        var original=Filters();var snapshot=CrushingSearchFilters.From(original);
        original.RuneFilters[0].RuneName="Modifié";original.EquipmentType.SetSelectedTypes([]);
        var target=new SearchItemsModel{MinimumBenefit=12345,MinimumRoiPercent=10};snapshot.ApplyTo(target);
        Assert.Equal("Danathor",target.SearchText);Assert.Equal(27,target.CraftJobId);Assert.Equal(150,target.LevelMin);Assert.Equal(200,target.LevelMax);
        Assert.Equal(RuneFilterMatchMode.All,target.RuneMatchMode);Assert.Equal("Rune Sa",Assert.Single(target.RuneFilters).RuneName);
        Assert.Equal(2,target.EquipmentType.GetSelectedTypes().Length);Assert.Equal(12345,target.MinimumBenefit);Assert.Equal(10,target.MinimumRoiPercent);
        target.RuneFilters.Clear();Assert.Single(snapshot.Runes);
    }
    [Fact] public void RejectsStaleResultsAndKeepsEachServersSearchIndependent()
    {
        var sync=new CrushingSearchSynchronization();var origin=Guid.NewGuid();var events=0;sync.Changed+=_=>events++;
        var first=sync.Publish("Kourial",CrushingSearchFilters.From(Filters()),true,origin);
        var second=sync.Publish("Kourial",CrushingSearchFilters.From(Filters("Autre")),false,origin);
        Assert.False(sync.PublishResults("Kourial",first.Version,[new(1){Name="Ancien résultat"}]));
        Assert.True(sync.PublishResults("Kourial",second.Version,[new(2){Name="Bon résultat"}]));
        sync.Publish("Brial",CrushingSearchFilters.From(Filters("Brial")),true,origin);
        Assert.Equal("Autre",sync.ForServer("Kourial")!.Filters.Text);Assert.Equal("Brial",sync.ForServer("Brial")!.Filters.Text);
        Assert.Equal("Bon résultat",Assert.Single(sync.ForServer("Kourial")!.Results!).Name);
        var before=events;sync.Publish("Kourial",CrushingSearchFilters.From(Filters("Autre")),false,origin);Assert.Equal(before,events);
    }
    [Fact] public void ModuleEditsReachOverlayAndOverlayEditsReturnAllCommonCriteria()
    {
        using var services=new ServiceCollection().BuildServiceProvider();var servers=new CurrentServerState();servers.SelectServer("Kourial");var data=Data(services,servers);
        data.CrushingSearch.Publish("Kourial",CrushingSearchFilters.From(Filters()),false,Guid.NewGuid());
        Assert.Equal("Danathor",data.EquipmentSearch.Text);Assert.Equal(27,data.EquipmentSearch.JobId);Assert.Equal(2,data.EquipmentSearch.Types.Count);
        Assert.Contains(10,data.EquipmentSearch.RuneIds);Assert.Equal(RuneFilterMatchMode.All,data.EquipmentSearch.RuneMatchMode);
        data.EquipmentSearch.Text="Coiffe";data.EquipmentSearch.LevelMin=190;data.EquipmentSearch.Types.Remove(EquipmentType.Cloak);data.PublishEquipmentSearchFilters();
        var module=new SearchItemsModel();data.CrushingSearch.ForServer("Kourial")!.Filters.ApplyTo(module);
        Assert.Equal("Coiffe",module.SearchText);Assert.Equal(190,module.LevelMin);Assert.Equal(EquipmentType.Hat,Assert.Single(module.EquipmentType.GetSelectedTypes()));
        Assert.Equal(Characteristic.Wisdom,Assert.Single(module.RuneFilters).Characteristic);
        data.CrushingSearch.Publish("Brial",CrushingSearchFilters.From(Filters("Autre serveur")),false,Guid.NewGuid());Assert.Equal("Coiffe",data.EquipmentSearch.Text);
    }
    [Fact] public void ModuleResultsRefreshOverlayOrderWithoutRepeatingAnAnalysisOrOpeningAClosedList()
    {
        using var services=new ServiceCollection().BuildServiceProvider();var servers=new CurrentServerState();servers.SelectServer("Kourial");var data=Data(services,servers);
        var model=new SearchItemsModel{SearchText="Anneau"};var update=data.CrushingSearch.Publish("Kourial",CrushingSearchFilters.From(model),false,Guid.NewGuid());
        Assert.True(data.CrushingSearch.PublishResults("Kourial",update.Version,[new(2){Name="Anneau B"},new(1){Name="Anneau A"}]));
        Assert.True(data.EquipmentSearch.HasSearched);Assert.Equal(new long[]{2,1},data.EquipmentSearch.Results.Select(e=>e.DofusDbId));
        data.EquipmentSearch.ResultsOpen=false;data.CrushingSearch.PublishResults("Kourial",update.Version,[new(1){Name="Anneau A"},new(2){Name="Anneau B"}]);
        Assert.False(data.EquipmentSearch.ResultsOpen);Assert.Equal(1,data.EquipmentSearch.Results[0].DofusDbId);
    }
    [Fact] public void ModuleOpenedLaterRestoresOverlayFiltersAndQueuesTheRequestedAnalysis()
    {
        using var services=new ServiceCollection().BuildServiceProvider();var data=Data(services,new CurrentServerState());
        data.CrushingSearch.Publish("Kourial",CrushingSearchFilters.From(Filters()),true,Guid.NewGuid());
        var page=new ServerPage{ServerName="Kourial"};const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(ServerPage).GetField("_sharedSearchHost",hidden)!.SetValue(page,data);
        typeof(ServerPage).GetProperty("Model",hidden)!.SetValue(page,new SearchItemsModel());
        typeof(ServerPage).GetMethod("ReconcileSharedCrushingSearch",hidden)!.Invoke(page,null);
        var model=(SearchItemsModel)typeof(ServerPage).GetProperty("Model",hidden)!.GetValue(page)!;
        Assert.Equal("Danathor",model.SearchText);Assert.Equal(27,model.CraftJobId);Assert.Equal(2,model.EquipmentType.GetSelectedTypes().Length);
        Assert.True((bool)typeof(ServerPage).GetField("_pendingSharedAnalysis",hidden)!.GetValue(page)!);
        model.SearchText="Retour module";typeof(ServerPage).GetMethod("PublishCommonCrushingFilters",hidden)!.Invoke(page,[false]);
        Assert.Equal("Retour module",data.CrushingSearch.ForServer("Kourial")!.Filters.Text);
    }
}
