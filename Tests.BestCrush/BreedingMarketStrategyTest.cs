using BestCrush.Domain.Models;
using BestCrush.Network.Protocol;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class BreedingMarketStrategyTest
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PricesRoutesThroughGenerationTenWithoutOwnedMountsAndRanksWeeklyProfit()
    {
        var rides = new Dictionary<int, BreedingRide> { [1] = new(1, 1, 1, 101, []), [2] = new(2, 1, 1, 102, []) };
        for (int generation = 2; generation <= 10; generation++)
            rides[generation + 1] = new(generation + 1, 1, generation, generation + 101,
                generation == 2 ? [[1, 2]] : [[2, generation]]);

        var names = rides.Values.ToDictionary(ride => ride.ItemId, ride => $"Monture {ride.Id}");
        var withoutSales = Calculate(rides, names, new(), new());
        var highest = withoutSales.Routes[11][0];
        Assert.Equal(10, rides[highest.RideId].Generation);
        Assert.True(highest.Cost > withoutSales.Routes[3][0].Cost);
        Assert.Equal(10, withoutSales.Report.Generations.Count);
        Assert.Null(withoutSales.Report.Recommended);

        long priceG10 = (long)decimal.Ceiling(highest.Cost) + 20_000;
        var sales = new Dictionary<long, IReadOnlyList<BreedingResaleOption>>
        {
            [103] = [new(1, 5_000, 0, Now)],
            [111] = [new(1, priceG10, 0, Now)]
        };
        var market = new Dictionary<long, BreedingMarketDemand>
        {
            [103] = Demand(103, [new(5_000, 56, Now.AddDays(-1))]),
            [111] = Demand(111, [new(priceG10, 1, Now.AddDays(-1))])
        };
        var result = Calculate(rides, names, sales, market).Report;
        Assert.Equal(2, result.Recommended!.Generation);
        Assert.Equal(2, result.Objectives.Count);
        Assert.True(result.Objectives[1].Margin > result.Recommended.Margin);

        market[103] = market[103] with { ListedToday = 4, ListedThisWeek = 4 };
        Assert.Equal(10, Calculate(rides, names, sales, market).Report.Recommended!.Generation);
    }

    [Fact]
    public void MarketChecklistIncludesEverySpeciesMissingPricesOrTurnover()
    {
        var rides = new BreedingRide[]
        {
            new(1, 1, 10, 101, []), new(2, 1, 10, 102, []), new(3, 1, 5, 103, []), new(4, 1, 5, 104, [])
        };
        var names = rides.ToDictionary(ride => ride.ItemId, ride => $"Monture {ride.Id}");
        var old = new BreedingLevelPrice(101, 1, 10_000, true, Now.AddDays(-7));
        var fresh = new BreedingLevelPrice(103, 1, 10_000, true, Now);
        var measured = fresh with { ItemId = 104 };
        var checks = BreedingTradeService.CompleteMarketChecks(rides, names,
            new[] { old, fresh, measured }.ToLookup(price => price.ItemId),
            new[] { fresh, measured }.ToLookup(price => price.ItemId),
            new Dictionary<long, DateTimeOffset> { [103] = Now.AddDays(-3), [104] = Now }, Now).ToArray();
        Assert.Equal([101L, 102L, 103L], checks.Select(check => check.ItemId));
        Assert.Equal(old.ObservedAtUtc, checks[0].LastObserved);
        Assert.Contains("Cours du marché", checks[2].Reason);
    }

    [Fact]
    public void UsesPriceMatchedTurnoverAndSharesTheCapAcrossOwnedMontures()
    {
        var demand = Demand(101, [new(10_000, 21, Now.AddDays(-1)), new(8_000, 31, Now.AddDays(-2))])
            with { ListedToday = 1, ListedThisWeek = 10 };
        var capacity = demand.AtPrice(10_000);
        Assert.Equal(21, capacity.SalesAtPrice);
        Assert.Equal(1, capacity.Remaining);
        Assert.Equal(0, demand.AtPrice(11_000).Remaining);
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        var selected = demand.Allocate([(first, [new(1, 10_000, 0, Now)]), (second, [new(200, 10_000, 100, Now)])]);
        Assert.Equal(first, Assert.Single(selected).Key);
    }

    [Fact]
    public void AdaptsTheMarketObjectiveToAnOwnedParentAfterRanking()
    {
        var rides = new Dictionary<int, BreedingRide>
        {
            [1] = new(1, 1, 1, 101, []), [2] = new(2, 1, 1, 102, []), [3] = new(3, 1, 2, 103, [[1, 2]])
        };
        var names = new Dictionary<long, string> { [101] = "Amande", [102] = "Dorée", [103] = "Amande et Dorée" };
        var market = new Dictionary<long, BreedingMarketDemand>
        {
            [103] = Demand(103, [new(5_000, 7, Now.AddDays(-1))])
        };
        var result = Calculate(rides, names, new() { [103] = [new(1, 5_000, 0, Now)] }, market);
        var mount = new PaddockMountReading(Guid.NewGuid(), 1, 0, 0, 20_000)
        {
            SpeciesId = 1, Sex = BreedingMountSex.Male, Fertility = BreedingMountFertility.Ready
        };
        var plan = BreedingObjectivePlan.Build(result.Report.Recommended!, result.Routes[3], rides, names,
            [new(mount, "Amande", 1, null, null)], new Dictionary<Guid, long?> { [mount.Id] = 0 },
            new Dictionary<int, long>(), new Dictionary<Guid, BreedingResaleOption>(), 10,
            new Dictionary<string, string> { ["Dorée"] = "Territoire des Dragodindes sauvages" });
        var reuse = Assert.Single(plan, step => step.OwnedId == mount.Id);
        Assert.True(reuse.Ready);
        var capture = Assert.Single(plan, step => step.Action == BreedingObjectiveAction.Capture);
        Assert.Equal("Dorée", capture.Name);
        Assert.Equal(BreedingMountSex.Female, capture.Sex);
        Assert.Equal(BreedingObjectiveAction.Sell, plan[^1].Action);
    }

    private static BreedingStrategyResult Calculate(Dictionary<int, BreedingRide> rides, Dictionary<long, string> names,
        Dictionary<long, IReadOnlyList<BreedingResaleOption>> sales, Dictionary<long, BreedingMarketDemand> market) =>
        BreedingMarketStrategy.Calculate(rides, names, new Dictionary<long, long>(), new Dictionary<int, long> { [1] = 20 },
            10, new Dictionary<int, long>(), sales, market, CancellationToken.None);

    private static BreedingMarketDemand Demand(long id, GlobalSalePoint[] points) =>
        new(new(new("Kourial", MarketObjectType.Resource, id), Now, [], points), 0, 0);
}
