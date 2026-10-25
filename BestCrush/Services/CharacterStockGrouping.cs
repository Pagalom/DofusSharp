namespace BestCrush.Services;

/// <summary>
/// In-memory, business-level view of fungible items. The UID is intentionally
/// omitted. A caller must obtain a verified, authoritative per-container
/// snapshot before treating this grouping as actual stock.
/// </summary>
public sealed record CharacterStockGroup(
    ulong ItemId,
    long Quantity,
    IReadOnlyList<CharacterInventoryEffect> Effects);

/// <summary>
/// Pure projection for an already trusted inventory/bank snapshot.
/// This does not bind any network session, identify an account, update
/// CurrentCharacterState, write SQLite or infer an item's life-long UID.
/// </summary>
public static class CharacterStockGrouping
{
    public static IReadOnlyList<CharacterStockGroup>? TryGroup(
        IReadOnlyList<CharacterInventoryItem>? snapshot)
    {
        // Null = not observed; [] = genuinely observed empty, but only after
        // the caller has independently validated the source's authority.
        if (snapshot is null)
            return null;

        Dictionary<(ulong ItemId, string Effects), CharacterStockGroup> groups = [];
        try
        {
            foreach (CharacterInventoryItem item in snapshot)
            {
                if (item is null || item.ItemId == 0 || item.Quantity <= 0 ||
                    item.Effects is null || item.Effects.Count > 64 ||
                    item.Effects.Any(x => x.EffectId == 0))
                    return null;

                CharacterInventoryEffect[] effects = item.Effects
                    .OrderBy(x => x.EffectId)
                    .ThenBy(x => x.Value)
                    .ToArray();

                // Canonical, unambiguous delimiter-separated sequence;
                // duplicated effects are preserved, ordering is irrelevant.
                string effectKey = string.Join(";", effects.Select(x =>
                    x.EffectId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ":" + x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                var key = (item.ItemId, effectKey);
                if (groups.TryGetValue(key, out CharacterStockGroup? current))
                {
                    groups[key] = current with
                    {
                        Quantity = checked(current.Quantity + item.Quantity)
                    };
                }
                else
                {
                    groups.Add(key, new CharacterStockGroup(
                        item.ItemId, item.Quantity, effects));
                }
            }
        }
        catch (OverflowException)
        {
            return null;
        }
        return groups.Values.ToArray();
    }
}
