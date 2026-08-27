namespace BestCrush.Domain.Models;

public sealed record EquipmentProfitabilityResult(
    Equipment Equipment,
    CoefficientObservation? Coefficient,
    MarketPriceObservation? EquipmentCost,
    CraftCostResult CraftCost,
    IReadOnlyList<EquipmentProfitabilityScenario> Scenarios,
    IReadOnlyCollection<string> MissingData)
{
    public bool IsPartial =>
        MissingData.Count > 0;

    public EquipmentProfitabilityScenario? BestByBenefit =>
        Scenarios.Count == 0
            ? null
            : Scenarios.MaxBy(
                scenario =>
                    scenario.Benefits
            );

    public EquipmentProfitabilityScenario? BestByYield =>
        Scenarios.Count == 0
            ? null
            : Scenarios.MaxBy(
                scenario =>
                    scenario.Yield
            );
}