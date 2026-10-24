namespace BestCrush.Network.Protocol;

/// <summary>
/// Reference data from a separately fetched DofusDb item, not an observed
/// instance. The network code and catalog's EffectId might use distinct
/// namespaces; matching is a research hypothesis, never a business rule.
/// </summary>
public sealed record ItemCatalogEffectReference(
    ulong EffectId, int? From, int? To);

public sealed record ItemCatalogCandidateReference(
    string Label,
    IReadOnlyList<long> ExpectedValues,
    IReadOnlyList<ItemCatalogEffectReference> Effects);

/// <summary>
/// Opt-in, value-free comparison of f10 with DofusDb item EffectId.
/// Works on known item-instance effect values only; does not decode identity,
/// emit any wire values/IDs or modify application state.
/// </summary>
public sealed class ItemEffectDofusDbCatalogStudy
{
    private readonly ItemCatalogCandidateReference[] _items;

    public ItemEffectDofusDbCatalogStudy(
        ItemCatalogCandidateReference first,
        ItemCatalogCandidateReference second)
    {
        _items = [first, second];
        if (first.Label != "A" || second.Label != "B" ||
            _items.Any(x => x.ExpectedValues.Count is < 1 or > 16 ||
                x.ExpectedValues.Contains(0) ||
                x.ExpectedValues.Distinct().Count() != x.ExpectedValues.Count ||
                x.Effects.Count is < 1 or > 64 ||
                x.Effects.Any(e => e.EffectId == 0)) ||
            first.ExpectedValues.Count == second.ExpectedValues.Count)
            throw new ArgumentException("Unsupported catalog reference set.");
    }

    public string? Observe(string opcode, ReadOnlySpan<byte> payload)
    {
        IReadOnlyList<(long Value, ulong Code)>? pairs =
            ItemEffectsWireShapeStudy.ReadCandidatePairs(opcode, payload);
        if (pairs is null)
            return null;

        ItemCatalogCandidateReference? item = _items.FirstOrDefault(x =>
            x.ExpectedValues.Count == pairs.Count &&
            x.ExpectedValues.OrderBy(v => v)
                .SequenceEqual(pairs.Select(p => p.Value).OrderBy(v => v)));
        if (item is null)
            return null;

        int matched = 0;
        int rangeKnown = 0;
        int rangeValid = 0;

        foreach ((long value, ulong code) in pairs)
        {
            ItemCatalogEffectReference[] hits = item.Effects
                .Where(e => e.EffectId == code).ToArray();
            if (hits.Length == 0)
                continue;
            matched++;

            // Ranges are indicative only: client and catalog versions can
            // differ, and certain effect categories use special encoding.
            if (hits.Any(e => e.From.HasValue && e.To.HasValue))
            {
                rangeKnown++;
                if (hits.Any(e => e.From.HasValue && e.To.HasValue &&
                    value >= Math.Min(e.From.Value, e.To.Value) &&
                    value <= Math.Max(e.From.Value, e.To.Value)))
                    rangeValid++;
            }
        }

        string ids = matched == pairs.Count
            ? "all"
            : matched == 0 ? "none" : "partial";

        string ranges = rangeKnown == 0
            ? "unknown"
            : rangeValid == rangeKnown ? "all" :
                rangeValid == 0 ? "none" : "partial";

        return "item=" + item.Label +
            " f10-vs-dofusdb-effectId=" + ids +
            " value-ranges=" + ranges +
            " catalog-effects=" + item.Effects.Count +
            " wire-effects=" + pairs.Count +
            " catalog-version=unverified";
    }
}
