using System.Numerics;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tests.BestCrush.Utils;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.BestCrush;

public sealed class PetFoodDataTest
{
    [Fact]
    public async Task MigrationKeepsExistingResourcesAndAddsNullableXpAndAllFoodCategories()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");connection.Open();
        using var db=new BestCrushDbContext(new DbContextOptionsBuilder<BestCrushDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261112230000_AddCrushEquipmentComparison");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Resources(Id,DofusDbId,Level,Name) VALUES('00000000-0000-0000-0000-000000000001',10,1,'Conservée')");
        await db.Database.MigrateAsync();
        Assert.Null((await db.Resources.SingleAsync()).PetExperience);
        Assert.Empty(await db.PetFoods.ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task SeedIsIdempotentPreservesNewerPricesAndNeverAssignsKourialPricesToAnotherServer()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        var at=DateTimeOffset.UtcNow;
        db.MarketPriceObservations.Add(new(){ObjectType=MarketObjectType.Resource,DofusDbId=10,ServerName="Kourial",
            Price=99,Quantity=10,Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=at.AddMinutes(1).UtcDateTime});
        await db.SaveChangesAsync();
        PetFoodSeedRow Row(long id,MarketObjectType type)=>new(id,"Aliment "+id,null,type,101.25,101,"Catalogue client",false,50,"Nom du fichier",2);
        var seed=new PetFoodSeed("test","XP Familiers.ods","hash",at,"Kourial",[Row(10,MarketObjectType.Resource),Row(20,MarketObjectType.Rune)]);
        await PetFoodCatalogService.ApplySeedAsync(db,seed,default);
        await PetFoodCatalogService.ApplySeedAsync(db,seed,default);
        Assert.Equal(2,await db.PetFoods.CountAsync());Assert.Equal(2,await db.MarketPriceObservations.CountAsync());
        Assert.Equal(101.25,(await db.PetFoods.FirstAsync()).Experience);
        Assert.DoesNotContain(await db.MarketPriceObservations.ToArrayAsync(),q=>q.DofusDbId==10 && q.Source==MarketPriceSource.Manual);
        var imported=await db.MarketPriceObservations.SingleAsync(q=>q.DofusDbId==20);
        Assert.Equal(MarketObjectType.Rune,imported.ObjectType);Assert.Equal("Kourial",imported.ServerName);
        Assert.Equal(MarketPriceSource.Manual,imported.Source);Assert.Equal(at.UtcDateTime,imported.ObservedAtUtc);
    }

    [Fact]
    public void QuantitiesUseClientXpThresholdsAndTotalCostBuysWholeCheapestLots()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        MarketPriceObservation Quote(int size,long price)=>new(){ObjectType=MarketObjectType.Resource,DofusDbId=13942,
            ServerName="A",Quantity=size,Price=price,Source=MarketPriceSource.InGameAutomatic,ObservedAtUtc=DateTime.UtcNow};
        var market=new EquipmentProfitabilityContext{ResourcePrices=new[]{Quote(1,50),Quote(10,301)}.ToDictionary(q=>(q.DofusDbId,q.Quantity)),
            RunePrices=new Dictionary<(long,int),MarketPriceObservation>(),EquipmentPrices=new Dictionary<(long,int),MarketPriceObservation>(),Coefficients=[]};
        var planner=new PetFoodPlanner(new(db,new TestDataPriorityProvider()),[],[],new Dictionary<long,BigInteger>(),market);
        var food=new PetFoodDefinition{DofusDbId=13942,Name="Bec de Dodox",MarketType=MarketObjectType.Resource,Experience=101.25};
        var row=PetFoodCatalogService.BuildRow(food,planner,market);
        Assert.Equal(30.1,row.UnitPrice);Assert.Equal(295,row.Quantity80);Assert.Equal(1774,row.Quantity100);
        Assert.Equal(8979,row.Cost80!.TotalCost);Assert.Equal(53477,row.Cost100!.TotalCost);
        food.Experience=null;
        var unknown=PetFoodCatalogService.BuildRow(food,planner,market);
        Assert.Null(unknown.Quantity80);Assert.Null(unknown.Cost100);Assert.NotNull(unknown.UnitPrice);
    }

    [Fact]
    public void PackagedSeedContainsUniqueResolvedIdsAndIgnoresRequestedCategoriesAndSpreadsheetTotals()
    {
        var seed=PetFoodCatalogService.ReadSeed();
        Assert.Equal(210,seed.Records.Length);Assert.Equal(210,seed.Records.DistinctBy(row=>row.Id).Count());
        Assert.DoesNotContain(seed.Records,row=>row.Name.StartsWith("Clef") || row.Name.StartsWith("Essence"));
        Assert.Equal("Kourial",seed.Server);
        Assert.All(seed.Records,row=>Assert.True(row.UnitPrice is null or >0));
        Assert.Equal("Fraise",Assert.Single(seed.Records,row=>row.UnitPrice is null).Name);
        Assert.Equal(500,Assert.Single(seed.Records,row=>row.Id==20125).Experience);
    }


}
