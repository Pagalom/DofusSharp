using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class BreedingLiquidityEvidenceTest
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 11, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(33215)]
    [InlineData(881)]
    public void TheSameDemandFormulaGivesNoSaleObjectiveButStillAllowsUseAsAParent(long item)
    {
        const long other = 600_001, child = 600_002;
        var rides = new Dictionary<int, BreedingRide>
        {
            [1] = new(1, 1, 1, item, []), [2] = new(2, 1, 1, other, []), [3] = new(3, 1, 2, child, [[1, 2]])
        };
        var names = new Dictionary<long, string> { [item] = "Parent", [other] = "Autre", [child] = "Descendance" };
        var availability = new MarketHistoryAvailability(Now, new HashSet<long> { child });
        var previous = History(item, Now.AddMinutes(-1), 23);
        var demand = new Dictionary<long, BreedingMarketDemand>
        {
            [item] = BreedingMarketDemand.FromEvidence(item, previous, availability, 0, 0, Now),
            [other] = BreedingMarketDemand.FromEvidence(other, null, availability, 0, 0, Now),
            [child] = BreedingMarketDemand.FromEvidence(child, History(child, Now, 35), availability, 0, 0, Now)
        };
        var sales = new Dictionary<long, IReadOnlyList<BreedingResaleOption>>
        {
            [item] = [new(1, 400_000_000, 0, Now)], [child] = [new(1, 10_000_000, 0, Now)]
        };
        var prices = Enumerable.Range(1, 200).ToDictionary(BreedingMountExperience.RequiredAtLevel,
            level => (long)BreedingMountExperience.RequiredAtLevel(level));
        var result = BreedingMarketStrategy.Calculate(rides, names, new Dictionary<long, long>(),
            new Dictionary<int, long> { [1] = 0 }, 10, prices, sales, demand, CancellationToken.None);
        Assert.Equal(3, result.Report.Recommended!.RideId);
        Assert.DoesNotContain(result.Report.Objectives, objective => objective.ItemId == item);
        Assert.Equal(0, result.Report.Generations[0].BestPriced!.WeeklyPotential);
        Assert.Equal(1, result.Routes[3][0].First!.RideId);
        Assert.Equal(BreedingDemandEvidence.HistoryUnavailable, demand[item].AtPrice(1).Evidence);
        Assert.Equal(0, demand[item].AtPrice(1).Remaining);
        Assert.Empty(demand[item].Allocate([(Guid.NewGuid(), sales[item])]));
        Assert.Equal(23, previous.Period(7).UnitsSold); // No historical observation was rewritten to zero.
        var goal = new BreedingMarketObjective(1, item, 1, "Parent", sales[item][0], result.Routes[1][0], demand[item].AtPrice(1));
        var journal = new BreedingObjectiveJournal();
        journal.Goals[BreedingObjectiveProgressStore.Key(goal)] = new() { OverrideCurrentCycle = true };
        Assert.False(BreedingObjectiveProgressStore.Available(journal, goal, Now));
    }

    [Fact]
    public void UnknownUnavailableAndObservedZeroAllGiveZeroQuotaButKeepDifferentEvidence()
    {
        var unavailable = new MarketHistoryAvailability(Now, new HashSet<long> { 2 });
        Assert.Equal(BreedingDemandEvidence.Missing, BreedingMarketDemand.FromEvidence(1, null, null, 0, 0, Now).AtPrice(1).Evidence);
        Assert.Equal(BreedingDemandEvidence.HistoryUnavailable, BreedingMarketDemand.FromEvidence(1, null, unavailable, 0, 0, Now).AtPrice(1).Evidence);
        var zero = BreedingMarketDemand.FromEvidence(1, History(1, Now, 0), unavailable, 0, 0, Now).AtPrice(1);
        Assert.Equal(BreedingDemandEvidence.Observed, zero.Evidence);
        Assert.Equal(0, zero.WeeklyLimit);
        Assert.Equal(0, zero.Remaining);
        var newHistory = BreedingMarketDemand.FromEvidence(1, History(1, Now.AddSeconds(1), 35), unavailable, 0, 0, Now.AddSeconds(2));
        Assert.Equal(BreedingDemandEvidence.Observed, newHistory.AtPrice(1).Evidence);
        Assert.True(newHistory.AtPrice(1).Remaining > 0);
    }

    [Fact]
    public void MissingMeanPriceRemovesOnlyTheImpossibleHistoryTaskNotUsefulOfferPriceChecks()
    {
        BreedingRide[] rides = [new(1, 3, 10, 33215, []), new(2, 3, 10, 33207, [])];
        var names = new Dictionary<long, string> { [33215] = "Prune et Turquoise", [33207] = "Prune et Amande" };
        BreedingLevelPrice[] quotes = [new(33215, 1, 10_000, true, Now), new(33207, 1, 10_000, true, Now)];
        var availability = new MarketHistoryAvailability(Now, new HashSet<long> { 33207 });
        var tasks = BreedingTradeService.CompleteMarketChecks(rides, names, quotes.ToLookup(q => q.ItemId),
            quotes.ToLookup(q => q.ItemId), new Dictionary<long, DateTimeOffset>(), Now, availability).ToArray();
        Assert.Equal(33207, Assert.Single(tasks).ItemId);
        Assert.Contains("Cours du marché", tasks[0].Reason);
        var missingOffers = BreedingTradeService.CompleteMarketChecks(rides, names, quotes.ToLookup(q => q.ItemId),
            Array.Empty<BreedingLevelPrice>().ToLookup(q => q.ItemId), new Dictionary<long, DateTimeOffset>(), Now, availability).ToArray();
        Assert.Equal(2, missingOffers.Length);
        Assert.All(missingOffers, task => Assert.DoesNotContain("Cours du marché", task.Reason));
    }

    private static GlobalSalesHistory History(long id, DateTimeOffset observed, long units) =>
        new(new("Kourial", MarketObjectType.Resource, id), observed, [], [new(400_000_000, units, observed.AddHours(-1))]);
}
