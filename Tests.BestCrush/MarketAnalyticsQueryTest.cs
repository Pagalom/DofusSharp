using System.Data.Common;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class MarketAnalyticsQueryTest(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly DateTime At = new(2026,11,1,12,0,0,DateTimeKind.Utc);
    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
        { Reads++; return ValueTask.FromResult(result); }
    }

    [Fact]
    public async Task PositiveStatCriteriaUseLightCatalogAndRejectNegativeBonuses()
    {
        using var database=new TestDatabase(); await using var db=database.CreateContext();
        foreach(var (id,bonus,type,level) in new[]{(1L,10,EquipmentType.Boots,20),(2L,-10,EquipmentType.Boots,20),(3L,10,EquipmentType.Ring,5)})
        {
            var equipment=new Equipment(id){Name=$"Objet {id}",Type=type,Level=level};
            equipment.Characteristics.Add(new(equipment,Characteristic.Wisdom,bonus,bonus)); db.Equipments.Add(equipment);
        }
        db.Runes.AddRange(new Rune(100){Name="Rune Sa",Characteristic=Characteristic.Wisdom},new Rune(101){Name="Rune Fo",Characteristic=Characteristic.Strength});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var service=new MarketAnalyticsService(db); var catalog=await service.GetCatalogAsync();
        var equipmentGroup=new MarketAnalyticsSeriesDefinition {SelectionMode=MarketAnalyticsSelectionMode.Criteria,ObjectType=MarketObjectType.Equipment,Characteristic=Characteristic.Wisdom,LevelMin=10,EquipmentTypes=[EquipmentType.Boots]};
        Assert.Equal(1,MarketAnalyticsService.ResolveMembers(equipmentGroup,catalog).Single().DofusDbId);
        var runeGroup=new MarketAnalyticsSeriesDefinition {SelectionMode=MarketAnalyticsSelectionMode.Criteria,ObjectType=MarketObjectType.Rune,Characteristic=Characteristic.Wisdom};
        Assert.Equal(100,MarketAnalyticsService.ResolveMembers(runeGroup,catalog).Single().DofusDbId);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ManyOverlappingGroupsUseOneScopedReadAndRespectServerTypeDateAndSources()
    {
        using var database=new TestDatabase(); await using var seed=database.CreateContext();
        seed.Runes.AddRange(new Rune(1){Name="Rune A"},new Rune(2){Name="Rune B"});
        seed.Resources.Add(new Resource(1){Name="Ressource même ID"});
        foreach(var row in new[]{Price(1,"A",At,10),Price(1,"A",At.AddHours(1),20),Price(2,"A",At,1000),Price(2,"A",At.AddHours(1),1000),Price(1,"B",At,999),Price(1,"A",At.AddHours(5),999),Price(1,"A",At,999,MarketPriceSource.Manual)})seed.MarketPriceObservations.Add(row);
        seed.MarketPriceObservations.Add(new(){ObjectType=MarketObjectType.Resource,DofusDbId=1,ServerName="A",Price=999,Quantity=1,Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=At});
        await seed.SaveChangesAsync();
        var counter=new ReadCounter();
        await using var db=new BestCrushDbContext(new DbContextOptionsBuilder<BestCrushDbContext>().UseSqlite(seed.Database.GetDbConnection()).AddInterceptors(counter).Options);
        var service=new MarketAnalyticsService(db);var catalog=await service.GetCatalogAsync();counter.Reads=0;
        var definitions=Enumerable.Range(0,8).Select(i=>new MarketAnalyticsSeriesDefinition {SelectionMode=MarketAnalyticsSelectionMode.AllOfType,ObjectType=MarketObjectType.Rune,ItemName=$"Runes {i}"}).ToArray();
        var result=await service.BuildComparisonAsync(new("A",definitions,MarketAnalyticsPeriod.Hours24,At,At.AddHours(2)),catalog);
        Assert.Equal(1,counter.Reads);Assert.Equal(4,result.ObservationsRead);Assert.Equal(8,result.Groups.Count);
        Assert.All(result.Groups,g=>Assert.Equal(150,g.FusedSamples[1].Index));
        foreach(var definition in definitions)definition.FuseMembers=false;
        Assert.Equal(16,MarketAnalyticsProjection.Curves(result,definitions).Count);Assert.Equal(1,counter.Reads);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CustomSelectionsCanMixFamiliesWithoutConfusingIdenticalIds()
    {
        using var database=new TestDatabase();await using var db=database.CreateContext();
        db.Runes.Add(new Rune(1){Name="Rune"});db.Resources.Add(new Resource(1){Name="Ressource"});
        db.MarketPriceObservations.Add(Price(1,"A",At,10));
        db.MarketPriceObservations.Add(new(){ObjectType=MarketObjectType.Resource,DofusDbId=1,ServerName="A",Price=1000,Quantity=1,Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=At});
        await db.SaveChangesAsync();var service=new MarketAnalyticsService(db);var catalog=await service.GetCatalogAsync();
        var definition=new MarketAnalyticsSeriesDefinition {SelectionMode=MarketAnalyticsSelectionMode.Custom,Members=[new(MarketObjectType.Rune,1,"Rune"),new(MarketObjectType.Resource,1,"Ressource")]};
        var result=await service.BuildComparisonAsync(new("A",[definition],MarketAnalyticsPeriod.Hours24,At,At.AddHours(1)),catalog);
        Assert.Equal(2,result.Groups.Single().Members.Count);Assert.Equal(505,result.Groups.Single().FusedSamples[0].Price);
    }

    [Fact]
    public async Task CanceledComparisonDoesNotStartARead()
    {
        using var database=new TestDatabase();await using var seed=database.CreateContext();var counter=new ReadCounter();
        await using var db=new BestCrushDbContext(new DbContextOptionsBuilder<BestCrushDbContext>().UseSqlite(seed.Database.GetDbConnection()).AddInterceptors(counter).Options);
        using var stop=new CancellationTokenSource();stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new MarketAnalyticsService(db).BuildComparisonAsync(new("A",[],MarketAnalyticsPeriod.Days7),[],stop.Token));
        Assert.Equal(0,counter.Reads);
    }

    private static MarketPriceObservation Price(long id,string server,DateTime at,long price,MarketPriceSource source=MarketPriceSource.InGameAutomatic)=>new(){ObjectType=MarketObjectType.Rune,DofusDbId=id,ServerName=server,Price=price,Quantity=1,Source=source,ObservedAtUtc=at};

    [Fact]
    public async Task ThousandObjectComparisonReadsOnceAndKeepsBoundedHistories()
    {
        using var database=new TestDatabase();await using var seed=database.CreateContext();
        seed.Runes.AddRange(Enumerable.Range(1,1000).Select(id=>new Rune(id){Name=$"Rune {id:0000}"}));
        for(int id=1;id<=1000;id++)for(int hour=0;hour<=24;hour++)seed.MarketPriceObservations.Add(Price(id,"A",At.AddHours(hour),id*100+hour*10));
        await seed.SaveChangesAsync();seed.ChangeTracker.Clear();
        var counter=new ReadCounter();
        await using var db=new BestCrushDbContext(new DbContextOptionsBuilder<BestCrushDbContext>().UseSqlite(seed.Database.GetDbConnection()).AddInterceptors(counter).Options);
        var service=new MarketAnalyticsService(db);var catalog=await service.GetCatalogAsync();counter.Reads=0;
        var definition=new MarketAnalyticsSeriesDefinition{SelectionMode=MarketAnalyticsSelectionMode.AllOfType,ObjectType=MarketObjectType.Rune,ItemName="Toutes les runes"};
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var result=await service.BuildComparisonAsync(new("A",[definition],MarketAnalyticsPeriod.Hours24,At,At.AddHours(24)),catalog);
        watch.Stop();output.WriteLine($"1000 objets, 25000 relevés : {watch.ElapsedMilliseconds} ms, {counter.Reads} lecture SQL.");
        Assert.Equal(1,counter.Reads);Assert.Equal(25000,result.ObservationsRead);Assert.Equal(1000,result.Groups.Single().BaselineMemberCount);
        Assert.All(result.Groups.Single().Members,m=>Assert.InRange(m.Samples.Count,2,160));
        Assert.True(result.Groups.Single().FusedSamples.Last().Index>100);
    }
}
