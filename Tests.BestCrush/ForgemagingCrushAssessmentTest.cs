using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace Tests.BestCrush;

public sealed class ForgemagingCrushAssessmentTest
{
    private static ForgemagingSession Session() => new() { Level = 100, Sink = 50,
        Lines = [new() { Characteristic = Characteristic.Strength, Current = 10, Target = 20, Minimum = 0, Maximum = 100 }] };
    private static ForgemagingRune Force(double? price = 10) => new(1, "Rune Fo", Characteristic.Strength, 1, price);

    [Fact]
    public void CeilingIsTheDifferenceOfTerminalValuesNotAnEstimatedFmExpense()
    {
        var session = Session(); var context = new ForgemagingCrushContext(200, DateTime.UtcNow);
        var result = ForgemagingCrushAssessment.Compare(session, [Force()], context);
        Assert.True(result.IsComplete); Assert.Equal(320, result.Current!.Value); Assert.Equal(620, result.Target!.Value);
        Assert.Equal(300, result.PreparationCeiling); Assert.Equal(10, session.Lines[0].Current);
        Assert.Equal(50, session.Sink); Assert.Empty(session.Attempts);
        var discounted = ForgemagingCrushAssessment.Compare(session, [Force()], context with { ValueMultiplier = .8 });
        Assert.Equal(240, discounted.PreparationCeiling);
    }

    [Fact]
    public void SacrificingHeavyLineDeductsItsExistingCrushValue()
    {
        var session = Session(); session.Lines[0].Current = 0; session.Lines[0].Target = 50;
        session.Lines.Add(new() { Characteristic = Characteristic.Ap, Current = 1, Target = 0, Minimum = 1, Maximum = 1 });
        var result = ForgemagingCrushAssessment.Compare(session, [Force(), new(2, "Rune Ga Pa", Characteristic.Ap, 1, 1000)], new(200, null));
        Assert.Equal(3020, result.Current!.Value); Assert.Equal(1520, result.Target!.Value);
        Assert.Equal(-1500, result.PreparationCeiling);
    }

    [Fact]
    public void PuitsAndConsumedRuneVariantsDoNotBecomeCrushingOutput()
    {
        var session = Session(); session.Sink = 0;
        ForgemagingRune[] runes = [Force(), new(3, "Rune Ra Fo", Characteristic.Strength, 10, 999999)];
        var first = ForgemagingCrushAssessment.Compare(session, runes, new(200, null));
        session.Sink = 100;
        Assert.Equal(first, ForgemagingCrushAssessment.Compare(session, runes, new(200, null)) with { Missing = first.Missing });
        Assert.Equal(300, first.PreparationCeiling);
    }

    [Fact]
    public void MissingCoefficientPriceOrRollCannotProduceAFakeCeiling()
    {
        var session = Session();
        Assert.Null(ForgemagingCrushAssessment.Compare(session, [Force()], null).PreparationCeiling);
        Assert.Null(ForgemagingCrushAssessment.Compare(session, [Force(null)], new(200, null)).PreparationCeiling);
        session.Lines[0].Current = null;
        Assert.Null(ForgemagingCrushAssessment.Compare(session, [Force()], new(200, null)).Current);
    }

    [Fact]
    public void FocusUsesAllPositiveLinesAndMissingPricesKeepTheGlobalComparisonIncomplete()
    {
        var session = Session();
        session.Lines.Add(new() { Characteristic = Characteristic.Wisdom, Current = 10, Target = 10, Minimum = 0, Maximum = 20 });
        var result = ForgemagingCrushAssessment.Compare(session,
            [Force(), new(2, "Rune Sa", Characteristic.Wisdom, 1, null)], new(200, null));
        Assert.False(result.IsComplete); Assert.Null(result.PreparationCeiling);
        Assert.Equal(Characteristic.Strength, result.Current!.Focus);
        Assert.Equal((16 + 46 / 2d) * 2 * 10, result.Current.Value);
        Assert.Contains(result.Missing, n => n.Contains("Rune Sa"));
    }
}
