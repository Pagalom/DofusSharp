using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

public sealed class MarketAnalyticsPresetService(IWorkspacePreferences preferences, string? filePath = null)
{
    private readonly object _sync = new();
    private const string PresetsKey =
        "Analytics.MarketPresets.v1";

    private static readonly JsonSerializerOptions
        JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<MarketAnalyticsPreset> GetAll()
    {
        lock (_sync) return Read();
    }

    private IReadOnlyList<MarketAnalyticsPreset> Read()
    {
        bool fromFile = filePath is not null && File.Exists(filePath);
        string json =
            fromFile ? File.ReadAllText(filePath!) : preferences.GetString(PresetsKey) ?? "[]";

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
        catch (JsonException error)
        {
            if (fromFile) throw new InvalidDataException("Les analyses enregistrées ne peuvent pas être lues.",error);
            return [];
        }
    }

    public void Save(
        MarketAnalyticsPreset preset)
    {
        lock (_sync)
        {
        List<MarketAnalyticsPreset> presets =
            Read()
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
    }

    public void Delete(Guid presetId)
    {
        lock (_sync)
        {
        List<MarketAnalyticsPreset> presets =
            Read()
                .Where(preset =>
                    preset.Id != presetId)
                .Select(Clone)
                .ToList();

        Persist(presets);
        }
    }

    public static MarketAnalyticsPreset Clone(
        MarketAnalyticsPreset preset)
    {
        return new MarketAnalyticsPreset
        {
            Id = preset.Id,
            Name = preset.Name,
            Period = preset.Period,
            Scale = preset.Scale,
            MaxQuoteAgeHours = preset.MaxQuoteAgeHours,
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
            SelectionMode = series.SelectionMode,
            Members = (series.Members ?? []).ToList(),
            Characteristic = series.Characteristic,
            EquipmentTypes = (series.EquipmentTypes ?? []).ToList(),
            LevelMin = series.LevelMin,
            LevelMax = series.LevelMax,
            SearchText = series.SearchText,
            FuseMembers = series.FuseMembers,
            IsVisible = series.IsVisible,
            GroupAggregation = series.GroupAggregation,
            LotQuantity = series.LotQuantity,
            ValueMode = series.ValueMode,
            Aggregation = series.Aggregation,
            SourceMode = series.SourceMode,
            Transformation = series.Transformation
        };
    }

    private void Persist(
        IReadOnlyCollection<MarketAnalyticsPreset> presets)
    {
        string json =
            JsonSerializer.Serialize(
                presets,
                JsonOptions
            );

        if (filePath is null) { preferences.SetString(PresetsKey,json); return; }
        string target = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary,json);
            File.Move(temporary,target,overwrite:true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
