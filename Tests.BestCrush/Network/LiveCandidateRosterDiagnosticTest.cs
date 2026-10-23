using BestCrush.Services;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;
using System.Text;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3c.10: test anonymous Debug gate and summaries without changing live
/// DI. All character IDs, names and windows below are synthetic.
/// </summary>
public sealed class LiveCandidateRosterDiagnosticTest
{
    private static byte[] Row(ulong id, string name) =>
        Message(Unsigned(3, id),
            Bytes(4, Message(Unsigned(3, 50),
                Bytes(6, Encoding.UTF8.GetBytes(name)))));

    private static byte[] Ksc(ulong id, string name) =>
        Message(Bytes(2, Row(id, name)));

    private static byte[] Ksb(ulong id, string name) =>
        Bytes(1, Bytes(1, Row(id, name)));

    private static void Link(
        EphemeralCandidateRosterWireObserver observer,
        CurrentServerState server,
        long conn, DofusClientWindowKey window,
        ulong id, string name)
    {
        NetworkCaptureLease lease = server.GetCaptureLease()!.Value;
        observer.Observe(new DofusWireMessage(
            "S→C", "ksc", Ksc(id, name), DateTime.UtcNow,
            lease, 1, ConnectionId: conn, CandidateWindow: window,
            SyntheticWindowForReplay: true), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Pending);
        observer.Observe(new DofusWireMessage(
            "S→C", "ksb", Ksb(id, name), DateTime.UtcNow,
            lease, 1, ConnectionId: conn, CandidateWindow: window,
            SyntheticWindowForReplay: true), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Linked);
    }

    [Fact]
    public void GateRequiresExactFlagDebugBuildAndOwnerServices()
    {
        DofusNetworkCaptureService.IsLiveRosterStudyEnabled(
            "1", debugBuild: true, hasOwnerServices: true).Should().BeTrue();
        DofusNetworkCaptureService.IsLiveRosterStudyEnabled(
            null, debugBuild: true, hasOwnerServices: true).Should().BeFalse();
        DofusNetworkCaptureService.IsLiveRosterStudyEnabled(
            "true", debugBuild: true, hasOwnerServices: true).Should().BeFalse();
        DofusNetworkCaptureService.IsLiveRosterStudyEnabled(
            "0", debugBuild: true, hasOwnerServices: true).Should().BeFalse();
    }

    [Fact]
    public void LiveDiagnosticCannotActivateInReleaseOrWithoutOsOwnershipServices()
    {
        DofusNetworkCaptureService.IsLiveRosterStudyEnabled(
            "1", debugBuild: false, hasOwnerServices: true).Should().BeFalse();
        DofusNetworkCaptureService.IsLiveRosterStudyEnabled(
            "1", debugBuild: true, hasOwnerServices: false).Should().BeFalse();
    }

    [Fact]
    public void AnonymousStatusReportsOnlyCountsForTwoDisjointWindows()
    {
        CurrentServerState server = new();
        server.SelectServer("SensitiveServerName");
        var state = new EphemeralCandidateRosterGroupsState();
        var observer = new EphemeralCandidateRosterWireObserver(server, state);
        observer.OnCaptureEpochChanged(1);
        Link(observer, server, 11, new DofusClientWindowKey(101, 1101),
            987654321, "Sable-Ciel");
        Link(observer, server, 22, new DofusClientWindowKey(102, 1102),
            987654322, "Hiver-Rouge");

        string result = observer.FormatAnonymousStatus(
            EphemeralCandidateRosterWireObserver.Result.Linked);
        result.Should().Contain("p3c10=status=linked");
        result.Should().Contain("linked-windows=2");
        result.Should().Contain("equal-roster-pairs=0");
        result.Should().Contain("disjoint-roster-pairs=1");
        result.Should().Contain("account=unverified server=unverified");
        result.Should().NotContain("987654321");
        result.Should().NotContain("987654322");
        result.Should().NotContain("SensitiveServerName");
        result.Should().NotContain("Sable-Ciel");
        result.Should().NotContain("Hiver-Rouge");
        result.Should().NotContain("1101");
    }

    [Fact]
    public void AnonymousStatusReportsEqualityButNeverVerifiedAccount()
    {
        CurrentServerState server = new();
        server.SelectServer("ManualServer");
        var state = new EphemeralCandidateRosterGroupsState();
        var observer = new EphemeralCandidateRosterWireObserver(server, state);
        observer.OnCaptureEpochChanged(1);

        // Two OS windows carrying a candidate with the same roster.
        Link(observer, server, 11, new DofusClientWindowKey(101, 1101),
            12345, "Sable-Ciel");
        Link(observer, server, 22, new DofusClientWindowKey(102, 1102),
            12345, "Sable-Ciel");

        string result = observer.FormatAnonymousStatus(
            EphemeralCandidateRosterWireObserver.Result.Linked);
        result.Should().Contain("equal-roster-pairs=1");
        result.Should().Contain("disjoint-roster-pairs=0");
        result.Should().Contain("account=unverified");
        result.Should().NotContain("12345");
    }

    [Fact]
    public void ManualServerChangeClearsAnonymousCountAndCapturesCannotRollback()
    {
        CurrentServerState server = new();
        server.SelectServer("Original");
        var state = new EphemeralCandidateRosterGroupsState();
        var observer = new EphemeralCandidateRosterWireObserver(server, state);
        observer.OnCaptureEpochChanged(1);
        Link(observer, server, 11, new DofusClientWindowKey(101, 1101),
            12345, "Sable-Ciel");

        server.SelectServer("NewServer");
        observer.OnManualServerChanged();
        observer.FormatAnonymousStatus(
            EphemeralCandidateRosterWireObserver.Result.Rejected)
            .Should().Contain("linked-windows=0");

        observer.OnCaptureEpochChanged(2);
        observer.FormatAnonymousStatus(
            EphemeralCandidateRosterWireObserver.Result.Ignored)
            .Should().Contain("linked-windows=0");
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
    }
}
