using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public sealed partial class EquipmentProfitabilityService
{
    private EquipmentProfitabilityScenario
        BuildScenario(
            Equipment equipment,
            CoefficientObservation coefficient,
            MarketPriceObservation? itemCost,
            CraftCostResult craftCost,
            Characteristic? focusedCharacteristic,
            IReadOnlyDictionary<
                Rune,
                double> runes,
            IReadOnlyDictionary<
                Rune,
                MarketValueResult> runeValues,
            Dictionary<
                Characteristic,
                double> estimatedLines,
            IReadOnlyDictionary<
                (
                    long DofusDbId,
                    int Quantity
                ),
                MarketPriceObservation> runePrices)
    {
        double runeValue =
            runeValues.Values.Sum(value =>
                value.Value);

        double adjustedRuneValue =
            runeValue *
            settingsProvider.CrushValueMultiplier;

        double targetRoi =
            Math.Max(
                0,
                settingsProvider.TargetRoi
            );

        double targetRoiPercent =
            targetRoi *
            100.0;

        double discountPercent =
            Math.Clamp(
                (1.0 -
                    settingsProvider
                        .CrushValueMultiplier) *
                100.0,
                0.0,
                100.0
            );

        double? purchaseBenefit =
            itemCost is null
                ? null
                : adjustedRuneValue -
                  itemCost.Price;

        double? purchaseYield =
            itemCost is null
                ? null
                : itemCost.Price == 0
                    ? 0
                    : purchaseBenefit /
                      itemCost.Price;

        double? craftBenefit =
            craftCost.TotalCost is long
                craftPrice
                    ? adjustedRuneValue -
                      craftPrice
                    : null;

        double? craftYield =
            craftCost.TotalCost is long
                craftCostValue
                    ? craftCostValue == 0
                        ? 0
                        : craftBenefit /
                          craftCostValue
                    : null;

        bool hasCompleteRuneValues =
            HasCompleteRuneValues(
                runes,
                runeValues
            );

        double? maximumCostAtTargetRoi =
            hasCompleteRuneValues
                ? adjustedRuneValue /
                  (1.0 + targetRoi)
                : null;

        double? purchaseTargetMargin =
            maximumCostAtTargetRoi is double
                maximumPurchaseCost &&
            itemCost is not null
                ? maximumPurchaseCost -
                  itemCost.Price
                : null;

        double? craftTargetMargin =
            maximumCostAtTargetRoi is double
                maximumCraftCost &&
            craftCost.TotalCost is long
                currentCraftCost
                ? maximumCraftCost -
                  currentCraftCost
                : null;

        double? purchaseMinimumCoefficient =
            hasCompleteRuneValues &&
            itemCost is not null
                ? FindMinimumCoefficientPercent(
                    equipment,
                    estimatedLines,
                    focusedCharacteristic,
                    runePrices,
                    itemCost.Price,
                    targetRoi,
                    coefficient.CoefficientPercent
                )
                : null;

                double? craftMinimumCoefficient =
                    hasCompleteRuneValues &&
                    craftCost.TotalCost is long craftThresholdCost
                        ? FindMinimumCoefficientPercent(
                            equipment,
                            estimatedLines,
                            focusedCharacteristic,
                            runePrices,
                            craftThresholdCost,
                            targetRoi,
                            coefficient.CoefficientPercent
                        )
                        : null;

        ProfitabilityState purchaseState =
            hasCompleteRuneValues
                ? GetProfitabilityState(
                    purchaseYield,
                    targetRoi
                )
                : ProfitabilityState.Unavailable;

        ProfitabilityState craftState =
            hasCompleteRuneValues
                ? GetProfitabilityState(
                    craftYield,
                    targetRoi
                )
                : ProfitabilityState.Unavailable;

        return new EquipmentProfitabilityScenario(
            equipment,
            coefficient,
            itemCost,
            craftCost,
            purchaseBenefit,
            purchaseYield,
            craftBenefit,
            craftYield,
            focusedCharacteristic,
            runes,
            runeValues
        )
        {
            AdjustedRuneValue =
                adjustedRuneValue,

            DiscountPercent =
                discountPercent,

            TargetRoiPercent =
                targetRoiPercent,

            MaximumCostAtTargetRoi =
                maximumCostAtTargetRoi,

            PurchaseTargetMargin =
                purchaseTargetMargin,

            CraftTargetMargin =
                craftTargetMargin,

            PurchaseMinimumCoefficientPercent =
                purchaseMinimumCoefficient,

            CraftMinimumCoefficientPercent =
                craftMinimumCoefficient,

            PurchaseState =
                purchaseState,

            CraftState =
                craftState
        };
    }

    private static bool HasCompleteRuneValues(
        IReadOnlyDictionary<Rune, double> runes,
        IReadOnlyDictionary<Rune, MarketValueResult> runeValues)
    {
        return runes
            .Where(pair =>
                pair.Value > 0)
            .All(pair =>
                runeValues.ContainsKey(
                    pair.Key
                ));
    }

    private static ProfitabilityState
        GetProfitabilityState(
            double? yield,
            double targetRoi)
    {
        if (yield is not double currentYield)
        {
            return ProfitabilityState.Unavailable;
        }

        if (currentYield <= 0)
        {
            return ProfitabilityState.NonProfitable;
        }

        if (currentYield + 0.0000001 <
            targetRoi)
        {
            return ProfitabilityState.PositiveBelowTarget;
        }

        return ProfitabilityState.TargetReached;
    }

    private double? FindMinimumCoefficientPercent(
        Equipment equipment,
        Dictionary<Characteristic, double>
            estimatedLines,
        Characteristic? focusedCharacteristic,
        IReadOnlyDictionary<
            (
                long DofusDbId,
                int Quantity
            ),
            MarketPriceObservation> runePrices,
        double currentCost,
        double targetRoi,
        double currentCoefficientPercent)
    {
        if (currentCost <= 0)
        {
            return 0;
        }

        double requiredAdjustedRuneValue =
            currentCost *
            (1.0 + targetRoi);

        double low = 0;

        double high =
            Math.Max(
                1.0,
                currentCoefficientPercent
            );

        const double maximumSearchCoefficient =
            1_000_000.0;

        double? highValue =
            CalculateAdjustedRuneValueAtCoefficient(
                equipment,
                estimatedLines,
                focusedCharacteristic,
                runePrices,
                high
            );

        if (highValue is null)
        {
            return null;
        }

        while (highValue.Value + 0.000001 <
                requiredAdjustedRuneValue &&
            high < maximumSearchCoefficient)
        {
            low = high;

            high =
                Math.Min(
                    maximumSearchCoefficient,
                    high * 2.0
                );

            highValue =
                CalculateAdjustedRuneValueAtCoefficient(
                    equipment,
                    estimatedLines,
                    focusedCharacteristic,
                    runePrices,
                    high
                );

            if (highValue is null)
            {
                return null;
            }
        }

        if (highValue.Value + 0.000001 <
            requiredAdjustedRuneValue)
        {
            return null;
        }

        for (int iteration = 0;
            iteration < 48;
            iteration++)
        {
            double middle =
                (low + high) /
                2.0;

            double? middleValue =
                CalculateAdjustedRuneValueAtCoefficient(
                    equipment,
                    estimatedLines,
                    focusedCharacteristic,
                    runePrices,
                    middle
                );

            if (middleValue is null)
            {
                return null;
            }

            if (middleValue.Value + 0.000001 >=
                requiredAdjustedRuneValue)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return Math.Ceiling(
                high *
                100.0
            ) /
            100.0;
    }

    private double?
        CalculateAdjustedRuneValueAtCoefficient(
            Equipment equipment,
            Dictionary<Characteristic, double>
                estimatedLines,
            Characteristic? focusedCharacteristic,
            IReadOnlyDictionary<
                (
                    long DofusDbId,
                    int Quantity
                ),
                MarketPriceObservation> runePrices,
            double coefficientPercent)
    {
        double multiplier =
            Math.Max(
                0,
                coefficientPercent
            ) /
            100.0;

        IReadOnlyDictionary<Rune, double>
            simulatedRunes =
                focusedCharacteristic is Characteristic
                    characteristic
                    ? crushService
                        .GetFocusedCrushResult(
                            estimatedLines,
                            characteristic,
                            equipment.Level,
                            multiplier
                        )
                    : crushService
                        .GetCrushResult(
                            estimatedLines,
                            equipment.Level,
                            multiplier
                        );

        double totalValue = 0;

        foreach ((
            Rune rune,
            double quantity)
            in simulatedRunes)
        {
            if (quantity <= 0)
            {
                continue;
            }

            MarketValueResult? value =
                marketPriceService
                    .CalculateValue(
                        rune.DofusDbId,
                        quantity,
                        runePrices
                    );

            if (value is null)
            {
                return null;
            }

            totalValue +=
                value.Value.Value;
        }

        return totalValue *
            settingsProvider
                .CrushValueMultiplier;
    }
}
