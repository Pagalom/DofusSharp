using System.Text;
using BestCrush.Services;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class WindowServerAttributionStateTest
{
    private static readonly DofusClientWindowKey A = new(111, 1111);
    private static readonly DofusClientWindowKey B = new(222, 2222);
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
        string key, byte[] body, DofusClientWindowKey? window = null,
        long game = 21, long epoch = 1, DateTime? at = null) =>
        new("S→C", key, body, at ?? At.AddSeconds(5),
            null, epoch, ConnectionId: game, CandidateWindow: window ?? A,
            SyntheticWindowForReplay: true);

    private static WindowServerAttributionState Start()
    {
        WindowServerAttributionState state = new();
        state.Reset(1);
        return state;
    }

    private static void Loaded(
        WindowServerAttributionState state,
        DofusClientWindowKey window, int id,
        long selectionFlow = 11, long gameFlow = 21,
        ulong characterId = 101, string name = "Alpha-One")
    {
        state.ObserveSelection(window, 1, selectionFlow, id, At);
        state.ObserveGame(Wire("ksc",
            Ksc((characterId, name)), window, gameFlow), true);
        state.ObserveGame(Wire("ksb",
            Ksb(characterId, name), window, gameFlow), true);
    }

    [Theory]
    [InlineData(295, "Draconiros")]
    [InlineData(293, "Tylezia")]
    [InlineData(355, "Kourial")]
    public void PublicReferenceIdMapsToItsExpectedName(int id, string name) =>
        DofusServerIdCatalog.NameFor(id).Should().Be(name);

    [Fact]
    public void TwoWindowsCanHaveIndependentDifferentServers()
    {
        var state = Start();
        Loaded(state, A, 293, gameFlow: 21, name: "Alpha-One");
        Loaded(state, B, 355, selectionFlow: 12, gameFlow: 22,
            characterId: 202, name: "Alpha-One");

        state.ForWindow(A).Should().Be(new WindowServerAttribution(293, "Tylezia"));
        state.ForWindow(B).Should().Be(new WindowServerAttribution(355, "Kourial"));
    }

    [Fact]
    public void SameWindowServerNeedsBothWireMessagesAndPriorSelection()
    {
        var state = Start();
        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One"))), true);
        state.ObserveGame(Wire("ksb", Ksb(101, "Alpha-One")), true);
        state.ForWindow(A).Should().BeNull();

        state.ObserveSelection(A, 1, 11, 295, At);
        state.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void SelectionAndGameOnTheSameTcpTokenCannotBeLinked()
    {
        var state = Start();
        state.ObserveSelection(A, 1, 21, 295, At);
        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One")), game: 21), true);
        state.ObserveGame(Wire("ksb", Ksb(101, "Alpha-One"), game: 21), true);
        state.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void ASelectionCannotBeAttachedToAnotherWindow()
    {
        var state = Start();
        state.ObserveSelection(A, 1, 11, 295, At);
        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One")), B), true);
        state.ObserveGame(Wire("ksb", Ksb(101, "Alpha-One"), B), true);
        state.ForWindow(A).Should().BeNull();
        state.ForWindow(B).Should().BeNull();
    }

    [Fact]
    public void TwoSelectionsBeforeLoadAreAmbiguousEvenWhenEqual()
    {
        var state = Start();
        state.ObserveSelection(A, 1, 11, 295, At);
        state.ObserveSelection(A, 1, 12, 295, At.AddSeconds(1));
        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One"))), true);
        state.ObserveGame(Wire("ksb", Ksb(101, "Alpha-One")), true);
        state.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void UnknownServerIdDoesNotBecomeAnAssumedServerName()
    {
        var state = Start();
        Loaded(state, A, 9999);
        state.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void AStaleSelectionIsNotUsedForARecentGame()
    {
        var state = Start();
        state.ObserveSelection(A, 1, 11, 295, At.AddMinutes(-5));
        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One"))), true);
        state.ObserveGame(Wire("ksb", Ksb(101, "Alpha-One")), true);
        state.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void CharacterSelectionMismatchCannotConfirmTheServer()
    {
        var state = Start();
        state.ObserveSelection(A, 1, 11, 295, At);
        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One"))), true);
        state.ObserveGame(Wire("ksb", Ksb(202, "Bravo-Two")), true);
        state.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void CrossTcpKscAndKsbCannotBeJoined()
    {
        var state = Start();
        state.ObserveSelection(A, 1, 11, 295, At);
        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One")), game: 21), true);
        state.ObserveGame(Wire("ksb", Ksb(101, "Alpha-One"), game: 22), true);
        state.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void MissingOwnerEvidenceCannotCreateServerLink()
    {
        var state = Start();
        state.ObserveSelection(A, 1, 11, 295, At);
        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One"))), false);
        state.ObserveGame(Wire("ksb", Ksb(101, "Alpha-One")), true);
        state.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void ReconnectClearsOldServerUntilFreshSameWindowEvidence()
    {
        var state = Start();
        Loaded(state, A, 295);
        state.ForWindow(A).Should().NotBeNull();

        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One")), game: 23), true);
        state.ForWindow(A).Should().BeNull();
        state.ObserveGame(Wire("ksb", Ksb(101, "Alpha-One"), game: 23), true);
        state.ForWindow(A).Should().BeNull();
        Loaded(state, A, 293, selectionFlow: 24, gameFlow: 25);
        state.ForWindow(A)!.ServerName.Should().Be("Tylezia");
    }

    [Fact]
    public void ClosedWindowIsPrunedAndQuarantinedUntilNewCapture()
    {
        var state = Start();
        Loaded(state, A, 295);
        state.RemoveMissingWindows([]);
        state.ForWindow(A).Should().BeNull();

        Loaded(state, A, 295, selectionFlow: 24, gameFlow: 25);
        state.ForWindow(A).Should().BeNull();

        state.Reset(2);
        state.ObserveSelection(A, 2, 30, 295, At);
        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One")),
            game: 31, epoch: 2), true);
        state.ObserveGame(Wire("ksb", Ksb(101, "Alpha-One"),
            game: 31, epoch: 2), true);
        state.ForWindow(A)!.ServerName.Should().Be("Draconiros");
    }

    [Fact]
    public void TwoWindowsOnSameServerHaveDistinctButSameMarketGameLeases()
    {
        var state = Start();
        Loaded(state, A, 295, gameFlow: 21);
        Loaded(state, B, 295, selectionFlow: 12, gameFlow: 22,
            characterId: 202, name: "Bravo-Two");
        CurrentServerState server = new(state, automaticRoutingEnabled: true);

        NetworkCaptureLease first = server.GetWindowCaptureLease(A)!.Value;
        NetworkCaptureLease second = server.GetWindowCaptureLease(B)!.Value;

        first.ServerName.Should().Be("Draconiros");
        second.ServerName.Should().Be("Draconiros");
        first.Should().NotBe(second);
        server.IsCaptureLeaseActive(first).Should().BeTrue();
        server.IsCaptureLeaseActive(second).Should().BeTrue();

        // Global UI selection has no authority over either lease.
        server.SelectServer("Tylezia");
        server.GetCaptureLease().Should().BeNull();
        server.IsCaptureLeaseActive(first).Should().BeTrue();
        server.IsCaptureLeaseActive(second).Should().BeTrue();
    }

    [Fact]
    public void ClosingOneWindowDoesNotRevokeAnotherWindowsSameServerLease()
    {
        var state = Start();
        Loaded(state, A, 295, gameFlow: 21);
        Loaded(state, B, 295, selectionFlow: 12, gameFlow: 22,
            characterId: 202, name: "Bravo-Two");
        CurrentServerState server = new(state, automaticRoutingEnabled: true);
        NetworkCaptureLease first = server.GetWindowCaptureLease(A)!.Value;
        NetworkCaptureLease second = server.GetWindowCaptureLease(B)!.Value;

        state.RemoveMissingWindows([B]);

        server.IsCaptureLeaseActive(first).Should().BeFalse();
        server.IsCaptureLeaseActive(second).Should().BeTrue();
        server.GetWindowCaptureLease(A).Should().BeNull();
    }

    [Fact]
    public void NewGameAttemptCannotReuseOldServerLeaseForSameWindow()
    {
        var state = Start();
        Loaded(state, A, 295);
        CurrentServerState server = new(state, automaticRoutingEnabled: true);
        NetworkCaptureLease old = server.GetWindowCaptureLease(A)!.Value;

        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One")),
            game: 31), ownerRevalidated: true);
        server.IsCaptureLeaseActive(old).Should().BeFalse();

        Loaded(state, A, 293, selectionFlow: 32, gameFlow: 33);
        NetworkCaptureLease replacement = server.GetWindowCaptureLease(A)!.Value;
        replacement.ServerName.Should().Be("Tylezia");
        server.IsCaptureLeaseActive(old).Should().BeFalse();
        server.IsCaptureLeaseActive(replacement).Should().BeTrue();
    }

    [Fact]
    public void OldCaptureEpochCannotRestoreARevokedServer()
    {
        var state = Start();
        Loaded(state, A, 295);
        state.Reset(2);
        state.ForWindow(A).Should().BeNull();
        state.ObserveSelection(A, 1, 11, 295, At);
        state.ObserveGame(Wire("ksc", Ksc((101, "Alpha-One")), epoch: 1), true);
        state.ObserveGame(Wire("ksb", Ksb(101, "Alpha-One"), epoch: 1), true);
        state.ForWindow(A).Should().BeNull();
    }
}
