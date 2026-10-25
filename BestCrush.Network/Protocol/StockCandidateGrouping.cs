namespace BestCrush.Network.Protocol;

/// <summary>
/// Aggregate only a structurally complete, fully decoded candidate snapshot.
/// ItemUid is intentionally NOT part of the grouping key or output.
/// ItemId + the complete multiset of real effects is the functional identity.
/// </summary>
public sealed record StockGroupedItemCandidate(
    ulong ItemIdCandidate,
    long QuantityCandidate,
    IReadOnlyList<StockEffectWireCandidate> Effects);

public sealed record StockGroupingCandidate(
    string Container,
    int SourceRows,
    IReadOnlyList<StockGroupedItemCandidate> Groups)
{
    /// <summary>Only metadata; no ItemIds, UIDs, quantities or effect values.</summary>
    public string Describe() =>
        "grouping=candidate groups=" + Groups.Count +
        " source-rows=" + SourceRows +
        " groups-with-effects=" + Groups.Count(x => x.Effects.Count > 0);
}

/// <summary>
/// Pure read-only preparation for future inventory/craft integration.
/// This is NOT authoritative until f3/f4 semantics and character scope
/// have been independently validated; no state is persisted or modified.
/// </summary>
public static class StockCandidateGrouping
{
    public static StockGroupingCandidate? TryGroup(
        StockSnapshotWireCandidate? snapshot)
    {
        if (snapshot?.Status != StockSnapshotCandidateStatus.CompleteCandidate)
            return null;

        Dictionary<(ulong ItemId, string Effects), StockGroupedItemCandidate> groups = [];
        try
        {
            foreach (StockItemWireCandidate row in snapshot.Rows)
            {
                if (row.Field3QuantityCandidate is not ulong quantity ||
                    quantity == 0 || quantity > long.MaxValue ||
                    row.Field4ItemIdCandidate is not ulong itemId || itemId == 0 ||
                    row.Field5InstanceUidCandidate is not > 0 ||
                    row.Effects is null ||
                    row.Effects.Count != row.EffectFields ||
                    row.Effects.Count > 64 ||
                    row.Effects.Any(x => x.EffectCode == 0))
                    return null;

                // Value/code ordering is immaterial. Preserve duplicate effects
                // and their signed numeric values; different jets cannot merge.
                StockEffectWireCandidate[] effects = row.Effects
                    .OrderBy(x => x.EffectCode)
                    .ThenBy(x => x.Value)
                    .ToArray();
                string canonicalEffects = string.Join(";", effects
                    .Select(x => x.EffectCode + ":" + x.Value));
                var key = (itemId, canonicalEffects);
                if (groups.TryGetValue(key, out StockGroupedItemCandidate? previous))
                {
                    groups[key] = previous with
                    {
                        QuantityCandidate = checked(
                            previous.QuantityCandidate + (long)quantity)
                    };
                }
                else
                {
                    groups.Add(key, new StockGroupedItemCandidate(
                        itemId, (long)quantity, effects));
                }
            }
        }
        catch (OverflowException)
        {
            return null;
        }

        return new StockGroupingCandidate(snapshot.Container,
            snapshot.Rows.Count, groups.Values.ToArray());
    }
}
