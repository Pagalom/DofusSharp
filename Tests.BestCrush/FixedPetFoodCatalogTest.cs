using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.EntityFrameworkCore;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public sealed class FixedPetFoodCatalogTest
{
    [Theory]
    [InlineData(13942, 1, 101)]
    [InlineData(13942, 10, 1012)]
    [InlineData(412, 4, 1)]
    [InlineData(648, 3, 1)]
    [InlineData(9389, 10, 81)]
    [InlineData(15065, 1, 15)]
    [InlineData(17161, 1, 1569)]
    [InlineData(17161, 10, 15693)]
    [InlineData(17161, 100, 156938)]
    [InlineData(16440, 10, 4)]
    [InlineData(20125, 1, 500)]
    public void FixedYieldsReproduceClientArithmeticForResourcesBossesCraftsAndSpecialFood(long id, int quantity, int expected)
    {
        Assert.Equal(expected, PetExperience.MealExperience(FixedPetFoodCatalog.All[id].Experience, quantity));
    }

    [Fact]
    public void CatalogueHasCompleteUniqueFiniteYieldsAndDoesNotTreatRetiredEssencesAsFood()
    {
        Assert.Equal(3521, FixedPetFoodCatalog.All.Count);
        Assert.Equal(2749, FixedPetFoodCatalog.Foods.Length);
        Assert.All(FixedPetFoodCatalog.All.Values, food => Assert.True(double.IsFinite(food.Experience) && food.Experience >= 0));
        Assert.DoesNotContain(FixedPetFoodCatalog.Foods, food => food.Id is 14675 or 26994);
        Assert.Null(FixedPetFoodCatalog.Definition(long.MaxValue).Experience);
    }

    [Fact]
    public async Task NoNetworkOrWritableFoodCacheIsNeededToReadTheSharedCatalogue()
    {
        string folder = Path.Combine(Path.GetTempPath(), "fixed-food-" + Guid.NewGuid());
        try
        {
            var catalog = new ActivityCatalogService(DofusDbClient.Production(), Moq.Mock.Of<IHttpClientFactory>(), folder);
            Assert.Same(await catalog.GetPetFoodsAsync(default), await catalog.GetPetFoodsAsync(default));
            using var stop = new CancellationTokenSource(); stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.GetPetFoodsAsync(stop.Token));
        }
        finally { if(Directory.Exists(folder)) Directory.Delete(folder,true); }
    }

    [Fact]
    public async Task SynchronizationReplacesLearnedAndManualXpClearsUnknownsAndPreservesMarketPrices()
    {
        using var database = new TestDatabase();
        using(var db = database.CreateContext())
        {
            db.Resources.AddRange(new Resource(13942){Name="Bec",PetExperience=101.2}, new Resource(999999){Name="Ancienne entrée",PetExperience=999});
            db.PetFoods.AddRange(new(){DofusDbId=13942,Name="Bec",Experience=101.2,ExperienceOrigin=MarketPriceSource.InGameAutomatic,ExperienceObservedAtUtc=DateTime.UtcNow},
                new(){DofusDbId=9389,Name="Feuille",Experience=1.6,ExperienceOrigin=MarketPriceSource.Manual,ExperienceObservedAtUtc=DateTime.UtcNow},
                new(){DofusDbId=999999,Name="Ancienne entrée",Experience=999,ExperienceOrigin=MarketPriceSource.Manual});
            db.MarketPriceObservations.Add(new(){DofusDbId=13942,ObjectType=MarketObjectType.Resource,ServerName="A",Price=500,Quantity=10,Source=MarketPriceSource.Manual,ObservedAtUtc=DateTime.UtcNow});
            await db.SaveChangesAsync();
        }
        var service = new PetFoodCatalogService(database,new TestDataPriorityProvider());
        await service.SynchronizeAsync(default);
        await service.SynchronizeAsync(default);
        using var reopened = database.CreateContext();
        Assert.Equal(101.25,(await reopened.PetFoods.SingleAsync(f=>f.DofusDbId==13942)).Experience);
        Assert.Equal(8.1868,(await reopened.Resources.SingleAsync(r=>r.DofusDbId==9389)).PetExperience!.Value,8);
        Assert.Null((await reopened.Resources.SingleAsync(r=>r.DofusDbId==999999)).PetExperience);
        Assert.False(await reopened.Resources.AnyAsync(r=>r.DofusDbId==7442));
        Assert.Equal(45,(await reopened.PetFoods.SingleAsync(f=>f.DofusDbId==7442)).Experience);
        Assert.All(await reopened.PetFoods.ToArrayAsync(), food=> {Assert.Null(food.ExperienceOrigin); Assert.Null(food.ExperienceObservedAtUtc);});
        Assert.Equal(500,Assert.Single(await reopened.MarketPriceObservations.Where(q=>q.ServerName=="A").ToArrayAsync()).Price);
        Assert.Equal(2749,(await service.GetFoodsAsync(default)).Length);
    }
}
