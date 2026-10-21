namespace BestCrush.Network.Capture;

/// <summary>
/// Anonymous, bounded-size counters for deciding WHERE a passive network
/// identity probe stopped seeing traffic. Never stores endpoints, packets,
/// player references, identifiers, or message payloads.
/// </summary>
public sealed class CharacterProbeTrafficDiagnostics
{
    private long _callbacks;
    private long _dofusTcp;
    private long _payloadPackets;
    private long _opened;
    private long _decodedMessages;
    private long _ksc;
    private long _ksb;
    private long _errors;

    public void PacketArrived() => Interlocked.Increment(ref _callbacks);

    public void DofusTcpPacket(int payloadBytes)
    {
        Interlocked.Increment(ref _dofusTcp);
        if (payloadBytes > 0)
            Interlocked.Increment(ref _payloadPackets);
    }

    public void EventsObserved(IReadOnlyList<CharacterCaptureEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (CharacterCaptureEvent entry in events)
        {
            if (entry.Kind == CharacterCaptureEventKind.Opened)
                Interlocked.Increment(ref _opened);

            if (entry.Kind != CharacterCaptureEventKind.Message)
                continue;

            Interlocked.Increment(ref _decodedMessages);
            if (entry.Direction != CharacterCaptureDirection.ServerToClient)
                continue;

            if (entry.Opcode == "ksc")
                Interlocked.Increment(ref _ksc);
            else if (entry.Opcode == "ksb")
                Interlocked.Increment(ref _ksb);
        }
    }

    public void PacketError() => Interlocked.Increment(ref _errors);

    public CharacterProbeTrafficSnapshot Snapshot() => new(
        Interlocked.Read(ref _callbacks),
        Interlocked.Read(ref _dofusTcp),
        Interlocked.Read(ref _payloadPackets),
        Interlocked.Read(ref _opened),
        Interlocked.Read(ref _decodedMessages),
        Interlocked.Read(ref _ksc),
        Interlocked.Read(ref _ksb),
        Interlocked.Read(ref _errors));
}

public sealed record CharacterProbeTrafficSnapshot(
    long CallbackPackets,
    long DofusTcpPackets,
    long TcpPayloadPackets,
    long OpenedSessions,
    long DecodedMessages,
    long KscMessages,
    long KsbMessages,
    long Errors)
{
    /// <summary>
    /// Guidance only, not proof that an OS socket is a Dofus client.
    /// osSockets is a snapshot of all local IPv4 connections with remote
    /// port 5555; it never exposes the socket or its owner.
    /// </summary>
    public string DiagnosticCode(int osSockets)
    {
        if (CallbackPackets == 0 && osSockets < 0)
            return "no-traffic-os-inventory-unavailable";
        if (CallbackPackets == 0)
            return osSockets > 0
                ? "capture-interface-or-filter-check"
                : "no-traffic-or-port-check";
        if (DofusTcpPackets == 0)
            return "tcp-packet-parsing-check";
        if (DecodedMessages == 0)
            return TcpPayloadPackets > 0
                ? "framing-or-startup-check"
                : "tcp-control-only";
        if (KscMessages == 0 && KsbMessages > 0)
            return "ksb-seen-ksc-missing";
        if (KscMessages == 0)
            return "ksc-missing";
        if (KsbMessages == 0)
            return "ksb-missing";
        return "identity-candidate-matching-check";
    }

    public string Format(string phase, int osSockets) =>
        "p3m3=capture-status phase=" + phase +
        " filtered-packets=" + CallbackPackets +
        " tcp=" + DofusTcpPackets +
        " payload-packets=" + TcpPayloadPackets +
        " opened=" + OpenedSessions +
        " decoded=" + DecodedMessages +
        " ksc=" + KscMessages +
        " ksb=" + KsbMessages +
        " errors=" + Errors +
        " os-ipv4-remote-port-sockets=" + osSockets +
        " diagnostic=" + DiagnosticCode(osSockets) +
        " identity=unverified";
}
