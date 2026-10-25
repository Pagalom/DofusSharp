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
    /// <summary>
    /// Anonymous reason counts for a rejected candidate. Never expose item
    /// identifiers, effect codes/values, quantities or row positions.
    /// </summary>
    public static string DescribeBlockers(StockSnapshotWireCandidate? snapshot)
    {
        if (snapshot?.Status != StockSnapshotCandidateStatus.CompleteCandidate)
            return "grouping-blockers=snapshot-not-complete";

        int badScalars = snapshot.Rows.Count(x =>
            x.Field3QuantityCandidate is not > 0 ||
            x.Field4ItemIdCandidate is not > 0 ||
            x.Field5InstanceUidCandidate is not > 0);
        int quantityOverflow = snapshot.Rows.Count(x =>
            x.Field3QuantityCandidate is ulong quantity &&
            quantity > (ulong)long.MaxValue);
        int effectUndecodable = snapshot.Rows.Count(x => x.Effects is null);
        int effectCountMismatch = snapshot.Rows.Count(x =>
            x.Effects is not null && x.Effects.Count != x.EffectFields);
        int effectLimit = snapshot.Rows.Count(x =>
            x.Effects is { Count: > 64 });
        int invalidCode = snapshot.Rows.Count(x =>
            x.Effects?.Any(e => e.EffectCode == 0) == true);

        // Each category is hardcoded in the decoder; no packet bytes are used
        // to create report text. Group by issue, never by item or effect ID.
        string shapes = string.Join(";", snapshot.Rows
            .Where(x => x.EffectDecodeIssue is not null)
            .GroupBy(x => FixedEffectIssue(x.EffectDecodeIssue))
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key + ":" + x.Count()));

        // Inspect only the first offending effect in each rejected row.
        // Shapes contain field numbers/wire types only; cap the variants to
        // keep output concise and avoid dumping arbitrary schema contents.
        const int MaxShapeVariants = 8;
        var missingShapes = snapshot.Rows
            .Where(x => x.EffectDecodeIssue == "missing-value" &&
                x.EffectDecodeShape is not null)
            .GroupBy(x => x.EffectDecodeShape!, StringComparer.Ordinal)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToArray();
        string missingValueShapes = string.Join("|", missingShapes
            .Take(MaxShapeVariants)
            .Select(x => "(" + x.Key + ")x" + x.Count()));
        if (missingShapes.Length > MaxShapeVariants)
            missingValueShapes += "|more-variants";

        return "grouping-blockers=" +
            "bad-scalars:" + badScalars +
            ",quantity-overflow:" + quantityOverflow +
            ",undecodable-effects:" + effectUndecodable +
            ",effect-count-mismatch:" + effectCountMismatch +
            ",effect-limit:" + effectLimit +
            ",invalid-effect-code:" + invalidCode +
            ",effect-shapes:[" + shapes + "]" +
            (missingValueShapes.Length > 0
                ? ",missing-value-wire-shapes:[" + missingValueShapes + "]"
                : "");
    }

    private static string FixedEffectIssue(string? issue) => issue switch
    {
        "too-many-effects" or "effect-encoding" or "effect-size" or
        "effect-malformed" or "missing-value" or "missing-code" or
        "multiple-values" or "multiple-codes" or "value-encoding" or
        "code-encoding" or "zero-code" => issue,
        _ => "other"
    };

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
