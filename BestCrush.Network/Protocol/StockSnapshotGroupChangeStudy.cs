namespace BestCrush.Network.Protocol;

/// <summary>
/// P3d.10: compare two full candidate snapshots for the SAME container,
/// within one TCP session. Only anonymous counts are printed. A quantity
/// change is a candidate, not proof of transfer direction or account ownership.
/// </summary>
public static class StockSnapshotGroupChangeStudy
{
    public static string Describe(
        StockGroupingCandidate? previous, StockGroupingCandidate? current)
    {
        if (current is null)
            return "snapshot-change=not-comparable";
        if (previous is null)
            return "snapshot-change=no-prior-snapshot";
        if (!string.Equals(previous.Container, current.Container,
                StringComparison.Ordinal))
            return "snapshot-change=different-container";

        try
        {
            var before = Index(previous);
            var after = Index(current);
            if (before is null || after is null)
                return "snapshot-change=not-comparable";

            int increased = 0;
            int decreased = 0;
            int newGroups = 0;
            int removed = 0;
            int unchanged = 0;
            foreach (var pair in after)
            {
                if (!before.TryGetValue(pair.Key, out long oldQuantity))
                {
                    newGroups++;
                    continue;
                }

                if (pair.Value > oldQuantity) increased++;
                else if (pair.Value < oldQuantity) decreased++;
                else unchanged++;
            }

            foreach (var pair in before)
                if (!after.ContainsKey(pair.Key))
                    removed++;

            return "snapshot-change=compared" +
                " increased-groups=" + increased +
                " decreased-groups=" + decreased +
                " new-groups=" + newGroups +
                " removed-groups=" + removed +
                " unchanged-groups=" + unchanged;
        }
        catch (OverflowException)
        {
            return "snapshot-change=not-comparable";
        }
    }

    private static Dictionary<(ulong ItemId, string Effects), long>? Index(
        StockGroupingCandidate snapshot)
    {
        Dictionary<(ulong ItemId, string Effects), long> items = [];
        foreach (StockGroupedItemCandidate group in snapshot.Groups)
        {
            if (group.ItemIdCandidate == 0 || group.QuantityCandidate <= 0 ||
                group.Effects is null || group.Effects.Count > 64 ||
                group.Effects.Any(x => x.EffectCode == 0))
                return null;

            // Multiset equality, signed values and repeated effect codes.
            string effectSignature = string.Join(";", group.Effects
                .OrderBy(x => x.EffectCode)
                .ThenBy(x => x.Value)
                .Select(x => x.EffectCode.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + ":" +
                    x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var key = (group.ItemIdCandidate, effectSignature);
            if (items.TryGetValue(key, out long existing))
                items[key] = checked(existing + group.QuantityCandidate);
            else
                items.Add(key, group.QuantityCandidate);
        }

        return items;
    }
}
