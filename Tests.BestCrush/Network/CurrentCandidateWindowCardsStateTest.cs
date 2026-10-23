using System.Text;
using BestCrush.Services;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3c.11: Win32 inventory controls ephemeral presentation even without
/// a TCP packet. All handles, names and IDs below are synthetic.
/// </summary>
public sealed class CurrentCandidateWindowCardsStateTest
{
    private static readonly DofusClientWindowKey A = new(101, 1101);
    private static readonly DofusClientWindowKey B = new(102, 1102);
    private static readonly DofusClientWindowKey ReopenedB = new(103, 1103);
    private static readonly DateTime At =
        new(2026, 10, 23, 19, 0, 0, DateTimeKind.Utc);

    private static byte[] Row(ulong id, string name) =>
        Message(Unsigned(3, id),
            Bytes(4, Message(Unsigned(3, 50),
                Bytes(6, Encoding.UTF8.GetBytes(name)))));

    private static byte[] Roster(params (ulong Id, string Name)[] people) =>
        Message(people.Select(x => Bytes(2, Row(x.Id, x.Name))).ToArray());

    private static byte[] Selected(ulong id, string name) =>
        Bytes(1, Bytes(1, Row(id, name)));

    private static (CurrentCandidateWindowCardsState Cards,
        EphemeralCandidateRosterWireObserver Observer,
        CurrentServerState Server) Create()
    {
        CurrentServerState server = new();
        server.SelectServer("SelectedManually");
        CurrentCandidateWindowCardsState cards = new();
        EphemeralCandidateRosterWireObserver observer =
            cards.EnableForResearch(server);
        observer.OnCaptureEpochChanged(1);
        return (cards, observer, server);
    }

    private static EphemeralCandidateRosterWireObserver.Result Send(
        EphemeralCandidateRosterWireObserver observer, CurrentServerState server,
        string key, byte[] body, DofusClientWindowKey window, long connection,
        long epoch = 1) =>
        observer.Observe(new DofusWireMessage(
            "S→C", key, body, At, server.GetCaptureLease(),
            epoch, ConnectionId: connection, CandidateWindow: window,
            SyntheticWindowForReplay: true), ownerRevalidated: true);

    private static void Link(EphemeralCandidateRosterWireObserver observer,
        CurrentServerState server, DofusClientWindowKey window,
        long connection, ulong active, string activeName,
        params (ulong Id, string Name)[] people)
    {
        Send(observer, server, "ksc", Roster(people), window, connection)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Pending);
        Send(observer, server, "ksb", Selected(active, activeName),
            window, connection)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Linked);
    }

    [Fact]
    public void NormalProcessStartupHasNoCandidateCardsAndNoDebugObserver()
    {
        CurrentCandidateWindowCardsState state = new();
        state.IsResearchEnabled.Should().BeFalse();
        state.Cards.Should().BeEmpty();
        state.HasCandidateLink(A).Should().BeFalse();
        state.RemoveMissingWindows([]);
        new CurrentCharacterState().Characters.Should().BeEmpty();
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
    }

    [Fact]
    public void CandidateCardAppearsOnlyAfterKscAndKsbOnSameWindowConnection()
    {
        var (cards, observer, server) = Create();
        cards.IsResearchEnabled.Should().BeTrue();

        Send(observer, server, "ksc",
            Roster((101, "Sable-Ciel"), (102, "Roche-Lune")), A, 11)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Pending);
        cards.Cards.Should().BeEmpty();

        Send(observer, server, "ksb", Selected(101, "Sable-Ciel"), A, 11)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Linked);
        cards.Cards.Should().ContainSingle().Which.Should().Be(
            new CandidateWindowRosterCard(A, 2));
        cards.HasCandidateLink(A).Should().BeTrue();
    }

    [Fact]
    public void TwoWindowsHaveIndependentCardsAndNoInferredAccountOrProfessions()
    {
        var (cards, observer, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel",
            (101, "Sable-Ciel"), (102, "Roche-Lune"));
        Link(observer, server, B, 22, 201, "Hiver-Rouge",
            (201, "Hiver-Rouge"));

        cards.Cards.Should().BeEquivalentTo([
            new CandidateWindowRosterCard(A, 2),
            new CandidateWindowRosterCard(B, 1)
        ]);
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
        new CurrentCharacterState().Characters.Should().BeEmpty();
    }

    [Fact]
    public void ClosingBWithoutAnyNewPacketRemovesOnlyBAndSignalsUiChange()
    {
        var (cards, observer, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel",
            (101, "Sable-Ciel"));
        Link(observer, server, B, 22, 201, "Hiver-Rouge",
            (201, "Hiver-Rouge"));
        int updates = 0;
        cards.Changed += () => updates++;

        // Only the Win32 polling loop fires. There is no new packet.
        cards.RemoveMissingWindows([A]);

        cards.Cards.Should().ContainSingle().Which.Window.Should().Be(A);
        cards.HasCandidateLink(B).Should().BeFalse();
        updates.Should().Be(1);
        cards.RemoveMissingWindows([A]);
        updates.Should().Be(1, "identical window inventories do not redraw");
    }

    [Fact]
    public void BReconnectsOnNewWindowWithoutBorrowingOldBOrA()
    {
        var (cards, observer, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel",
            (101, "Sable-Ciel"));
        Link(observer, server, B, 22, 201, "Hiver-Rouge",
            (201, "Hiver-Rouge"));
        cards.RemoveMissingWindows([A]);

        // Old HWND/PID is quarantined, even if Win32 reuses its value.
        Send(observer, server, "ksc", Roster((201, "Hiver-Rouge")), B, 23)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Pending);
        Send(observer, server, "ksb", Selected(201, "Hiver-Rouge"), B, 23)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);

        Link(observer, server, ReopenedB, 24, 201, "Hiver-Rouge",
            (201, "Hiver-Rouge"));
        cards.Cards.Should().BeEquivalentTo([
            new CandidateWindowRosterCard(A, 1),
            new CandidateWindowRosterCard(ReopenedB, 1)
        ]);
    }

    [Fact]
    public void ClosingPendingOnlyWindowRevokesUnfinishedKscWithoutTcpPackets()
    {
        var (cards, observer, server) = Create();
        Send(observer, server, "ksc",
            Roster((201, "Hiver-Rouge")), B, 22)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Pending);
        cards.Cards.Should().BeEmpty();

        cards.RemoveMissingWindows([A]);
        Send(observer, server, "ksb",
            Selected(201, "Hiver-Rouge"), B, 22)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        cards.Cards.Should().BeEmpty();
    }

    [Fact]
    public void ServerManualChangeClearsVisibleCandidatesWithoutAccountVerification()
    {
        var (cards, observer, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel",
            (101, "Sable-Ciel"));
        server.SelectServer("OtherManualChoice");
        observer.OnManualServerChanged();
        cards.NotifyChanged();

        cards.Cards.Should().BeEmpty();
        cards.HasCandidateLink(A).Should().BeFalse();
    }

    [Fact]
    public void RestartOfCaptureInvalidatesAllCardsAndOldEpochCannotReappear()
    {
        var (cards, observer, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel",
            (101, "Sable-Ciel"));
        observer.OnCaptureEpochChanged(2);
        cards.Cards.Should().BeEmpty();

        Send(observer, server, "ksc", Roster((101, "Sable-Ciel")),
            A, 11, epoch: 1)
            .Should().Be(EphemeralCandidateRosterWireObserver.Result.Rejected);
        cards.Cards.Should().BeEmpty();
    }

    [Fact]
    public void CandidatePresentationNeverExportsRosterIdsNamesOrHandleInString()
    {
        var (cards, observer, server) = Create();
        Link(observer, server, A, 11, 987654321, "Sable-Ciel",
            (987654321, "Sable-Ciel"));

        string text = cards.Cards.Single().ToString();
        text.Should().Contain("redacted");
        text.Should().Contain("account=unverified");
        text.Should().NotContain("987654321");
        text.Should().NotContain("Sable-Ciel");
        text.Should().NotContain("101");
        text.Should().NotContain("1101");
        text.Should().NotContain("SelectedManually");
    }

    [Fact]
    public void RedrawingCandidateCardsDoesNotSelectAnotherDofusWindow()
    {
        var (cards, observer, server) = Create();
        Link(observer, server, A, 11, 101, "Sable-Ciel",
            (101, "Sable-Ciel"));
        Link(observer, server, B, 22, 201, "Hiver-Rouge",
            (201, "Hiver-Rouge"));
        CurrentDofusClientsState clients = new();
        // No UI selection or character state may be written by the
        // candidate card presenter, even if the roster is linked.
        clients.SelectedClient.Should().BeNull();
        cards.NotifyChanged();
        clients.SelectedClient.Should().BeNull();
        new CurrentCharacterState().ActiveProfile.Should().BeNull();
    }
}
