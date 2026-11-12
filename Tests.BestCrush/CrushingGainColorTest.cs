using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class CrushingGainColorTest
{
    [Theory]
    [InlineData(0, 100, "#FFFFFF")]
    [InlineData(25, 100, "#E4F2E9")]
    [InlineData(50, 100, "#C8E5D3")]
    [InlineData(100, 100, "#91CBA6")]
    [InlineData(250, 100, "#91CBA6")]
    [InlineData(-25, 100, "#F6C9CD")]
    [InlineData(-50, 100, "#EC939B")]
    [InlineData(-100, 100, "#EC939B")]
    [InlineData(100, 0, "#91CBA6")]
    public void ColorUsesRelativeGainAndSaturatesAtAsymmetricThresholds(double gain, double cost, string expected)
        => Assert.Equal(expected, CrushingGainColor.For(gain, cost));

    [Fact]
    public void UnknownInvalidAndUnavailableValuesStayNeutral()
    {
        Assert.Equal("#FFFFFF", CrushingGainColor.For(100, null));
        Assert.Equal("#FFFFFF", CrushingGainColor.For(100, -1));
        Assert.Equal("#FFFFFF", CrushingGainColor.For(double.NaN, 100));
        Assert.Equal("#FFFFFF", CrushingGainColor.For(double.MinValue, 100));
        Assert.Equal("#FFFFFF", CrushingGainColor.For(100, double.PositiveInfinity));
    }
}
