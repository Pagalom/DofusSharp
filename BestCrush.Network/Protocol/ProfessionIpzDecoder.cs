using System.Collections.ObjectModel;

namespace BestCrush.Network.Protocol;

/// <summary>
/// P1d research-only observation of the Dofus 3.6.11.15 ipz wire layout.
/// Neither profession IDs nor character identity have been established.
/// Values remain local to the decoder and must never be logged.
/// </summary>
public sealed class ProfessionIpzRow
{
    public int Level { get; }
    public ulong ExperienceCounter { get; }
    public IReadOnlyDictionary<int, ulong> UnidentifiedVarints { get; }

    internal ProfessionIpzRow(
        int level, ulong experienceCounter, Dictionary<int, ulong> other)
    {
        Level = level;
        ExperienceCounter = experienceCounter;
        UnidentifiedVarints = new ReadOnlyDictionary<int, ulong>(other);
    }

    public override string ToString() => "[ipz row: values redacted]";
}

public sealed class ProfessionIpzSnapshot
{
    public IReadOnlyList<ProfessionIpzRow> Rows { get; }
    public int ImplicitZeroExperienceRows { get; }

    internal ProfessionIpzSnapshot(List<ProfessionIpzRow> rows, int implicitZeroExperienceRows)
    {
        Rows = rows.AsReadOnly();
        ImplicitZeroExperienceRows = implicitZeroExperienceRows;
    }

    public override string ToString() => "[ipz snapshot: values redacted]";
}

public static class ProfessionIpzDecoder
{
    // 20 f1 rows are observed both in P1b and in the player's 20-job UI.
    // Reject different shapes rather than guessing or mixing game versions.
    private const int ExpectedRows = 20;
    private const int MaxBodyBytes = 16 * 1024;
    private const int MaxRowBytes = 1024;

    public static bool TryDecode(
        ReadOnlySpan<byte> body, out ProfessionIpzSnapshot? snapshot) =>
        TryDecode(body, out snapshot, out _);

    /// <summary>
    /// Diagnostics are fixed categorical strings only. Never include packet
    /// contents, numeric fields, raw bytes or private reference values.
    /// In proto3 a missing scalar varint denotes the default value zero;
    /// f2 (cumulative XP candidate) may therefore be omitted for level 1.
    /// </summary>
    public static bool TryDecode(
        ReadOnlySpan<byte> body, out ProfessionIpzSnapshot? snapshot,
        out string? rejectionReason)
    {
        snapshot = null;
        rejectionReason = null;
        if (body.IsEmpty || body.Length > MaxBodyBytes)
        {
            rejectionReason = "size";
            return false;
        }

        try
        {
            List<ProtoField>? root = ProtoWire.ReadFields(body);
            if (root is null)
            {
                rejectionReason = "invalid-root";
                return false;
            }

            if (root.Count != ExpectedRows)
            {
                rejectionReason = "row-count";
                return false;
            }

            if (root.Any(f => f.Number != 1 ||
                    f.WireType != ProtoWireType.LengthDelimited ||
                    f.Bytes is null || f.Bytes.Length is < 1 or > MaxRowBytes))
            {
                rejectionReason = "row-envelope";
                return false;
            }

            List<ProfessionIpzRow> rows = new(ExpectedRows);
            int implicitZeroXp = 0;
            foreach (ProtoField record in root)
            {
                List<ProtoField>? fields = ProtoWire.ReadFields(record.Bytes!);
                if (fields is null)
                {
                    rejectionReason = "invalid-row";
                    return false;
                }

                ProtoField[] levels = fields.Where(f => f.Number == 1).ToArray();
                ProtoField[] xp = fields.Where(f => f.Number == 2).ToArray();
                if (levels.Length != 1)
                {
                    rejectionReason = "level-cardinality";
                    return false;
                }

                if (levels[0].WireType != ProtoWireType.Varint ||
                    levels[0].Varint is < 1 or > 200)
                {
                    rejectionReason = "level-type-or-range";
                    return false;
                }

                // protobuf scalar zero values can be absent on the wire.
                // A missing f2 is not yet proof that this is XP, but is
                // consistent with an explicit varint zero.
                if (xp.Length > 1)
                {
                    rejectionReason = "xp-duplicate";
                    return false;
                }

                if (xp.Length == 1 && xp[0].WireType != ProtoWireType.Varint)
                {
                    rejectionReason = "xp-type";
                    return false;
                }

                ulong experience = xp.Length == 1 ? xp[0].Varint : 0;
                if (xp.Length == 0)
                    implicitZeroXp++;

                Dictionary<int, ulong> other = [];
                HashSet<int> duplicated = [];
                foreach (ProtoField field in fields)
                {
                    if (field.Number <= 2 ||
                        field.WireType != ProtoWireType.Varint)
                        continue;

                    // Repeated unknown scalars are not considered stable
                    // job identifiers. Do not infer semantics from values.
                    if (!other.TryAdd(field.Number, field.Varint))
                        duplicated.Add(field.Number);
                }

                foreach (int key in duplicated)
                    other.Remove(key);

                rows.Add(new ProfessionIpzRow(
                    (int)levels[0].Varint, experience, other));
            }

            snapshot = new ProfessionIpzSnapshot(rows, implicitZeroXp);
            return true;
        }
        catch (OverflowException)
        {
            rejectionReason = "invalid-wire";
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            rejectionReason = "invalid-wire";
            return false;
        }
    }

    /// <summary>
    /// Strict single-row update seen after a craft. Unlike the full roster,
    /// it cannot establish which professions exist on an account by itself.
    /// A caller must already possess a valid 20-row roster from the same
    /// verified Game lease and match its candidate job ID.
    /// </summary>
    public static bool TryDecodeSingleRow(
        ReadOnlySpan<byte> body, out ProfessionIpzRow? row)
    {
        row = null;
        if (body.IsEmpty || body.Length > MaxRowBytes + 8)
            return false;

        try
        {
            List<ProtoField>? root = ProtoWire.ReadFields(body);
            if (root is not { Count: 1 } ||
                root[0].Number != 1 ||
                root[0].WireType != ProtoWireType.LengthDelimited ||
                root[0].Bytes is not { Length: > 0 and <= MaxRowBytes } bytes)
                return false;

            List<ProtoField>? fields = ProtoWire.ReadFields(bytes);
            if (fields is null || fields.Count is < 2 or > 24)
                return false;

            ProtoField[] levels = fields.Where(f => f.Number == 1).ToArray();
            ProtoField[] xp = fields.Where(f => f.Number == 2).ToArray();
            ProtoField[] ids = fields.Where(f => f.Number == 5).ToArray();
            if (levels.Length != 1 || levels[0].WireType != ProtoWireType.Varint ||
                levels[0].Varint is < 1 or > 200 ||
                xp.Length > 1 ||
                (xp.Length == 1 && xp[0].WireType != ProtoWireType.Varint) ||
                ids.Length != 1 || ids[0].WireType != ProtoWireType.Varint ||
                ids[0].Varint == 0)
                return false;

            ulong experience = xp.Length == 1 ? xp[0].Varint : 0;
            row = new ProfessionIpzRow(
                (int)levels[0].Varint,
                experience,
                new Dictionary<int, ulong> { [5] = ids[0].Varint });
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Only structural facts and a count of matching local reference pairs
    /// are emitted. The field statistics are clues, not mappings to jobs.
    /// </summary>
    public static string DescribeAnonymously(
        ProfessionIpzSnapshot snapshot,
        IReadOnlyList<ProfessionReference> references)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(references);

        HashSet<(int Level, ulong Xp)> observed = snapshot.Rows
            .Select(row => (row.Level, row.ExperienceCounter))
            .ToHashSet();

        int matched = references.Distinct().Count(reference =>
            reference.Level >= 1 &&
            observed.Contains((reference.Level, (ulong)reference.ProgressXp)));

        int rowCount = snapshot.Rows.Count;
        int[] keys = snapshot.Rows
            .SelectMany(row => row.UnidentifiedVarints.Keys)
            .Distinct()
            .OrderBy(key => key)
            .Take(12)
            .ToArray();

        string extra = keys.Length == 0
            ? "none"
            : string.Join(" ", keys.Select(key =>
            {
                ulong[] values = snapshot.Rows
                    .Where(row => row.UnidentifiedVarints.ContainsKey(key))
                    .Select(row => row.UnidentifiedVarints[key])
                    .ToArray();
                return $"f{key}:present={values.Length}/{rowCount}," +
                       $"distinct={values.Distinct().Count()}";
            }));

        return $"rows={rowCount} matched={matched}/{references.Count} " +
               $"xp-implicit-zero={snapshot.ImplicitZeroExperienceRows} " +
               $"unknown=[{extra}] identity=unverified";
    }
}
