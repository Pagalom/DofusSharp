namespace BestCrush.Network.Protocol;

/// <summary>
/// Research-only structural equivalence of FULL item effect protobufs.
/// Unlike numeric f5 decoding, this handles an intact unrecognized f6/f7/f8/f9
/// payload without interpreting it. Equal byte content (after normalizing
/// the order of outer protobuf fields) is evidence of equivalent wire shape
/// and content, NOT semantic proof that two game items are identical.
/// Neither effect wire values nor identifiers are ever written to a report.
/// </summary>
public sealed class StockOpaqueWireSnapshotStudy
{
    private const int MaxEffectCount = 64;
    private const int MaxEffectBytes = 1024;

    internal sealed record WireGroups(
        string Container, int Rows,
        Dictionary<(ulong ItemTypeCandidate, string EffectSignature), long> Quantities,
        int StructuredEffectRows);

    private readonly Dictionary<int, (WireGroups? Inventory, WireGroups? Bank)>
        _sessions = [];
    private const int MaxSessions = 16;

    public void DropSession(int sessionNumber) =>
        _sessions.Remove(sessionNumber);

    /// <summary>
    /// Stores only the last complete candidate per session/container.
    /// A malformed/partial candidate actively invalidates that baseline.
    /// No cross-container, cross-flow, UID or permanent ownership matching.
    /// </summary>
    public string Observe(int sessionNumber, StockSnapshotWireCandidate? snapshot)
    {
        if (sessionNumber <= 0 || snapshot is null ||
            snapshot.Container is not ("bank" or "inventory"))
            return "wire-grouping=not-ready wire-snapshot-change=not-comparable";

        if (!_sessions.TryGetValue(sessionNumber, out var scope))
        {
            if (_sessions.Count >= MaxSessions)
                _sessions.Clear();
            scope = default;
        }

        WireGroups? current = TryCreate(snapshot);
        WireGroups? previous = snapshot.Container == "bank" ?
            scope.Bank : scope.Inventory;

        if (snapshot.Container == "bank")
            scope.Bank = current;
        else
            scope.Inventory = current;
        _sessions[sessionNumber] = scope;

        if (current is null)
            return "wire-grouping=not-ready wire-snapshot-change=not-comparable";

        string summary = "wire-grouping=candidate groups=" +
            current.Quantities.Count + " source-rows=" + current.Rows +
            " rows-with-structured-effects=" + current.StructuredEffectRows;

        if (previous is null)
            return summary + " wire-snapshot-change=no-prior-snapshot";

        int increased = 0, decreased = 0, added = 0, removed = 0, unchanged = 0;
        foreach (var pair in current.Quantities)
        {
            if (!previous.Quantities.TryGetValue(pair.Key, out long old))
                added++;
            else if (pair.Value > old)
                increased++;
            else if (pair.Value < old)
                decreased++;
            else
                unchanged++;
        }
        foreach (var pair in previous.Quantities)
            if (!current.Quantities.ContainsKey(pair.Key))
                removed++;

        return summary + " wire-snapshot-change=compared" +
            " increased-groups=" + increased +
            " decreased-groups=" + decreased +
            " new-groups=" + added +
            " removed-groups=" + removed +
            " unchanged-groups=" + unchanged;
    }

    internal static WireGroups? TryCreate(StockSnapshotWireCandidate? snapshot)
    {
        if (snapshot?.Status != StockSnapshotCandidateStatus.CompleteCandidate ||
            snapshot.Container is not ("bank" or "inventory"))
            return null;

        Dictionary<(ulong, string), long> quantities = [];
        int structuredRows = 0;
        try
        {
            foreach (StockItemWireCandidate row in snapshot.Rows)
            {
                if (row.Field3QuantityCandidate is not ulong quantity ||
                    quantity == 0 || quantity > (ulong)long.MaxValue ||
                    row.Field4ItemIdCandidate is not ulong itemId || itemId == 0 ||
                    row.Field5InstanceUidCandidate is not > 0 ||
                    row.ExactEffectWireEntries is null ||
                    row.ExactEffectWireEntries.Count != row.EffectFields ||
                    row.ExactEffectWireEntries.Count > MaxEffectCount)
                    return null;

                List<string> effectSignatures = [];
                foreach (byte[] raw in row.ExactEffectWireEntries)
                {
                    if (raw is not { Length: > 0 and <= MaxEffectBytes } ||
                        !TryCanonicalEffect(raw, out string signature))
                        return null;
                    effectSignatures.Add(signature);
                }

                effectSignatures.Sort(StringComparer.Ordinal);
                string signatureKey = string.Join(";", effectSignatures
                    .Select(x => x.Length + ":" + x));
                var key = (itemId, signatureKey);

                if (quantities.TryGetValue(key, out long previous))
                    quantities[key] = checked(previous + (long)quantity);
                else
                    quantities.Add(key, (long)quantity);

                if (row.Effects is null && effectSignatures.Count > 0)
                    structuredRows++;
            }
        }
        catch (OverflowException)
        {
            return null;
        }

        return new WireGroups(snapshot.Container, snapshot.Rows.Count,
            quantities, structuredRows);
    }

    /// <summary>
    /// Normalize only OUTER field order. Unknown/length-delimited nested
    /// values are opaque and kept exactly (hex in RAM ONLY). This intentionally
    /// cannot merge two encodings with differing nested representations.
    /// </summary>
    private static bool TryCanonicalEffect(byte[] raw, out string signature)
    {
        signature = string.Empty;
        List<ProtoField>? fields = ProtoWire.ReadFields(raw);
        if (fields is null || fields.Count is < 1 or > 64)
            return false;

        // An effect without exactly one nonzero f10 cannot be safely
        // distinguished by its game effect code.
        ProtoField[] effectCodes = fields.Where(x => x.Number == 10).ToArray();
        if (effectCodes.Length != 1 ||
            effectCodes[0].WireType != ProtoWireType.Varint ||
            effectCodes[0].Varint == 0)
            return false;

        List<string> entries = [];
        foreach (ProtoField field in fields)
        {
            if (field.Number <= 0)
                return false;
            string value = field.WireType switch
            {
                ProtoWireType.Varint =>
                    "v:" + field.Varint.ToString("X16",
                        System.Globalization.CultureInfo.InvariantCulture),
                ProtoWireType.Fixed32 =>
                    "32:" + field.Fixed32.ToString("X8",
                        System.Globalization.CultureInfo.InvariantCulture),
                ProtoWireType.Fixed64 =>
                    "64:" + field.Fixed64.ToString("X16",
                        System.Globalization.CultureInfo.InvariantCulture),
                ProtoWireType.LengthDelimited when field.Bytes is not null =>
                    "len:" + Convert.ToHexString(field.Bytes),
                _ => ""
            };
            if (value.Length == 0)
                return false;
            entries.Add(field.Number + "=" + value);
        }
        entries.Sort(StringComparer.Ordinal);
        signature = string.Join("|", entries);
        return true;
    }
}
