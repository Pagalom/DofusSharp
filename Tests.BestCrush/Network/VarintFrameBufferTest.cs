using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class VarintFrameBufferTest
{
    [Fact]
    public void RetainsSplitLengthPrefixAndPayloadUntilTheWholeFrameArrives()
    {
        VarintFrameBuffer buffer = new();
        buffer.TryReadFrame(out byte[] empty).Should().BeFalse();
        empty.Should().BeEmpty();
        byte[] payload = Enumerable.Repeat((byte)0x42, 130).ToArray();
        buffer.Append([0x82]);
        buffer.TryReadFrame(out _).Should().BeFalse();
        buffer.Append(Message([0x01], payload[..129]));
        buffer.TryReadFrame(out _).Should().BeFalse();
        buffer.Append(payload[129..]);
        buffer.TryReadFrame(out byte[] frame).Should().BeTrue();
        frame.Should().Equal(payload);
        buffer.TryReadFrame(out _).Should().BeFalse();
    }

    [Fact]
    public void ReadsOneCoalescedFrameAtATimeAndRetainsIncompleteNextFrame()
    {
        VarintFrameBuffer buffer = new();
        buffer.Append([2, 10, 11, 1, 12, 2, 13]);
        buffer.TryReadFrame(out byte[] first).Should().BeTrue();
        first.Should().Equal(10, 11);
        buffer.TryReadFrame(out byte[] second).Should().BeTrue();
        second.Should().Equal(12);
        buffer.TryReadFrame(out _).Should().BeFalse();
        buffer.Append([14]);
        buffer.TryReadFrame(out byte[] third).Should().BeTrue();
        third.Should().Equal(13, 14);
    }

    [Fact]
    public void ZeroLengthDiscardsOneByteAndRequiresAnotherCallToReadFollowingFrame()
    {
        VarintFrameBuffer buffer = new();
        buffer.Append([0, 1, 42]);
        buffer.TryReadFrame(out byte[] skipped).Should().BeFalse();
        skipped.Should().BeEmpty();
        buffer.TryReadFrame(out byte[] frame).Should().BeTrue();
        frame.Should().Equal(42);
    }

    [Fact]
    public void LengthAboveSixteenMiBDiscardsExactlyOnePrefixByte()
    {
        VarintFrameBuffer buffer = new();
        // Length 16777217 is rejected; the remaining 80 80 08 prefix is 131072.
        buffer.Append([0x81, 0x80, 0x80, 0x08]);
        buffer.TryReadFrame(out _).Should().BeFalse();
        byte[] payload = new byte[131072];
        buffer.Append(payload);
        buffer.TryReadFrame(out byte[] frame).Should().BeTrue();
        frame.Should().Equal(payload);
    }

    [Fact]
    public void TcpFragmentsProduceAnkamaMessagesInWireOrderWithoutNpcap()
    {
        byte[] market = Market(42, MarketOffer(501, 42, 12, 100, 900, 8000));
        byte[] item = Bytes(2, Bytes(5, Item(8001, 42, 1, Stat(118, 10))));
        byte[] crush = Bytes(1, CrushRow(8001, 3.71f, Rune(100, 2)));
        byte[] wire = Message(Frame(Any("jzn", market)), Frame(Any("kdb", item)), Frame(Any("kci", crush)));
        TcpReassembler stream = new();
        List<AnkamaAny> received = [];

        Feed(1000, wire[..5]);
        Feed(1020, wire[20..]);
        received.Should().BeEmpty();
        Feed(1005, wire[5..20]);
        Feed(1000, wire); // Retransmission must not duplicate frames.

        received.Select(message => message.Key).Should().Equal("jzn", "kdb", "kci");
        received.Select(message => Convert.ToHexString(message.Body))
            .Should().Equal(Convert.ToHexString(market), Convert.ToHexString(item), Convert.ToHexString(crush));
        SemanticDecoders.TryDecodeMarket(received[0].Body)!.ItemId.Should().Be(42);
        SemanticDecoders.TryDecodeItemDetail(received[1].Body)!.ItemUid.Should().Be(8001);
        SemanticDecoders.TryDecodeCrush(received[2].Body)!.Lines.Single().CoefficientPercent.Should().Be(371f);

        void Feed(uint sequence, byte[] bytes)
        {
            foreach (ReadOnlyMemory<byte> part in stream.Push(sequence, bytes))
            {
                stream.FrameBuffer.Append(part.Span);
                while (stream.FrameBuffer.TryReadFrame(out byte[] frame))
                    received.Add(AnkamaFrameDecoder.FindAny(frame)!);
            }
        }
    }
}
