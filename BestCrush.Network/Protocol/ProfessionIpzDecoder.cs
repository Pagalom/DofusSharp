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

    internal ProfessionIpzSnapshot(List<ProfessionIpzRow> rows) =>
        Rows = rows.AsReadOnly();

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
        ReadOnlySpan<byte> body, out ProfessionIpzSnapshot? snapshot)
    {
        snapshot = null;
        if (body.IsEmpty || body.Length > MaxBodyBytes)
            return false;

        try
        {
            List<ProtoField>? root = ProtoWire.ReadFields(body);
            if (root is null || root.Count != ExpectedRows ||
                root.Any(f => f.Number != 1 ||
                    f.WireType != ProtoWireType.LengthDelimited ||
                    f.Bytes is null || f.Bytes.Length is < 1 or > MaxRowBytes))
                return false;

            List<ProfessionIpzRow> rows = new(ExpectedRows);
            foreach (ProtoField record in root)
            {
                List<ProtoField>? fields = ProtoWire.ReadFields(record.Bytes!);
                if (fields is null)
                    return false;

                ProtoField[] levels = fields.Where(f => f.Number == 1).ToArray();
                ProtoField[] xp = fields.Where(f => f.Number == 2).ToArray();
                if (levels.Length != 1 || xp.Length != 1 ||
                    levels[0].WireType != ProtoWireType.Varint ||
                    xp[0].WireType != ProtoWireType.Varint ||
                    levels[0].Varint is < 1 or > 200)
                    return false;

                Dictionary<int, ulong> other = [];
                HashSet<int> duplicated = [];
                foreach (ProtoField field in fields)
                {
                    if (field.Number <= 2 ||
                        field.WireType != ProtoWireType.Varint)
                        continue;

                    // Repeated unknown scalars are not considered stable
                    // job identifiers. Do not infer semantics from their values.
                    if (!other.TryAdd(field.Number, field.Varint))
                        duplicated.Add(field.Number);
                }

                foreach (int key in duplicated)
                    other.Remove(key);

                rows.Add(new ProfessionIpzRow(
                    (int)levels[0].Varint, xp[0].Varint, other));
            }

            snapshot = new ProfessionIpzSnapshot(rows);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            // Malformed varint length rejected without exposing wire data.
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
               $"unknown=[{extra}] identity=unverified";
    }
}
