using BestCrush.Services;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// Fabricated protocol data only. These tests must not confer account or
/// character identity or perform any economic persistence.
/// </summary>
public sealed class ProfessionGameLinkStudyTest
{
    private static readonly DofusClientWindowKey TyleziaWindow = new(111, 1111);
    private static readonly DofusClientWindowKey KourialWindow = new(222, 2222);

    private static byte[] Roster() =>
        Message(Enumerable.Range(1, 20)
            .Select(index => Bytes(1, Message(
                Unsigned(1, (ulong)index),
                Unsigned(2, (ulong)index * 111))))
            .ToArray());

    private static DofusWireMessage Wire(
        DofusClientWindowKey? window,
        NetworkCaptureLease? lease,
        byte[]? body = null,
        long epoch = 3,
        long connectionId = 21,
        string key = "ipz",
        string direction = "S→C") =>
        new(direction, key, body ?? Roster(),
            new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
            lease, epoch, ConnectionId: connectionId,
            CandidateWindow: window, SyntheticWindowForReplay: true);

    private static NetworkCaptureLease Lease(
        string server, DofusClientWindowKey window,
        long epoch = 3, long connectionId = 21) =>
        new(server, 7, window, epoch, connectionId);

    [Fact]
    public void VerifiedGameIpzHasAnonymousWindowSlotAndOnlyPublicServerName()
    {
        ProfessionGameLinkStudy study = new();
        var window = TyleziaWindow;
        var message = Wire(window, Lease("Tylezia", window));

        string? output = study.Observe(message, activeGameLease: true);
        output.Should().Be(
            "p3d=ipz link=verified-game slot=1 server=Tylezia shape=roster20 account=unverified mapping=unverified");
        output.Should().NotContain("1111");
        output.Should().NotContain("222");
        output.Should().NotContain("Pagalom");
    }

    [Fact]
    public void DifferentClientsStaySeparateAndRepeatedClientKeepsItsSlot()
    {
        ProfessionGameLinkStudy study = new();
        string first = study.Observe(
            Wire(TyleziaWindow, Lease("Tylezia", TyleziaWindow)), true)!;
        string second = study.Observe(
            Wire(KourialWindow, Lease("Kourial", KourialWindow)), true)!;
        string repeat = study.Observe(
            Wire(TyleziaWindow, Lease("Tylezia", TyleziaWindow)), true)!;

        first.Should().Contain("slot=1 server=Tylezia");
        second.Should().Contain("slot=2 server=Kourial");
        repeat.Should().Contain("slot=1 server=Tylezia");
    }

    [Fact]
    public void MissingInactiveOrMismatchedGameEvidenceNeverCreatesVerifiedLink()
    {
        ProfessionGameLinkStudy study = new();
        var valid = Lease("Tylezia", TyleziaWindow);
        var cases = new[]
        {
            Wire(TyleziaWindow, valid),
            Wire(TyleziaWindow, null),
            Wire(null, valid),
            Wire(KourialWindow, valid),
            Wire(TyleziaWindow, Lease("Tylezia", TyleziaWindow, connectionId: 99)),
            Wire(TyleziaWindow, Lease("Tylezia", TyleziaWindow, epoch: 8))
        };

        foreach (DofusWireMessage message in cases)
        {
            bool active = !ReferenceEquals(message, cases[0]);
            string? output = study.Observe(message, active);
            output.Should().Contain("link=unverified slot=none server=unverified");
        }
    }

    [Fact]
    public void OtherIpzShapeIsDetectedWithoutWritingPrivateFields()
    {
        ProfessionGameLinkStudy study = new();
        string? output = study.Observe(
            Wire(TyleziaWindow, Lease("Tylezia", TyleziaWindow),
                Message(Bytes(1, Message(Unsigned(1, 8), Unsigned(2, 987654))))),
            true);

        output.Should().Contain("shape=other");
        output.Should().NotContain("987654");
    }

    [Fact]
    public void NonProfessionTrafficIsIgnoredAndNewEpochResetsWindowSlots()
    {
        ProfessionGameLinkStudy study = new();
        study.Observe(Wire(TyleziaWindow, Lease("Tylezia", TyleziaWindow), key: "ksb"),
            true).Should().BeNull();
        study.Observe(Wire(TyleziaWindow, Lease("Tylezia", TyleziaWindow),
            direction: "C→S"), true).Should().BeNull();

        study.Observe(Wire(TyleziaWindow, Lease("Tylezia", TyleziaWindow)), true)
            .Should().Contain("slot=1");

        study.Observe(Wire(KourialWindow,
                Lease("Kourial", KourialWindow, epoch: 4),
                epoch: 4),
            true).Should().Contain("slot=1 server=Kourial");

        study.Observe(Wire(TyleziaWindow,
                Lease("Tylezia", TyleziaWindow, epoch: 3),
                epoch: 3),
            true).Should().BeNull("delayed packets cannot re-open older captures");
    }
}
