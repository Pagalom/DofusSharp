using DofusSharp.DofusDb.ApiClients.Search;

namespace BestCrush.Services;

public sealed record BossSoulItem(long ItemId, long BossId, long GroupId);

public sealed partial class ActivityCatalogService
{
    public Task<BossSoulItem[]> GetBossSoulItemsAsync(CancellationToken token) => CachedAsync("boss-soul-items-v2", async () =>
    {
        var items = await Fetch(factory.Items(), new() { Predicates = [new DofusDbSearchPredicate.Eq("typeId", "327")],
            Select = ["id", "typeId", "possibleEffects"] }, token);
        return items.Where(x => x.Id is > 0 && x.TypeId == 327).Select(x => new {
                ItemId = x.Id!.Value,
                Boss = x.PossibleEffects?.Where(e => HasSoulEffect(e, 4058, 593)).ToArray(),
                Group = x.PossibleEffects?.Where(e => HasSoulEffect(e, 4057, 592)).ToArray()
            }).Where(x => x.Boss is [{ Value: > 0 }] && x.Group is [{ DiceSide: > 0 }])
            .Select(x => new BossSoulItem(x.ItemId, x.Boss![0].Value!.Value, x.Group![0].DiceSide!.Value))
            .DistinctBy(x => x.ItemId).ToArray();
    }, token);
}
