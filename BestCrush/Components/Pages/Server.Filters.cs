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
    private async Task ApplyOpportunitiesAsync()
    {
        Model ??= new SearchItemsModel();

        Model.ProfitabilityMode =
            ProfitabilityFilterMode.Best;
        Model.CompleteDataOnly = true;
        Model.FreshDataOnly = true;
        Model.RequireCoefficient = true;

        _sortOrder = SortOrder.BestYield;
        _selectedPresetId = null;
        _presetMessage =
            "✓ Filtre Opportunités appliqué.";

        await Search(
            forceRefresh: false
        );
    }

    private bool HasEconomicFilters()
    {
        return Model is not null &&
            (
                Model.MinimumRoiPercent is not null ||
                Model.MinimumBenefit is not null ||
                Model.CompleteDataOnly ||
                Model.FreshDataOnly ||
                Model.RequireCoefficient ||
                Model.RuneFilters.Count > 0
            );
    }

    private bool MatchesExactRuneFilters(
        ItemCrushResult item)
    {
        IReadOnlyList<RuneFilterCriterion> criteria =
            Model?.RuneFilters ?? [];

        if (criteria.Count == 0)
        {
            return true;
        }

        HashSet<long> obtainedRuneIds =
            item.Runes
                .Where(entry => entry.Value > 0)
                .Select(entry => entry.Key.DofusDbId)
                .ToHashSet();

        return Model!.RuneMatchMode ==
            RuneFilterMatchMode.All
                ? criteria.All(
                    criterion =>
                        obtainedRuneIds.Contains(
                            criterion.DofusDbId
                        )
                )
                : criteria.Any(
                    criterion =>
                        obtainedRuneIds.Contains(
                            criterion.DofusDbId
                        )
                );
    }

    private bool PassesEconomicFilters(
        ItemCrushResult item)
    {
        SearchItemsModel? model =
            Model;

        if (model is null)
        {
            return true;
        }

        if (model.RequireCoefficient &&
            item.Coefficient.CoefficientPercent <= 0)
        {
            return false;
        }

        if (model.CompleteDataOnly &&
            IsPartial(item))
        {
            return false;
        }

        if (model.FreshDataOnly &&
            !HasFreshData(item))
        {
            return false;
        }

        double effectiveYield =
            GetEffectiveYield(item);

        if (model.MinimumRoiPercent
                is double minimumRoi &&
            effectiveYield * 100.0 < minimumRoi)
        {
            return false;
        }

        double effectiveBenefit =
            GetEffectiveBenefit(item);

        if (model.MinimumBenefit
                is double minimumBenefit &&
            effectiveBenefit < minimumBenefit)
        {
            return false;
        }

        return true;
    }

    private bool HasFreshData(
        ItemCrushResult item)
    {
        if (DataFreshnessEvaluator
                .Evaluate(
                    item.Coefficient.ObservedAtUtc
                ) != DataFreshness.Fresh)
        {
            return false;
        }

        if (item.RuneValues.Values.Any(
            value =>
                value.Freshness !=
                    DataFreshness.Fresh))
        {
            return false;
        }

        bool purchaseFresh =
            item.Cost is not null &&
            DataFreshnessEvaluator
                .Evaluate(
                    item.Cost.ObservedAtUtc
                ) == DataFreshness.Fresh;

        bool craftFresh =
            item.CraftCost.IsComplete &&
            item.CraftCost.Resources.All(
                resource =>
                    resource.Purchase?.Freshness ==
                        DataFreshness.Fresh
            );

        return (Model?.ProfitabilityMode ??
            ProfitabilityFilterMode.Best) switch
        {
            ProfitabilityFilterMode.Purchase =>
                purchaseFresh,

            ProfitabilityFilterMode.Craft =>
                craftFresh,

            _ =>
                purchaseFresh || craftFresh
        };
    }

    private double GetEffectiveBenefit(
        ItemCrushResult item)
    {
        return (Model?.ProfitabilityMode ??
            ProfitabilityFilterMode.Best) switch
        {
            ProfitabilityFilterMode.Purchase =>
                item.PurchaseBenefit ??
                    double.MinValue,

            ProfitabilityFilterMode.Craft =>
                item.CraftBenefit ??
                    double.MinValue,

            _ => item.Benefits
        };
    }

    private double GetEffectiveYield(
        ItemCrushResult item)
    {
        return (Model?.ProfitabilityMode ??
            ProfitabilityFilterMode.Best) switch
        {
            ProfitabilityFilterMode.Purchase =>
                item.PurchaseYield ??
                    double.MinValue,

            ProfitabilityFilterMode.Craft =>
                item.CraftYield ??
                    double.MinValue,

            _ => item.Yield
        };
    }

    private double GetEffectiveTargetMargin(
        ItemCrushResult item)
    {
        return (Model?.ProfitabilityMode ??
            ProfitabilityFilterMode.Best) switch
        {
            ProfitabilityFilterMode.Purchase =>
                item.PurchaseTargetMargin ??
                    double.MinValue,

            ProfitabilityFilterMode.Craft =>
                item.CraftTargetMargin ??
                    double.MinValue,

            _ => Math.Max(
                item.PurchaseTargetMargin ??
                    double.MinValue,
                item.CraftTargetMargin ??
                    double.MinValue
            )
        };
    }

}
