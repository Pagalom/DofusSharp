namespace BestCrush.Services;

/// <summary>Summary gain color: white at break-even, full pastel at +100% / -50% ROI.</summary>
public static class CrushingGainColor
{
    public static string For(double gain, double? cost)
    {
        if (!double.IsFinite(gain) || gain == double.MinValue || gain == 0 ||
            cost is not { } investment || !double.IsFinite(investment) || investment < 0)
            return "#FFFFFF";

        // A known positive cash gain without purchases uses the green endpoint, without displaying a percentage ROI.
        double intensity = investment == 0 ? 1 : Math.Clamp(gain > 0 ? gain / investment : -gain / investment / .5, 0, 1);
        var target = gain > 0 ? (R: 145, G: 203, B: 166) : (R: 236, G: 147, B: 155);
        int Blend(int channel) => (int)Math.Round(255 + (channel - 255) * intensity, MidpointRounding.AwayFromZero);
        return $"#{Blend(target.R):X2}{Blend(target.G):X2}{Blend(target.B):X2}";
    }
}
