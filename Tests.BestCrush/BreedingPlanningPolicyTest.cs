using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Microsoft.Maui.Storage;
using Moq;

namespace Tests.BestCrush;

public sealed class BreedingPlanningPolicyTest
{
    [Fact]
    public void DefaultIsEconomyAndSixtyFivePercentForEveryGenerationNotEveryColour()
    {
        var policy = new BreedingPlanningOptions();
        Assert.Equal(65, policy.MinimumGenerationChancePercent);
        Assert.Equal(BreedingPlanningMode.Economy, policy.Mode);
        Assert.False(policy.Accepts(116, 117));
        Assert.True(policy.Accepts(117, 117));
        Assert.Equal(65.1m, BreedingDecisions.TargetGenerationChance(117, 117).Base);
        var result = Calculate(policy);
        foreach (var route in result.Routes.Values.SelectMany(routes => routes).Where(route => route.Kind == BreedingSupplyKind.Cross))
            Assert.True(policy.Accepts(route.FirstLevel, route.SecondLevel));
        Assert.Equal(BreedingSupplyKind.Cross, result.Routes[4][0].Kind);
    }

    [Fact]
    public void EconomyStartsWithFreeCaptureAndCanPreferReproductionOrDirectPurchase()
    {
        var capture = Calculate(new());
        Assert.Equal(BreedingSupplyKind.Capture, capture.Routes[1][0].Kind);
        Assert.Equal(0, capture.Routes[1][0].Cost);
        Assert.Equal(BreedingSupplyKind.Cross, capture.Routes[3][0].Kind);
        var cheaperPurchase = Calculate(new(), 1);
        Assert.Equal(BreedingSupplyKind.Purchase, cheaperPurchase.Routes[3][0].Kind);
    }

    [Fact]
    public void SpeedPrioritizesDirectlyPurchasableTargetsAndThePlanUsesThatRoute()
    {
        var policy = new BreedingPlanningOptions(65, BreedingPlanningMode.Speed);
        var result = Calculate(policy);
        Assert.Equal(BreedingSupplyKind.Purchase, result.Routes[1][0].Kind);
        Assert.Equal(BreedingSupplyKind.Purchase, result.Routes[3][0].Kind);
        Assert.True(result.Routes[3][0].Cost > Calculate(new()).Routes[3][0].Cost);
        var goal = new BreedingMarketObjective(3, 103, 2, "Cible", new(1, 50_000, 0, DateTimeOffset.UtcNow), result.Routes[3][0], null);
        var steps = BreedingObjectivePlan.Build(goal, result.Routes[3], Rides, Names, [],
            new Dictionary<Guid, long?>(), Prices(), new Dictionary<Guid, BreedingResaleOption>(), 10,
            new Dictionary<string, string>(), policy);
        Assert.Equal(BreedingObjectiveAction.Buy, steps[0].Action);
        Assert.DoesNotContain(steps, step => step.Action is BreedingObjectiveAction.Cross or BreedingObjectiveAction.Capture);
        var high = new PaddockMountReading(Guid.NewGuid(), 200, BreedingMountExperience.RequiredAtLevel(200), 0, 20_000);
        Assert.Equal(200, policy.OwnedParentLevels(high, null, Prices())!.Value.Second);
    }

    [Fact]
    public void MissingExperiencePricesOrUnreachableThresholdNeverMakeTrainingFree()
    {
        Assert.False(Calculate(new(), prices: new()).Routes.ContainsKey(3) &&
            Calculate(new(), prices: new()).Routes[3].Any(route => route.Kind == BreedingSupplyKind.Cross));
        Assert.DoesNotContain(Calculate(new(95)).Routes[3], route => route.Kind == BreedingSupplyKind.Cross);
        Assert.Null(new BreedingPlanningOptions().OwnedParentLevels(null, null, new Dictionary<int, long>()));
    }

    [Fact]
    public void AnOwnedLevelTwoHundredParentOnlyRequiresLevelThirtyFourFromTheOther()
    {
        var parent = new PaddockMountReading(Guid.NewGuid(), 200, BreedingMountExperience.RequiredAtLevel(200), 0, 20_000);
        var selected = new BreedingPlanningOptions().OwnedParentLevels(parent, null, Prices());
        Assert.NotNull(selected);
        Assert.Equal(200, selected.Value.First);
        Assert.Equal(34, selected.Value.Second);
        Assert.True(new BreedingPlanningOptions().Accepts(selected.Value.First, selected.Value.Second));
        var other = parent with { Id = Guid.NewGuid() };
        Assert.Equal(0, new BreedingPlanningOptions().OwnedParentLevels(parent, other, new Dictionary<int, long>())!.Value.Cost);
    }

    [Fact]
    public void TheActualRouteUsesTheOwnedHighLevelParentAndLowersTheOtherTrainingGoal()
    {
        var policy = new BreedingPlanningOptions(); var result = Calculate(policy);
        var mount = new PaddockMountReading(Guid.NewGuid(), 200, BreedingMountExperience.RequiredAtLevel(200), 0, 20_000)
            { SpeciesId = 1, Sex = BreedingMountSex.Male, Fertility = BreedingMountFertility.Ready, Endurance = 20_000, Love = 20_000 };
        var goal = new BreedingMarketObjective(3, 103, 2, "Cible", new(1, 50_000, 0, DateTimeOffset.UtcNow), result.Routes[3][0], null);
        var steps = BreedingObjectivePlan.Build(goal, result.Routes[3], Rides, Names,
            [new(mount, "Amande", 1, null, null)], new Dictionary<Guid, long?> { [mount.Id] = 0 }, Prices(),
            new Dictionary<Guid, BreedingResaleOption>(), 10, new Dictionary<string, string>(), policy);
        Assert.Equal(200, Assert.Single(steps, step => step.OwnedId == mount.Id).Level);
        Assert.Equal(34, Assert.Single(steps, step => step.Action == BreedingObjectiveAction.Prepare && step.Name == "Dorée").Level);
        Assert.Equal(65.1m, Assert.Single(steps, step => step.Action == BreedingObjectiveAction.Cross).GenerationChancePercent);
    }

    [Fact]
    public void SeveralTargetColoursShareTheAcceptedGenerationChance()
    {
        var rides = new Dictionary<int, BreedingRide>(Rides) { [5] = new(5, 1, 2, 105, [[1, 2]]) };
        var result = BreedingMarketStrategy.Calculate(rides, Names, new Dictionary<long, long>(),
            new Dictionary<int, long> { [1] = 0 }, 10, Prices(),
            new Dictionary<long, IReadOnlyList<BreedingResaleOption>>(), new Dictionary<long, BreedingMarketDemand>(), CancellationToken.None);
        var cross = result.Routes[3][0];
        Assert.True(cross.SuccessChance * 2 >= .65m);
        Assert.True(cross.SuccessChance < .65m);
    }

    [Fact]
    public void HighRepeatedPreparationCostsCanJustifyMoreExperienceThanTheMinimum()
    {
        var policy = new BreedingPlanningOptions();
        var minimum = policy.OwnedParentLevels(null, null, Prices())!.Value;
        var expensiveParents = policy.OwnedParentLevels(null, null, Prices(), 100_000_000)!.Value;
        Assert.True(expensiveParents.First + expensiveParents.Second > minimum.First + minimum.Second);
    }

    [Fact]
    public void NewPolicyKeepsAcquisitionsButReopensInsufficientParentTraining()
    {
        var acquisition = new BreedingObjectiveStep(BreedingObjectiveAction.Capture, "Amande") { RideId = 1 };
        var training = new BreedingObjectiveStep(BreedingObjectiveAction.Prepare, "Amande", Level: 1) { RideId = 1 };
        var progress = new BreedingObjectiveProgress { Steps = [acquisition, training], Done = [0, 1], Cycles = 4, OverrideCurrentCycle = true };
        var policy = new BreedingPlanningOptions();
        policy.Rebase(progress, [acquisition, training with { Level = 117 }]);
        Assert.Equal([0], progress.Done);
        Assert.Equal(4, progress.Cycles);
        Assert.True(progress.OverrideCurrentCycle);
        Assert.Equal(policy, progress.Planning);
    }

    [Fact]
    public void BreedingSettingsPersistAndNotifyWithoutWritingTheRealUserPreferences()
    {
        var values = new Dictionary<string, int>();
        var preferences = new Mock<IPreferences>();
        preferences.Setup(p => p.Get(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Returns((string key, int fallback, string? _) => values.GetValueOrDefault(key, fallback));
        preferences.Setup(p => p.Set(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback((string key, int value, string? _) => values[key] = value);
        var settings = new BestCrushSettingsService(preferences.Object);
        int changes = 0; settings.BreedingSettingsChanged += () => changes++;
        Assert.Equal(new BreedingPlanningOptions(), settings.BreedingPlanning);
        settings.BreedingMinimumChancePercent = 70;
        settings.BreedingPlanningMode = BreedingPlanningMode.Speed;
        settings.BreedingMinimumChancePercent = 70;
        Assert.Equal(2, changes);
        var restarted = new BestCrushSettingsService(preferences.Object);
        Assert.Equal(new BreedingPlanningOptions(70, BreedingPlanningMode.Speed), restarted.BreedingPlanning);
        restarted.BreedingMinimumChancePercent = 999;
        Assert.Equal(100, restarted.BreedingMinimumChancePercent);
    }

    private static readonly Dictionary<int, BreedingRide> Rides = new()
    {
        [1] = new(1, 1, 1, 101, []), [2] = new(2, 1, 1, 102, []),
        [3] = new(3, 1, 2, 103, [[1, 2]]), [4] = new(4, 1, 3, 104, [[1, 3]])
    };
    private static readonly Dictionary<long, string> Names = new() { [101] = "Amande", [102] = "Dorée", [103] = "Cible", [104] = "Suivante" };
    private static Dictionary<int, long> Prices() => Enumerable.Range(1, 200).ToDictionary(BreedingMountExperience.RequiredAtLevel, level => (long)BreedingMountExperience.RequiredAtLevel(level));
    private static BreedingStrategyResult Calculate(BreedingPlanningOptions policy, long targetPrice = 10_000_000, Dictionary<int, long>? prices = null) =>
        BreedingMarketStrategy.Calculate(Rides, Names, new Dictionary<long, long> { [101] = 100, [102] = 100, [103] = targetPrice },
            new Dictionary<int, long> { [1] = 0 }, 10, prices ?? Prices(),
            new Dictionary<long, IReadOnlyList<BreedingResaleOption>>(), new Dictionary<long, BreedingMarketDemand>(), CancellationToken.None, policy);
}
