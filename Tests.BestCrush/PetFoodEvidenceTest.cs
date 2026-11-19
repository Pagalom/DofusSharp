using BestCrush.Domain.Models;
using BestCrush.Services;
using Tests.BestCrush.Utils;
using Microsoft.EntityFrameworkCore;

namespace Tests.BestCrush;

public sealed class PetFoodEvidenceTest
{
    private static PetFeedingObservation Meal(int quantity,long xp,string build="3.7.14.18")=>new()
        {Id=Guid.NewGuid().ToString("N").PadRight(64,'0'),FoodItemId=13942,PetItemId=12133,Quantity=quantity,
            ExperienceBefore=0,ExperienceAfter=xp,ClientBuild=build,ObservedAtUtc=DateTime.UtcNow};
    [Fact]
    public void ObservedIntegerXpNeverTurnsAFractionalYieldIntoAnExactRoundedValue()
    {
        var evidence=PetFoodEvidenceReader.Summarize([Meal(1,101),Meal(10,1012)],101.25);
        Assert.True(evidence.Matches);Assert.Equal(101.2,evidence.Minimum);Assert.Equal(101.3,evidence.Maximum);
        Assert.False(PetFoodEvidenceReader.Summarize([Meal(1,101),Meal(10,1012)],101).Matches);
        Assert.Null(PetFoodEvidenceReader.Summarize([Meal(1,101)],null).Matches);
        Assert.False(PetFoodEvidenceReader.Summarize([Meal(1,101),Meal(1,110)],101.25).Consistent);
    }
    [Fact]
    public async Task MealsAreDurableIdempotentAndNeverOverwriteTheReferenceOrOtherClientBuilds()
    {
        using var database=new TestDatabase();using var db=database.CreateContext();
        db.PetFoods.Add(new(){DofusDbId=13942,Name="Bec de Dodox",Experience=101.25});await db.SaveChangesAsync();
        var observation=new global::BestCrush.Network.Protocol.PetFeedingObservation(1,13942,12133,1,0,101,DateTime.UtcNow);
        string id=new('A',64);
        Assert.True(await LivePetFeedingService.SaveAsync(db,observation,id,"3.7.14.18",default));
        Assert.False(await LivePetFeedingService.SaveAsync(db,observation,id,"3.7.14.18",default));
        db.PetFeedingObservations.Add(Meal(1,80,"old-build"));await db.SaveChangesAsync();
        using var reopened=database.CreateContext();
        var foods=await reopened.PetFoods.ToArrayAsync();
        var evidence=await PetFoodEvidenceReader.ReadAsync(reopened,foods,"3.7.14.18",default);
        Assert.Single(evidence);Assert.True(evidence[13942].Matches);Assert.Equal(1,evidence[13942].Meals);
        Assert.Equal(101.25,foods[0].Experience);
        Assert.False(await LivePetFeedingService.SaveAsync(db,observation with{ExperienceAfter=179592},new('B',64),"3.7.14.18",default));
    }
}
