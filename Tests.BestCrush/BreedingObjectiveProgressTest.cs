using BestCrush.Network.Protocol;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class BreedingObjectiveProgressTest
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
    private static BreedingMarketObjective Goal(int level = 1) => new(18, 33011, 1, "Dragodinde Dorée",
        new(level, 10_000, 0, Now, Fertile: true), new(18, BreedingSupplyKind.Capture, 100), new(35, 35, 18, 3, 18, Now));
    private static BreedingObjectiveStep Sell => new(BreedingObjectiveAction.Sell, "Dragodinde Dorée", Level: 1) { RideId = 18 };

    [Fact]
    public void ProgressAndSelectedGoalSurviveRestartAndRemainIndependent()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var journal = new BreedingObjectiveJournal { Selected = "second" };
            journal.Goals["first"] = new() { Steps = [new(BreedingObjectiveAction.Buy, "Dorée"), Sell], Done = [0] };
            journal.Goals["second"] = new() { Steps = [Sell] };
            new BreedingObjectiveProgressStore(path).Save("player-a", journal);
            var saved = new BreedingObjectiveProgressStore(path).Read("player-a");
            Assert.Equal("second", saved.Selected);
            Assert.Equal(50, BreedingObjectiveProgressStore.Percent(saved.Goals["first"]));
            Assert.Equal(0, BreedingObjectiveProgressStore.Percent(saved.Goals["second"]));
            Assert.Empty(new BreedingObjectiveProgressStore(path).Read("player-b").Goals);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ListingResetsOnlyItsCycleAndBlocksEveryLevelUntilEstimatedAbsorption()
    {
        var goal = Goal(); string key = BreedingObjectiveProgressStore.Key(goal);
        var journal = new BreedingObjectiveJournal();
        journal.Goals[key] = new() { Steps = [Sell] };
        journal.Goals["another"] = new() { Steps = [Sell], Done = [0] };
        BreedingObjectiveProgressStore.ConfirmStep(journal, goal, [Sell], 0, Now);
        Assert.Equal(0, BreedingObjectiveProgressStore.Percent(journal.Goals[key]));
        Assert.Equal(1, journal.Goals[key].Cycles);
        Assert.Equal(100, BreedingObjectiveProgressStore.Percent(journal.Goals["another"]));
        Assert.False(BreedingObjectiveProgressStore.Available(journal, Goal(100), Now.AddHours(1)));
        Assert.True(BreedingObjectiveProgressStore.Available(journal, Goal(100), Now.AddHours(5)));
        Assert.Throws<InvalidOperationException>(() => BreedingObjectiveProgressStore.ConfirmStep(journal, goal, [Sell], 0, Now));
        Assert.Single(journal.Pending);
    }

    [Fact]
    public void ExpiredDelayDoesNotOverrideQuotaOrStaleMarketHistory()
    {
        var journal = new BreedingObjectiveJournal(); var goal = Goal();
        journal.Pending.Add(new(goal.ItemId, Now.AddDays(-1), Now.AddHours(-1)));
        Assert.False(BreedingObjectiveProgressStore.Available(journal, goal with { Capacity = goal.Capacity! with { DailyRemaining = 0 } }, Now));
        Assert.False(BreedingObjectiveProgressStore.Available(journal, goal, Now.AddDays(3)));
        Assert.False(BreedingObjectiveProgressStore.Available(journal, goal with { Capacity = null }, Now));
    }

    [Fact]
    public void ExplicitOverrideAllowsOneCycleKeepsWarningAndExtendsTheWaitingQueue()
    {
        var goal = Goal() with { Capacity = Goal().Capacity! with { DailyRemaining = 0 } };
        string key = BreedingObjectiveProgressStore.Key(goal);
        var journal = new BreedingObjectiveJournal();
        journal.Goals[key] = new() { Steps = [Sell], OverrideCurrentCycle = true };
        journal.Pending.Add(new(goal.ItemId, Now, Now.AddHours(5)));
        Assert.True(BreedingObjectiveProgressStore.Available(journal, goal, Now));
        Assert.False(BreedingObjectiveProgressStore.Available(journal, goal, Now.AddDays(3)));
        BreedingObjectiveProgressStore.ConfirmStep(journal, goal, [Sell], 0, Now);
        Assert.False(journal.Goals[key].OverrideCurrentCycle);
        Assert.False(BreedingObjectiveProgressStore.Available(journal, goal, Now));
        Assert.Equal(2, journal.Pending.Count);
        Assert.True(BreedingObjectiveProgressStore.WaitingUntil(journal, goal.ItemId, Now) > Now.AddHours(9));
    }

    [Fact]
    public void ReconnectedMountCanCompleteTrainingButDisappearanceNeverProvesSale()
    {
        var progress = new BreedingObjectiveProgress { Steps = [new(BreedingObjectiveAction.Train, "Dorée", Guid.NewGuid(), Level: 100) { RideId = 18 }, Sell] };
        var mount = new PaddockMountReading(Guid.NewGuid(), 100, 100_000, 0, 20_000) { SpeciesId = 18 };
        Assert.True(BreedingObjectiveProgressStore.Observe(progress, [mount]));
        Assert.Equal(50, BreedingObjectiveProgressStore.Percent(progress));
        Assert.False(BreedingObjectiveProgressStore.Observe(progress, []));
        Assert.DoesNotContain(1, progress.Done);
    }

    [Fact]
    public void AmbiguousMountsAndUnknownFertilityDoNotCompletePreparation()
    {
        var progress = new BreedingObjectiveProgress { Steps = [new(BreedingObjectiveAction.Prepare, "Dorée", Level: 100) { RideId = 18 }] };
        var mount = new PaddockMountReading(Guid.NewGuid(), 100, 0, 0, 20_000) { SpeciesId = 18 };
        BreedingObjectiveProgressStore.Observe(progress, [mount]);
        Assert.Empty(progress.Done);
        progress.Steps[0] = progress.Steps[0] with { OwnedId = null };
        Assert.False(BreedingObjectiveProgressStore.Observe(progress, [mount with { Fertility = BreedingMountFertility.Ready }, mount with { Id = Guid.NewGuid(), Fertility = BreedingMountFertility.Ready }]));
    }

    [Fact]
    public void OneMountCannotSatisfyTwoParentAcquisitions()
    {
        var buy = new BreedingObjectiveStep(BreedingObjectiveAction.Buy, "Dorée", Sex: BreedingMountSex.Female) { RideId = 18 };
        var progress = new BreedingObjectiveProgress { Steps = [buy, buy, Sell] };
        var mount = new PaddockMountReading(Guid.NewGuid(), 1, 0, 0, 0) { SpeciesId = 18, Sex = BreedingMountSex.Female, Fertility = BreedingMountFertility.Fertile };
        Assert.True(BreedingObjectiveProgressStore.Observe(progress, [mount]));
        Assert.Equal([0], progress.Done);
        Assert.False(BreedingObjectiveProgressStore.Observe(progress, [mount]));
    }

    [Fact]
    public void DamagedJournalIsPreservedInsteadOfSilentlyReset()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{broken");
            Assert.Throws<InvalidDataException>(() => new BreedingObjectiveProgressStore(path).Save("a", new()));
            Assert.Equal("{broken", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
