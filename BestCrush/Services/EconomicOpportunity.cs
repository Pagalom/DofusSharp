using BestCrush.Domain.Models;

namespace BestCrush.Services;

public sealed record OpportunityEvidence(IReadOnlyList<MarketPriceObservation> Prices,
    CoefficientObservation? Coefficient = null)
{
    public DateTime? OldestUtc => Prices.Select(x => (DateTime?)x.ObservedAtUtc)
        .Append(Coefficient?.ObservedAtUtc).Where(x => x is not null).DefaultIfEmpty().Min();
    public bool IsStale(DateTime now) => OldestUtc is not { } oldest ||
        DataFreshnessEvaluator.Evaluate(oldest,now) == DataFreshness.Stale;
}

public sealed record EconomicOpportunity(string Origin, string Strategy, string Server,
    StockCraftPlan Plan, double? Revenue, OpportunityEvidence Evidence, IReadOnlyList<string> MissingData,
    string? Focus = null, IReadOnlyList<(string Name, double Quantity)>? Runes = null)
{
    public bool UsesEstimatedPrices { get; init; }
    public double? Benefit => Revenue - Plan.EconomicCost;
    public double? RoiPercent => Plan.EconomicCost is > 0 ? Benefit / Plan.EconomicCost * 100 : null;
    public bool IsComplete => Plan.HasCompleteValuation && Revenue is not null && MissingData.Count == 0;
    public string OriginName => Origin == "craft" ? "Craft" : "Concassage";
    public string SourceUrl => Origin == "craft"
        ? $"/modules/craft?item={Plan.Product.DofusDbId}&quantity={Plan.Quantity}"
        : $"/servers/{Uri.EscapeDataString(Server)}?stocks=true&item={Plan.Product.DofusDbId}";
}

/// <summary>Modules publish their calculations; the summary only filters and orders them.</summary>
public interface IOpportunityProvider
{
    string Origin { get; }
    Task<IReadOnlyList<EconomicOpportunity>> EvaluateAsync(OpportunityScanContext context, CancellationToken cancellationToken);
}

public sealed record OpportunityScanContext(string Server, IReadOnlyList<StockCraftPlan> Plans,
    EquipmentProfitabilityContext Market);

public static class OpportunityRanking
{
    public static IReadOnlyList<EconomicOpportunity> Select(IEnumerable<EconomicOpportunity> candidates,
        DateTime now, double minimumBenefit, double minimumRoi, double? budget, bool excludeStale, string sort)
    {
        var eligible = candidates.Where(x => x.IsComplete && x.Benefit > 0 && x.Benefit >= minimumBenefit &&
            (minimumRoi <= 0 || x.RoiPercent >= minimumRoi) &&
            (budget is null || x.Plan.CashRequired <= budget) && (!excludeStale || !x.Evidence.IsStale(now)));
        return (sort switch
        {
            "roi" => eligible.OrderByDescending(x => x.RoiPercent),
            "cash" => eligible.OrderBy(x => x.Plan.CashRequired),
            _ => eligible.OrderByDescending(x => x.Benefit)
        }).ThenBy(x => x.Plan.Product.Name).ThenBy(x => x.Origin).ToArray();
    }
}
