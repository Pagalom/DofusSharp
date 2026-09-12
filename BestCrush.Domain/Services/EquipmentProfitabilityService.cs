using BestCrush.Domain.Models;

using DofusSharp.Dofocus.ApiClients.Models.Items;

namespace BestCrush.Domain.Services;

public sealed class EquipmentProfitabilityService(
    ItemsService itemsService,
    CrushService crushService,
    CoefficientService coefficientService,
    MarketPriceService marketPriceService,
    CraftCostService craftCostService,
    IBestCrushSettingsProvider settingsProvider)
{
    public async Task<EquipmentProfitabilityContext>
        LoadContextAsync(
            string serverName,
            CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation>
            runePrices =
                await marketPriceService
                    .GetLatestObservationsForServerAsync(
                        MarketObjectType.Rune,
                        serverName,
                        cancellationToken
                    );

        IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation>
            equipmentPrices =
                await marketPriceService
                    .GetLatestObservationsForServerAsync(
                        MarketObjectType.Equipment,
                        serverName,
                        cancellationToken
                    );

        IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation>
            resourcePrices =
                await marketPriceService
                    .GetLatestObservationsForServerAsync(
                        MarketObjectType.Resource,
                        serverName,
                        cancellationToken
                    );

        Dictionary<long, CoefficientObservation>
            coefficients =
                (await coefficientService
                    .GetLatestObservationsForServerAsync(
                        serverName,
                        cancellationToken
                    ))
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value
                );

        return new EquipmentProfitabilityContext
        {
            RunePrices = runePrices,
            EquipmentPrices = equipmentPrices,
            ResourcePrices = resourcePrices,
            Coefficients = coefficients
        };
    }

    public async Task<EquipmentProfitabilityResult>
        CalculateAsync(
            Equipment equipment,
            string serverName,
            EquipmentProfitabilityContext context,
            bool forceRefresh = false)
    {
        HashSet<string> missingData = [];

        CoefficientObservation? coefficient =
            await ResolveCoefficientAsync(
                equipment,
                serverName,
                context,
                missingData,
                forceRefresh
            );

        context.EquipmentPrices.TryGetValue(
            (
                equipment.DofusDbId,
                1
            ),
            out MarketPriceObservation? itemCost
        );

        if (itemCost is null)
        {
            missingData.Add(
                "Prix local de l'équipement manquant"
            );
        }

        CraftCostResult craftCost =
            craftCostService.Calculate(
                equipment,
                context.ResourcePrices,
                context.EquipmentPrices
            );

        if (!craftCost.IsComplete)
        {
            missingData.Add(
                $"Craft incomplet : " +
                $"{craftCost.MissingIngredientCount} " +
                "prix d'ingrédient manquant(s)"
            );
        }

        if (coefficient is null ||
            (
                itemCost is null &&
                craftCost.TotalCost is null
            ))
        {
            return new EquipmentProfitabilityResult(
                equipment,
                coefficient,
                itemCost,
                craftCost,
                [],
                missingData
            );
        }

        double coefficientMultiplier =
            coefficient.CoefficientPercent /
            100;

        Dictionary<Characteristic, double>
            estimatedLines =
                equipment.Characteristics
                    .ToDictionary(
                        characteristic =>
                            characteristic.Characteristic,
                        characteristic =>
                            settingsProvider
                                .CrushYieldEstimationMode ==
                                    CrushYieldEstimationMode.Conservative
                                ? Math.Min(
                                    characteristic.From,
                                    characteristic.To
                                )
                                : (
                                    characteristic.From +
                                    characteristic.To
                                ) / 2.0
                    );

        List<EquipmentProfitabilityScenario>
            scenarios = [];

        IReadOnlyDictionary<Rune, double>
            runesWithoutFocus =
                crushService.GetCrushResult(
                    estimatedLines,
                    equipment.Level,
                    coefficientMultiplier
                );

        IReadOnlyDictionary<
            Rune,
            MarketValueResult>?
            valuesWithoutFocus =
                CalculateRuneValues(
                    runesWithoutFocus,
                    context.RunePrices,
                    missingData
                );

        if (valuesWithoutFocus is not null)
        {
            scenarios.Add(
                BuildScenario(
                    equipment,
                    coefficient,
                    itemCost,
                    craftCost,
                    null,
                    runesWithoutFocus,
                    valuesWithoutFocus,
                    estimatedLines,
                    context.RunePrices
                )
            );
        }

        foreach ((
            Characteristic characteristic,
            double value)
            in estimatedLines)
        {
            if (value <= 0)
            {
                continue;
            }

            IReadOnlyDictionary<Rune, double>
                runesWithFocus =
                    crushService
                        .GetFocusedCrushResult(
                            estimatedLines,
                            characteristic,
                            equipment.Level,
                            coefficientMultiplier
                        );

            IReadOnlyDictionary<
                Rune,
                MarketValueResult>?
                valuesWithFocus =
                    CalculateRuneValues(
                        runesWithFocus,
                        context.RunePrices,
                        missingData
                    );

            if (valuesWithFocus is null)
            {
                continue;
            }

            scenarios.Add(
                BuildScenario(
                    equipment,
                    coefficient,
                    itemCost,
                    craftCost,
                    characteristic,
                    runesWithFocus,
                    valuesWithFocus,
                    estimatedLines,
                    context.RunePrices
                )
            );
        }

        return new EquipmentProfitabilityResult(
            equipment,
            coefficient,
            itemCost,
            craftCost,
            scenarios,
            missingData
        );
    }

    public async Task<EquipmentProfitabilityResult>
        CalculateAsync(
            Equipment equipment,
            string serverName,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
    {
        EquipmentProfitabilityContext context =
            await LoadContextAsync(
                serverName,
                cancellationToken
            );

        return await CalculateAsync(
            equipment,
            serverName,
            context,
            forceRefresh
        );
    }

    private async Task<CoefficientObservation?>
        ResolveCoefficientAsync(
            Equipment equipment,
            string serverName,
            EquipmentProfitabilityContext context,
            HashSet<string> missingData,
            bool forceRefresh)
    {
        if (context.Coefficients.TryGetValue(
            equipment.DofusDbId,
            out CoefficientObservation?
                coefficient))
        {
            return coefficient;
        }

        DofocusItem detailedItem;

        try
        {
            detailedItem =
                await itemsService.GetItemAsync(
                    equipment.DofusDbId,
                    forceRefresh
                );
        }
        catch
        {
            missingData.Add(
                "Coefficient de brisage indisponible"
            );

            return null;
        }

        DofocusCoefficientRecord? dofocusCoefficient =
            detailedItem.Coefficients
                .Where(record =>
                    record.ServerName ==
                    serverName)
                .OrderByDescending(record =>
                    record.LastUpdate)
                .FirstOrDefault();

        if (dofocusCoefficient is null)
        {
            missingData.Add(
                "Coefficient de brisage indisponible"
            );

            return null;
        }

        coefficient =
            await coefficientService
                .AddObservationAsync(
                    equipment.DofusDbId,
                    serverName,
                    dofocusCoefficient.Coefficient,
                    CoefficientSource.DofocusInitial
                );

        context.Coefficients[
            equipment.DofusDbId
        ] = coefficient;

        return coefficient;
    }

    private IReadOnlyDictionary<
        Rune,
        MarketValueResult>?
        CalculateRuneValues(
            IReadOnlyDictionary<
                Rune,
                double> runes,
            IReadOnlyDictionary<
                (
                    long DofusDbId,
                    int Quantity
                ),
                MarketPriceObservation> prices,
            HashSet<string> missingData)
    {
        Dictionary<
            Rune,
            MarketValueResult> result = [];

        bool hasMissingPrice = false;

        foreach ((
            Rune rune,
            double quantity)
            in runes)
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
                        prices
                    );

            if (value is null)
            {
                missingData.Add(
                    $"Prix de rune manquant : " +
                    rune.Name
                );

                hasMissingPrice = true;

                continue;
            }

            result[rune] =
                value.Value;
        }

        if (result.Count == 0 &&
            hasMissingPrice)
        {
            return null;
        }

        return result;
    }

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