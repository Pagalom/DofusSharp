namespace BestCrush.Domain.Services;

public enum CrushYieldEstimationMode
{
    Conservative = 0,
    Average = 1
}

public interface IBestCrushSettingsProvider
{
    bool EquipmentCaptureEnabled { get; }

    bool RuneCaptureEnabled { get; }

    bool ResourceCaptureEnabled { get; }

    bool CoefficientCaptureEnabled { get; }

    CrushYieldEstimationMode CrushYieldEstimationMode { get; }

    double TargetRoiPercent { get; }

    double TargetRoi =>
        TargetRoiPercent / 100.0;
}