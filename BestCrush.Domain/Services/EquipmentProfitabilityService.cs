using BestCrush.Domain.Models;

using DofusSharp.Dofocus.ApiClients.Models.Items;

namespace BestCrush.Domain.Services;

public sealed partial class EquipmentProfitabilityService(
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

        IReadOnlyList<Rune> potentialRunes =
            crushService
                .GetCrushResult(
                    estimatedLines,
                    equipment.Level,
                    coefficient: 1.0
                )
                .Keys
                .OrderBy(rune =>
                    rune.Name)
                .ToArray();

        Dictionary<Rune, MarketValueResult>
            potentialRuneUnitValues = [];

        foreach (Rune rune in potentialRunes)
        {
            MarketValueResult? unitValue =
                marketPriceService
                    .CalculateValue(
                        rune.DofusDbId,
                        1,
                        context.RunePrices
                    );

            if (unitValue is MarketValueResult value)
            {
                potentialRuneUnitValues[rune] =
                    value;
            }
            else
            {
                missingData.Add(
                    $"Prix de rune manquant : " +
                    rune.Name
                );
            }
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
            )
            {
                PotentialRunes =
                    potentialRunes,

                PotentialRuneUnitValues =
                    potentialRuneUnitValues
            };
        }

        double coefficientMultiplier =
            coefficient.CoefficientPercent /
            100;

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
        )
        {
            PotentialRunes =
                potentialRunes,

            PotentialRuneUnitValues =
                potentialRuneUnitValues
        };
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

}
