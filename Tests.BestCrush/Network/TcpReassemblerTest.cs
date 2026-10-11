using BestCrush.Network.Capture;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class TcpReassemblerTest
{
    [Fact]
    public void EmptyPayloadDoesNotChooseTheInitialSequence()
    {
        TcpReassembler stream = new();
        stream.Push(100, []).Should().BeEmpty();
        Flatten(stream.Push(200, [1, 2])).Should().Equal(1, 2);
    }

    [Fact]
    public void WaitsForAGapAndThenReleasesQueuedSegmentsInSequenceOrder()
    {
        TcpReassembler stream = new();
        Flatten(stream.Push(100, [1, 2])).Should().Equal(1, 2);
        stream.Push(106, [7, 8]).Should().BeEmpty();
        stream.Push(104, [5, 6]).Should().BeEmpty();
        Flatten(stream.Push(102, [3, 4])).Should().Equal(3, 4, 5, 6, 7, 8);
    }

    [Fact]
    public void IgnoresRetransmissionsAndTrimsOverlapWithAlreadyDeliveredBytes()
    {
        TcpReassembler stream = new();
        Flatten(stream.Push(100, [1, 2, 3])).Should().Equal(1, 2, 3);
        stream.Push(100, [9, 9, 9]).Should().BeEmpty();
        Flatten(stream.Push(101, [2, 3, 4, 5])).Should().Equal(4, 5);
    }

    [Fact]
    public void FirstPendingSegmentAtAGivenSequenceWins()
    {
        TcpReassembler stream = new();
        Flatten(stream.Push(100, [1])).Should().Equal(1);
        stream.Push(102, [3]).Should().BeEmpty();
        stream.Push(102, [99, 100]).Should().BeEmpty();
        Flatten(stream.Push(101, [2])).Should().Equal(2, 3);
    }

    [Fact]
    public void OverlappingQueuedSegmentContinuesAfterGapIsFilled()
    {
        TcpReassembler stream = new();
        Flatten(stream.Push(100, [1, 2])).Should().Equal(1, 2);
        stream.Push(104, [5, 6, 7, 8]).Should().BeEmpty();

        // The arriving segment overlaps the FIRST HALF of the pending one.
        // The pending tail is immediately released without an extra packet.
        Flatten(stream.Push(102, [3, 4, 5, 6]))
            .Should().Equal(3, 4, 5, 6, 7, 8);
        Flatten(stream.Push(108, [9, 10]))
            .Should().Equal(9, 10);
    }

    [Fact]
    public void MultipleOverlappingQueuedSegmentsEmitEachByteOnlyOnce()
    {
        TcpReassembler stream = new();
        Flatten(stream.Push(100, [0, 1])).Should().Equal(0, 1);

        stream.Push(106, [6, 7, 8]).Should().BeEmpty();
        stream.Push(104, [4, 5, 6, 7]).Should().BeEmpty();

        Flatten(stream.Push(102, [2, 3, 4, 5]))
            .Should().Equal(2, 3, 4, 5, 6, 7, 8);
        Flatten(stream.Push(109, [9]))
            .Should().Equal(9);
    }

    [Fact]
    public void FullyCoveredQueuedSegmentsAreDiscardedWithoutBlocking()
    {
        TcpReassembler stream = new();
        Flatten(stream.Push(100, [1, 2])).Should().Equal(1, 2);

        stream.Push(104, [5, 6]).Should().BeEmpty();
        stream.Push(106, [7, 8]).Should().BeEmpty();

        Flatten(stream.Push(102, [3, 4, 5, 6, 7, 8]))
            .Should().Equal(3, 4, 5, 6, 7, 8);
        Flatten(stream.Push(108, [9]))
            .Should().Equal(9);
    }

    [Fact]
    public void OverlappingQueuedSegmentsKeepVarintFramesReadable()
    {
        TcpReassembler stream = new();

        AppendContiguous(stream, 100, [2, 10]);
        AppendContiguous(stream, 104, [20, 2, 30]);
        AppendContiguous(stream, 102, [11, 1, 20]);

        stream.FrameBuffer.TryReadFrame(out byte[] first)
            .Should().BeTrue();
        first.Should().Equal(10, 11);

        stream.FrameBuffer.TryReadFrame(out byte[] second)
            .Should().BeTrue();
        second.Should().Equal(20);

        stream.FrameBuffer.TryReadFrame(out _)
            .Should().BeFalse();

        AppendContiguous(stream, 107, [31]);
        stream.FrameBuffer.TryReadFrame(out byte[] third)
            .Should().BeTrue();
        third.Should().Equal(30, 31);

        stream.FrameBuffer.TryReadFrame(out _)
            .Should().BeFalse();
    }

    [Fact]
    public void GapQueueRetainsExactByteCountAndDrainsToZero()
    {
        TcpReassembler stream = new(maximumPendingBytes: 4);

        Flatten(stream.Push(100, [1, 2])).Should().Equal(1, 2);
        stream.PendingBytes.Should().Be(0);
        stream.Push(104, [5, 6, 7, 8]).Should().BeEmpty();
        stream.PendingSegments.Should().Be(1);
        stream.PendingBytes.Should().Be(4);

        Flatten(stream.Push(102, [3, 4]))
            .Should().Equal(3, 4, 5, 6, 7, 8);
        stream.PendingSegments.Should().Be(0);
        stream.PendingBytes.Should().Be(0);
    }

    [Fact]
    public void OutOfOrderPendingByteLimitRejectsNewSegmentWithoutGrowingQueue()
    {
        TcpReassembler stream = new(maximumPendingBytes: 4);

        Flatten(stream.Push(100, [1])).Should().Equal(1);
        stream.Push(110, [10, 11, 12]).Should().BeEmpty();
        stream.PendingBytes.Should().Be(3);

        Action overflow = () => Flatten(stream.Push(120, [20, 21]));
        overflow.Should().Throw<TcpReassemblyLimitExceededException>();
        stream.PendingBytes.Should().Be(3);
        stream.PendingSegments.Should().Be(1);
    }

    [Fact]
    public void PendingSegmentCountLimitRejectsDistinctSegmentsButNotDuplicates()
    {
        TcpReassembler stream = new(maximumPendingSegments: 2);

        Flatten(stream.Push(100, [1])).Should().Equal(1);
        stream.Push(104, [5]).Should().BeEmpty();
        stream.Push(106, [7]).Should().BeEmpty();
        stream.Push(104, [99, 98]).Should().BeEmpty();

        stream.PendingSegments.Should().Be(2);
        stream.PendingBytes.Should().Be(2);

        Action overflow = () => Flatten(stream.Push(108, [9]));
        overflow.Should().Throw<TcpReassemblyLimitExceededException>();
        stream.PendingSegments.Should().Be(2);
        stream.PendingBytes.Should().Be(2);

        Flatten(stream.Push(101, [2, 3, 4, 5, 6, 7]))
            .Should().Equal(2, 3, 4, 5, 6, 7);
        stream.PendingSegments.Should().Be(0);
        stream.PendingBytes.Should().Be(0);
    }

    [Fact]
    public void LargeInOrderSegmentIsNotMistakenForIndefinitelyPendingData()
    {
        TcpReassembler stream = new(maximumPendingBytes: 2);

        Flatten(stream.Push(100, [1, 2, 3, 4, 5]))
            .Should().Equal(1, 2, 3, 4, 5);

        stream.PendingSegments.Should().Be(0);
        stream.PendingBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void InvalidPendingLimitsAreRejected(int bytes, int segments)
    {
        Action create = () => new TcpReassembler(
            maximumPendingBytes: bytes,
            maximumPendingSegments: segments);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static void AppendContiguous(
        TcpReassembler stream,
        uint sequence,
        byte[] payload)
    {
        foreach (ReadOnlyMemory<byte> segment in stream.Push(sequence, payload))
            stream.FrameBuffer.Append(segment.Span);
    }

    private static byte[] Flatten(IEnumerable<ReadOnlyMemory<byte>> segments) =>
        segments.SelectMany(segment => segment.ToArray()).ToArray();
}
