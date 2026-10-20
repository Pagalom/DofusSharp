using System.Net;
using BestCrush.Network.Protocol;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class TcpSocketOwnerMatcherTest
{
    private static readonly IPAddress Client = IPAddress.Parse("192.0.2.10");
    private static readonly IPAddress Server = IPAddress.Parse("198.51.100.20");
    private static readonly TcpSocketTuple Socket =
        new(Client, 49321, Server, 5555);

    [Fact]
    public void BothPacketDirectionsNormalizeToTheSameSocket()
    {
        TcpSocketTuple.FromPacket(Client, 49321, Server, 5555, 5555)
            .Should().Be(Socket);
        TcpSocketTuple.FromPacket(Server, 5555, Client, 49321, 5555)
            .Should().Be(Socket);
    }

    [Fact]
    public void AmbiguousOrUnrelatedPacketCannotProduceSocket()
    {
        TcpSocketTuple.FromPacket(Client, 5555, Server, 5555, 5555)
            .Should().BeNull();
        TcpSocketTuple.FromPacket(Client, 1234, Server, 5678, 5555)
            .Should().BeNull();
    }

    [Fact]
    public void ExactFourTupleMatchResolvesPidThenUniqueWindow()
    {
        TcpSocketOwner[] owners =
        [
            new(Socket with { LocalPort = 49322 }, 1200),
            new(Socket, 1300)
        ];
        int? pid = TcpSocketOwnerMatcher.FindUniqueOwner(Socket, owners);
        pid.Should().Be(1300);
        TcpSocketOwnerMatcher.FindUniqueWindowSlot(pid,
        [
            (1200, 1), (1300, 2)
        ]).Should().Be(2);
    }

    [Fact]
    public void SameRemotePortButDifferentLocalSocketNeverMatches()
    {
        TcpSocketOwnerMatcher.FindUniqueOwner(Socket,
        [
            new(Socket with { LocalPort = 49322 }, 1300),
            new(Socket with { RemoteAddress = IPAddress.Parse("203.0.113.5") }, 1400)
        ]).Should().BeNull();
    }

    [Fact]
    public void DuplicateOwnerEntriesOrMultipleWindowsFailClosed()
    {
        TcpSocketOwnerMatcher.FindUniqueOwner(Socket,
        [
            new(Socket, 1300), new(Socket, 1400)
        ]).Should().BeNull();

        TcpSocketOwnerMatcher.FindUniqueWindowSlot(1300,
        [
            (1300, 1), (1300, 2)
        ]).Should().BeNull();
    }

    [Fact]
    public void MissingWindowOrSocketFailsClosed()
    {
        TcpSocketOwnerMatcher.FindUniqueOwner(Socket, [])
            .Should().BeNull();
        TcpSocketOwnerMatcher.FindUniqueWindowSlot(null, [(1300, 1)])
            .Should().BeNull();
        TcpSocketOwnerMatcher.FindUniqueWindowSlot(1400, [(1300, 1)])
            .Should().BeNull();
    }
}
