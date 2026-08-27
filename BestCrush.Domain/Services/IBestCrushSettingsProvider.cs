namespace BestCrush.Domain.Services;

public interface IBestCrushSettingsProvider
{
    bool EquipmentCaptureEnabled { get; }

    bool RuneCaptureEnabled { get; }

    bool ResourceCaptureEnabled { get; }

    bool CoefficientCaptureEnabled { get; }

    double TargetRoiPercent { get; }

    double TargetRoi =>
        TargetRoiPercent / 100.0;
}