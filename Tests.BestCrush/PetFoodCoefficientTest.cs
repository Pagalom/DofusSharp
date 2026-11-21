using BestCrush.Domain.Models;
using BestCrush.Domain;
using BestCrush.Services;
using Tests.BestCrush.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tests.BestCrush;

public sealed class PetFoodCoefficientTest
{
    [Fact]
    public async Task MigrationConvertsLatestValidMealAndRemovesDetailedHistory()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");connection.Open();
        using var db=new BestCrushDbContext(new DbContextOptionsBuilder<BestCrushDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261118190000_AddPetFeedingObservations");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Resources(Id,DofusDbId,Level,Name,PetExperience)
                VALUES('00000000-0000-0000-0000-000000000001',10,1,'Aliment',101.25);
            INSERT INTO PetFoods(DofusDbId,Name,Level,MarketType,Experience,SheetExperience,ExperienceSource,Craftable)
                VALUES(10,'Aliment',1,1,101.25,101,'Import',0),(20,'Sans repas',1,1,2.5,2,'Import',0);
            INSERT INTO PetFeedingObservations(Id,FoodItemId,PetItemId,Quantity,ExperienceBefore,ExperienceAfter,ObservedAtUtc,ClientBuild)
                VALUES('early',10,1,1,0,101,'2026-11-19 10:00:00','old'),
                      ('latest',10,1,10,100,1112,'2026-11-20 10:00:00','new'),
                      ('invalid',10,1,0,100,200,'2026-11-21 10:00:00','new'),
                      ('capped',10,1,1,179500,179592,'2026-11-21 11:00:00','new');
            """);
        await db.Database.MigrateAsync();
        var food=await db.PetFoods.SingleAsync(f=>f.DofusDbId==10);
        Assert.Equal(101.2,food.Experience);
        Assert.Equal(new DateTime(2026,11,20,10,0,0),food.ExperienceObservedAtUtc);
        Assert.Equal("Relevé en jeu",food.ExperienceSource);
        Assert.Equal(101.2,(await db.Resources.SingleAsync()).PetExperience);
        var unseen=await db.PetFoods.SingleAsync(f=>f.DofusDbId==20);
        Assert.Equal(2.5,unseen.Experience);Assert.Null(unseen.ExperienceObservedAtUtc);
        using var command=connection.CreateCommand();
        command.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PetFeedingObservations'";
        Assert.Equal(0L,await command.ExecuteScalarAsync());
        command.CommandText="SELECT COUNT(*) FROM pragma_table_info('PetFoods') WHERE name='SheetExperience'";
        Assert.Equal(0L,await command.ExecuteScalarAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(3,1,1.0/3)]
    [InlineData(10,1012,101.2)]
    [InlineData(1,101,101)]
    [InlineData(1,0,0)]
    public async Task StoresOnlyTheComputedCoefficientAndDateAndUpdatesTheResource(int quantity,long gain,double expected)
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var at=DateTime.UtcNow;
        var meal=new global::BestCrush.Network.Protocol.PetFeedingObservation(1,13942,12133,quantity,100,100+gain,at);
        Assert.True(await LivePetFeedingService.SaveAsync(db,meal,default));
        using var reopened=database.CreateContext();
        var food=Assert.Single(await reopened.PetFoods.ToArrayAsync());
        Assert.Equal(expected,food.Experience);Assert.Equal(at,food.ExperienceObservedAtUtc);
        Assert.Equal(expected,Assert.Single(await reopened.Resources.ToArrayAsync()).PetExperience);
        Assert.DoesNotContain(reopened.Model.GetEntityTypes(),type=>type.GetTableName()=="PetFeedingObservations");
        Assert.False(await LivePetFeedingService.SaveAsync(db,meal,default));
    }

    [Fact]
    public async Task LatestMealReplacesThePreviousCoefficientWithoutHistoryOrAverages()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        db.PetFoods.Add(new(){DofusDbId=13942,Name="Bec",MarketType=MarketObjectType.Resource,Experience=101.25});await db.SaveChangesAsync();
        var first=new global::BestCrush.Network.Protocol.PetFeedingObservation(1,13942,12133,1,0,101,DateTime.UtcNow);
        Assert.True(await LivePetFeedingService.SaveAsync(db,first,default));
        var next=first with{Sequence=2,Quantity=10,ExperienceAfter=1012,ObservedAtUtc=first.ObservedAtUtc.AddSeconds(1)};
        Assert.True(await LivePetFeedingService.SaveAsync(db,next,default));
        Assert.False(await LivePetFeedingService.SaveAsync(db,first,default));
        Assert.Equal(101.2,Assert.Single(await db.PetFoods.ToArrayAsync()).Experience);
        Assert.True(await LivePetFeedingService.SaveAsync(db,next with{ObservedAtUtc=next.ObservedAtUtc.AddDays(1)},default));
        Assert.Equal(1,await db.PetFoods.CountAsync());
        Assert.False(await LivePetFeedingService.SaveAsync(db,next with{ExperienceAfter=179592},default));
        Assert.False(await LivePetFeedingService.SaveAsync(db,next with{Quantity=0},default));
    }
}
