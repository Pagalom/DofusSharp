using BestCrush.Domain.Services;

namespace Tests.BestCrush;

public sealed class PetExperienceTest
{
    [Fact]
    public void MatchesDodoxPreviewAndActualClientLevelIndexing()
    {
        Assert.Equal(101, PetExperience.MealExperience(101.25, 1));
        Assert.Equal(1012, PetExperience.MealExperience(101.25, 10));
        Assert.Equal(23, PetExperience.LevelAt(101));
        Assert.Equal(44, PetExperience.LevelAt(1012));
        Assert.Equal(80, PetExperience.LevelAt(30001));
        Assert.Equal(70, PetExperience.LevelAt(12526));
        Assert.Equal(179592, PetExperience.RequiredAtLevel(100));
    }

    [Fact]
    public void ComputesWholeMealsWithoutRoundingTheUnitYieldFirst()
    {
        Assert.Equal(0, PetExperience.MealExperience(.004999999888241291 * 10, 1));
        int quantity = PetExperience.UnitsFor(.004999999888241291 * 10, 1)!.Value;
        Assert.Equal(20, quantity);
        Assert.Equal(1, PetExperience.MealExperience(.004999999888241291 * 10, quantity));
        Assert.Equal(360, PetExperience.UnitsFor(500, PetExperience.RequiredAtLevel(100)));
        Assert.Null(PetExperience.UnitsFor(0, 1));
        Assert.Null(PetExperience.UnitsFor(double.NaN, 1));
    }
}
