using BestCrush.Network.Capture;
using FluentAssertions;

namespace Tests.BestCrush.Network;

/// <summary>
/// Metadata-only diagnostics: no game account data, packet contents,
/// addresses, ports or persistent state.
/// </summary>
public sealed class CharacterProbeTrafficDiagnosticsTest
{
    private static readonly DateTime T0 =
        new(2026, 10, 21, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoFilteredPacketsWithAnActiveSocketSuggestsInterfaceOrFilter()
    {
        CharacterProbeTrafficSnapshot s =
            new CharacterProbeTrafficDiagnostics().Snapshot();
        s.DiagnosticCode(2).Should().Be("capture-interface-or-filter-check");
        s.Format("final", 2).Should().Contain("filtered-packets=0")
            .And.Contain("os-ipv4-remote-port-sockets=2")
            .And.Contain("identity=unverified");
    }

    [Fact]
    public void NoPacketsAndNoActiveSocketDoesNotClaimAnIdentityFailure()
    {
        var s = new CharacterProbeTrafficDiagnostics().Snapshot();
        s.DiagnosticCode(0).Should().Be("no-traffic-or-port-check");
        s.KscMessages.Should().Be(0);
        s.KsbMessages.Should().Be(0);
    }

    [Fact]
    public void OsSocketInventoryFailureDoesNotPretendNoPortConnection()
    {
        var s = new CharacterProbeTrafficDiagnostics().Snapshot();
        s.DiagnosticCode(-1).Should().Be("no-traffic-os-inventory-unavailable");
        s.Format("live", -1).Should().Contain("os-ipv4-remote-port-sockets=-1");
    }

    [Fact]
    public void FilteredPacketWithoutTcpMetadataIsDistinguished()
    {
        CharacterProbeTrafficDiagnostics d = new();
        d.PacketArrived();
        d.Snapshot().DiagnosticCode(1).Should().Be("tcp-packet-parsing-check");
    }

    [Fact]
    public void OnlyTcpControlPacketsDoNotCountAsDecodedMessages()
    {
        CharacterProbeTrafficDiagnostics d = new();
        d.PacketArrived();
        d.DofusTcpPacket(0);
        d.Snapshot().DiagnosticCode(1).Should().Be("tcp-control-only");
    }

    [Fact]
    public void TcpPayloadWithoutDecodedMessagesPointsToFraming()
    {
        CharacterProbeTrafficDiagnostics d = new();
        d.PacketArrived();
        d.DofusTcpPacket(123);
        d.Snapshot().DiagnosticCode(1).Should().Be("framing-or-startup-check");
    }

    [Fact]
    public void KsbWithoutKscIsVisibleEvenIfModeFilteringHidesOtherMessages()
    {
        CharacterProbeTrafficDiagnostics d = new();
        d.PacketArrived();
        d.DofusTcpPacket(15);
        d.EventsObserved([
            new CharacterCaptureEvent(T0, 1,
                CharacterCaptureDirection.ClientToServer,
                CharacterCaptureEventKind.Opened),
            new CharacterCaptureEvent(T0, 1,
                CharacterCaptureDirection.ServerToClient,
                CharacterCaptureEventKind.Message, Opcode: "ksb", BodyLength: 138),
            new CharacterCaptureEvent(T0, 1,
                CharacterCaptureDirection.ServerToClient,
                CharacterCaptureEventKind.Message, Opcode: "ipz", BodyLength: 70)
        ]);

        var s = d.Snapshot();
        s.OpenedSessions.Should().Be(1);
        s.DecodedMessages.Should().Be(2);
        s.KsbMessages.Should().Be(1);
        s.KscMessages.Should().Be(0);
        s.DiagnosticCode(1).Should().Be("ksb-seen-ksc-missing");
    }

    [Fact]
    public void CounterOnlyUsesServerToClientIdentityMessages()
    {
        CharacterProbeTrafficDiagnostics d = new();
        d.EventsObserved([
            new CharacterCaptureEvent(T0, 1,
                CharacterCaptureDirection.ClientToServer,
                CharacterCaptureEventKind.Message, Opcode: "ksb", BodyLength: 2),
            new CharacterCaptureEvent(T0, 1,
                CharacterCaptureDirection.ServerToClient,
                CharacterCaptureEventKind.Message, Opcode: "ksc", BodyLength: 4)
        ]);

        var s = d.Snapshot();
        s.DecodedMessages.Should().Be(2);
        s.KsbMessages.Should().Be(0);
        s.KscMessages.Should().Be(1);
        s.DiagnosticCode(1).Should().Be("ksb-missing");
    }

    [Fact]
    public void BothMessageFamiliesOnlyPointToCandidateMatchingNotConfirmedIdentity()
    {
        CharacterProbeTrafficDiagnostics d = new();
        d.EventsObserved([
            new CharacterCaptureEvent(T0, 2,
                CharacterCaptureDirection.ServerToClient,
                CharacterCaptureEventKind.Message, Opcode: "ksc", BodyLength: 40),
            new CharacterCaptureEvent(T0, 2,
                CharacterCaptureDirection.ServerToClient,
                CharacterCaptureEventKind.Message, Opcode: "ksb", BodyLength: 30)
        ]);
        d.PacketError();

        var s = d.Snapshot();
        s.DiagnosticCode(1).Should().Be("identity-candidate-matching-check");
        s.Errors.Should().Be(1);
        string line = s.Format("final", 1);
        line.Should().Contain("ksc=1").And.Contain("ksb=1")
            .And.Contain("errors=1").And.Contain("identity=unverified");
        line.Should().NotContain("192.0.2.1");
        line.Should().NotContain("Pagalom");
    }
}
