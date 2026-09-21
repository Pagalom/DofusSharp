namespace BestCrush.Domain.Models;

public sealed record EquipmentProfitabilityResult(
    Equipment Equipment,
    CoefficientObservation? Coefficient,
    MarketPriceObservation? EquipmentCost,
    CraftCostResult CraftCost,
    IReadOnlyList<EquipmentProfitabilityScenario> Scenarios,
    IReadOnlyCollection<string> MissingData)
{
    // Runes that can be produced from the equipment's characteristics,
    // independently from the current crushing coefficient. This keeps
    // the overlay informative even when no coefficient is known yet.
    public IReadOnlyList<Rune> PotentialRunes { get; init; } = [];

    // Unit market values are coefficient-independent as well. They let
    // the hover tooltip distinguish "price missing" from "coefficient
    // missing" instead of hiding the rune list entirely.
    public IReadOnlyDictionary<Rune, MarketValueResult>
        PotentialRuneUnitValues { get; init; } =
            new Dictionary<Rune, MarketValueResult>();

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