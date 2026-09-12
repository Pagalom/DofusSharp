using BestCrush.Domain.Services;
using BestCrush.Models;
using Microsoft.Maui.Storage;
using System.Text.Json;

namespace BestCrush.Services;

public sealed class BestCrushSettingsService
    : IBestCrushSettingsProvider
{
    private const string EquipmentCaptureKey =
        "Settings.EquipmentCaptureEnabled";

    private const string RuneCaptureKey =
        "Settings.RuneCaptureEnabled";

    private const string ResourceCaptureKey =
        "Settings.ResourceCaptureEnabled";

    private const string CoefficientCaptureKey =
        "Settings.CoefficientCaptureEnabled";

    private const string TargetRoiKey =
        "Settings.TargetRoiPercent";

    private const string CrushYieldEstimationModeKey =
        "Settings.CrushYieldEstimationMode";

    private const string CrushValueDiscountKey =
        "Settings.CrushValueDiscountPercent";

    private const string SearchFilterPresetsKey =
        "Settings.SearchFilterPresets";

    private const string
        DevToolRemoveScreenshotsByDefaultKey =
            "Settings.DevTool_RemoveScreenshotsByDefault";

    public bool EquipmentCaptureEnabled
    {
        get =>
            Preferences.Get(
                EquipmentCaptureKey,
                true
            );

        set =>
            Preferences.Set(
                EquipmentCaptureKey,
                value
            );
    }

    public bool RuneCaptureEnabled
    {
        get =>
            Preferences.Get(
                RuneCaptureKey,
                true
            );

        set =>
            Preferences.Set(
                RuneCaptureKey,
                value
            );
    }

    public bool ResourceCaptureEnabled
    {
        get =>
            Preferences.Get(
                ResourceCaptureKey,
                true
            );

        set =>
            Preferences.Set(
                ResourceCaptureKey,
                value
            );
    }

    public bool CoefficientCaptureEnabled
    {
        get =>
            Preferences.Get(
                CoefficientCaptureKey,
                true
            );

        set =>
            Preferences.Set(
                CoefficientCaptureKey,
                value
            );
    }

    public bool DevTool_RemoveScreenshotsByDefault
    {
        get =>
            Preferences.Get(
                DevToolRemoveScreenshotsByDefaultKey,
                true
            );

        set =>
            Preferences.Set(
                DevToolRemoveScreenshotsByDefaultKey,
                value
            );
    }

    public CrushYieldEstimationMode CrushYieldEstimationMode
    {
        get
        {
            int storedValue =
                Preferences.Get(
                    CrushYieldEstimationModeKey,
                    (int)CrushYieldEstimationMode.Average
                );

            return Enum.IsDefined(
                typeof(CrushYieldEstimationMode),
                storedValue
            )
                ? (CrushYieldEstimationMode)storedValue
                : CrushYieldEstimationMode.Average;
        }

        set =>
            Preferences.Set(
                CrushYieldEstimationModeKey,
                (int)value
            );
    }

    public double TargetRoiPercent
    {
        get =>
            Preferences.Get(
                TargetRoiKey,
                15.0
            );

        set =>
            Preferences.Set(
                TargetRoiKey,
                Math.Clamp(
                    value,
                    0.0,
                    1000.0
                )
            );
    }

    public double CrushValueDiscountPercent
    {
        get =>
            Preferences.Get(
                CrushValueDiscountKey,
                5.0
            );

        set =>
            Preferences.Set(
                CrushValueDiscountKey,
                Math.Clamp(
                    value,
                    0.0,
                    100.0
                )
            );
    }

    public IReadOnlyList<SearchFilterPreset>
        GetSearchFilterPresets()
    {
        string json =
            Preferences.Get(
                SearchFilterPresetsKey,
                string.Empty
            );

        if (string.IsNullOrWhiteSpace(
            json))
        {
            return [];
        }

        try
        {
            return JsonSerializer
                .Deserialize<List<SearchFilterPreset>>(
                    json
                )
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveSearchFilterPresets(
        IEnumerable<SearchFilterPreset> presets)
    {
        string json =
            JsonSerializer.Serialize(
                presets
                    .OrderBy(
                        preset => preset.Name,
                        StringComparer.CurrentCultureIgnoreCase
                    )
                    .ToList()
            );

        Preferences.Set(
            SearchFilterPresetsKey,
            json
        );
    }

    double IBestCrushSettingsProvider
        .TargetRoiPercent =>
            TargetRoiPercent;

    double IBestCrushSettingsProvider
        .CrushValueDiscountPercent =>
            CrushValueDiscountPercent;
}
