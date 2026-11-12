using DofusSharp.DofusDb.ApiClients.Search;

namespace BestCrush.Services;

public sealed record CraftingJob(long Id, string Name, IReadOnlyList<long> ItemIds);

public sealed partial class ActivityCatalogService
{
    public Task<CraftingJob[]> GetCraftingJobsAsync(CancellationToken token) => CachedAsync("crafting-jobs-v1", async () =>
    {
        var jobs = await Fetch(factory.Jobs(), new() { Select = ["id", "name"] }, token);
        var skills = await Fetch(factory.Skills(), new()
        {
            Select = ["parentJobId", "isForgemagus", "craftableItemIds"]
        }, token);
        return skills.Where(x => x.IsForgemagus == false && x.ParentJobId is > 0 && x.CraftableItemIds?.Count > 0)
            .GroupBy(x => x.ParentJobId!.Value)
            .Select(group => new CraftingJob(group.Key,
                jobs.FirstOrDefault(job => job.Id == group.Key)?.Name?.Fr ?? $"Métier #{group.Key}",
                group.SelectMany(skill => skill.CraftableItemIds!).Distinct().Order().ToArray()))
            .OrderBy(x => x.Name).ToArray();
    }, token);
}
