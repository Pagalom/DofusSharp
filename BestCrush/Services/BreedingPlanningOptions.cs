using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public enum BreedingPlanningMode { Economy, Speed }

public sealed record BreedingPlanningOptions(decimal MinimumGenerationChancePercent = 65m,
    BreedingPlanningMode Mode = BreedingPlanningMode.Economy)
{
    public int CapturesPerDay { get; init; }
    public long? BudgetKamas { get; init; }
    internal int CaptureBudget => CapturesPerDay > 0 ? CapturesPerDay : 20;
    // Planning defaults, not game-imposed daily quotas. Area nets assume three targets per encounter.
    internal static int DefaultCaptures(int? breederLevel) => breederLevel switch
    { >= 200 => 120, >= 150 => 60, >= 100 => 40, _ => 20 };

    internal void Validate()
    {
        if (MinimumGenerationChancePercent is < 0 or > 100 || !Enum.IsDefined(Mode) || CapturesPerDay is < 0 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(MinimumGenerationChancePercent));
    }

    internal bool Accepts(int first, int second) =>
        BreedingDecisions.TargetGenerationChance(first, second).Base >= MinimumGenerationChancePercent;

    internal void Rebase(BreedingObjectiveProgress progress, IReadOnlyList<BreedingObjectiveStep> steps)
    {
        var completed = progress.Done.Select(index => progress.Steps[index]).ToList();
        var done = new HashSet<int>();
        for (int i = 0; i < steps.Count; i++)
        {
            var next = steps[i];
            int match = completed.FindIndex(old => old.Action == next.Action && old.Name == next.Name && old.Sex == next.Sex &&
                old.First == next.First && old.Second == next.Second &&
                (next.Action is not (BreedingObjectiveAction.Prepare or BreedingObjectiveAction.Train) ||
                 old.Level is { } oldLevel && next.Level is { } target && oldLevel >= target));
            if (match < 0) continue;
            done.Add(i); completed.RemoveAt(match);
        }
        progress.Steps = steps.ToList(); progress.Done = done; progress.Partial.Clear(); progress.Planning = this;
    }

    internal IOrderedEnumerable<BreedingProduction> Order(IEnumerable<BreedingProduction> routes) => Mode == BreedingPlanningMode.Speed
        // No fabricated capture duration: speed prefers immediately purchasable targets and fewer breeding operations.
        ? routes.OrderBy(route => route.ExpectedCrosses).ThenBy(route => route.ExpectedCaptures)
            .ThenBy(route => route.TrainingExperience).ThenBy(route => route.Cost)
        : routes.OrderBy(route => route.Cost).ThenBy(route => route.ExpectedCrosses).ThenBy(route => route.TrainingExperience);

    internal (int First, int Second, long Cost)? OwnedParentLevels(PaddockMountReading? first,
        PaddockMountReading? second, IReadOnlyDictionary<int, long> prices, decimal fixedAttemptCost = 0)
    {
        (int First, int Second, long Cost, int Experience, decimal ExpectedCost, decimal Attempts)? best = null;
        for (int a = first?.Level ?? 1; a <= 200; a++)
        for (int b = second?.Level ?? 1; b <= 200; b++)
        {
            if (!Accepts(a, b)) continue;
            int xpA = Math.Max(0, BreedingMountExperience.RequiredAtLevel(a) - (first?.Experience ?? 0));
            int xpB = Math.Max(0, BreedingMountExperience.RequiredAtLevel(b) - (second?.Experience ?? 0));
            if (!TryCost(xpA, out long costA) || !TryCost(xpB, out long costB)) continue;
            long cost = checked(costA + costB);
            var candidate = (First: a, Second: b, Cost: cost, Experience: xpA + xpB,
                ExpectedCost: (fixedAttemptCost + cost) / (BreedingDecisions.TargetGenerationChance(a, b).Base / 100m),
                Attempts: 100m / BreedingDecisions.TargetGenerationChance(a, b).Base);
            if (best is null || (Mode == BreedingPlanningMode.Speed
                ? (candidate.Attempts, candidate.Experience, candidate.Cost).CompareTo((best.Value.Attempts, best.Value.Experience, best.Value.Cost)) < 0
                : (candidate.ExpectedCost, candidate.Experience).CompareTo((best.Value.ExpectedCost, best.Value.Experience)) < 0))
                best = candidate;
        }
        return best is { } value ? (value.First, value.Second, value.Cost) : null;

        bool TryCost(int xp, out long cost) { cost = 0; return xp == 0 || prices.TryGetValue(xp, out cost); }
    }
}
