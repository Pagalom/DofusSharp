using DofusSharp.DofusDb.ApiClients.Models.Items;
using DofusSharp.DofusDb.ApiClients.Search;

namespace BestCrush.Services;

public sealed partial class ActivityCatalogService
{
    public Task<EmptySoulStone[]> GetEmptySoulStonesAsync(CancellationToken token) =>
        CachedAsync("empty-soul-stones-v2", () => LoadEmptySoulStonesAsync(token), token);

    private async Task<EmptySoulStone[]> LoadEmptySoulStonesAsync(CancellationToken token)
    {
        var items = await Fetch(factory.Items(), new() { Predicates = [new DofusDbSearchPredicate.Eq("typeId", "83")],
            Select = ["id", "name", "typeId", "possibleEffects"] }, token);
        return items.Where(x => x.Id is > 0 && x.TypeId == 83).Select(x => new {
                Item = x, Effects = x.PossibleEffects?.Where(e => HasSoulEffect(e, 705, 119)).ToArray()
            }).Where(x => x.Effects is [{ DiceNum: 100, Value: > 0 }])
            .Select(x => new EmptySoulStone(x.Item.Id!.Value, x.Item.Name?.Fr ?? $"Pierre #{x.Item.Id}", x.Effects![0].Value!.Value))
            .ToArray();
    }

    private static bool HasSoulEffect(DofusDbItemRawEffect effect, long actionId, long baseEffectId) =>
        (effect.EffectId ?? effect.ActionId) is { } id ? id == actionId : effect.BaseEffectId == baseEffectId;
}
