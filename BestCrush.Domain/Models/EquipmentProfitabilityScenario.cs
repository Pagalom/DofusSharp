namespace BestCrush.Domain.Models;

public sealed record EquipmentProfitabilityScenario(
    Equipment Equipment,
    CoefficientObservation Coefficient,
    MarketPriceObservation? Cost,
    CraftCostResult CraftCost,
    double? PurchaseBenefit,
    double? PurchaseYield,
    double? CraftBenefit,
    double? CraftYield,
    Characteristic? FocusedCharacteristic,
    IReadOnlyDictionary<Rune, double> Runes,
    IReadOnlyDictionary<Rune, MarketValueResult> RuneValues)
{
    public double EstimatedRuneValue =>
        RuneValues.Values.Sum(value =>
            value.Value);

    public double Benefits =>
        PurchaseBenefit is null
            ? CraftBenefit ?? double.MinValue
            : CraftBenefit is null
                ? PurchaseBenefit.Value
                : Math.Max(
                    PurchaseBenefit.Value,
                    CraftBenefit.Value
                );

    public double Yield =>
        PurchaseYield is null
            ? CraftYield ?? double.MinValue
            : CraftYield is null
                ? PurchaseYield.Value
                : Math.Max(
                    PurchaseYield.Value,
                    CraftYield.Value
                );
}