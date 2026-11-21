using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Maui.Storage;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class MarketSourceSelectionTest
{
    private sealed class MemoryPreferences:IPreferences
    {
        private readonly Dictionary<string,object> _values=[];
        public bool ContainsKey(string key,string? sharedName=null)=>_values.ContainsKey(key);
        public void Remove(string key,string? sharedName=null)=>_values.Remove(key);
        public void Clear(string? sharedName=null)=>_values.Clear();
        public void Set<T>(string key,T value,string? sharedName=null)=>_values[key]=value!;
        public T Get<T>(string key,T defaultValue,string? sharedName=null)=>_values.TryGetValue(key,out var value)?(T)value:defaultValue;
    }
    private static DataPriorityService Priority()=>new(new MemoryPreferences());
    private static MarketPriceObservation Quote(long id,int lot,MarketPriceSource source,long value,string server="A",MarketObjectType type=MarketObjectType.Resource)=>new()
        {DofusDbId=id,Quantity=lot,Source=source,Price=value,ObjectType=type,ServerName=server,ObservedAtUtc=DateTime.UtcNow};
    private static async Task Seed(BestCrushDbContext db)
    {
        foreach(long id in new long[]{1,2})foreach(int lot in new[]{1,10})
            db.MarketPriceObservations.AddRange(Quote(id,lot,MarketPriceSource.Manual,100*lot),Quote(id,lot,MarketPriceSource.InGameAutomatic,200*lot));
        db.MarketPriceObservations.AddRange(Quote(3,1,MarketPriceSource.Manual,300),Quote(4,1,MarketPriceSource.InGameAutomatic,400),
            Quote(1,1,MarketPriceSource.Manual,500,"B"),Quote(1,1,MarketPriceSource.InGameAutomatic,600,"B"),
            Quote(1,1,MarketPriceSource.Manual,700,type:MarketObjectType.Rune),Quote(1,1,MarketPriceSource.InGameAutomatic,800,type:MarketObjectType.Rune));
        await db.SaveChangesAsync();
    }

    [Fact]
    public void UpgradedGamePriorityBecomesTheCustomDefaultAndSurvivesRestart()
    {
        var preferences=new MemoryPreferences();preferences.Set("BestCrush.DataPriority",DataPriority.InGameAutomatic.ToString());
        var priority=new DataPriorityService(preferences);priority.Priority=DataPriority.Custom;
        Assert.Equal(DataPriority.InGameAutomatic,priority.CustomDefault);
        var reopened=new DataPriorityService(preferences);Assert.Equal(DataPriority.Custom,reopened.Priority);
        Assert.Equal(DataPriority.InGameAutomatic,reopened.CustomDefault);
    }

    [Fact]
    public async Task LotOverridesRowOverridesTabAndMissingSourcesFallBackWithoutLeakingToOtherServersOrTabs()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();await Seed(db);
        var priority=Priority();var edits=new MarketSourceSelectionService(database,priority,new());
        var prices=new MarketPriceService(db,priority);
        await edits.ChooseAsync("A",MarketObjectType.Resource,0,0,MarketPriceSource.InGameAutomatic);
        await edits.ChooseAsync("A",MarketObjectType.Resource,1,0,MarketPriceSource.Manual);
        await edits.ChooseAsync("A",MarketObjectType.Resource,1,10,MarketPriceSource.InGameAutomatic);
        Assert.Equal(DataPriority.Custom,priority.Priority);
        var selected=await prices.GetLatestObservationsForServerAsync(MarketObjectType.Resource,"A");
        Assert.Equal(100,selected[(1,1)].Price);Assert.Equal(2000,selected[(1,10)].Price);
        Assert.Equal(200,selected[(2,1)].Price);Assert.Equal(300,selected[(3,1)].Price);Assert.Equal(400,selected[(4,1)].Price);
        Assert.Null(await prices.GetLatestObservationAsync(MarketObjectType.Resource,99,"A",1));
        Assert.Equal(500,(await prices.GetLatestObservationAsync(MarketObjectType.Resource,1,"B",1))!.Price);
        Assert.Equal(700,(await prices.GetLatestObservationAsync(MarketObjectType.Rune,1,"A",1))!.Price);
        using var reopened=database.CreateContext();
        Assert.Equal(2000,(await new MarketPriceService(reopened,priority).GetLatestObservationAsync(MarketObjectType.Resource,1,"A",10))!.Price);
        await edits.ChooseAsync("A",MarketObjectType.Resource,1,0,MarketPriceSource.Manual);
        Assert.Equal(1000,(await prices.GetLatestObservationAsync(MarketObjectType.Resource,1,"A",10))!.Price);
        await edits.ChooseAsync("A",MarketObjectType.Resource,0,0,MarketPriceSource.InGameAutomatic);
        Assert.Equal(200,(await prices.GetLatestObservationAsync(MarketObjectType.Resource,1,"A",1))!.Price);
        Assert.Single(await db.DataSourcePreferences.ToArrayAsync());
    }

    [Theory]
    [InlineData(DataPriority.Manual,100,300,400)]
    [InlineData(DataPriority.InGameAutomatic,200,300,400)]
    public async Task GlobalChoiceResetsAllIndividualChoicesAndRetainsTheOtherSourceAsFallback(DataPriority mode,long both,long onlyManual,long onlyGame)
    {
        using var database=new TestDatabase();using var db=database.CreateContext();await Seed(db);
        var priority=Priority();var changes=new MarketDataChangeNotifier();var edits=new MarketSourceSelectionService(database,priority,changes);
        await edits.ChooseAsync("A",MarketObjectType.Resource,1,1,MarketPriceSource.Manual);
        await edits.ChooseAsync("B",MarketObjectType.Resource,1,1,MarketPriceSource.InGameAutomatic);
        await edits.SetGlobalAsync(mode);
        Assert.Equal(mode,priority.Priority);Assert.Empty(await db.DataSourcePreferences.ToArrayAsync());
        var prices=new MarketPriceService(db,priority);
        Assert.Equal(both,(await prices.GetLatestObservationAsync(MarketObjectType.Resource,1,"A",1))!.Price);
        Assert.Equal(onlyManual,(await prices.GetLatestObservationAsync(MarketObjectType.Resource,3,"A",1))!.Price);
        Assert.Equal(onlyGame,(await prices.GetLatestObservationAsync(MarketObjectType.Resource,4,"A",1))!.Price);
        await edits.ChooseAsync("A",MarketObjectType.Resource,1,1,MarketPriceSource.Manual);
        Assert.Equal(DataPriority.Custom,priority.Priority);Assert.Equal(mode,priority.CustomDefault);
        Assert.Equal(mode==DataPriority.Manual?100:200,(await prices.GetLatestObservationAsync(MarketObjectType.Resource,2,"A",1))!.Price);
        Assert.Contains(changes.ChangesSince("A",0).Changes,c=>c.DofusDbId==0);
    }

    [Fact]
    public async Task CoefficientsHaveSeparateChoicesFromEquipmentPricesAndKeepTheirInitialFallback()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();var priority=Priority();
        var coefficient=new CoefficientService(db,priority);
        await coefficient.AddObservationAsync(1,"A",100,CoefficientSource.Manual);
        await coefficient.AddObservationAsync(1,"A",200,CoefficientSource.InGameAutomatic);
        await coefficient.AddObservationAsync(2,"A",300,CoefficientSource.DofocusInitial);
        var edits=new MarketSourceSelectionService(database,priority,new());
        await edits.ChooseAsync("A",MarketObjectType.Equipment,0,0,MarketPriceSource.InGameAutomatic,DataValueKind.CrushingCoefficient);
        await edits.ChooseAsync("A",MarketObjectType.Equipment,0,0,MarketPriceSource.Manual);
        Assert.Equal(200,(await coefficient.GetLatestObservationAsync(1,"A"))!.CoefficientPercent);
        Assert.Equal(300,(await coefficient.GetLatestObservationAsync(2,"A"))!.CoefficientPercent);
        await edits.ChooseAsync("A",MarketObjectType.Equipment,1,0,MarketPriceSource.Manual,DataValueKind.CrushingCoefficient);
        Assert.Equal(100,(await coefficient.GetLatestObservationsForServerAsync("A"))[1].CoefficientPercent);
    }

    [Fact]
    public async Task MigrationPreservesXpAndDatesAndAddsOriginsWithoutStoringRawMeals()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");connection.Open();
        using var db=new BestCrushDbContext(new DbContextOptionsBuilder<BestCrushDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261121050000_KeepOnlyPetFoodCoefficient");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO PetFoods(DofusDbId,Name,Level,MarketType,Experience,ExperienceObservedAtUtc,ExperienceSource,Craftable)
            VALUES(1,'Jeu',1,1,0.3333333333333333,'2026-11-21 10:00:00','Relevé en jeu',0),
                  (2,'Initial',1,1,100,NULL,'Import',0),(3,'Inconnu',1,1,NULL,NULL,'',0);
            """);
        await db.Database.MigrateAsync();
        var foods=await db.PetFoods.OrderBy(f=>f.DofusDbId).ToArrayAsync();
        Assert.Equal(1.0/3,foods[0].Experience);Assert.NotNull(foods[0].ExperienceObservedAtUtc);
        Assert.Equal(MarketPriceSource.InGameAutomatic,foods[0].ExperienceOrigin);
        Assert.Equal(MarketPriceSource.Manual,foods[1].ExperienceOrigin);Assert.Null(foods[2].ExperienceOrigin);
        Assert.Empty(await db.DataSourcePreferences.ToArrayAsync());Assert.False(db.Database.HasPendingModelChanges());
    }
}
