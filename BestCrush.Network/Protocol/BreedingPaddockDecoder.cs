namespace BestCrush.Network.Protocol;

public enum PaddockGauge
{
    SerenityLoss,
    SerenityGain,
    Endurance,
    Maturity,
    Love,
    Experience
}

public sealed record PaddockGaugeReading(PaddockGauge Gauge, bool Active, int Fuel);

public sealed record PaddockMountReading(
    Guid Id, int Level, int Experience, int Serenity, int? Maturity);

public sealed record PaddockReading(
    IReadOnlyList<PaddockGaugeReading> Gauges, IReadOnlyList<PaddockMountReading> Mounts);

/// <summary>
/// Periodic server-to-client enclosure snapshot observed on Dofus 3.7.13.17.
/// Mount identifiers are scoped to the observed game session.
/// </summary>
public static class BreedingPaddockDecoder
{
    public static PaddockReading? Decode(byte[] body)
    {
        if (body.Length is 0 or > 64 * 1024 || ProtoWire.ReadFields(body) is not { } root ||
            ReadMessage(root, 1) is not { } enclosure)
            return null;

        return DecodeEnclosure(enclosure);
    }

    public static PaddockReading? DecodeSelectedEnclosure(byte[] body)
    {
        if (body.Length is 0 or > 64 * 1024 || ProtoWire.ReadFields(body) is not { } root ||
            ReadMessage(root, 1) is not { } result || ReadMessage(result, 2) is not { } enclosure)
            return null;

        return DecodeEnclosure(enclosure);
    }

    public static int? DecodeEnclosureRequest(byte[] body) =>
        body.Length is > 0 and <= 16 && ProtoWire.ReadFields(body) is { } fields &&
        TryNumber(fields, 1, out int number) && number is >= 1 and <= 6 ? number : null;

    public static IReadOnlyList<PaddockMountReading>? DecodeStable(byte[] body)
    {
        if (body.Length is 0 or > 256 * 1024 || ProtoWire.ReadFields(body) is not { } root ||
            ReadMessage(root, 1) is not { } stable)
            return null;

        return DecodeMounts(stable.Where(field => field.Number == 1).Take(501), 500);
    }

    public static IReadOnlyList<Guid>? DecodeStableTransfer(byte[] body, bool intoStable)
    {
        if (body.Length is 0 or > 64 * 1024 || ProtoWire.ReadFields(body) is not { } root)
            return null;

        List<ProtoField>? results = intoStable ? root : ReadMessage(root, 1);
        if (results is null)
            return null;

        var ids = new HashSet<Guid>();
        foreach (var entry in results.Where(field => field.Number == 1))
        {
            if (entry.Bytes is not { } bytes || ProtoWire.ReadFields(bytes) is not { } result ||
                !TryReadId(result, out Guid id) ||
                !result.Any(field => field.Number == 2) ||
                !TryNumber(result, 2, out int status) || status < 0)
                return null;
            if (status == 0)
                ids.Add(id);
        }
        return ids.ToArray();
    }

    private static PaddockReading? DecodeEnclosure(List<ProtoField> enclosure)
    {
        var activeField = enclosure.Where(field => field.Number == 1).ToArray();
        List<ulong>? active = activeField switch
        {
            [] => [],
            [{ Bytes: { } bytes }] => ProtoWire.ReadPackedVarints(bytes),
            _ => null
        };
        if (active is null || active.Count > 2 || active.Distinct().Count() != active.Count ||
            active.Any(value => value > (ulong)PaddockGauge.Experience))
            return null;

        var gauges = new Dictionary<PaddockGauge, PaddockGaugeReading>();
        foreach (var field in enclosure.Where(field => field.Number == 4))
        {
            if (field.Bytes is not { } bytes || ProtoWire.ReadFields(bytes) is not { } fields ||
                !TryNumber(fields, 3, out int kind) || kind is < 0 or > (int)PaddockGauge.Experience ||
                !TryNumber(fields, 2, out int fuel) || fuel < 0)
                return null;

            var gauge = (PaddockGauge)kind;
            if (!gauges.TryAdd(gauge, new(gauge, active.Contains((ulong)gauge), fuel)))
                return null;
        }
        if (gauges.Count != Enum.GetValues<PaddockGauge>().Length)
            return null;

        var mounts = DecodeMounts(enclosure.Where(field => field.Number == 5), 500);
        return mounts is null ? null : new(gauges.Values.OrderBy(reading => reading.Gauge).ToArray(), mounts);
    }

    private static IReadOnlyList<PaddockMountReading>? DecodeMounts(IEnumerable<ProtoField> entries, int limit)
    {
        var mounts = new Dictionary<Guid, PaddockMountReading>();
        foreach (var field in entries)
        {
            if (mounts.Count >= limit ||
                field.Bytes is not { } bytes || ProtoWire.ReadFields(bytes) is not { } fields ||
                !TryReadId(fields, out Guid id) ||
                ReadMessage(fields, 2) is not { } stats ||
                !TryNumber(stats, 8, out int level) || level is < 1 or > 200 ||
                !TryNumber(stats, 13, out int experience) || experience < 0 ||
                !TryNumber(stats, 5, out int serenity) || serenity is < -5_000 or > 5_000 ||
                !TryReadMaturity(stats, out int? maturity) ||
                !mounts.TryAdd(id, new(id, level, experience, serenity, maturity)))
                return null;
        }

        return mounts.Values.ToArray();
    }

    private static bool TryReadId(List<ProtoField> fields, out Guid id)
    {
        id = Guid.Empty;
        return ReadBytes(fields, 1) is { Length: 36 } identifier &&
            Guid.TryParseExact(ProtoWire.TryUtf8(identifier), "D", out id) && id != Guid.Empty;
    }

    private static bool TryReadMaturity(List<ProtoField> fields, out int? maturity)
    {
        maturity = null;
        foreach (var field in fields.Where(field => field.Number == 6))
        {
            if (field.Bytes is not { } bytes || ProtoWire.ReadFields(bytes) is not { } stat ||
                !TryNumber(stat, 1, out int kind))
                return false;
            if (kind != 1)
                continue;
            if (maturity.HasValue || !TryNumber(stat, 2, out int value) || value is < 0 or > 20_000)
                return false;
            maturity = value;
        }
        return true;
    }

    private static byte[]? ReadBytes(List<ProtoField> fields, int number) =>
        fields.Where(field => field.Number == number).ToArray() switch
        {
            [{ Bytes: { } bytes }] => bytes,
            _ => null
        };

    private static List<ProtoField>? ReadMessage(List<ProtoField> fields, int number) =>
        ReadBytes(fields, number) is { } bytes ? ProtoWire.ReadFields(bytes) : null;

    private static bool TryNumber(List<ProtoField> fields, int number, out int value)
    {
        value = 0;
        var matches = fields.Where(field => field.Number == number).ToArray();
        if (matches is [])
            return true; // Protobuf wire default.
        if (matches is not [{ WireType: ProtoWireType.Varint, Varint: var raw }] ||
            unchecked((long)raw) is < int.MinValue or > int.MaxValue)
            return false;
        value = unchecked((int)raw);
        return true;
    }
}
