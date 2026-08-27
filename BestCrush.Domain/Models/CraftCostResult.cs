namespace BestCrush.Domain.Models;

public sealed record CraftCostResult(
    IReadOnlyList<CraftResourceCostLine> Resources)
{
    public bool IsComplete =>
        Resources.All(resource =>
            resource.HasPrice);

    public int MissingResourceCount =>
        Resources.Count(resource =>
            !resource.HasPrice);

    public long KnownCost =>
        Resources
            .Where(resource =>
                resource.Cost is not null)
            .Sum(resource =>
                resource.Cost!.Value);

    public long? TotalCost =>
        IsComplete
            ? KnownCost
            : null;
}