using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Models;
using Microsoft.AspNetCore.Components;
using Rune = BestCrush.Domain.Models.Rune;

namespace BestCrush.Components.Pages;

public partial class History
{
    private void LoadAnalyticsPresets()
    {
        _analyticsPresets =
            MarketAnalyticsPresetService
                .GetAll()
                .ToList();
    }

    private async Task BuildAnalyticsResultsAsync()
    {
        List<MarketAnalyticsSeriesResult> results = [];

        foreach (
            MarketAnalyticsSeriesDefinition series
            in _analyticsSeries)
        {
            if (series.DofusDbId <= 0)
            {
                continue;
            }

            MarketAnalyticsSeriesResult result =
                await MarketAnalyticsService
                    .BuildSeriesAsync(
                        series,
                        ServerName,
                        _analyticsPeriod
                    );

            results.Add(result);
        }

        _analyticsResults = results;
    }

    private async Task RefreshAnalyticsAsync()
    {
        _loading = true;
        _errorMessage = null;

        try
        {
            await BuildAnalyticsResultsAsync();
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private Task NewAnalyticsAsync()
    {
        _selectedAnalyticsPresetId = null;
        _analyticsName = "Nouvelle analyse";
        _analyticsPeriod = MarketAnalyticsPeriod.Days7;
        _analyticsSeries = [];
        _analyticsResults = [];
        _analyticsDraft = null;
        _editingAnalyticsSeriesId = null;
        _errorMessage = null;

        return Task.CompletedTask;
    }

    private async Task OnAnalyticsPresetChangedAsync(
        ChangeEventArgs args)
    {
        string? value = args.Value?.ToString();

        if (!Guid.TryParse(value, out Guid presetId))
        {
            await NewAnalyticsAsync();
            return;
        }

        MarketAnalyticsPreset? preset =
            _analyticsPresets.FirstOrDefault(
                candidate =>
                    candidate.Id == presetId
            );

        if (preset is null)
        {
            return;
        }

        _selectedAnalyticsPresetId = preset.Id;
        _analyticsName = preset.Name;
        _analyticsPeriod = preset.Period;
        _analyticsSeries = preset.Series
            .Select(
                BestCrush.Services
                    .MarketAnalyticsPresetService
                    .Clone)
            .ToList();
        _analyticsDraft = null;
        _editingAnalyticsSeriesId = null;

        await RefreshAnalyticsAsync();
    }

    private Task SaveAnalyticsPresetAsync(
        bool saveAs)
    {
        string name =
            _analyticsName.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            _errorMessage =
                "Donne un nom à l'analyse avant de l'enregistrer.";
            return Task.CompletedTask;
        }

        Guid id =
            saveAs ||
            _selectedAnalyticsPresetId is null
                ? Guid.NewGuid()
                : _selectedAnalyticsPresetId.Value;

        MarketAnalyticsPreset preset = new()
        {
            Id = id,
            Name = name,
            Period = _analyticsPeriod,
            Series = _analyticsSeries
                .Select(
                    BestCrush.Services
                        .MarketAnalyticsPresetService
                        .Clone)
                .ToList()
        };

        MarketAnalyticsPresetService.Save(preset);
        LoadAnalyticsPresets();

        _selectedAnalyticsPresetId = id;
        _analyticsName = name;
        _errorMessage = null;

        return Task.CompletedTask;
    }

    private Task DeleteAnalyticsPresetAsync()
    {
        if (_selectedAnalyticsPresetId is not Guid presetId)
        {
            return Task.CompletedTask;
        }

        MarketAnalyticsPresetService.Delete(presetId);
        LoadAnalyticsPresets();

        _selectedAnalyticsPresetId = null;
        _analyticsName = "Nouvelle analyse";

        return Task.CompletedTask;
    }

    private bool AnalyticsPresetIsModified
    {
        get
        {
            if (_selectedAnalyticsPresetId is not Guid presetId)
            {
                return false;
            }

            MarketAnalyticsPreset? saved =
                _analyticsPresets.FirstOrDefault(
                    preset =>
                        preset.Id == presetId
                );

            if (saved is null ||
                !string.Equals(
                    saved.Name,
                    _analyticsName.Trim(),
                    StringComparison.Ordinal) ||
                saved.Period != _analyticsPeriod ||
                saved.Series.Count != _analyticsSeries.Count)
            {
                return true;
            }

            for (int index = 0;
                index < saved.Series.Count;
                index++)
            {
                if (!AnalyticsSeriesEqual(
                    saved.Series[index],
                    _analyticsSeries[index]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool AnalyticsSeriesEqual(
        MarketAnalyticsSeriesDefinition left,
        MarketAnalyticsSeriesDefinition right)
    {
        return
            left.Id == right.Id &&
            left.ObjectType == right.ObjectType &&
            left.DofusDbId == right.DofusDbId &&
            string.Equals(
                left.ItemName,
                right.ItemName,
                StringComparison.Ordinal) &&
            left.LotQuantity == right.LotQuantity &&
            left.ValueMode == right.ValueMode &&
            left.Aggregation == right.Aggregation &&
            left.SourceMode == right.SourceMode &&
            left.Transformation == right.Transformation;
    }

    private void BeginAddAnalyticsSeries()
    {
        MarketAnalyticsSeriesDefinition? draft =
            CreateDefaultAnalyticsSeries();

        if (draft is null)
        {
            _errorMessage =
                "Aucun élément n'est disponible pour créer une série.";
            return;
        }

        _editingAnalyticsSeriesId = null;
        _analyticsDraft = draft;
        _errorMessage = null;
    }

    private MarketAnalyticsSeriesDefinition?
        CreateDefaultAnalyticsSeries()
    {
        foreach (MarketObjectType objectType in new[]
        {
            MarketObjectType.Rune,
            MarketObjectType.Resource,
            MarketObjectType.Equipment
        })
        {
            AnalyticsCatalogItem? first =
                GetAnalyticsCatalogItems(objectType)
                    .FirstOrDefault();

            if (first is not null)
            {
                return new MarketAnalyticsSeriesDefinition
                {
                    ObjectType = objectType,
                    DofusDbId = first.DofusDbId,
                    ItemName = first.Name,
                    LotQuantity =
                        objectType == MarketObjectType.Equipment
                            ? 1
                            : null
                };
            }
        }

        return null;
    }

    private void BeginEditAnalyticsSeries(Guid seriesId)
    {
        MarketAnalyticsSeriesDefinition? series =
            _analyticsSeries.FirstOrDefault(candidate =>
                candidate.Id == seriesId);

        if (series is null)
        {
            return;
        }

        _editingAnalyticsSeriesId = seriesId;
        MarketAnalyticsSeriesDefinition draft =
            BestCrush.Services
                .MarketAnalyticsPresetService
                .Clone(series);

        if (draft.LotQuantity is null)
        {
            draft.ValueMode =
                MarketAnalyticsValueMode.UnitPrice;
        }

        _analyticsDraft = draft;
        _errorMessage = null;
    }

    private void CancelAnalyticsSeriesDraft()
    {
        _analyticsDraft = null;
        _editingAnalyticsSeriesId = null;
    }

    private async Task CommitAnalyticsSeriesDraftAsync()
    {
        if (_analyticsDraft is null)
        {
            return;
        }

        MarketAnalyticsSeriesDefinition committed =
            BestCrush.Services
                .MarketAnalyticsPresetService
                .Clone(_analyticsDraft);

        if (committed.LotQuantity is null)
        {
            committed.ValueMode =
                MarketAnalyticsValueMode.UnitPrice;
        }

        if (_editingAnalyticsSeriesId is Guid seriesId)
        {
            int index = _analyticsSeries.FindIndex(candidate =>
                candidate.Id == seriesId);

            if (index >= 0)
            {
                committed.Id = seriesId;
                _analyticsSeries[index] = committed;
            }
        }
        else
        {
            _analyticsSeries.Add(committed);
        }

        _analyticsDraft = null;
        _editingAnalyticsSeriesId = null;

        await RefreshAnalyticsAsync();
    }

    private async Task RemoveAnalyticsSeriesAsync(
        Guid seriesId)
    {
        _analyticsSeries.RemoveAll(
            series =>
                series.Id == seriesId);

        if (_editingAnalyticsSeriesId == seriesId)
        {
            CancelAnalyticsSeriesDraft();
        }

        await RefreshAnalyticsAsync();
    }

    private async Task OnAnalyticsPeriodChangedAsync(
        ChangeEventArgs args)
    {
        if (Enum.TryParse(
            args.Value?.ToString(),
            out MarketAnalyticsPeriod period))
        {
            _analyticsPeriod = period;
            await RefreshAnalyticsAsync();
        }
    }

    private void OnAnalyticsDraftObjectTypeChanged(
        ChangeEventArgs args)
    {
        if (_analyticsDraft is null ||
            !Enum.TryParse(
            args.Value?.ToString(),
            out MarketObjectType objectType))
        {
            return;
        }

        AnalyticsCatalogItem? first =
            GetAnalyticsCatalogItems(objectType)
                .FirstOrDefault();

        if (first is null)
        {
            return;
        }

        _analyticsDraft.ObjectType = objectType;
        _analyticsDraft.DofusDbId = first.DofusDbId;
        _analyticsDraft.ItemName = first.Name;
        _analyticsDraft.LotQuantity =
            objectType == MarketObjectType.Equipment
                ? 1
                : null;
        _analyticsDraft.ValueMode =
            MarketAnalyticsValueMode.UnitPrice;
    }

    private void OnAnalyticsDraftItemChanged(
        ChangeEventArgs args)
    {
        if (_analyticsDraft is null ||
            !long.TryParse(
            args.Value?.ToString(),
            out long dofusDbId))
        {
            return;
        }

        AnalyticsCatalogItem? item =
            GetAnalyticsCatalogItems(
                _analyticsDraft.ObjectType)
                .FirstOrDefault(candidate =>
                    candidate.DofusDbId == dofusDbId);

        if (item is null)
        {
            return;
        }

        _analyticsDraft.DofusDbId = item.DofusDbId;
        _analyticsDraft.ItemName = item.Name;
    }

    private void OnAnalyticsDraftLotChanged(
        ChangeEventArgs args)
    {
        if (_analyticsDraft is null)
        {
            return;
        }

        string? value = args.Value?.ToString();

        _analyticsDraft.LotQuantity =
            int.TryParse(value, out int quantity) &&
            quantity > 0
                ? quantity
                : null;

        if (_analyticsDraft.LotQuantity is null)
        {
            _analyticsDraft.ValueMode =
                MarketAnalyticsValueMode.UnitPrice;
        }
    }

    private void OnAnalyticsDraftValueModeChanged(
        ChangeEventArgs args)
    {
        if (_analyticsDraft is not null &&
            Enum.TryParse(
            args.Value?.ToString(),
            out MarketAnalyticsValueMode value))
        {
            _analyticsDraft.ValueMode =
                _analyticsDraft.LotQuantity is null
                    ? MarketAnalyticsValueMode.UnitPrice
                    : value;
        }
    }

    private void OnAnalyticsDraftAggregationChanged(
        ChangeEventArgs args)
    {
        if (_analyticsDraft is not null &&
            Enum.TryParse(
            args.Value?.ToString(),
            out MarketAnalyticsAggregation value))
        {
            _analyticsDraft.Aggregation = value;
        }
    }

    private void OnAnalyticsDraftSourceChanged(
        ChangeEventArgs args)
    {
        if (_analyticsDraft is not null &&
            Enum.TryParse(
            args.Value?.ToString(),
            out MarketAnalyticsSourceMode value))
        {
            _analyticsDraft.SourceMode = value;
        }
    }

    private void OnAnalyticsDraftTransformationChanged(
        ChangeEventArgs args)
    {
        if (_analyticsDraft is not null &&
            Enum.TryParse(
            args.Value?.ToString(),
            out MarketAnalyticsTransformation value))
        {
            _analyticsDraft.Transformation = value;
        }
    }

    private IEnumerable<AnalyticsCatalogItem>
        GetAnalyticsCatalogItems(
            MarketObjectType objectType)
    {
        IEnumerable<AnalyticsCatalogItem> items =
            objectType switch
            {
                MarketObjectType.Resource =>
                    _resources.Values.Select(item =>
                        new AnalyticsCatalogItem(
                            item.DofusDbId,
                            item.Name)),

                MarketObjectType.Equipment =>
                    _equipments.Values.Select(item =>
                        new AnalyticsCatalogItem(
                            item.DofusDbId,
                            item.Name)),

                _ =>
                    _runes.Select(item =>
                        new AnalyticsCatalogItem(
                            item.DofusDbId,
                            item.Name))
            };

        return items.OrderBy(item => item.Name);
    }

    private sealed record AnalyticsCatalogItem(
        long DofusDbId,
        string Name
    );

}
