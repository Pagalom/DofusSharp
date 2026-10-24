using System.Text;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>Fabricated Game and profession messages; never live account data.</summary>
public sealed class CurrentWindowProfessionsStateTest
{
    private static readonly DofusClientWindowKey A = new(111, 1111);
    private static readonly DofusClientWindowKey B = new(222, 2222);
    private static readonly DateTime At =
        new(2026, 10, 24, 14, 0, 0, DateTimeKind.Utc);

    private static byte[] CharacterRow(ulong id, string name) =>
        Message(Unsigned(3, id), Bytes(4, Message(
            Unsigned(3, 38), Bytes(6, Encoding.UTF8.GetBytes(name)))));

    private static byte[] Ksc(ulong id, string name) =>
        Message(Bytes(2, CharacterRow(id, name)));

    private static byte[] Ksb(ulong id, string name) =>
        Bytes(1, Bytes(1, CharacterRow(id, name)));

    private static byte[] Roster() =>
        Message(ProfessionPublicJobIdReference.ReferenceTwenty
            .Select((id, index) => Bytes(1, Message(
                Unsigned(1, (ulong)(index + 1)),
                Unsigned(2, (ulong)((index + 1) * 100)),
                Unsigned(5, id))))
            .ToArray());

    private static byte[] Single(ulong jobId, ulong level, ulong xp) =>
        Message(Bytes(1, Message(
            Unsigned(1, level), Unsigned(2, xp), Unsigned(5, jobId))));

    private static DofusWireMessage Wire(
        string key, byte[] body, DofusClientWindowKey owner,
        long connectionId, NetworkCaptureLease? lease = null,
        long epoch = 3) =>
        new("S→C", key, body, At.AddSeconds(5), lease, epoch,
            ConnectionId: connectionId, CandidateWindow: owner,
            SyntheticWindowForReplay: true);

    private static (CurrentWindowProfessionsState View,
        WindowServerAttributionState Attribution,
        CurrentServerState Server) Start()
    {
        WindowServerAttributionState attribution = new();
        attribution.Reset(3);
        CurrentServerState server = new(attribution, automaticRoutingEnabled: true);
        return (new CurrentWindowProfessionsState(server), attribution, server);
    }

    private static NetworkCaptureLease Load(
        WindowServerAttributionState attribution, CurrentServerState server,
        DofusClientWindowKey window, int serverId, long connectionId,
        ulong characterId = 101)
    {
        attribution.ObserveSelection(window, 3, connectionId - 1, serverId, At);
        attribution.ObserveGame(Wire(
            "ksc", Ksc(characterId, "Pagalom"), window, connectionId), true);
        attribution.ObserveGame(Wire(
            "ksb", Ksb(characterId, "Pagalom"), window, connectionId), true);
        return server.GetGameCaptureLease(window, 3, connectionId)!.Value;
    }

    [Fact]
    public void VerifiedGameRosterAppearsOnlyOnItsExactWindow()
    {
        var (view, attribution, server) = Start();
        var lease = Load(attribution, server, A, 293, 21);
        int changed = 0;
        view.Changed += () => changed++;

        view.Observe(Wire("ipz", Roster(), A, 21, lease)).Should().BeTrue();
        view.ForWindow(A).Should().HaveCount(20);
        view.ForWindow(B).Should().BeNull();
        view.ForWindow(A)!.Should().Contain(job =>
            job.Name == "Bûcheron" && job.Level == 1 &&
            job.CumulativeExperience == 100);
        changed.Should().Be(1);
    }

    [Fact]
    public void GameWindowIsolationPreservesTwoIdenticallyNamedCharacters()
    {
        var (view, attribution, server) = Start();
        var leaseA = Load(attribution, server, A, 293, 21);
        var leaseB = Load(attribution, server, B, 355, 31, characterId: 202);

        view.Observe(Wire("ipz", Roster(), A, 21, leaseA)).Should().BeTrue();
        view.Observe(Wire("ipz", Roster(), B, 31, leaseB)).Should().BeTrue();

        view.Observe(Wire("ipz", Single(2, 40, 9000), A, 21, leaseA))
            .Should().BeTrue();

        view.ForWindow(A)!.Single(job => job.Name == "Bûcheron")
            .Level.Should().Be(40);
        view.ForWindow(B)!.Single(job => job.Name == "Bûcheron")
            .Level.Should().Be(1);
    }

    [Fact]
    public void SingleUpdateCannotInitializeProfileWithoutFullRoster()
    {
        var (view, attribution, server) = Start();
        var lease = Load(attribution, server, A, 293, 21);
        view.Observe(Wire("ipz", Single(2, 20, 2000), A, 21, lease))
            .Should().BeFalse();
        view.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void StaleAndWrongServerLeasesNeverPopulateAnotherWindow()
    {
        var (view, attribution, server) = Start();
        var lease = Load(attribution, server, A, 293, 21);
        view.Observe(Wire("ipz", Roster(), B, 21, lease)).Should().BeFalse();
        view.Observe(Wire("ipz", Roster(), A, 22, lease)).Should().BeFalse();
        view.ForWindow(A).Should().BeNull();

        view.Observe(Wire("ipz", Roster(), A, 21, lease)).Should().BeTrue();
        attribution.Reset(4);
        view.ForWindow(A).Should().BeNull("previous Game lease was revoked");
    }

    [Fact]
    public void ClosedWindowExpiresProfessionsEvenWithoutNewPackets()
    {
        var (view, attribution, server) = Start();
        var lease = Load(attribution, server, A, 293, 21);
        view.Observe(Wire("ipz", Roster(), A, 21, lease)).Should().BeTrue();
        view.RemoveMissingWindows([B]);
        view.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void UnknownOrAmbiguousJobIdsRejectWholeRoster()
    {
        var (view, attribution, server) = Start();
        var lease = Load(attribution, server, A, 293, 21);
        byte[] unknown = Message(ProfessionPublicJobIdReference.ReferenceTwenty
            .Select((id, index) => Bytes(1, Message(
                Unsigned(1, (ulong)(index + 1)),
                Unsigned(5, index == 3 ? 999ul : id))))
            .ToArray());
        view.Observe(Wire("ipz", unknown, A, 21, lease))
            .Should().BeFalse();
        view.ForWindow(A).Should().BeNull();
    }

    [Fact]
    public void MonoCraftUpdatesExistingJobWithoutInventingOthers()
    {
        var (view, attribution, server) = Start();
        var lease = Load(attribution, server, A, 293, 21);
        view.Observe(Wire("ipz", Roster(), A, 21, lease));
        view.Observe(Wire("ipz", Single(2, 1, 150), A, 21, lease))
            .Should().BeTrue();
        view.ForWindow(A)!.Single(job => job.Name == "Bûcheron")
            .CumulativeExperience.Should().Be(150);

        view.Observe(Wire("ipz", Single(2, 1, 100), A, 21, lease))
            .Should().BeFalse("older XP must not overwrite a newer craft");
        view.Observe(Wire("ipz", Single(999, 1, 1000), A, 21, lease))
            .Should().BeFalse("unknown professions are not invented");
    }
}
