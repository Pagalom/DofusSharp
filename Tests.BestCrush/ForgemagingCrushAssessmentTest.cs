using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using static Tests.BestCrush.Utils.EconomicTestData;

namespace Tests.BestCrush;

public sealed class ForgemagingCrushAssessmentTest
{
    private static ForgemagingSession Session() => new() { Level = 100, Sink = 50,
        Lines = [new() { Characteristic = Characteristic.Strength, Current = 10, Target = 20, Minimum = 0, Maximum = 100 }] };
    private static ForgemagingRune Force() => new(1, "Rune Fo", Characteristic.Strength, 1, 3);

    private static ForgemagingCrushContext Context(params (long Id, int Quantity, long Price)[] quotes) =>
        new(200, DateTime.UtcNow)
        {
            ResalePrices = quotes.ToDictionary(quote => (quote.Id, quote.Quantity), quote =>
                Price(quote.Id, quote.Quantity, quote.Price, type: MarketObjectType.Rune))
        };

    [Fact]
    public void AnOverWithLessThanTenExpectedRunesUsesTheSingleResaleQuote()
    {
        var session = Session();
        session.Level = 1;
        session.Lines[0].Current = 6;
        session.Lines[0].Target = 7;
        var context = Context((1, 1, 105), (1, 10, 930), (1, 1000, 79_998)) with
            { CoefficientPercent = 128.02008, ValueMultiplier = .95 };

        var result = ForgemagingCrushAssessment.Compare(session, [Force()], context);
        Assert.True(result.IsComplete);
        Assert.Equal(137.87, result.Current!.Value, 2);
        Assert.Equal(139.76, result.Target!.Value, 2);
        Assert.Equal(1.90, result.PreparationCeiling!.Value, 2);
    }

    [Fact]
    public void PreparationChoosesTheBestNetAttemptEvenWhenTheSavedTargetIsInvalid()
    {
        var session = Session();
        session.Lines[0].Target = 100_001;
        var pa = new ForgemagingRune(2, "Rune Pa Fo", Characteristic.Strength, 3, 4);
        var context = Context((1, 1, 11));

        var plan = CrushingPreparationPlanner.Plan(session, [Force(), pa], context);

        Assert.NotNull(plan.Next);
        Assert.Equal(pa, plan.Next.Rune);
        Assert.Equal(320, plan.Current!.Value);
        Assert.Equal(410, plan.Next.OnCriticalSuccess.Value);
        Assert.Equal(86, plan.Next.EstimatedNetGain, 2);
        Assert.True(plan.Next.CoveredBySink);
        Assert.Equal(100_001, session.Lines[0].Target);
        Assert.Equal(10, session.Lines[0].Current);
    }

    [Fact]
    public void PreparationCanStopForRiskBudgetOrMissingPrices()
    {
        var session = Session();
        var context = Context((1, 1, 11));
        session.Sink = 0;
        var risky = CrushingPreparationPlanner.Plan(session, [Force()], context);
        Assert.Null(risky.Next);
        Assert.Contains("Conservez le jet actuel", risky.StopReason);

        session.Sink = 50;
        session.Budget = 2;
        Assert.Null(CrushingPreparationPlanner.Plan(session, [Force()], context).Next);

        session.Budget = null;
        var incomplete = CrushingPreparationPlanner.Plan(session, [Force()], Context((1, 10, 80)));
        Assert.Null(incomplete.Next);
        Assert.Contains("données", incomplete.StopReason);
    }

    [Fact]
    public void CeilingIsTheDifferenceOfTerminalValuesNotAnEstimatedFmExpense()
    {
        var session = Session(); var context = Context((1, 1, 11));
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
        var result = ForgemagingCrushAssessment.Compare(session, [Force(), new(2, "Rune Ga Pa", Characteristic.Ap, 1, 1000)], Context((1, 1, 11), (2, 1, 1001)));
        Assert.Equal(3020, result.Current!.Value); Assert.Equal(1520, result.Target!.Value);
        Assert.Equal(-1500, result.PreparationCeiling);
    }

    [Fact]
    public void PuitsAndConsumedRuneVariantsDoNotBecomeCrushingOutput()
    {
        var session = Session(); session.Sink = 0;
        ForgemagingRune[] runes = [Force(), new(3, "Rune Ra Fo", Characteristic.Strength, 10, 999999)];
        var first = ForgemagingCrushAssessment.Compare(session, runes, Context((1, 1, 11)));
        session.Sink = 100;
        Assert.Equal(first, ForgemagingCrushAssessment.Compare(session, runes, Context((1, 1, 11))) with { Missing = first.Missing });
        Assert.Equal(300, first.PreparationCeiling);
    }

    [Fact]
    public void MissingCoefficientPriceOrRollCannotProduceAFakeCeiling()
    {
        var session = Session();
        Assert.Null(ForgemagingCrushAssessment.Compare(session, [Force()], null).PreparationCeiling);
        Assert.Null(ForgemagingCrushAssessment.Compare(session, [Force()], Context((1, 10, 80))).PreparationCeiling);
        session.Lines[0].Current = null;
        Assert.Null(ForgemagingCrushAssessment.Compare(session, [Force()], Context((1, 1, 11))).Current);
    }

    [Fact]
    public void FocusUsesAllPositiveLinesAndMissingPricesKeepTheGlobalComparisonIncomplete()
    {
        var session = Session();
        session.Lines.Add(new() { Characteristic = Characteristic.Wisdom, Current = 10, Target = 10, Minimum = 0, Maximum = 20 });
        var result = ForgemagingCrushAssessment.Compare(session,
            [Force(), new(2, "Rune Sa", Characteristic.Wisdom, 1, null)], Context((1, 1, 11)));
        Assert.False(result.IsComplete); Assert.Null(result.PreparationCeiling);
        Assert.Equal(Characteristic.Strength, result.Current!.Focus);
        Assert.Equal((16 + 46 / 2d) * 2 * 10, result.Current.Value);
        Assert.Contains(result.Missing, n => n.Contains("Rune Sa"));
    }
}
