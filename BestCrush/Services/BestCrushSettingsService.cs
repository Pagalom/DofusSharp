using BestCrush.Domain.Services;
using Microsoft.Maui.Storage;

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

    double IBestCrushSettingsProvider
        .TargetRoiPercent =>
            TargetRoiPercent;
}