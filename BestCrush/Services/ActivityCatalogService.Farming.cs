namespace BestCrush.Services;

public sealed partial class ActivityCatalogService
{
    public Task<FarmingZone[]> GetFarmingZonesAsync(CancellationToken token) => CachedAsync("farming-zones-v1", async () =>
    {
        var areas = await Fetch(factory.Areas(), new() { Select = ["id", "name"] }, token);
        var regionNames = areas.Where(area => area.Id is not null).DistinctBy(area => area.Id)
            .ToDictionary(area => area.Id!.Value, area => area.Name?.Fr ?? area.Name?.En);
        var subareas = await Fetch(factory.SubAreas(), new() { Select = ["id", "name", "areaId", "monsters"] }, token);
        return subareas.Where(area => area.Id is > 0 && area.Monsters is { Count: > 0 })
            .Select(area => new FarmingZone(area.Id!.Value, area.Name?.Fr ?? area.Name?.En ?? $"Zone #{area.Id}",
                area.AreaId is { } regionId ? regionNames.GetValueOrDefault(regionId) : null,
                area.Monsters!.Where(id => id > 0).Distinct().Order().ToArray()))
            .Where(zone => zone.MonsterIds.Length > 0).DistinctBy(zone => zone.Id)
            .OrderBy(zone => zone.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(zone => zone.Id).ToArray();
    }, token);
}
