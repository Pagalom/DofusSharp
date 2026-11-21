using BestCrush.Domain.Models;

namespace BestCrush.Services;

// These are review statuses, never expiration dates for stored values.
public static class PetDataFreshness
{
    public static readonly TimeSpan PriceFreshFor=TimeSpan.FromMinutes(30);
    public static readonly TimeSpan PriceReviewAfter=TimeSpan.FromHours(72);
    public static readonly TimeSpan ExperienceReviewAfter=TimeSpan.FromDays(7);
    public static DataFreshness Price(DateTime observedAtUtc,DateTime? nowUtc=null)
    {
        var age=(nowUtc ?? DateTime.UtcNow)-observedAtUtc;
        return age<PriceFreshFor ? DataFreshness.Fresh : age<PriceReviewAfter ? DataFreshness.Aging : DataFreshness.Stale;
    }
    public static bool ExperienceNeedsReview(DateTime? observedAtUtc,DateTime? nowUtc=null)=>
        observedAtUtc is null || (nowUtc ?? DateTime.UtcNow)-observedAtUtc.Value>ExperienceReviewAfter;
    public static string PriceTooltip(DateTime observedAtUtc,DateTime? nowUtc=null)=>
        $"Prix {Price(observedAtUtc,nowUtc) switch {DataFreshness.Fresh=>"à jour",DataFreshness.Aging=>"passable",_=>"obsolète : à actualiser à l’HDV"}} · {observedAtUtc.ToLocalTime():dd/MM HH:mm}";
}
