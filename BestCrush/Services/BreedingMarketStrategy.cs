using BestCrush.Domain.Services;

namespace BestCrush.Services;

public enum BreedingSupplyKind { Capture, Purchase, Cross }

public sealed record BreedingProduction(int RideId, BreedingSupplyKind Kind, decimal Cost)
{
    public BreedingProduction? First { get; init; }
    public BreedingProduction? Second { get; init; }
    public int FirstLevel { get; init; } = 1;
    public int SecondLevel { get; init; } = 1;
    public decimal SuccessChance { get; init; } = 1;
    internal decimal CostForParent => Kind == BreedingSupplyKind.Cross ? 2 * Cost : Cost;
}

public sealed record BreedingMarketObjective(int RideId, long ItemId, int Generation, string Name, BreedingResaleOption Sale,
    BreedingProduction Production, BreedingMarketCapacity? Capacity)
{
    internal decimal Margin => Sale.Net - Production.Cost;
    internal decimal? WeeklyPotential => Margin * Capacity?.WeeklyLimit;
    internal bool CanSell => Margin > 0 && Capacity is { Remaining: > 0 };
}

public sealed record BreedingGenerationReview(int Generation, int Species, int WithoutPrice, int WithoutCost, int WithoutVolume,
    BreedingMarketObjective? Best, BreedingMarketObjective? BestPriced);

public sealed record BreedingMarketStrategyReport(IReadOnlyList<BreedingGenerationReview> Generations,
    IReadOnlyList<BreedingMarketObjective> Objectives, BreedingMarketObjective? Recommended);

internal sealed record BreedingStrategyResult(BreedingMarketStrategyReport Report,
    IReadOnlyDictionary<int, IReadOnlyList<BreedingProduction>> Routes);

internal static class BreedingMarketStrategy
{
    internal static BreedingStrategyResult Calculate(IReadOnlyDictionary<int, BreedingRide> rides,
        IReadOnlyDictionary<long, string> names, IReadOnlyDictionary<long, long> purchases,
        IReadOnlyDictionary<int, long> captureCosts, long? preparation,
        IReadOnlyDictionary<int, long> experienceCosts,
        IReadOnlyDictionary<long, IReadOnlyList<BreedingResaleOption>> sales, IReadOnlyDictionary<long, BreedingMarketDemand> demand,
        CancellationToken token)
    {
        var routes = new Dictionary<int, IReadOnlyList<BreedingProduction>>();
        var cheapest = new Dictionary<int, BreedingProduction>();
        var cheapestParents = new Dictionary<int, BreedingProduction>();
        var options = new Dictionary<int, BreedingMarketObjective[]>();
        var training = ParentTraining(experienceCosts);
        var outcomeCounts = rides.Values.SelectMany(ride => ride.Parents.Select(parent => (Pair: (parent[0], parent[1]), ride.Generation)))
            .GroupBy(row => row.Pair).ToDictionary(rows => rows.Key, rows =>
            {
                int generation = rows.Max(row => row.Generation);
                return (Generation: generation, Count: rows.Count(row => row.Generation == generation));
            });

        foreach (var ride in rides.Values.OrderBy(ride => ride.Generation).ThenBy(ride => ride.Id))
        {
            token.ThrowIfCancellationRequested();
            var sources = new List<BreedingProduction>();
            if (ride.Generation == 1 && captureCosts.TryGetValue(ride.Family, out long capture))
                sources.Add(new(ride.Id, BreedingSupplyKind.Capture, capture));
            if (purchases.TryGetValue(ride.ItemId, out long price))
                sources.Add(new(ride.Id, BreedingSupplyKind.Purchase, price));

            if (preparation is { } prep)
            {
                foreach (var pair in ride.Parents)
                {
                    if (!cheapestParents.TryGetValue(pair[0], out var first) || !cheapestParents.TryGetValue(pair[1], out var second))
                        continue;
                    var (generation, count) = outcomeCounts[(pair[0], pair[1])];
                    // The forecast targets the highest direct generation of each pair.
                    if (ride.Generation != generation) continue;
                    var source = training.Select(levels =>
                    {
                        decimal chance = BreedingDecisions.TargetGenerationChance(levels.First, levels.Second).Base / 100m / count;
                        // A bred parent must also have the required sex. Model one in two.
                        decimal cost = (first.CostForParent + second.CostForParent + 2m * prep + levels.Cost) / chance;
                        return new BreedingProduction(ride.Id, BreedingSupplyKind.Cross, cost)
                        {
                            First = first, Second = second, FirstLevel = levels.First, SecondLevel = levels.Second, SuccessChance = chance
                        };
                    }).MinBy(route => route.Cost)!;
                    sources.Add(source);
                }
            }
            if (sources.Count == 0) continue;
            var ordered = sources.OrderBy(source => source.Cost).ToArray();
            routes.Add(ride.Id, ordered);
            cheapest.Add(ride.Id, ordered[0]);
            cheapestParents.Add(ride.Id, ordered.MinBy(source => source.CostForParent)!);
            if (!sales.TryGetValue(ride.ItemId, out var offers) || !names.TryGetValue(ride.ItemId, out var name)) continue;
            var liquidity = demand.GetValueOrDefault(ride.ItemId);
            options.Add(ride.Id, offers.Select(sale => new BreedingMarketObjective(ride.Id, ride.ItemId,
                ride.Generation, name, sale, ordered[0], liquidity?.AtPrice(sale.SalePrice))).ToArray());
        }

        // Levels and fertility share the species' observed demand. Keep one target per species.
        var sellable = Order(options.Values.SelectMany(rows => rows)
            .Where(row => row.Margin > 0 && row.Capacity is { WeeklyLimit: > 0 }))
            .DistinctBy(row => row.ItemId).ToArray();
        var recommended = Order(options.Values.SelectMany(rows => rows).Where(row => row.CanSell)).FirstOrDefault();
        var generations = rides.Values.GroupBy(ride => ride.Generation).OrderBy(group => group.Key).Select(group =>
        {
            var rows = group.Where(ride => options.ContainsKey(ride.Id)).SelectMany(ride => options[ride.Id]).ToArray();
            return new BreedingGenerationReview(group.Key, group.Count(),
                group.Count(ride => !sales.ContainsKey(ride.ItemId)),
                group.Count(ride => sales.ContainsKey(ride.ItemId) && !cheapest.ContainsKey(ride.Id)),
                group.Count(ride => sales.ContainsKey(ride.ItemId) && !demand.ContainsKey(ride.ItemId)),
                sellable.FirstOrDefault(row => row.Generation == group.Key), rows.MaxBy(row => row.Margin));
        }).ToArray();
        return new(new(generations, sellable, recommended), routes);
    }

    private static IOrderedEnumerable<BreedingMarketObjective> Order(IEnumerable<BreedingMarketObjective> objectives) =>
        objectives.OrderByDescending(row => row.WeeklyPotential).ThenByDescending(row => row.Margin)
            .ThenBy(row => row.Production.Cost).ThenBy(row => row.Sale.Level);

    private static (int First, int Second, decimal Cost)[] ParentTraining(IReadOnlyDictionary<int, long> costs)
    {
        var levels = Enumerable.Range(1, 200).Where(level => level == 1 || costs.ContainsKey(BreedingMountExperience.RequiredAtLevel(level)))
            .Select(level => (Level: level, Cost: costs.GetValueOrDefault(BreedingMountExperience.RequiredAtLevel(level)))).ToArray();
        // The probability depends on the sum of levels. Keep its least expensive combination.
        return levels.SelectMany(a => levels.Select(b => (First: a.Level, Second: b.Level, Cost: (decimal)a.Cost + b.Cost)))
            .GroupBy(pair => pair.First + pair.Second).Select(group => group.MinBy(pair => pair.Cost)).ToArray();
    }
}
