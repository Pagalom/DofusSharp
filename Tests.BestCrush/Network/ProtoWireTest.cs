using BestCrush.NetworkProbe.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class ProtoWireTest
{
    [Fact]
    public void ReadsAllSupportedWireTypesFromLiteralBytesInFieldOrder()
    {
        // f1 varint 150; f2 fixed64; f3 "abc"; f4 fixed32 float 1.
        byte[] bytes = Convert.FromHexString("0896011108070605040302011A03616263250000803F");

        var fields = ProtoWire.ReadFields(bytes)!;

        fields.Select(field => field.Number).Should().Equal(1, 2, 3, 4);
        fields.Select(field => field.WireType).Should().Equal(
            ProtoWireType.Varint, ProtoWireType.Fixed64, ProtoWireType.LengthDelimited, ProtoWireType.Fixed32);
        fields[0].Varint.Should().Be(150);
        fields[1].Fixed64.Should().Be(0x0102030405060708UL);
        fields[2].Bytes.Should().Equal((byte)'a', (byte)'b', (byte)'c');
        ProtoWire.ToFloat(fields[3].Fixed32).Should().Be(1f);
    }

    [Fact]
    public void KeepsDuplicateFieldsAndAcceptsEmptyMessageAndEmptyBytes()
    {
        ProtoWire.ReadFields([]).Should().BeEmpty();
        var fields = ProtoWire.ReadFields(Convert.FromHexString("080108021200"))!;
        fields.Select(field => field.Number).Should().Equal(1, 1, 2);
        fields.Take(2).Select(field => field.Varint).Should().Equal(1UL, 2UL);
        fields[2].Bytes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("00")]
    [InlineData("0B")]
    [InlineData("0D010203")]
    [InlineData("110102")]
    [InlineData("1204616263")]
    [InlineData("12FFFFFFFF0F")]
    [InlineData("0880")]
    public void RejectsUnsupportedWireTypesAndTruncatedOrInvalidFields(string hex)
    {
        ProtoWire.ReadFields(Convert.FromHexString(hex)).Should().BeNull();
    }

    [Fact]
    public void OversizedFieldNumberStillThrowsCheckedOverflow()
    {
        Action read = () => ProtoWire.ReadFields(Varint(ulong.MaxValue));
        read.Should().Throw<OverflowException>();
    }

    [Fact]
    public void VarintReadsAtOffsetAndLeavesOffsetAfterConsumedBytesOnFailure()
    {
        byte[] bytes = Convert.FromHexString("00960180");
        int offset = 1;
        ProtoWire.TryReadVarint(bytes, ref offset, out ulong value).Should().BeTrue();
        value.Should().Be(150);
        offset.Should().Be(3);
        ProtoWire.TryReadVarint(bytes, ref offset, out _).Should().BeFalse();
        offset.Should().Be(4);
    }

    [Fact]
    public void PackedVarintsKeepZeroAndUnsignedLimitAndRejectAnIncompleteTail()
    {
        ProtoWire.ReadPackedVarints(Convert.FromHexString("009601FFFFFFFFFFFFFFFFFF01"))!
            .Should().Equal(0UL, 150UL, ulong.MaxValue);
        ProtoWire.ReadPackedVarints([]).Should().BeEmpty();
        ProtoWire.ReadPackedVarints(Convert.FromHexString("0180")).Should().BeNull();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("41090A0D42", "A\t\n\rB")]
    [InlineData("00", null)]
    [InlineData("1B", null)]
    [InlineData("FF", "\uFFFD")]
    public void Utf8KeepsCurrentControlFilteringAndReplacementFallback(string hex, string? expected)
    {
        ProtoWire.TryUtf8(Convert.FromHexString(hex)).Should().Be(expected);
    }
}
