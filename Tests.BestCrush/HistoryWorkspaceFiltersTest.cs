using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class HistoryWorkspaceFiltersTest
{
    private static readonly DateTime At = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CrushingFeedReusesCoefficientIdsAndMergesBeforePagingWithoutWriting()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        db.Equipments.Add(new Equipment(42) { Name = "Bottes", Type = EquipmentType.Boots });
        db.CoefficientObservations.AddRange(
            Coefficient(42,"A",At,110), Coefficient(42,"A",At.AddHours(-2),130),
            Coefficient(42,"B",At.AddHours(2),999));
        await db.SaveChangesAsync();
        var service = new HistoryService(db);
        var sessionId = await service.SaveCrushSessionAsync(Session("A",At.AddHours(-1)));
        db.ChangeTracker.Clear();
        int count = await db.CoefficientObservations.CountAsync();

        var first = await service.GetCrushingFeedAsync("A",null,null,HistorySortColumn.Date,HistorySortDirection.Descending,2);
        Assert.Equal(2,first.Rows.Count); Assert.True(first.HasMore);
        Assert.Equal(CrushHistoryEntryKind.Coefficient,first.Rows[0].Kind);
        Assert.Equal(sessionId,first.Rows[1].Id);
        Assert.Equal(76,first.Rows[1].Value);
        Assert.Single(first.Rows[1].Session!.Runes.Single().Lots);
        var second = await service.GetCrushingFeedAsync("A",null,null,HistorySortColumn.Date,HistorySortDirection.Descending,2,new() { Skip = 2 });
        Assert.Single(second.Rows); Assert.False(second.HasMore);
        Assert.Empty(first.Rows.Select(r=>r.Id).Intersect(second.Rows.Select(r=>r.Id)));
        var items = await service.GetItemHistoryAsync("A",null,null,null,null,null,HistorySortColumn.Date,HistorySortDirection.Descending,50);
        Assert.Equal(items.Rows.Select(r=>r.ObservationId).Order(),first.Rows.Concat(second.Rows).Where(r=>r.Kind==CrushHistoryEntryKind.Coefficient).Select(r=>r.Id).Order());
        Assert.Equal(count,await db.CoefficientObservations.CountAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task EquipmentFiltersAreAppliedBeforeUnionAndPagination()
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        db.Equipments.AddRange(new Equipment(42){Name="Bottes",Type=EquipmentType.Boots,Level=20},new Equipment(43){Name="Anneau",Type=EquipmentType.Ring,Level=10});
        db.CoefficientObservations.AddRange(Coefficient(42,"A",At,110),Coefficient(43,"A",At,120));
        db.CoefficientObservations.Add(new CoefficientObservation { DofusDbId=42,ServerName="A",CoefficientPercent=999,Source=CoefficientSource.Manual,IsCleared=true,ObservedAtUtc=At.AddHours(1) });
        db.MarketPriceObservations.Add(Price(42,MarketObjectType.Equipment,At.AddHours(2),MarketPriceSource.Manual,10));
        await db.SaveChangesAsync();
        var service = new HistoryService(db);
        var options = new HistoryQueryOptions { ItemDataKind=ItemHistoryDataKind.Coefficient,EquipmentTypes=[EquipmentType.Boots],IncludeCleared=false,FromUtc=At.AddDays(-1),BeforeUtc=At.AddHours(2),Source=HistoryObservationSource.Game };
        var page = await service.GetItemHistoryAsync("A",null,15,25,null,null,HistorySortColumn.Date,HistorySortDirection.Descending,1,options:options);
        Assert.Single(page.Rows); Assert.False(page.HasMore); Assert.Equal(110,page.Rows[0].Value);
        var manual = await service.GetItemHistoryAsync("A",null,null,null,null,null,HistorySortColumn.Date,HistorySortDirection.Descending,50,options:options with {Source=HistoryObservationSource.Manual,IncludeCleared=true});
        Assert.Single(manual.Rows); Assert.True(manual.Rows[0].IsCleared);
    }

    [Theory]
    [InlineData(MarketObjectType.Resource)]
    [InlineData(MarketObjectType.Rune)]
    public async Task MarketFiltersRespectExclusiveDateBoundarySourceLotsAndClearedRows(MarketObjectType type)
    {
        using var database = new TestDatabase();
        await using var db = database.CreateContext();
        if(type==MarketObjectType.Resource)db.Resources.Add(new Resource(42){Name="Ressource"});else db.Runes.Add(new Rune(42){Name="Rune"});
        db.MarketPriceObservations.AddRange(Price(42,type,At,MarketPriceSource.Manual,10),Price(42,type,At.AddHours(1),MarketPriceSource.InGameAutomatic,10),Price(42,type,At.AddHours(2),MarketPriceSource.InGameAutomatic,100),Price(42,type,At.AddHours(3),MarketPriceSource.InGameAutomatic,10),Price(42,type,At.AddMinutes(90),MarketPriceSource.InGameAutomatic,10,true));
        await db.SaveChangesAsync();
        var service=new HistoryService(db);
        var options=new HistoryQueryOptions { FromUtc=At,BeforeUtc=At.AddHours(3),Source=HistoryObservationSource.Game,LotQuantity=10,IncludeCleared=false };
        var page=await service.GetMarketHistoryAsync(type,"A",null,null,null,HistorySortColumn.Date,HistorySortDirection.Descending,1,options:options);
        Assert.Single(page.Rows); Assert.False(page.HasMore); Assert.Equal(At.AddHours(1),page.Rows[0].ObservedAtUtc);
        var cleared=await service.GetMarketHistoryAsync(type,"A",null,null,null,HistorySortColumn.Date,HistorySortDirection.Descending,1,options:options with {IncludeCleared=true});
        Assert.True(cleared.HasMore); Assert.True(cleared.Rows[0].IsCleared);
    }

    [Fact]
    public async Task CrushingKindDateAndRuneFiltersApplyToTheCorrectSources()
    {
        using var database=new TestDatabase(); await using var db=database.CreateContext();
        var equipment=new Equipment(42){Name="Bottes"};
        equipment.Characteristics.Add(new(equipment,Characteristic.Strength,1,10));
        db.Equipments.Add(equipment);
        db.Runes.Add(new Rune(100){Name="Rune Fo",Characteristic=Characteristic.Strength});
        db.CoefficientObservations.Add(Coefficient(42,"A",At,110)); await db.SaveChangesAsync();
        var service=new HistoryService(db); await service.SaveCrushSessionAsync(Session("A",At.AddMinutes(30)));
        var all=await service.GetCrushingFeedAsync("A",null,100,HistorySortColumn.Date,HistorySortDirection.Descending,50,new(){FromUtc=At,BeforeUtc=At.AddHours(1)});
        Assert.Equal(2,all.Rows.Count);
        var coefficients=await service.GetCrushingFeedAsync("A",null,100,HistorySortColumn.Date,HistorySortDirection.Descending,50,new(){CrushKind=CrushHistoryEntryKind.Coefficient});
        Assert.Single(coefficients.Rows); Assert.Null(coefficients.Rows[0].Session);
        var operations=await service.GetCrushingFeedAsync("A",null,100,HistorySortColumn.Date,HistorySortDirection.Descending,50,new(){CrushKind=CrushHistoryEntryKind.Operation});
        Assert.Single(operations.Rows); Assert.NotNull(operations.Rows[0].Session);
        var missing=await service.GetCrushingFeedAsync("A",null,999,HistorySortColumn.Date,HistorySortDirection.Descending,50);
        Assert.Empty(missing.Rows);
    }

    private static CoefficientObservation Coefficient(long id,string server,DateTime at,double value)=>new(){DofusDbId=id,ServerName=server,CoefficientPercent=value,Source=CoefficientSource.InGameAutomatic,ObservedAtUtc=at};
    private static MarketPriceObservation Price(long id,MarketObjectType type,DateTime at,MarketPriceSource source,int lot,bool cleared=false)=>new(){DofusDbId=id,ObjectType=type,ServerName="A",Quantity=lot,Price=100,Source=source,IsCleared=cleared,ObservedAtUtc=at};
    private static CrushHistorySessionWriteModel Session(string server,DateTime at)=>new(server,at.AddSeconds(-2),at,80,5,76,[new(42,"Bottes au concassage",371,CoefficientSource.InGameAutomatic,0)],[new(100,"Rune Fo",10,80,[new(1,10,80,false,MarketPriceSource.Manual,at.AddMinutes(-1))])]);
}
