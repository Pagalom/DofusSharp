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
    public void OverlappingQueuedSegmentCurrentlyRemainsBlockedAfterGapIsFilled()
    {
        // Existing limitation, intentionally characterized rather than fixed (see A3 notes).
        TcpReassembler stream = new();
        Flatten(stream.Push(100, [1, 2])).Should().Equal(1, 2);
        stream.Push(104, [5, 6, 7, 8]).Should().BeEmpty();
        Flatten(stream.Push(102, [3, 4, 5, 6])).Should().Equal(3, 4, 5, 6);
        stream.Push(108, [9, 10]).Should().BeEmpty();
        // Only a new segment beginning at 106 unblocks the missing tail.
        Flatten(stream.Push(106, [7, 8])).Should().Equal(7, 8, 9, 10);
    }

    private static byte[] Flatten(IEnumerable<ReadOnlyMemory<byte>> segments) =>
        segments.SelectMany(segment => segment.ToArray()).ToArray();
}
