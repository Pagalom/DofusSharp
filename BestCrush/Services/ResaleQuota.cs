namespace BestCrush.Services;

/// <summary>An indicative share of the weekly market, expressed in units over a rolling day.</summary>
public sealed record ResaleQuota(long WeeklyUnits, DateTimeOffset ValidUntilUtc)
{
    public double UsualDailySales => WeeklyUnits / 7d;
    public long SuggestedUnits => WeeklyUnits / 14 + (WeeklyUnits % 14 > 0 ? 1 : 0);
    public long Remaining(long alreadyListed) => Math.Max(0, SuggestedUnits - alreadyListed);
}
