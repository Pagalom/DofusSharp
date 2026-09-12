namespace BestCrush.Domain.Models;

public enum ProfitabilityState
{
    Unavailable = 0,
    TargetReached = 1,
    PositiveBelowTarget = 2,
    NonProfitable = 3
}

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

    public double AdjustedRuneValue
    {
        get;
        init;
    }

    public double DiscountPercent
    {
        get;
        init;
    }

    public double TargetRoiPercent
    {
        get;
        init;
    }

    public double? MaximumCostAtTargetRoi
    {
        get;
        init;
    }

    public double? PurchaseTargetMargin
    {
        get;
        init;
    }

    public double? CraftTargetMargin
    {
        get;
        init;
    }

    public double? PurchaseMinimumCoefficientPercent
    {
        get;
        init;
    }

    public double? CraftMinimumCoefficientPercent
    {
        get;
        init;
    }

    public ProfitabilityState PurchaseState
    {
        get;
        init;
    } = ProfitabilityState.Unavailable;

    public ProfitabilityState CraftState
    {
        get;
        init;
    } = ProfitabilityState.Unavailable;

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
