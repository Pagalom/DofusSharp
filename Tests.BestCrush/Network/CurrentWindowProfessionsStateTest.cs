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
    private static byte[] RosterWithOneJob(
        ulong targetJobId, int level, ulong totalXp) =>
        Message(ProfessionPublicJobIdReference.ReferenceTwenty
            .Select(id => Bytes(1, Message(
                Unsigned(1, (ulong)(id == targetJobId ? level : 1)),
                Unsigned(2, id == targetJobId ? totalXp : 0UL),
                Unsigned(5, id))))
            .ToArray());

    [Fact]
    public void WindowSpecificProfessionProgressUpdatesWithoutCrossAccountLeak()
    {
        var (view, attribution, server) = Start();
        var leaseA = Load(attribution, server, A, 293, 21);
        var leaseB = Load(attribution, server, B, 355, 31, characterId: 202);

        view.Observe(Wire("ipz", RosterWithOneJob(16, 8, 708),
            A, 21, leaseA)).Should().BeTrue();
        view.Observe(Wire("ipz", RosterWithOneJob(16, 3, 76),
            B, 31, leaseB)).Should().BeTrue();

        var firstA = view.ForWindow(A)!.Single(job => job.Name == "Bijoutier");
        firstA.LevelStartCumulativeExperience.Should().Be(560);
        firstA.NextLevelCumulativeExperience.Should().Be(720);
        ProfessionProgressPresentation.For(firstA)!.Value.Percent.Should().Be(92.5m);

        var firstB = view.ForWindow(B)!.Single(job => job.Name == "Bijoutier");
        firstB.LevelStartCumulativeExperience.Should().Be(60);
        firstB.NextLevelCumulativeExperience.Should().Be(120);
        ProfessionProgressPresentation.For(firstB)!.Value.Percent.Should().Be(26.67m);

        view.Observe(Wire("ipz", Single(16, 8, 715),
            A, 21, leaseA)).Should().BeTrue();
        var updatedA = view.ForWindow(A)!.Single(job => job.Name == "Bijoutier");
        ProfessionProgressPresentation.For(updatedA)!.Value.EarnedInLevel
            .Should().Be(155);
        ProfessionProgressPresentation.For(updatedA)!.Value.RequiredInLevel
            .Should().Be(160);

        view.Observe(Wire("ipz", Single(16, 9, 725),
            A, 21, leaseA)).Should().BeTrue();
        var levelUpA = view.ForWindow(A)!.Single(job => job.Name == "Bijoutier");
        levelUpA.LevelStartCumulativeExperience.Should().Be(720);
        levelUpA.NextLevelCumulativeExperience.Should().Be(900);
        ProfessionProgressPresentation.For(levelUpA)!.Value.EarnedInLevel
            .Should().Be(5);

        var unchangedB = view.ForWindow(B)!.Single(job => job.Name == "Bijoutier");
        unchangedB.CumulativeExperience.Should().Be(76);
        unchangedB.Level.Should().Be(3);
        ProfessionProgressPresentation.For(unchangedB)!.Value.Percent
            .Should().Be(26.67m);
    }

    [Fact]
    public void ContradictoryXpCannotCreateAFalseProgressBar()
    {
        var (view, attribution, server) = Start();
        var lease = Load(attribution, server, A, 293, 21);

        view.Observe(Wire("ipz", RosterWithOneJob(16, 8, 800),
            A, 21, lease)).Should().BeTrue();

        var invalid = view.ForWindow(A)!.Single(job => job.Name == "Bijoutier");
        invalid.Level.Should().Be(8);
        invalid.CumulativeExperience.Should().Be(800);
        invalid.LevelStartCumulativeExperience.Should().BeNull();
        invalid.NextLevelCumulativeExperience.Should().BeNull();
        ProfessionProgressPresentation.For(invalid).Should().BeNull();

        // A later authoritative update with a consistent level repairs
        // the view. Nothing was written into account-level state.
        view.Observe(Wire("ipz", Single(16, 9, 825),
            A, 21, lease)).Should().BeTrue();
        var repaired = view.ForWindow(A)!.Single(job => job.Name == "Bijoutier");
        repaired.LevelStartCumulativeExperience.Should().Be(720);
        repaired.NextLevelCumulativeExperience.Should().Be(900);
        ProfessionProgressPresentation.For(repaired)!.Value.EarnedInLevel
            .Should().Be(105);
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
    }

}
