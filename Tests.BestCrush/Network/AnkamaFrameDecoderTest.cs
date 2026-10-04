using System.Text;
using BestCrush.NetworkProbe.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class AnkamaFrameDecoderTest
{
    [Fact]
    public void DecodesLiteralAnyWithoutInterpretingItsBody()
    {
        // type.ankama.com/jzn, body = f1 varint 42.
        byte[] bytes = Convert.FromHexString("0A13747970652E616E6B616D612E636F6D2F6A7A6E1202082A");
        AnkamaAny result = AnkamaFrameDecoder.FindAny(bytes)!;
        result.TypeUrl.Should().Be("type.ankama.com/jzn");
        result.Key.Should().Be("jzn");
        result.Body.Should().Equal(0x08, 0x2A);

        AnkamaFrameDecoder.FindAny(Any("kci", [0xFF]))!.Body.Should().Equal(0xFF);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void SearchesNestedEnvelopesOnlyThroughDepthFive(int wrappers, bool found)
    {
        byte[] bytes = Any("kdb", []);
        for (int depth = 0; depth < wrappers; depth++)
            bytes = Bytes(7, bytes);

        var result = AnkamaFrameDecoder.FindAny(bytes);
        (result is not null).Should().Be(found);
        if (found)
        {
            result!.Key.Should().Be("kdb");
            result.Body.Should().BeEmpty();
        }
    }

    [Fact]
    public void RootEnvelopeWinsBeforeNestedEnvelopeAndSiblingsKeepTheirOrder()
    {
        byte[] nested = Bytes(7, Any("kci", [1]));
        AnkamaFrameDecoder.FindAny(Message(nested, Any("jzn", [2])))!.Key.Should().Be("jzn");
        var result = AnkamaFrameDecoder.FindAny(Message(nested, Bytes(8, Any("kdb", [3]))))!;
        result.Key.Should().Be("kci");
        result.Body.Should().Equal(1);
    }

    [Theory]
    [InlineData("Type.ankama.com/jzn")]
    [InlineData("type.example.com/jzn")]
    public void PrefixIsMatchedExactly(string url)
    {
        AnkamaFrameDecoder.FindAny(Message(Bytes(1, Encoding.UTF8.GetBytes(url)), Bytes(2, [])))
            .Should().BeNull();
    }

    [Fact]
    public void RequiresBodyAndRejectsMalformedOuterMessageEvenWithAValidAny()
    {
        AnkamaFrameDecoder.FindAny(Bytes(1, Encoding.UTF8.GetBytes("type.ankama.com/jzn")))
            .Should().BeNull();
        AnkamaFrameDecoder.FindAny(Message(Any("jzn", []), [0]))
            .Should().BeNull();
    }
}
