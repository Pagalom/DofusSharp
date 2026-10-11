using System.Buffers.Binary;
using System.Text;

namespace Tests.BestCrush.Network;

// Synthetic byte fixtures, not captured traffic. Field layouts are taken from the
// current SemanticDecoders methods; no production encoder or decoder is used here.
internal static class NetworkPayload
{
    public static byte[] Message(params byte[][] fields) => fields.SelectMany(field => field).ToArray();

    public static byte[] Varint(ulong value)
    {
        List<byte> bytes = [];
        while (value >= 128)
        {
            bytes.Add((byte)((value & 127) | 128));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    public static byte[] Unsigned(int number, ulong value) =>
        Message(Varint((ulong)number << 3), Varint(value));

    public static byte[] Bytes(int number, byte[] value) =>
        Message(Varint(((ulong)number << 3) | 2), Varint((ulong)value.Length), value);

    public static byte[] Packed(params ulong[] values) => values.SelectMany(Varint).ToArray();

    public static byte[] Float(int number, float value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(value));
        return Message(Varint(((ulong)number << 3) | 5), bytes);
    }

    public static byte[] Any(string key, byte[] body) => Message(
        Bytes(1, Encoding.UTF8.GetBytes("type.ankama.com/" + key)), Bytes(2, body));

    public static byte[] Frame(byte[] body) => Message(Varint((ulong)body.Length), body);

    // Item object: uid=f1, quantity=f2 (default 1 when absent), stats=f3, itemId=f5.
    public static byte[] Item(ulong uid, ulong itemId, ulong? quantity = null, params byte[][] stats) =>
        Message(Unsigned(1, uid), quantity.HasValue ? Unsigned(2, quantity.Value) : [],
            Message(stats.Select(stat => Bytes(3, stat)).ToArray()), Unsigned(5, itemId));

    public static byte[] Stat(ulong effectId, long value) =>
        Message(Unsigned(1, effectId), Unsigned(10, unchecked((ulong)value)));

    // Current jzn layout, as documented by TryDecodeCurrentJzn.
    public static byte[] MarketOffer(ulong offerId, ulong itemId, params ulong[] prices) =>
        Message(Unsigned(2, itemId), Unsigned(5, offerId), Bytes(6, Packed(prices)));

    public static byte[] Market(ulong itemId, params byte[][] offers) =>
        Message(Unsigned(1, itemId), Message(offers.Select(offer => Bytes(2, offer)).ToArray()));

    // Current jzs layout: root f1=offers, f2=category, f3=item ID.
    // Each offer has f1=item ID, f2=packed numeric slots, f3=opaque
    // listing reference, f4=category. These are synthetic fixtures.
    public static byte[] JzsOffer(
        ulong itemId,
        ulong offerReference,
        ulong category,
        params ulong[] slots) =>
        Message(
            Unsigned(1, itemId),
            Bytes(2, Packed(slots)),
            Unsigned(3, offerReference),
            Unsigned(4, category));

    public static byte[] JzsMarket(
        ulong itemId,
        ulong category,
        params byte[][] offers) =>
        Message(
            Message(offers.Select(offer => Bytes(1, offer)).ToArray()),
            Unsigned(2, category),
            Unsigned(3, itemId));

    // Synthetic wire fixtures for the 2026-10-11 Dofus kch/kag messages.
    // kch carries root f3 / inner f1 item: f3 quantity, f4 itemId,
    // f5 UID. kag uses row f1/f2 float fractions, f3 UID and repeated
    // f4 runes containing f1 quantity and f2 rune item ID.
    public static byte[] CurrentItemDetail(
        ulong uid, ulong itemId, ulong quantity = 1) =>
        Message(
            Bytes(3, Bytes(1, Message(
                Unsigned(3, quantity),
                Unsigned(4, itemId),
                Unsigned(5, uid)))),
            Unsigned(4, 63));

    public static byte[] CurrentCrushRune(ulong runeItemId, ulong quantity) =>
        Bytes(4, Message(
            Unsigned(1, quantity),
            Unsigned(2, runeItemId)));

    public static byte[] CurrentCrushRow(
        ulong itemUid, float coefficientFraction, params byte[][] runes) =>
        Message(
            Float(1, coefficientFraction),
            Float(2, coefficientFraction),
            Unsigned(3, itemUid),
            Message(runes));

    public static byte[] CurrentCrush(params byte[][] rows) =>
        Message(rows.Select(row => Bytes(1, row)).ToArray());

    // kci: root repeated f1 rows; row f1 runes, f2 float fraction, f3 item UID.
    public static byte[] CrushRow(ulong uid, float coefficient, params byte[][] runes) =>
        Message(Message(runes.Select(rune => Bytes(1, rune)).ToArray()), Float(2, coefficient), Unsigned(3, uid));

    public static byte[] Rune(ulong itemId, ulong quantity) => Message(Unsigned(1, itemId), Unsigned(3, quantity));
}
