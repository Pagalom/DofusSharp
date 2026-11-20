using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace Tests.BestCrush;

public sealed class ForgemagingGoalRemovalTest
{
    private static readonly Characteristic Stat = Characteristic.Chance;
    private static ForgemagingLine Line(int? value) => new() { Characteristic = Stat, Current = value, Target = 1 };
    private static ForgemagingSession Session() => new() { Lines = [Line(0)] };
    private static ForgemagingAttempt Attempt(int? before, int? after, Characteristic rune = Characteristic.Strength) =>
        new() { Rune = new(1, "Rune", rune, 1, 10), Before = [Line(before)], After = [Line(after)] };

    [Fact]
    public void UnusedGoalCanBeRemovedWithoutChangingOtherLinesOrHistory()
    {
        var session = Session();
        var native = new ForgemagingLine { Characteristic = Characteristic.Strength, Minimum = 1, Maximum = 10, Current = 5, Target = 10 };
        session.Lines.Add(native);
        // Zero-valued goal placeholders appear in all snapshots, including unrelated attempts.
        var history = Attempt(0, 0);
        session.Attempts.Add(history);
        Assert.True(ForgemagingSessionActions.CanRemoveGoalLine(session, Stat));
        Assert.True(ForgemagingSessionActions.RemoveGoalLine(session, Stat));
        Assert.Same(native, Assert.Single(session.Lines));
        Assert.Same(history, Assert.Single(session.Attempts));
        Assert.Single(history.Before); Assert.Single(history.After);
        Assert.False(ForgemagingSessionActions.RemoveGoalLine(session, Stat));
    }

    [Fact]
    public void GainThenLossKeepsTheLineEvenWhenItsCurrentValueIsZero()
    {
        var session = Session();
        session.Attempts.Add(Attempt(0, 1, Stat));
        session.Attempts.Add(Attempt(1, 0));
        Assert.False(ForgemagingSessionActions.RemoveGoalLine(session, Stat));
        Assert.Single(session.Lines); Assert.Equal(2, session.Attempts.Count);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(null, 0)]
    public void PreviouslyObservedOrUnknownValuesPreventRemoval(int? before, int? after)
    {
        var session = Session(); session.Attempts.Add(Attempt(before, after));
        Assert.False(ForgemagingSessionActions.RemoveGoalLine(session, Stat));
    }

    [Fact]
    public void FailedRuneOnTheSameStatStillCountsAsHistory()
    {
        var session = Session(); session.Attempts.Add(Attempt(0, 0, Stat));
        Assert.False(ForgemagingSessionActions.RemoveGoalLine(session, Stat));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(null, false)]
    public void NativeOrNonzeroOrUnknownCurrentLinesCannotBeRemoved(int? current, bool native)
    {
        var session = Session(); session.Lines[0].Current = current;
        if (native) { session.Lines[0].Minimum = 0; session.Lines[0].Maximum = 10; }
        Assert.False(ForgemagingSessionActions.RemoveGoalLine(session, Stat));
    }
}
