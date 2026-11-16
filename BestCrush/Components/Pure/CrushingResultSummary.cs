using BestCrush.Domain.Models;

namespace BestCrush.Components.Pure;

public readonly record struct CrushingResultSummary(double? RuneValue, double? Gain, double? Cost,
    Characteristic? RuneFocus = null, long? FocusRuneIconId = null)
{
    public double? RoiPercent => Cost is > 0 ? Gain / Cost * 100 : null;
}
