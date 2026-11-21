using BestCrush.Domain.Models;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class PetDataFreshnessTest
{
    [Theory]
    [InlineData(0,DataFreshness.Fresh)]
    [InlineData(29,DataFreshness.Fresh)]
    [InlineData(30,DataFreshness.Aging)]
    [InlineData(60,DataFreshness.Aging)]
    [InlineData(4319,DataFreshness.Aging)]
    [InlineData(4320,DataFreshness.Stale)]
    [InlineData(10080,DataFreshness.Stale)]
    public void PriceAgeUsesThirtyMinutesAndSeventyTwoHours(int minutes,DataFreshness expected)
    {
        var now=new DateTime(2026,11,21,12,0,0,DateTimeKind.Utc);
        Assert.Equal(expected,PetDataFreshness.Price(now.AddMinutes(-minutes),now));
    }

    [Fact]
    public void XpAndPriceUseSeparateReviewClocks()
    {
        var now=new DateTime(2026,11,21,12,0,0,DateTimeKind.Utc);
        Assert.False(PetDataFreshness.ExperienceNeedsReview(now.AddDays(-4),now));
        Assert.Equal(DataFreshness.Stale,PetDataFreshness.Price(now.AddDays(-4),now));
        Assert.True(PetDataFreshness.ExperienceNeedsReview(now.AddDays(-8),now));
        Assert.True(PetDataFreshness.ExperienceNeedsReview(null,now));
    }
}
