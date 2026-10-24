using BestCrush.Network.Protocol;
using DofusSharp.DofusDb.ApiClients;
using DofusSharp.DofusDb.ApiClients.Models.Items;

namespace BestCrush.NetworkProbe.Capture;

/// <summary>
/// Load only two PUBLIC item catalog records. This is opt-in, requires API
/// access, retains data in memory and never logs HTTP bodies or effect IDs.
/// No player data, game traffic, character name or endpoint is sent to DofusDb.
/// </summary>
internal static class DofusDbEffectCatalogReferenceLoader
{
    private const long KwakanneauId = 2418;
    private const long KwaktureId = 2427;

    internal static async Task<ItemEffectDofusDbCatalogStudy?> TryLoadAsync()
    {
        using CancellationTokenSource deadline =
            new(TimeSpan.FromSeconds(10));
        try
        {
            var client = DofusDbClient.Production().Items();
            DofusDbItem first = await client.GetAsync(
                KwakanneauId, deadline.Token);
            DofusDbItem second = await client.GetAsync(
                KwaktureId, deadline.Token);

            // Fail closed on stale/renamed catalog entries or absent effects:
            // external sources can change independently of the game client.
            if (first.Id != KwakanneauId || second.Id != KwaktureId ||
                !string.Equals(first.Name?.Fr, "Kwakanneau de Flammes",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(second.Name?.Fr, "Kwakture de Flammes",
                    StringComparison.OrdinalIgnoreCase))
                return null;

            ItemCatalogCandidateReference? a = Build("A",
                [99, 20], first);
            ItemCatalogCandidateReference? b = Build("B",
                [24, 26, 5, 3], second);
            return a is not null && b is not null
                ? new ItemEffectDofusDbCatalogStudy(a, b)
                : null;
        }
        catch (Exception)
        {
            // Do not print URLs, service responses, tokens or raw exceptions.
            return null;
        }
    }

    private static ItemCatalogCandidateReference? Build(
        string label, IReadOnlyList<long> values, DofusDbItem item)
    {
        if (item.Effects is null ||
            item.Effects.Count is < 1 or > 64)
            return null;

        ItemCatalogEffectReference[] effects = item.Effects
            .Select(e => new ItemCatalogEffectReference(
                e.EffectId is > 0 ? checked((ulong)e.EffectId.Value) : 0,
                e.From, e.To,
                e.Characteristic, e.ElementId))
            .ToArray();
        return new ItemCatalogCandidateReference(label, values, effects);
    }
}
