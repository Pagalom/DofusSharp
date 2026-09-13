using System.Text.Json;
using BestCrush.Domain.Models;
using Microsoft.Maui.Storage;

namespace BestCrush.Services;

public sealed class MarketAnalyticsPresetService
{
    private const string PresetsKey =
        "Analytics.MarketPresets.v1";

    private static readonly JsonSerializerOptions
        JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<MarketAnalyticsPreset> GetAll()
    {
        string json =
            Preferences.Get(
                PresetsKey,
                "[]"
            );

        try
        {
            List<MarketAnalyticsPreset>? presets =
                JsonSerializer.Deserialize<
                    List<MarketAnalyticsPreset>>(
                        json,
                        JsonOptions
                    );

            return (presets ?? [])
                .OrderBy(preset => preset.Name)
                .Select(Clone)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public void Save(
        MarketAnalyticsPreset preset)
    {
        List<MarketAnalyticsPreset> presets =
            GetAll()
                .Select(Clone)
                .ToList();

        int index = presets.FindIndex(
            existing =>
                existing.Id == preset.Id
        );

        if (index >= 0)
        {
            presets[index] = Clone(preset);
        }
        else
        {
            presets.Add(Clone(preset));
        }

        Persist(presets);
    }

    public void Delete(Guid presetId)
    {
        List<MarketAnalyticsPreset> presets =
            GetAll()
                .Where(preset =>
                    preset.Id != presetId)
                .Select(Clone)
                .ToList();

        Persist(presets);
    }

    public static MarketAnalyticsPreset Clone(
        MarketAnalyticsPreset preset)
    {
        return new MarketAnalyticsPreset
        {
            Id = preset.Id,
            Name = preset.Name,
            Period = preset.Period,
            Series = preset.Series
                .Select(Clone)
                .ToList()
        };
    }

    public static MarketAnalyticsSeriesDefinition Clone(
        MarketAnalyticsSeriesDefinition series)
    {
        return new MarketAnalyticsSeriesDefinition
        {
            Id = series.Id,
            ObjectType = series.ObjectType,
            DofusDbId = series.DofusDbId,
            ItemName = series.ItemName,
            LotQuantity = series.LotQuantity,
            ValueMode = series.ValueMode,
            Aggregation = series.Aggregation,
            SourceMode = series.SourceMode,
            Transformation = series.Transformation
        };
    }

    private static void Persist(
        IReadOnlyCollection<MarketAnalyticsPreset> presets)
    {
        string json =
            JsonSerializer.Serialize(
                presets,
                JsonOptions
            );

        Preferences.Set(
            PresetsKey,
            json
        );
    }
}
