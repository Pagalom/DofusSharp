using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Models;
using DofusSharp.Dofocus.ApiClients.Models.Items;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BestCrush.Components.Pages;

public partial class Server
{
    private ItemCrushResult SelectBestScenarioForCurrentSort(
        IGrouping<Equipment, ItemCrushResult> group)
    {
        Func<ItemCrushResult, double> selector =
            _sortOrder switch
            {
                SortOrder.BestYield =>
                    GetEffectiveYield,

                SortOrder.PurchaseBenefit =>
                    result =>
                        result.PurchaseBenefit ??
                            double.MinValue,

                SortOrder.PurchaseYield =>
                    result =>
                        result.PurchaseYield ??
                            double.MinValue,

                SortOrder.CraftBenefit =>
                    result =>
                        result.CraftBenefit ??
                            double.MinValue,

                SortOrder.CraftYield =>
                    result =>
                        result.CraftYield ??
                            double.MinValue,

                SortOrder.HighestTargetMargin =>
                    GetEffectiveTargetMargin,

                SortOrder.HighestRuneValue =>
                    result =>
                        result.AdjustedRuneValue,

                _ => GetEffectiveBenefit
            };

        return group
            .OrderByDescending(
                selector
            )
            .First();
    }

    private IEnumerable<ItemCrushResult> OrderResults(
        IEnumerable<ItemCrushResult> results)
    {
        return _sortOrder switch
        {
            SortOrder.BestYield =>
                results.OrderByDescending(
                    GetEffectiveYield
                ),

            SortOrder.PurchaseBenefit =>
                results.OrderByDescending(
                    result =>
                        result.PurchaseBenefit ??
                            double.MinValue
                ),

            SortOrder.PurchaseYield =>
                results.OrderByDescending(
                    result =>
                        result.PurchaseYield ??
                            double.MinValue
                ),

            SortOrder.CraftBenefit =>
                results.OrderByDescending(
                    result =>
                        result.CraftBenefit ??
                            double.MinValue
                ),

            SortOrder.CraftYield =>
                results.OrderByDescending(
                    result =>
                        result.CraftYield ??
                            double.MinValue
                ),

            SortOrder.HighestTargetMargin =>
                results.OrderByDescending(
                    GetEffectiveTargetMargin
                ),

            SortOrder.HighestRuneValue =>
                results.OrderByDescending(
                    result =>
                        result.AdjustedRuneValue
                ),

            SortOrder.NameAscending =>
                results.OrderBy(
                    result => result.Equipment.Name,
                    StringComparer.CurrentCultureIgnoreCase
                ),

            SortOrder.HighestCoefficient =>
                results
                    .OrderByDescending(
                        result =>
                            result.Coefficient
                                .CoefficientPercent
                    )
                    .ThenBy(
                        result => result.Equipment.Name,
                        StringComparer.CurrentCultureIgnoreCase
                    ),

            SortOrder.LevelAscending =>
                results
                    .OrderBy(
                        result =>
                            result.Equipment.Level
                    )
                    .ThenBy(
                        result => result.Equipment.Name,
                        StringComparer.CurrentCultureIgnoreCase
                    ),

            SortOrder.LevelDescending =>
                results
                    .OrderByDescending(
                        result =>
                            result.Equipment.Level
                    )
                    .ThenBy(
                        result => result.Equipment.Name,
                        StringComparer.CurrentCultureIgnoreCase
                    ),

            _ =>
                results.OrderByDescending(
                    GetEffectiveBenefit
                )
        };
    }

    IEnumerable<Equipment> FilterItems(IReadOnlyCollection<Equipment> items)
    {
        IEnumerable<Equipment> result = items;

        if (!string.IsNullOrWhiteSpace(
            Model?.SearchText))
        {
            string search =
                Model.SearchText.Trim();

            result = result.Where(
                item =>
                    item.Name.Contains(
                        search,
                        StringComparison.CurrentCultureIgnoreCase
                    )
            );
        }

        if (Model?.LevelMin is int levelMin)
        {
            result = result.Where(
                item => item.Level >= levelMin
            );
        }

        if (Model?.LevelMax is int levelMax)
        {
            result = result.Where(
                item => item.Level <= levelMax
            );
        }

        HashSet<EquipmentType> equipmentTypes =
            ComputeEquipmentTypes();

        if (equipmentTypes.Count > 0)
        {
            result = result.Where(
                item =>
                    equipmentTypes.Contains(
                        item.Type
                    )
            );
        }

        IReadOnlyList<RuneFilterCriterion> criteria =
            Model?.RuneFilters ?? [];

        if (criteria.Count > 0)
        {
            result = result.Where(
                equipment =>
                {
                    bool Matches(
                        RuneFilterCriterion criterion)
                    {
                        return equipment.Characteristics
                            .Any(
                                candidate =>
                                    candidate.Characteristic ==
                                    criterion.Characteristic
                            );
                    }

                    return Model!.RuneMatchMode ==
                        RuneFilterMatchMode.All
                            ? criteria.All(Matches)
                            : criteria.Any(Matches);
                }
            );
        }

        return result;
    }

    HashSet<EquipmentType> ComputeEquipmentTypes()
    {
        HashSet<EquipmentType> equipmentTypes = new();

        if (Model?.EquipmentType.Amulet is true)
        {
            equipmentTypes.Add(EquipmentType.Amulet);
        }

        if (Model?.EquipmentType.Ring is true)
        {
            equipmentTypes.Add(EquipmentType.Ring);
        }

        if (Model?.EquipmentType.Belt is true)
        {
            equipmentTypes.Add(EquipmentType.Belt);
        }

        if (Model?.EquipmentType.Boots is true)
        {
            equipmentTypes.Add(EquipmentType.Boots);
        }

        if (Model?.EquipmentType.Hat is true)
        {
            equipmentTypes.Add(EquipmentType.Hat);
        }

        if (Model?.EquipmentType.Cloak is true)
        {
            equipmentTypes.Add(EquipmentType.Cloak);
        }

        if (Model?.EquipmentType.Shield is true)
        {
            equipmentTypes.Add(EquipmentType.Shield);
        }

        if (Model?.EquipmentType.Trophy is true)
        {
            equipmentTypes.Add(EquipmentType.Trophy);
        }

        if (Model?.EquipmentType.Bow is true)
        {
            equipmentTypes.Add(EquipmentType.Bow);
        }

        if (Model?.EquipmentType.Lance is true)
        {
            equipmentTypes.Add(EquipmentType.Lance);
        }

        if (Model?.EquipmentType.Scythe is true)
        {
            equipmentTypes.Add(EquipmentType.Scythe);
        }

        if (Model?.EquipmentType.Axe is true)
        {
            equipmentTypes.Add(EquipmentType.Axe);
        }

        if (Model?.EquipmentType.Tool is true)
        {
            equipmentTypes.Add(EquipmentType.Tool);
        }

        if (Model?.EquipmentType.Pickaxe is true)
        {
            equipmentTypes.Add(EquipmentType.Pickaxe);
        }

        if (Model?.EquipmentType.Wand is true)
        {
            equipmentTypes.Add(EquipmentType.Wand);
        }

        if (Model?.EquipmentType.Staff is true)
        {
            equipmentTypes.Add(EquipmentType.Staff);
        }

        if (Model?.EquipmentType.Dagger is true)
        {
            equipmentTypes.Add(EquipmentType.Dagger);
        }

        if (Model?.EquipmentType.Sword is true)
        {
            equipmentTypes.Add(EquipmentType.Sword);
        }

        if (Model?.EquipmentType.Hammer is true)
        {
            equipmentTypes.Add(EquipmentType.Hammer);
        }

        if (Model?.EquipmentType.Shovel is true)
        {
            equipmentTypes.Add(EquipmentType.Shovel);
        }

        return equipmentTypes;
    }

    private static ItemCrushResult ToItemCrushResult(
        EquipmentProfitabilityScenario scenario)
    {
        return new ItemCrushResult(
            scenario.Equipment,
            scenario.Coefficient,
            scenario.Cost,
            scenario.CraftCost,
            scenario.PurchaseBenefit,
            scenario.PurchaseYield,
            scenario.CraftBenefit,
            scenario.CraftYield,
            scenario.FocusedCharacteristic,
            scenario.Runes,
            scenario.RuneValues,
            scenario.AdjustedRuneValue,
            scenario.TargetRoiPercent,
            scenario.MaximumCostAtTargetRoi,
            scenario.PurchaseTargetMargin,
            scenario.CraftTargetMargin,
            scenario.PurchaseMinimumCoefficientPercent,
            scenario.CraftMinimumCoefficientPercent,
            scenario.PurchaseState,
            scenario.CraftState
        );
    }

    readonly record struct ItemCrushResult(
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
        IReadOnlyDictionary<Rune, MarketValueResult> RuneValues,
        double AdjustedRuneValue,
        double TargetRoiPercent,
        double? MaximumCostAtTargetRoi,
        double? PurchaseTargetMargin,
        double? CraftTargetMargin,
        double? PurchaseMinimumCoefficientPercent,
        double? CraftMinimumCoefficientPercent,
        ProfitabilityState PurchaseState,
        ProfitabilityState CraftState)
    {
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

}
