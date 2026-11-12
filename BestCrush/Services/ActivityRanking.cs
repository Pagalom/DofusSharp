using System.Globalization;
using BestCrush.Domain.Models;

namespace BestCrush.Services;

public sealed record ActivityFilters(string Search = "", string Strategy = "", string Sort = "kinetics",
    long? ItemId = null, double Minimum = 0, double? Budget = null, int? MaximumLevel = null,
    bool CompleteOnly = false, bool ExcludeOldPrices = false, bool RespectJobs = false,
    string Origin = "", double MinimumRoi = 0, bool ReadyOnly = false,
    double MinimumMedianDiscount = 0, int? MaximumMedianAgeDays = null, double? TradingTargetRoi = null,
    IReadOnlyCollection<MarketObjectType>? TradingCategories = null, FarmingSource? FarmSource = null,
    string? HarvestJob = null, bool RespectHarvestLevels = false,
    FarmingCombatSelection? CombatSelection = null, FarmingCombatCatalog? CombatCatalog = null);

public enum FarmingSource { Harvest, Combat }

public static class ActivityRanking
{
    public static IReadOnlyList<ActivityRecommendation> Select(IEnumerable<ActivityRecommendation> candidates,
        ActivityFilters filters, DateTime now, IReadOnlyDictionary<string, int>? levels = null)
    {
        var rows = candidates.Where(x => (filters.ItemId is null || x.ItemId == filters.ItemId) &&
            (filters.TradingCategories is not { Count: > 0 } categories || categories.Contains(x.ObjectType)) &&
            (filters.Origin.Length == 0 || x.Module == filters.Origin) &&
            (filters.MinimumRoi <= 0 || x.Profitability.RoiPercent >= filters.MinimumRoi) &&
            (!filters.ReadyOnly || x.Spending == 0) &&
            MatchesSearch(x, filters.Search) &&
            (filters.Strategy.Length == 0 || x.Strategy == filters.Strategy) &&
            MatchesFarming(x, filters, levels) &&
            (filters.MaximumLevel is null || x.Level <= filters.MaximumLevel) &&
            (filters.Minimum <= 0 || x.Profitability.Gain >= filters.Minimum) &&
            (filters.MinimumMedianDiscount <= 0 || x.HistoricalPrice?.DiscountPercent >= filters.MinimumMedianDiscount) &&
            (filters.MaximumMedianAgeDays is null || x.HistoricalPrice?.DaysSinceMedian(now) <= filters.MaximumMedianAgeDays) &&
            (filters.Budget is null || x.Spending <= filters.Budget) &&
            (!filters.CompleteOnly || x.Complete && x.Profitability.Gain > 0) &&
            (!filters.ExcludeOldPrices || x.PriceUpdatedUtc is { } at && now - at <= TimeSpan.FromHours(12)) &&
            (!filters.RespectJobs || levels is null || x.Job is null || x.RequiredJobLevel is null ||
                !levels.TryGetValue(x.Job, out int level) || level >= x.RequiredJobLevel) &&
            (!filters.RespectJobs || levels is null || x.CraftJob is null || x.RequiredCraftLevel is null ||
                !levels.TryGetValue(x.CraftJob, out int craftLevel) || craftLevel >= x.RequiredCraftLevel));
        var ranked = rows.OrderByDescending(x => filters.TradingTargetRoi is not { } target || x.MeetsTradingTarget(target));
        return (filters.Sort switch
        {
            "name" => ranked,
            "gain" => ranked.ThenByDescending(x => x.Profitability.Gain),
            "cost" => ranked.ThenBy(x => x.Cost is null).ThenBy(x => x.Cost),
            "volume" => ranked.ThenByDescending(x => x.Profitability.Kinetics.UnitsPerDay),
            "monster" or "zone" when filters.CombatCatalog is { } catalog =>
                ranked.ThenBy(x => FarmingSortKey(x, filters, catalog), FarmingLocationComparer),
            _ => ranked.ThenByDescending(x => x.Profitability.Gain > 0).ThenByDescending(x => x.Profitability.CanCompareKinetics)
                .ThenByDescending(x => x.Profitability.MarketDailyGainCapacity).ThenByDescending(x => x.Profitability.Gain)
        }).ThenBy(x => x.Name).ThenBy(x => x.Id).ToArray();
    }

    private static bool MatchesFarming(ActivityRecommendation row, ActivityFilters filters,
        IReadOnlyDictionary<string, int>? levels)
    {
        if (filters.FarmSource is not { } source)
            return true;
        if (row.Module != "farming")
            return false;
        if (source == FarmingSource.Combat)
            return row.Job is null && row.MonsterIds.Count > 0 &&
                (filters.CombatSelection is not { Count: > 0 } selected || row.MonsterIds.Any(selected.IncludesMonster));

        return row.Job is { } job &&
            (filters.HarvestJob is null || string.Equals(job, filters.HarvestJob, StringComparison.CurrentCultureIgnoreCase)) &&
            (!filters.RespectHarvestLevels ||
                (HasLevel(job, row.RequiredJobLevel, levels) && HasLevel(row.CraftJob, row.RequiredCraftLevel, levels)));
    }

    private static readonly IComparer<string?> FarmingLocationComparer = Comparer<string?>.Create((left, right) =>
        left is null ? (right is null ? 0 : 1) : right is null ? -1 : StringComparer.CurrentCultureIgnoreCase.Compare(left, right));

    private static string? FarmingSortKey(ActivityRecommendation row, ActivityFilters filters, FarmingCombatCatalog catalog)
    {
        if (row.Module != "farming" || row.Job is not null) return null;
        var selection = filters.CombatSelection ?? FarmingCombatSelection.Empty;
        return filters.Sort == "zone" ? catalog.ZoneNameFor(row.MonsterIds, selection)
            : catalog.MonsterNameFor(row.MonsterIds, selection);
    }

    private static bool HasLevel(string? job, int? required, IReadOnlyDictionary<string, int>? levels) =>
        job is null || required is null || levels is null || !levels.TryGetValue(job, out int level) || level >= required;

    private static bool MatchesSearch(ActivityRecommendation row, string search)
    {
        if (row.SoulCapture is null && row.Module != "farming")
            return row.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase);

        var comparison = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return comparison.IndexOf(row.Name, search, options) >= 0 ||
            row.SoulCapture is { } soul && comparison.IndexOf(soul.Dungeon, search, options) >= 0;
    }
}
