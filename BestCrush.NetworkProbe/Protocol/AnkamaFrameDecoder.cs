namespace BestCrush.NetworkProbe.Protocol;

internal sealed record AnkamaAny(string TypeUrl, string Key, byte[] Body);

internal static class AnkamaFrameDecoder
{
    private const string Prefix = "type.ankama.com/";

    public static AnkamaAny? FindAny(byte[] frame) => FindAnyRecursive(frame, 0);

    private static AnkamaAny? FindAnyRecursive(byte[] data, int depth)
    {
        if (depth > 5)
            return null;

        List<ProtoField>? fields = ProtoWire.ReadFields(data);
        if (fields is null)
            return null;

        ProtoField? typeField = fields.FirstOrDefault(f =>
            f.Number == 1 &&
            f.WireType == ProtoWireType.LengthDelimited &&
            f.Bytes is not null &&
            ProtoWire.TryUtf8(f.Bytes) is string s &&
            s.StartsWith(Prefix, StringComparison.Ordinal));

        if (typeField?.Bytes is not null)
        {
            string typeUrl = ProtoWire.TryUtf8(typeField.Bytes)!;
            ProtoField? body = fields.FirstOrDefault(f =>
                f.Number == 2 &&
                f.WireType == ProtoWireType.LengthDelimited &&
                f.Bytes is not null);

            if (body?.Bytes is not null)
            {
                string key = typeUrl[Prefix.Length..];
                return new AnkamaAny(typeUrl, key, body.Bytes);
            }
        }

        foreach (ProtoField field in fields)
        {
            if (field.WireType != ProtoWireType.LengthDelimited || field.Bytes is null)
                continue;

            AnkamaAny? nested = FindAnyRecursive(field.Bytes, depth + 1);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
