using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace Tests.BestCrush;

public sealed class ForgemagingPlannerTest
{
    private static ForgemagingLine Line(Characteristic stat = Characteristic.Strength, int current = 0,
        int target = 50, int? min = 0, int? max = 100) => new() { Characteristic = stat, Current = current, Target = target, Minimum = min, Maximum = max };
    private static ForgemagingRune Rune(int gain = 1, double? price = 10, Characteristic stat = Characteristic.Strength) => new(gain, "Rune", stat, gain, price);

    [Fact]
    public void PublishedNaturalStepAndLowLevelRegimesHaveExpectedProbabilities()
    {
        var normal = ForgemagingPlanner.Evaluate(200, [Line(current: 70)], Rune(10));
        Assert.Equal("Palier 80 %", normal.Regime); Assert.Equal(25, normal.Critical); Assert.Equal(50, normal.Neutral); Assert.Equal(25, normal.Failure);
        var before = ForgemagingPlanner.Evaluate(200, [Line(current: 60)], Rune(10));
        Assert.Equal("Naturel", before.Regime); Assert.Equal(47.5, before.Critical); Assert.Equal(50, before.Neutral); Assert.Equal(2.5, before.Failure);
        var low = ForgemagingPlanner.Evaluate(169, [Line(current: 0)], Rune());
        Assert.Equal(58.5, low.Critical); Assert.Equal(41.5, low.Neutral); Assert.Equal(0, low.Failure);
        Assert.Equal(47.5, ForgemagingPlanner.Evaluate(170, [Line()], Rune()).Critical);
    }

    [Fact]
    public void QualityExcludesTargetAndNegativeLinesAndExoticPaIncreasesDifficulty()
    {
        var force = Line(current: 60);
        var full = Line(Characteristic.Intelligence, 100);
        var negative = Line(Characteristic.Chance, -10, -1, -10, -1);
        var odds = ForgemagingPlanner.Evaluate(200, [force, full, negative], Rune(10));
        Assert.Equal(17.5, odds.Critical);
        full.Current = 0;
        Assert.Equal(47.5, ForgemagingPlanner.Evaluate(200, [force, full, negative], Rune(10)).Critical);
        var exo = Line(Characteristic.Ap, 1, 1, null, null);
        Assert.Equal(17.5, ForgemagingPlanner.Evaluate(200, [force, full, negative, exo], Rune(10)).Critical);
    }

    [Theory]
    [InlineData(Characteristic.Ap)]
    [InlineData(Characteristic.Mp)]
    [InlineData(Characteristic.Range)]
    public void ExoticHeavyRunesUseOnePercentAndNoNeutralSuccess(Characteristic characteristic)
    {
        var result = ForgemagingPlanner.Evaluate(200, [Line(characteristic, 0, 1, null, null)], Rune(stat: characteristic));
        Assert.Equal(1, result.Critical); Assert.Equal(0, result.Neutral); Assert.Equal(99, result.Failure);
    }

    [Fact]
    public void LineCapsAndCombinedExcessRejectImpossibleGoalsWithoutInventingAChance()
    {
        var session = new ForgemagingSession { Lines = [Line(Characteristic.Ap, 0, 1, null, null), Line(Characteristic.Mp, 0, 1, null, null)] };
        Assert.Contains("101", ForgemagingPlanner.Validate(session));
        Assert.NotNull(ForgemagingPlanner.Evaluate(200, [Line(Characteristic.Ap, 1, 1, null, null)], Rune(stat: Characteristic.Mp)).Blocked);
        Assert.NotNull(ForgemagingPlanner.Evaluate(200, [Line(Characteristic.Ap, 1, 1, 1, 1)], Rune(stat: Characteristic.Ap)).Blocked);
        Assert.Null(ForgemagingPlanner.Evaluate(200, [Line(Characteristic.Vitality, 499, 505, 400, 500)], Rune(5, stat: Characteristic.Vitality)).Blocked);
    }

    [Fact]
    public void CostAdviceAccountsForUsefulGainProbabilitiesPricesAndDoesNotUseMissingAsZero()
    {
        var session = new ForgemagingSession { Level = 200, Lines = [Line(current: 10, target: 20)] };
        var basic = Rune(1, 100); var ra = Rune(10, 50); var unknown = Rune(3, null);
        var result = ForgemagingPlanner.Recommend(session, [basic, ra, unknown]);
        Assert.Equal(ra, result.First().Rune); Assert.Equal(unknown, result.Last().Rune);
        Assert.Null(result.Last().CostPerUsefulPoint);
        Assert.Equal(10, result.First().UsefulGain);
        session.Lines[0].Target = 11;
        Assert.Equal(1, ForgemagingPlanner.Recommend(session, [ra]).Single().UsefulGain);
        session.Lines[0].Current = null; Assert.Empty(ForgemagingPlanner.Recommend(session, [ra]));
    }

    [Fact]
    public void WellDelaysHeavyRepassAndUnknownWellNeverBecomesZero()
    {
        var session = new ForgemagingSession { Level = 200, Sink = 100, Lines = [Line(current: 10), Line(Characteristic.Ap, 0, 1, 1, 1)] };
        var result = ForgemagingPlanner.Recommend(session, [Rune(), Rune(stat: Characteristic.Ap)]);
        Assert.Equal(Characteristic.Strength, result[0].Rune.Characteristic);
        Assert.Equal(1, result[1].Phase);
        session.Sink = null;
        Assert.All(ForgemagingPlanner.Recommend(session, [Rune(), Rune(stat: Characteristic.Ap)]), a => Assert.Null(a.SinkCovers));
    }

    [Fact]
    public void ActualLossesReconstructWellAndUndoRestoresStateCostAndCurrentTargets()
    {
        var session = new ForgemagingSession { Level = 200, Sink = 0, Lines = [Line(current: 10), Line(Characteristic.Ap, 1, 1, 1, 1)] };
        var after = session.Lines.Select(l => l.Copy()).ToArray(); after[0].Current = 11; after[1].Current = 0;
        ForgemagingSessionActions.Record(session, Rune(price: 12), ForgemagingOutcome.Neutral, after, DateTime.UtcNow);
        Assert.Equal(99, session.Sink); Assert.Equal(12, session.KnownSpent); Assert.Equal(11, session.Lines[0].Current);
        session.Lines[0].Target = 80;
        ForgemagingSessionActions.UndoLast(session);
        Assert.Equal(0, session.Sink); Assert.Equal(0, session.KnownSpent); Assert.Equal(10, session.Lines[0].Current); Assert.Equal(80, session.Lines[0].Target);
        Assert.Empty(session.Attempts);
    }

    [Fact]
    public void FailureConsumesWellAndMalusCostsHalfWhileCriticalDoesNotConsumeIt()
    {
        var session = new ForgemagingSession { Level = 200, Sink = 10, Lines = [Line(current: -10, target: -1, min: -10, max: -1)] };
        var after = session.Lines.Select(l => l.Copy()).ToArray();
        ForgemagingSessionActions.Record(session, Rune(price: null), ForgemagingOutcome.Failure, after, DateTime.UtcNow);
        Assert.Equal(9.5, session.Sink); Assert.Equal(1, session.UnpricedAttempts);
        ForgemagingSessionActions.Record(session, Rune(), ForgemagingOutcome.Critical, [], DateTime.UtcNow);
        Assert.Equal(9.5, session.Sink); Assert.Equal(-9, session.Lines[0].Current);
        session.Sink = null;
        ForgemagingSessionActions.Record(session, Rune(), ForgemagingOutcome.Failure, session.Lines, DateTime.UtcNow);
        Assert.Null(session.Sink);
    }

    [Fact]
    public void InconsistentManualResultsAreRejectedBeforeChangingStateOrChargingARune()
    {
        var session = new ForgemagingSession { Level = 200, Lines = [Line(current: 10)] };
        Assert.Throws<ArgumentException>(() => ForgemagingSessionActions.Record(session, Rune(), ForgemagingOutcome.Neutral, [Line(current: 20)], DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => ForgemagingSessionActions.Record(session, Rune(), ForgemagingOutcome.Failure, [], DateTime.UtcNow));
        Assert.Equal(10, session.Lines.Single().Current); Assert.Empty(session.Attempts);
    }

    [Theory]
    [InlineData("Rune Vi", Characteristic.Vitality, 5)]
    [InlineData("Rune Pa Vi", Characteristic.Vitality, 15)]
    [InlineData("Rune Ra Vi", Characteristic.Vitality, 50)]
    [InlineData("Rune Pa Ine", Characteristic.Intelligence, 3)]
    [InlineData("Rune Ra Ini", Characteristic.Initiative, 100)]
    [InlineData("Rune Ga Pa", Characteristic.Ap, 1)]
    public void RuneGainsUseTheCharacteristicAndTheExistingVariant(string name, Characteristic characteristic, int gain) =>
        Assert.Equal(gain, ForgemagingDataService.RuneGain(name, characteristic));
}
