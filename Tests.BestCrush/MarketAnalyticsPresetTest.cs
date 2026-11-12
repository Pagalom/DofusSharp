using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class MarketAnalyticsPresetTest
{
    internal sealed class MemoryPreferences : IWorkspacePreferences
    {
        public readonly Dictionary<string,string> Values=[];
        public string? GetString(string key)=>Values.GetValueOrDefault(key);
        public void SetString(string key,string value)=>Values[key]=value;
        public void Remove(string key)=>Values.Remove(key);
    }
    [Fact]
    public void ExistingSavedAnalysesRemainReadableAndAreNotRewrittenOnLoad()
    {
        var preferences=new MemoryPreferences();
        string json=JsonSerializer.Serialize(new[]{new {id=Guid.NewGuid(),name="Ancienne analyse",period=1,series=new[]{new{id=Guid.NewGuid(),objectType=2,dofusDbId=42L,itemName="Rune Sa",lotQuantity=10,valueMode=0,aggregation=0,sourceMode=0,transformation=1}}}});
        preferences.Values["Analytics.MarketPresets.v1"]=json;
        var service=new MarketAnalyticsPresetService(preferences);var preset=service.GetAll().Single();
        Assert.Equal("Ancienne analyse",preset.Name);Assert.Equal(MarketAnalyticsSelectionMode.Single,preset.Series[0].SelectionMode);
        Assert.Equal(42,preset.Series[0].DofusDbId);Assert.Equal(72,preset.MaxQuoteAgeHours);
        Assert.True(preset.Series[0].IsVisible);
        Assert.Equal(json,preferences.Values["Analytics.MarketPresets.v1"]);
    }
    [Fact]
    public void GroupsCriteriaMixedSelectionsAndScaleSurviveSaveAndCloneIndependently()
    {
        var service=new MarketAnalyticsPresetService(new MemoryPreferences());
        var preset=new MarketAnalyticsPreset{Name="Comparaison",Scale=MarketAnalyticsTransformation.Raw,MaxQuoteAgeHours=24,Series=[new(){SelectionMode=MarketAnalyticsSelectionMode.Criteria,Characteristic=Characteristic.Wisdom,EquipmentTypes=[EquipmentType.Boots],LevelMin=10,LevelMax=100,SearchText="Bouftou",FuseMembers=false,IsVisible=false,GroupAggregation=MarketAnalyticsGroupAggregation.Median,Members=[new(MarketObjectType.Rune,42,"Rune Sa")] }]};
        service.Save(preset);preset.Series[0].EquipmentTypes.Clear();preset.Series[0].Members.Clear();
        var saved=service.GetAll().Single();Assert.Equal(MarketAnalyticsTransformation.Raw,saved.Scale);Assert.Equal(24,saved.MaxQuoteAgeHours);
        Assert.Single(saved.Series[0].EquipmentTypes);Assert.Single(saved.Series[0].Members);
        Assert.Equal(Characteristic.Wisdom,saved.Series[0].Characteristic);Assert.False(saved.Series[0].FuseMembers);
        Assert.False(saved.Series[0].IsVisible);
        Assert.Equal(MarketAnalyticsGroupAggregation.Median,saved.Series[0].GroupAggregation);
        var clone=MarketAnalyticsPresetService.Clone(saved);clone.Series[0].EquipmentTypes.Clear();Assert.Single(saved.Series[0].EquipmentTypes);
    }

    [Fact]
    public void LargeSelectionsMigrateToTheLocalFileAndKeepLegacyPreferencesUntouched()
    {
        string path=Path.Combine(Path.GetTempPath(),"bestcrush-analytics-"+Guid.NewGuid()+".json");
        var preferences=new MemoryPreferences();var legacy=new MarketAnalyticsPresetService(preferences);
        legacy.Save(new(){Name="Ancienne"});string original=preferences.Values["Analytics.MarketPresets.v1"];
        try
        {
            var service=new MarketAnalyticsPresetService(preferences,path);
            Assert.Single(service.GetAll());Assert.False(File.Exists(path));
            service.Save(new(){Name="Groupe volumineux",Series=[new(){SelectionMode=MarketAnalyticsSelectionMode.Custom,Members=Enumerable.Range(1,5000).Select(id=>new MarketAnalyticsMember(MarketObjectType.Rune,id,$"Rune {id}")).ToList()}]});
            var reloaded=new MarketAnalyticsPresetService(preferences,path).GetAll();
            Assert.Equal(2,reloaded.Count);Assert.Equal(5000,reloaded.Single(p=>p.Name=="Groupe volumineux").Series[0].Members.Count);
            Assert.Equal(original,preferences.Values["Analytics.MarketPresets.v1"]);
            Assert.True(new FileInfo(path).Length>64000);
        }
        finally {if(File.Exists(path))File.Delete(path);}
    }

    [Fact]
    public void UnreadableFileCannotBeOverwrittenByAnEmptyConfiguration()
    {
        string path=Path.Combine(Path.GetTempPath(),"bestcrush-analytics-"+Guid.NewGuid()+".json");
        try
        {
            File.WriteAllText(path,"JSON interrompu");
            var service=new MarketAnalyticsPresetService(new MemoryPreferences(),path);
            Assert.Throws<InvalidDataException>(()=>service.Save(new(){Name="Nouvelle"}));
            Assert.Equal("JSON interrompu",File.ReadAllText(path));
        }
        finally {if(File.Exists(path))File.Delete(path);}
    }
}
