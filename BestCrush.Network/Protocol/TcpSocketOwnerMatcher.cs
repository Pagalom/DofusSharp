using System.Net;

namespace BestCrush.Network.Protocol;

/// <summary>
/// Local TCP tuple. Values are held transiently in RAM and must never be
/// printed, serialized, logged or used as account/server identifiers.
/// </summary>
public readonly record struct TcpSocketTuple(
    IPAddress LocalAddress, ushort LocalPort,
    IPAddress RemoteAddress, ushort RemotePort)
{
    /// <summary>
    /// Orient an observed packet relative to the local Dofus client.
    /// Reject ambiguous or unrelated traffic instead of guessing.
    /// </summary>
    public static TcpSocketTuple? FromPacket(
        IPAddress source, int sourcePort,
        IPAddress destination, int destinationPort, int gamePort)
    {
        if (source is null || destination is null ||
            gamePort is < 1 or > ushort.MaxValue ||
            sourcePort is < 1 or > ushort.MaxValue ||
            destinationPort is < 1 or > ushort.MaxValue ||
            source.AddressFamily != destination.AddressFamily)
            return null;

        if (sourcePort == gamePort && destinationPort != gamePort)
            return new TcpSocketTuple(destination, (ushort)destinationPort,
                                      source, (ushort)sourcePort);
        if (destinationPort == gamePort && sourcePort != gamePort)
            return new TcpSocketTuple(source, (ushort)sourcePort,
                                      destination, (ushort)destinationPort);
        return null;
    }
}

public readonly record struct TcpSocketOwner(TcpSocketTuple Socket, int ProcessId);

/// <summary>
/// Exact four-tuple PID resolution. Absence and collisions fail closed;
/// a PID/window association alone is NOT an account identity.
/// </summary>
public static class TcpSocketOwnerMatcher
{
    public static int? FindUniqueOwner(
        TcpSocketTuple captured,
        IReadOnlyList<TcpSocketOwner> establishedSockets)
    {
        ArgumentNullException.ThrowIfNull(establishedSockets);
        int? owner = null;
        foreach (TcpSocketOwner candidate in establishedSockets)
        {
            if (candidate.Socket != captured)
                continue;

            if (candidate.ProcessId <= 0 || owner.HasValue)
                return null;

            owner = candidate.ProcessId;
        }
        return owner;
    }

    public static int? FindUniqueWindowSlot(
        int? ownerPid, IReadOnlyList<(int ProcessId, int WindowSlot)> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (ownerPid is null or <= 0)
            return null;

        int? slot = null;
        foreach ((int processId, int windowSlot) in windows)
        {
            if (processId != ownerPid)
                continue;

            if (windowSlot <= 0 || slot.HasValue)
                return null;

            slot = windowSlot;
        }
        return slot;
    }
}
