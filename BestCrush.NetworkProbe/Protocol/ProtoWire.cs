using System.Buffers.Binary;
using System.Text;

namespace BestCrush.NetworkProbe.Protocol;

internal enum ProtoWireType : int
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    Fixed32 = 5
}

internal sealed record ProtoField(
    int Number,
    ProtoWireType WireType,
    ulong Varint = 0,
    byte[]? Bytes = null,
    uint Fixed32 = 0,
    ulong Fixed64 = 0);

internal static class ProtoWire
{
    public static List<ProtoField>? ReadFields(ReadOnlySpan<byte> data)
    {
        List<ProtoField> result = new();
        int offset = 0;

        while (offset < data.Length)
        {
            if (!TryReadVarint(data, ref offset, out ulong tag) || tag == 0)
                return null;

            int number = checked((int)(tag >> 3));
            ProtoWireType wireType = (ProtoWireType)(tag & 7);

            switch (wireType)
            {
                case ProtoWireType.Varint:
                    if (!TryReadVarint(data, ref offset, out ulong v))
                        return null;
                    result.Add(new ProtoField(number, wireType, Varint: v));
                    break;

                case ProtoWireType.Fixed64:
                    if (offset + 8 > data.Length)
                        return null;
                    ulong f64 = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
                    offset += 8;
                    result.Add(new ProtoField(number, wireType, Fixed64: f64));
                    break;

                case ProtoWireType.LengthDelimited:
                    if (!TryReadVarint(data, ref offset, out ulong len64) || len64 > int.MaxValue)
                        return null;
                    int len = (int)len64;
                    if (offset + len > data.Length)
                        return null;
                    byte[] bytes = data.Slice(offset, len).ToArray();
                    offset += len;
                    result.Add(new ProtoField(number, wireType, Bytes: bytes));
                    break;

                case ProtoWireType.Fixed32:
                    if (offset + 4 > data.Length)
                        return null;
                    uint f32 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
                    offset += 4;
                    result.Add(new ProtoField(number, wireType, Fixed32: f32));
                    break;

                default:
                    return null;
            }
        }

        return result;
    }

    public static List<ulong>? ReadPackedVarints(byte[] bytes)
    {
        List<ulong> values = new();
        int offset = 0;

        while (offset < bytes.Length)
        {
            if (!TryReadVarint(bytes, ref offset, out ulong value))
                return null;
            values.Add(value);
        }

        return values;
    }

    public static bool TryReadVarint(ReadOnlySpan<byte> data, ref int offset, out ulong value)
    {
        value = 0;
        int shift = 0;

        for (int count = 0; count < 10 && offset < data.Length; count++)
        {
            byte b = data[offset++];
            value |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                return true;

            shift += 7;
        }

        return false;
    }

    public static string? TryUtf8(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        try
        {
            string value = Encoding.UTF8.GetString(bytes);
            return value.All(c => !char.IsControl(c) || c is '\r' or '\n' or '\t')
                ? value
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static float ToFloat(uint raw) => BitConverter.Int32BitsToSingle(unchecked((int)raw));
}
