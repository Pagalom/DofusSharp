using System.Text;
using BestCrush.Services;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3c.9 synthetic S->C ksc/ksb messages ONLY, with separate prevalidated
/// owner evidence. Never a real account or authenticated game-server ID.
/// </summary>
public sealed class EphemeralCandidateRosterWireObserverTest
{
    private static readonly DofusClientWindowKey A = new(101, 1101);
    private static readonly DofusClientWindowKey B = new(102, 1102);
    private static readonly DateTime At =
        new(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);

    private static byte[] Row(ulong id, string name, ulong level = 50) =>
        Message(Unsigned(3, id), Bytes(4, Message(
            Unsigned(3, level), Bytes(6, Encoding.UTF8.GetBytes(name)))));

    private static byte[] Ksc(params (ulong Id, string Name)[] rows) =>
        Message(rows.Select(x => Bytes(2, Row(x.Id, x.Name))).ToArray());

    private static byte[] Ksb(ulong id, string name) =>
        Bytes(1, Bytes(1, Row(id, name)));

    private static DofusWireMessage Wire(
        CurrentServerState server, string key, byte[] body,
        DofusClientWindowKey? window = null, long connectionId = 11,
        long epoch = 1, string direction = "S→C",
        NetworkCaptureLease? lease = null) =>
        new(direction, key, body, At,
            lease ?? server.GetCaptureLease(),
            epoch, ConnectionId: connectionId,
            CandidateWindow: window ?? A, SyntheticWindowForReplay: true);

    private static (EphemeralCandidateRosterWireObserver Observer,
        EphemeralCandidateRosterGroupsState State, CurrentServerState Server)
        Create()
    {
        CurrentServerState server = new();
        server.SelectServer("ManualServer");
        EphemeralCandidateRosterGroupsState state = new();
        EphemeralCandidateRosterWireObserver observer = new(server, state);
        observer.OnCaptureEpochChanged(1);
        return (observer, state, server);
    }

    private static void Link(
        EphemeralCandidateRosterWireObserver observer,
        CurrentServerState server, DofusClientWindowKey window,
        long connectionId, ulong selected, string name,
        params (ulong Id, string Name)[] roster)
    {
        observer.Observe(Wire(server, "ksc", Ksc(roster),
            window, connectionId), ownerRevalidated: true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Pending);
        observer.Observe(Wire(server, "ksb", Ksb(selected, name),
            window, connectionId), ownerRevalidated: true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Linked);
    }

    [Fact]
    public void RealWireFamilyCanLinkTwoDisjointRostersOnlyWhenOwnerIsPrevalidated()
    {
        var (observer, state, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel",
            (101, "Sable-Ciel"), (102, "Roche-Lune"));
        Link(observer, server, B, 22, 201, "Hiver-Rouge",
            (201, "Hiver-Rouge"));

        state.LiveWindowCount.Should().Be(2);
        state.Compare(A, B, server.GetCaptureLease()!.Value, 1)
            .Should().Be(EphemeralCandidateRosterGroupsState
                .Comparison.DisjointRosterCandidates);
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
        new CurrentCharacterState().Characters.Should().BeEmpty();
    }

    [Fact]
    public void TwoDifferentSelectionsInSameRosterRemainOnlyCandidateEquals()
    {
        var (observer, state, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel",
            (101, "Sable-Ciel"), (102, "Roche-Lune"));
        Link(observer, server, B, 22, 102, "Roche-Lune",
            (102, "Roche-Lune"), (101, "Sable-Ciel"));
        state.Compare(A, B, server.GetCaptureLease()!.Value, 1)
            .Should().Be(EphemeralCandidateRosterGroupsState
                .Comparison.SameRosterCandidate);
    }

    [Fact]
    public void InvalidOrMissingOwnerEvidenceCannotEvenCreatePendingRoster()
    {
        var (observer, state, server) = Create();
        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel"))), ownerRevalidated: false)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        observer.Observe(Wire(server, "ksb",
            Ksb(101, "Sable-Ciel")), ownerRevalidated: true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        state.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public void CandidateTitleCannotReplaceMissingSameFlowKsc()
    {
        var (observer, state, server) = Create();
        observer.Observe(Wire(server, "ksb",
            Ksb(101, "Sable-Ciel")), ownerRevalidated: true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        state.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public void KscAndKsbFromDifferentTcpConnectionsCannotCombine()
    {
        var (observer, state, server) = Create();
        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel")), A, 11), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Pending);
        observer.Observe(Wire(server, "ksb",
            Ksb(101, "Sable-Ciel"), A, 12), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        state.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public void WindowSwitchWithinSameTcpFlowIsRejected()
    {
        var (observer, state, server) = Create();
        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel")), A, 11), true);
        observer.Observe(Wire(server, "ksb",
            Ksb(101, "Sable-Ciel"), B, 11), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        state.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public void NameMismatchWithinSameCharacterCandidateFailsClosed()
    {
        var (observer, state, server) = Create();
        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel")), A, 11), true);
        observer.Observe(Wire(server, "ksb",
            Ksb(101, "Autre-Nom"), A, 11), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        observer.Observe(Wire(server, "ksb",
            Ksb(101, "Sable-Ciel"), A, 11), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        state.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public void RepeatedKsbRevokesEarlierLinkedCandidate()
    {
        var (observer, state, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel", (101, "Sable-Ciel"));
        observer.Observe(Wire(server, "ksb",
            Ksb(102, "Roche-Lune"), A, 11), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        state.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public void LossOfOwnerAfterLinkImmediatelyRevokesCandidate()
    {
        var (observer, state, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel", (101, "Sable-Ciel"));
        observer.Observe(Wire(server, "ksb",
            Ksb(101, "Sable-Ciel"), A, 11), false)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        state.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public void ReconnectionDoesNotReusePreviousFlowOrBorrowOtherWindow()
    {
        var (observer, state, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel", (101, "Sable-Ciel"));
        observer.Disconnect(A, server.GetCaptureLease()!.Value, 1, 11);
        Link(observer, server, A, 12, 101, "Sable-Ciel", (101, "Sable-Ciel"));
        state.IsCurrent(A, server.GetCaptureLease()!.Value, 1, 12)
            .Should().BeTrue();
        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel")), A, 11), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
    }

    [Fact]
    public void OSWindowClosureRemovesPendingAndLinkedEvidence()
    {
        var (observer, state, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel", (101, "Sable-Ciel"));
        observer.Observe(Wire(server, "ksc",
            Ksc((201, "Hiver-Rouge")), B, 22), true);
        observer.ReconcileWindows([B]);
        state.LiveWindowCount.Should().Be(0);
        observer.Observe(Wire(server, "ksb",
            Ksb(201, "Hiver-Rouge"), B, 22), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Linked);
        state.LiveWindowCount.Should().Be(1);
        observer.Observe(Wire(server, "ksb",
            Ksb(101, "Sable-Ciel"), A, 11), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
    }

    [Fact]
    public void ManualServerChangeClearsImmediatelyAndBlocksOldLease()
    {
        var (observer, state, server) = Create();
        NetworkCaptureLease old = server.GetCaptureLease()!.Value;
        Link(observer, server, A, 11, 101, "Sable-Ciel", (101, "Sable-Ciel"));
        server.SelectServer("AnotherManualServer");
        observer.OnManualServerChanged();

        state.LiveWindowCount.Should().Be(0);
        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel")), A, 12,
            lease: old), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        Link(observer, server, A, 13, 101, "Sable-Ciel", (101, "Sable-Ciel"));
        state.LiveWindowCount.Should().Be(1);
    }

    [Fact]
    public void CaptureRestartClearsCandidatesAndStaleMessageCannotRevive()
    {
        var (observer, state, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel", (101, "Sable-Ciel"));
        observer.OnCaptureEpochChanged(2);
        state.LiveWindowCount.Should().Be(0);
        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel")), A, 11, epoch: 1), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel")), A, 12, epoch: 2), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Pending);
    }

    [Fact]
    public void OutboundMessagesAndUnrelatedOpcodesNeverCreateCandidates()
    {
        var (observer, state, server) = Create();
        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel")), direction: "C→S"), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Ignored);
        observer.Observe(Wire(server, "knc",
            Ksc((101, "Sable-Ciel"))), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Ignored);
        state.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public void ClosedWindowKeyCannotBeReusedWithNewTcpConnectionInSameCapture()
    {
        var (observer, state, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel", (101, "Sable-Ciel"));
        observer.ReconcileWindows([]);

        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel")), A, 12), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Pending);
        observer.Observe(Wire(server, "ksb",
            Ksb(101, "Sable-Ciel"), A, 12), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        state.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public void SameCaptureEpochNotificationDoesNotLosePendingRoster()
    {
        var (observer, state, server) = Create();
        observer.Observe(Wire(server, "ksc",
            Ksc((101, "Sable-Ciel")), A, 11), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Pending);
        observer.OnCaptureEpochChanged(1);
        observer.Observe(Wire(server, "ksb",
            Ksb(101, "Sable-Ciel"), A, 11), true)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Linked);
        state.LiveWindowCount.Should().Be(1);
    }

    [Fact]
    public void WireAdapterNeverLogsSensitiveValuesOrVerifiesAccount()
    {
        var (observer, state, server) = Create();
        Link(observer, server, A, 11, 987654321, "Sable-Ciel",
            (987654321, "Sable-Ciel"));
        observer.ToString().Should().NotContain("987654321")
            .And.NotContain("Sable-Ciel")
            .And.Contain("account=unverified");
        state.ToString().Should().NotContain("987654321")
            .And.NotContain("Sable-Ciel");
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
    }
}
