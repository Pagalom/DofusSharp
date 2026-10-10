using System.Buffers.Binary;
using System.Text.Json;

namespace BestCrush.Network.Protocol;

/// <summary>
/// Translates wire field numbers from the active protocol profile into the
/// canonical protobuf layout consumed by SemanticDecoders. This keeps all
/// decoder field-number migration in protocol-map.json; domain semantics
/// and persistence rules still belong to application code.
/// </summary>
public sealed class ProtocolWireNormalizer
{
    private sealed record FieldRule(
        int Canonical,
        Scope? Children);

    private sealed record Scope(
        IReadOnlyDictionary<int, FieldRule> ByWire,
        bool IsIdentity);

    private readonly IReadOnlyDictionary<string, Scope> _layouts;

    private ProtocolWireNormalizer(IReadOnlyDictionary<string, Scope> layouts) =>
        _layouts = layouts;

    public static ProtocolWireNormalizer Empty { get; } =
        new(new Dictionary<string, Scope>(StringComparer.Ordinal));

    public static ProtocolWireNormalizer Read(
        JsonElement root,
        Func<string, string?> resolveKey)
    {
        if (root.TryGetProperty("schema_version", out JsonElement version) &&
            (version.ValueKind != JsonValueKind.Number ||
             !version.TryGetInt32(out int schemaVersion) ||
             schemaVersion != 1))
        {
            throw new InvalidDataException(
                "protocol-map.json: unsupported schema_version (expected 1).");
        }

        if (!root.TryGetProperty("field_mappings", out JsonElement mappings))
            return Empty;

        if (mappings.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                "protocol-map.json: field_mappings must be an object.");

        Dictionary<string, Scope> layouts = new(StringComparer.Ordinal);

        foreach (JsonProperty mapping in mappings.EnumerateObject())
        {
            string? key = resolveKey(mapping.Name);
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidDataException(
                    $"protocol-map.json: unknown or disabled message '{mapping.Name}'.");

            Scope scope = ReadScope(mapping.Value, mapping.Name, 0);

            if (!layouts.TryAdd(key, scope))
                throw new InvalidDataException(
                    $"protocol-map.json: two field_mappings share wire key '{key}'.");
        }

        return new ProtocolWireNormalizer(layouts);
    }

    /// <summary>
    /// Unknown keys and identity layouts preserve the input byte-for-byte.
    /// A malformed message using a nonidentity layout fails closed: no
    /// canonical bytes are delivered to business decoders.
    /// </summary>
    public byte[] Normalize(string key, byte[] body)
    {
        if (!_layouts.TryGetValue(key, out Scope? layout) ||
            layout.IsIdentity)
            return body;

        try
        {
            return TryRewrite(body, layout, 0, out byte[] rewritten)
                ? rewritten
                : [];
        }
        catch (OverflowException)
        {
            return [];
        }
    }

    private static Scope ReadScope(JsonElement node, string location, int depth)
    {
        if (depth > 8 || node.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"protocol-map.json: invalid field scope at '{location}'.");

        Dictionary<int, FieldRule> byWire = [];
        bool identity = true;

        foreach (JsonProperty entry in node.EnumerateObject())
        {
            if (!int.TryParse(entry.Name, out int canonical) ||
                canonical <= 0 || canonical > 536870911)
                throw new InvalidDataException(
                    $"protocol-map.json: invalid canonical field at '{location}.{entry.Name}'.");

            int wire;
            Scope? children = null;

            if (entry.Value.ValueKind == JsonValueKind.Number)
            {
                wire = entry.Value.GetInt32();
            }
            else if (entry.Value.ValueKind == JsonValueKind.Object &&
                     entry.Value.TryGetProperty("wire", out JsonElement source) &&
                     source.ValueKind == JsonValueKind.Number)
            {
                wire = source.GetInt32();

                if (entry.Value.TryGetProperty("fields", out JsonElement nested))
                    children = ReadScope(nested, location + "." + entry.Name, depth + 1);
            }
            else
            {
                throw new InvalidDataException(
                    $"protocol-map.json: invalid field mapping at '{location}.{entry.Name}'.");
            }

            if (wire <= 0 || wire > 536870911 || !byWire.TryAdd(
                    wire, new FieldRule(canonical, children)))
                throw new InvalidDataException(
                    $"protocol-map.json: duplicate/invalid wire field at '{location}.{entry.Name}'.");

            identity &= canonical == wire && (children?.IsIdentity ?? true);
        }

        return new Scope(byWire, identity);
    }

    private static bool TryRewrite(
        byte[] bytes,
        Scope scope,
        int depth,
        out byte[] rewritten)
    {
        rewritten = [];

        if (depth > 8)
            return false;

        if (scope.IsIdentity)
        {
            rewritten = bytes;
            return true;
        }

        List<ProtoField>? fields = ProtoWire.ReadFields(bytes);
        if (fields is null)
            return false;

        List<byte> output = new(bytes.Length);

        foreach (ProtoField field in fields)
        {
            bool isMapped = scope.ByWire.TryGetValue(
                field.Number, out FieldRule? rule);
            int canonical = isMapped ? rule!.Canonical : field.Number;

            // A declared nested scope requires a length-delimited message,
            // never packed varints or arbitrary bytes.
            if (rule?.Children is not null &&
                (field.WireType != ProtoWireType.LengthDelimited ||
                 field.Bytes is null))
                return false;

            WriteVarint(output, ((ulong)canonical << 3) | (uint)field.WireType);

            switch (field.WireType)
            {
                case ProtoWireType.Varint:
                    WriteVarint(output, field.Varint);
                    break;

                case ProtoWireType.Fixed32:
                    Span<byte> f32 = stackalloc byte[4];
                    BinaryPrimitives.WriteUInt32LittleEndian(f32, field.Fixed32);
                    output.AddRange(f32.ToArray());
                    break;

                case ProtoWireType.Fixed64:
                    Span<byte> f64 = stackalloc byte[8];
                    BinaryPrimitives.WriteUInt64LittleEndian(f64, field.Fixed64);
                    output.AddRange(f64.ToArray());
                    break;

                case ProtoWireType.LengthDelimited:
                    byte[] payload = field.Bytes ?? [];

                    if (rule?.Children is not null &&
                        !TryRewrite(
                            payload,
                            rule.Children,
                            depth + 1,
                            out payload))
                        return false;

                    WriteVarint(output, (ulong)payload.Length);
                    output.AddRange(payload);
                    break;

                default:
                    return false;
            }
        }

        rewritten = output.ToArray();
        return true;
    }

    private static void WriteVarint(List<byte> bytes, ulong value)
    {
        while (value >= 128)
        {
            bytes.Add((byte)((value & 0x7f) | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
    }
}
