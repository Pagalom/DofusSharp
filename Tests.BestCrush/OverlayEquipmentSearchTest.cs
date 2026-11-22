using System.Net;
using System.Reflection;
using BestCrush.Components.Overlays;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Models;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class OverlayEquipmentSearchTest
{
    private static Equipment Item(long id, string name, int level, EquipmentType type, params (Characteristic Stat,int From,int To)[] lines)
    {
        var item=new Equipment(id){Name=name,Level=level,Type=type};
        item.Characteristics=lines.Select(l=>new ItemCharacteristicLine(item,l.Stat,l.From,l.To)).ToArray();return item;
    }
    private static readonly Rune[] Runes=[new(10){Name="Rune Fo",Characteristic=Characteristic.Strength},new(11){Name="Rune Sa",Characteristic=Characteristic.Wisdom}];
    [Fact] public void CombinesNameJobTypesLevelsAndAnyOrAllPositiveAverageRunes()
    {
        var state=new OverlayEquipmentSearchState();
        var a=Item(1,"Anneau A",100,EquipmentType.Ring,(Characteristic.Strength,1,20),(Characteristic.Wisdom,2,3));
        var b=Item(2,"Anneau B",120,EquipmentType.Ring,(Characteristic.Strength,10,20));
        var c=Item(3,"Anneau C",130,EquipmentType.Ring,(Characteristic.Wisdom,-5,-1));
        var d=Item(4,"Amulette",100,EquipmentType.Amulet,(Characteristic.Strength,1,2));
        state.Load([a,b,c,d],Runes);state.LoadJobs([new(16,"Bijoutier",[1,2,3,4])]);
        state.Text="anneau";state.JobId=16;state.Types.Add(EquipmentType.Ring);state.LevelMin=100;state.LevelMax=120;
        state.RuneIds.UnionWith([10,11]);state.Apply();Assert.Equal(new long[]{1,2},state.Results.Select(e=>e.DofusDbId));
        state.RuneMatchMode=RuneFilterMatchMode.All;state.Apply();Assert.Equal(1,Assert.Single(state.Results).DofusDbId);
        state.ResetFilters();state.Text="";state.RuneIds.Add(11);state.Apply();Assert.Equal(1,Assert.Single(state.Results).DofusDbId);
    }
    [Fact] public void ResultsSurviveClosingDraftEditsAndCatalogueUpdatesWithoutKeepingRecipeGraphs()
    {
        var state=new OverlayEquipmentSearchState();var source=Item(1,"Anneau A",100,EquipmentType.Ring,(Characteristic.Strength,10,20));
        state.Load([source],Runes);state.Text="Anneau";state.Apply();
        state.ResultsOpen=false;state.Text="Autre recherche non validée";
        state.Merge([Item(1,"Anneau A",101,EquipmentType.Ring,(Characteristic.Strength,20,30)),Item(2,"Anneau B",110,EquipmentType.Ring)]);
        Assert.Equal(2,state.Results.Count);Assert.False(state.ResultsOpen);Assert.Equal("Autre recherche non validée",state.Text);
        Assert.Equal(101,state.Results[0].Level);Assert.Equal(20,Assert.Single(state.Results[0].Characteristics).From);
        Assert.NotSame(source,state.Results[0]);Assert.Empty(state.Results[0].Recipe);Assert.Empty(state.Results[0].EquipmentRecipe);
        state.LevelMin=150;state.LevelMax=100;state.Apply();Assert.NotNull(state.Error);Assert.Equal(2,state.Results.Count);
    }
    [Fact] public async Task LoadsNonCraftableItemsLocallyAndDoesNotReloadTheCatalogueOnEverySearch()
    {
        using var database=new TestDatabase();using(var db=database.CreateContext())
        {db.Equipments.Add(Item(1,"Sans recette",1,EquipmentType.Sword,(Characteristic.Strength,10,20)));db.Runes.AddRange(Runes.Select(r=>new Rune(r.DofusDbId){Name=r.Name,Characteristic=r.Characteristic}));await db.SaveChangesAsync();}
        using var provider=new ServiceCollection().AddSingleton<IDbContextFactory<BestCrushDbContext>>(database).BuildServiceProvider();
        var data=Data(provider);await data.PrepareEquipmentSearchAsync();data.EquipmentSearch.Apply();
        Assert.Equal("Sans recette",Assert.Single(data.EquipmentSearch.Results).Name);
        using(var db=database.CreateContext()){db.Equipments.Add(Item(2,"Ajout ultérieur",1,EquipmentType.Ring));await db.SaveChangesAsync();}
        await data.PrepareEquipmentSearchAsync();data.EquipmentSearch.Apply();Assert.Single(data.EquipmentSearch.Results);
        data.PublishEquipmentSearchCatalogue([Item(2,"Ajout ultérieur",1,EquipmentType.Ring)]);Assert.Equal(2,data.EquipmentSearch.Results.Count);
    }
    [Fact] public async Task SearchFocusUsesFullEquipmentAverageEvenWithConservativeSettings()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var item=Item(1,"Anneau",100,EquipmentType.Ring,(Characteristic.Strength,10,30));db.Equipments.Add(item);
        db.Runes.Add(new(10){Name="Rune Fo",Characteristic=Characteristic.Strength});
        db.CoefficientObservations.Add(new(){DofusDbId=1,ServerName="Kourial",CoefficientPercent=100,ObservedAtUtc=DateTime.UtcNow,Source=CoefficientSource.InGameAutomatic});await db.SaveChangesAsync();
        var priority=new TestDataPriorityProvider();var prices=new MarketPriceService(db,priority);var settings=new Conservative();
        using var provider=new ServiceCollection().AddSingleton(new ItemsService(db,null!,null!,null!,null!)).AddSingleton(prices)
            .AddSingleton(new CoefficientService(db,priority)).AddSingleton(new CraftCostService(prices)).AddSingleton(new CrushService(database))
            .AddSingleton<IBestCrushSettingsProvider>(settings).AddSingleton<EquipmentProfitabilityService>().BuildServiceProvider();
        var data=Data(provider);Assert.True(await data.FocusSearchEquipmentAsync(1));Assert.Null(data.Error);
        Assert.Equal("Jet moyen",data.Profitability!.RollLabel);Assert.Equal("Kourial",data.Profitability.Server);
        Assert.Equal(30,Assert.Single(((Equipment)data.Profitability.Item).Characteristics).To);
        var expected=new CrushService(database).GetCrushResult(new(){{Characteristic.Strength,20}},100,1).Single().Value;
        Assert.Equal(expected,data.Profitability.Scenario(null)!.Runes.Single().Value,6);
    }
    [Fact] public async Task CompactFiltersUseNamedIconsAndKeepSearchResultsWhenReopened()
    {
        using var bootstrap=new ServiceCollection().BuildServiceProvider();var data=Data(bootstrap);
        data.EquipmentSearch.Load([Item(1,"Anneau A",100,EquipmentType.Ring)],Runes);data.EquipmentSearch.LoadJobs([new(16,"Bijoutier",[1])]);
        data.EquipmentSearch.Apply();data.EquipmentSearch.FiltersOpen=true;
        using var provider=new ServiceCollection().AddLogging().AddSingleton(data).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        async Task<string> Html()=>await renderer.Dispatcher.InvokeAsync(async()=>WebUtility.HtmlDecode((await renderer.RenderComponentAsync<ProfitabilitySearch>()).ToHtmlString()));
        var filters=await Html();Assert.Contains("img/dofus/jobs/16.png",filters);Assert.Contains("title=\"Bijoutier\"",filters);Assert.Contains("title=\"Rune Fo\"",filters);
        Assert.DoesNotContain("ROI",filters);Assert.Contains("Niveau minimum",filters);Assert.Contains("Mes équipements",filters);
        data.EquipmentSearch.FiltersOpen=false;var results=await Html();Assert.Contains("Anneau A",results);Assert.Contains("Jet moyen",results);
    }
    private static OverlayWorkspaceService Data(IServiceProvider services)
    {var servers=new CurrentServerState();servers.SelectServer("Kourial");return new(services.GetRequiredService<IServiceScopeFactory>(),servers,new(),null!,null!,new(),NullLogger<OverlayWorkspaceService>.Instance);}
    private sealed class Conservative:IBestCrushSettingsProvider
    {
        public bool EquipmentCaptureEnabled=>true;public bool RuneCaptureEnabled=>true;public bool ResourceCaptureEnabled=>true;public bool CoefficientCaptureEnabled=>true;
        public CrushYieldEstimationMode CrushYieldEstimationMode=>CrushYieldEstimationMode.Conservative;public double TargetRoiPercent=>0;public double CrushValueDiscountPercent=>0;
    }
}
