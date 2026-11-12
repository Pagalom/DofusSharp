using System.Text.Json.Serialization;

namespace BestCrush.Domain.Models;

public sealed record CrushRuneComparison(string Name, double? Estimated, int Obtained);
public sealed record CrushFmRuneUse(string Name, ForgemagingOutcome Outcome, int Count, double KnownCost, int Unpriced);

public sealed record CrushEquipmentComparison(
    double? EstimatedValue,
    double? ObtainedValue,
    Characteristic? EstimatedFocus,
    int FmRunes,
    double KnownFmCost,
    int UnpricedFmRunes,
    long? EstimatedCraftCost,
    IReadOnlyList<CrushRuneComparison> Runes)
{
    public long? RecordedAcquisitionCost { get; init; }
    public IReadOnlyList<CrushFmRuneUse> FmBreakdown { get; init; } = [];
    [JsonIgnore] public double? Difference => ObtainedValue - EstimatedValue;
    [JsonIgnore] public double? Margin => UnpricedFmRunes == 0
        ? ObtainedValue - KnownFmCost - (RecordedAcquisitionCost ?? EstimatedCraftCost) : null;
}
