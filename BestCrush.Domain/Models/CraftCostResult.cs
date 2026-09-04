namespace BestCrush.Domain.Models;

public sealed record CraftCostResult(
    IReadOnlyList<CraftResourceCostLine> Resources)
{
    public bool IsComplete =>
        Resources.All(resource =>
            resource.HasPrice);

    public int MissingIngredientCount =>
        Resources.Count(resource =>
            !resource.HasPrice);

    // Conservé pour compatibilité avec les vues / tests existants.
    public int MissingResourceCount =>
        MissingIngredientCount;

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