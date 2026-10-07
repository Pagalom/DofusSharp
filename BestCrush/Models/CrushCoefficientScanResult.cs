namespace BestCrush.Models;

public sealed record CrushCoefficientScanResult(
    long DofusDbId,
    string EquipmentName,
    double CoefficientPercent,
    int RowY
);
