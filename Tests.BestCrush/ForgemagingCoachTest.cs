using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace Tests.BestCrush;

public sealed class ForgemagingCoachTest
{
    private static ForgemagingLine Line(Characteristic stat, int current, int target, int max = 100) =>
        new() { Characteristic = stat, Current = current, Target = target, Minimum = max == 1 ? 1 : 0, Maximum = max };
    private static ForgemagingRune Rune(Characteristic stat, int gain = 1, double? price = 10) =>
        new((long)stat * 100 + gain, "Rune " + stat + gain, stat, gain, price);

    [Fact]
    public void WellAndActualRepairsChangeTheNextStageWithoutProjectingSuccesses()
    {
        var session = new ForgemagingSession { Level = 200, Sink = 0,
            Lines = [Line(Characteristic.Strength, 10, 20), Line(Characteristic.Ap, 0, 1, 1), Line(Characteristic.Wisdom, 20, 21, 20)] };
        ForgemagingRune[] runes = [Rune(Characteristic.Strength), Rune(Characteristic.Ap), Rune(Characteristic.Wisdom)];
        var first = ForgemagingCoach.Plan(session, runes);
        Assert.Equal(Characteristic.Ap, first.Next!.Advice.Rune.Characteristic);
        session.Sink = 99;
        var withWell = ForgemagingCoach.Plan(session, runes);
        Assert.Equal(Characteristic.Strength, withWell.Next!.Advice.Rune.Characteristic);
        Assert.Contains("puits", withWell.Next.Reason);
        Assert.Equal(10, session.Lines[0].Current); Assert.Empty(session.Attempts);
        session.Lines[0].Current = 20;
        Assert.Equal(Characteristic.Ap, ForgemagingCoach.Plan(session, runes).Next!.Advice.Rune.Characteristic);
        session.Lines[1].Current = 1;
        Assert.Equal(Characteristic.Wisdom, ForgemagingCoach.Plan(session, runes).Next!.Advice.Rune.Characteristic);
    }

    [Fact]
    public void VariantChoiceRespectsNaturalBoundaryAndExistingWellBeforeComparativeCost()
    {
        var session = new ForgemagingSession { Level = 200, Sink = 3, Lines = [Line(Characteristic.Strength, 90, 100)] };
        var normal = Rune(Characteristic.Strength, 1, 100); var pa = Rune(Characteristic.Strength, 3, 100);
        var ra = Rune(Characteristic.Strength, 10, 1);
        Assert.Equal(pa, ForgemagingCoach.Plan(session, [normal, pa, ra]).Next!.Advice.Rune);
        session.Sink = 0;
        Assert.Equal(ra, ForgemagingCoach.Plan(session, [normal, pa, ra]).Next!.Advice.Rune);
        session.Lines[0].Current = 99;
        Assert.Equal(normal, ForgemagingCoach.Plan(session, [normal, pa, ra]).Next!.Advice.Rune);
    }

    [Fact]
    public void UnknownPriceAndWellStayUnknownAndExistingOverProducesARelevantNotice()
    {
        var session = new ForgemagingSession { Level = 200, Sink = null,
            Lines = [Line(Characteristic.Strength, 30, 40), Line(Characteristic.Intelligence, 101, 100)] };
        var result = ForgemagingCoach.Plan(session, [Rune(Characteristic.Strength, 1, null), Rune(Characteristic.Strength, 3, null)]);
        Assert.NotNull(result.Next); Assert.Null(result.Next!.Advice.CostPerUsefulPoint); Assert.Null(result.Next.Advice.SinkCovers);
        Assert.Contains(result.Notices, n => n.Contains("Puits inconnu"));
        Assert.Contains(result.Notices, n => n.Contains("Prix incomplets"));
        Assert.Contains(result.Notices, n => n.Contains("peuvent le dégrader"));
        Assert.Contains("pas sur un coût supposé nul", result.Next.Reason);
    }

    [Fact]
    public void BudgetLimitsRecommendationsAndReachingTheGoalStopsTheCoach()
    {
        var session = new ForgemagingSession { Level = 200, Budget = 5, Lines = [Line(Characteristic.Strength, 0, 10)] };
        var expensive = Rune(Characteristic.Strength, 3, 10); var unknown = Rune(Characteristic.Strength, 1, null);
        Assert.Null(ForgemagingCoach.Plan(session, [expensive, unknown]).Next);
        session.Attempts.Add(new() { Rune = Rune(Characteristic.Strength, 1, 5), RuneCount = 1 });
        Assert.Contains("Budget atteint", ForgemagingCoach.Plan(session, [expensive]).StopReason);
        session.Lines[0].Current = 10;
        Assert.Contains("arrêter", ForgemagingCoach.Plan(session, [expensive]).StopReason);
        session.Lines[0].Current = null;
        Assert.Contains("Renseignez", ForgemagingCoach.Plan(session, [expensive]).StopReason);
    }

    [Fact]
    public void IncompletePastCostsNeverMakeTheRemainingBudgetLookExact()
    {
        var session = new ForgemagingSession { Level = 200, Budget = 10, Lines = [Line(Characteristic.Strength, 0, 10)] };
        session.Attempts.Add(new() { Rune = Rune(Characteristic.Strength, 1, null) });
        var plan = ForgemagingCoach.Plan(session, [Rune(Characteristic.Strength, 1, 5)]);
        Assert.Contains(plan.Notices, n => n.Contains("budget restant exact est inconnu"));
    }
}
