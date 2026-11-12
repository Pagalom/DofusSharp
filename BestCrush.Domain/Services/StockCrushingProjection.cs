using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

/// <summary>Cash reading of an already calculated crush; no price loads or yield calculations.</summary>
public static class StockCrushingProjection
{
    public static CraftCostResult RemainingCost(StockCraftPlan plan) => new(
        plan.Ingredients.Select(x => new CraftResourceCostLine(x.ItemId, x.Name,
            x.Required, x.Purchase, x.ObjectType)).ToArray());

    public static EquipmentProfitabilityScenario Apply(EquipmentProfitabilityScenario scenario, StockCraftPlan plan)
    {
        var cost = RemainingCost(plan);
        double? benefit = plan.HasRecipe && cost.TotalCost is { } cash
            ? scenario.AdjustedRuneValue - cash : null;
        double? roi = cost.TotalCost is > 0 ? benefit / cost.TotalCost : null;
        // With no purchase, a percentage ROI has no denominator. Keep it unknown;
        // the view explicitly shows "Sans achat" instead of 0% or infinity.
        bool runesComplete = scenario.Runes.Where(x => x.Value > 0)
            .All(x => scenario.RuneValues.ContainsKey(x.Key));
        var state = !runesComplete || benefit is null ? ProfitabilityState.Unavailable
            : benefit <= 0 ? ProfitabilityState.NonProfitable
            : cost.TotalCost == 0 || roi * 100 >= scenario.TargetRoiPercent ? ProfitabilityState.TargetReached
            : ProfitabilityState.PositiveBelowTarget;
        return scenario with {
            CraftCost = cost, CraftBenefit = benefit, CraftYield = roi, CraftState = state,
            CraftTargetMargin = plan.HasRecipe ? scenario.MaximumCostAtTargetRoi - cost.TotalCost : null,
            // These market-cost thresholds cannot be reused for the cash reading.
            CraftMinimumCoefficientPercent = null
        };
    }
}
