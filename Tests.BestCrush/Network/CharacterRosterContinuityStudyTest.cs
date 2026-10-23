using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3c.5, synthetic candidate character IDs only. This study does not
/// authenticate an Ankama account or emit values derived from characters.
/// </summary>
public sealed class CharacterRosterContinuityStudyTest
{
    private const ulong X = 9000101;
    private const ulong Y = 9000102;
    private const ulong Z = 9000201;

    private static byte[] Leaf(string name, int level) =>
        Message(Unsigned(3, (ulong)level),
            Bytes(6, Encoding.UTF8.GetBytes(name)));

    private static byte[] Row(ulong id, string name, int level) =>
        Message(Unsigned(3, id), Bytes(4, Leaf(name, level)));

    private static byte[] Ksc(bool accountA, bool reverse = false,
        bool overlap = false)
    {
        byte[][] rows = accountA
            ? [Bytes(2, Row(X, "Sable-Ciel", 125)),
               Bytes(2, Row(Y, "Roche-Lune", 2))]
            : overlap
                ? [Bytes(2, Row(X, "Sable-Ciel", 125)),
                   Bytes(2, Row(Z, "Hiver-Rouge", 1))]
                : [Bytes(2, Row(Z, "Hiver-Rouge", 1))];
        if (reverse)
            Array.Reverse(rows);
        return Message(rows);
    }

    private static byte[] Ksb(ulong id) => id switch
    {
        X => Bytes(1, Bytes(1, Row(X, "Sable-Ciel", 125))),
        Y => Bytes(1, Bytes(1, Row(Y, "Roche-Lune", 2))),
        _ => Bytes(1, Bytes(1, Row(Z, "Hiver-Rouge", 1)))
    };

    private static void Phase(
        CharacterRosterContinuityStudy study,
        int phase, int gameSession, byte[] roster, ulong selected)
    {
        study.StartPhase(phase).Should().BeTrue();
        study.ObserveOpenedSession(gameSession);
        study.Observe(gameSession,
            CharacterCaptureDirection.ServerToClient, "ksc", roster)
            .Should().Contain("status=pending");
        study.Observe(gameSession,
            CharacterCaptureDirection.ServerToClient, "ksb", Ksb(selected))
            .Should().Contain("status=linked");
    }

    private static CharacterRosterContinuityStudy Complete(
        bool reverseSecond = false,
        bool sameRosterOnB = false, bool overlappingB = false,
        ulong finalSelected = X)
    {
        CharacterRosterContinuityStudy study = new();
        Phase(study, 0, 2, Ksc(true), X);
        Phase(study, 1, 4, Ksc(true, reverseSecond), Y);
        Phase(study, 2, 6,
            Ksc(sameRosterOnB, overlap: overlappingB),
            sameRosterOnB ? X : Z);
        Phase(study, 3, 8, Ksc(true), finalSelected);
        return study;
    }

    [Fact]
    public void RealisticAtoAOtherToBtoAProducesOnlyRosterGroupCandidate()
    {
        var study = Complete();
        string summary = study.Summarize().Last();
        summary.Should().Contain("status=roster-group-candidate");
        summary.Should().Contain("same-account-rosters=yes");
        summary.Should().Contain("cross-account-distinct=yes");
        summary.Should().Contain("cross-account-disjoint=yes");
        summary.Should().Contain("selected-x-y-distinct=yes");
        summary.Should().Contain("selected-x-return-equal=yes");
        summary.Should().Contain("account=unverified server=unverified semantic=unverified");
    }

    [Fact]
    public void RosterCandidateNeverExposesPrivateNamesIdsLevelsOrHashes()
    {
        string summary = string.Join("\n", Complete().Summarize());
        summary.Should().NotContain("9000101");
        summary.Should().NotContain("9000201");
        summary.Should().NotContain("Sable-Ciel");
        summary.Should().NotContain("Roche-Lune");
        summary.Should().NotContain("Hiver-Rouge");
        summary.Should().NotContain("125");
        summary.Should().NotContain("hash");
    }

    [Fact]
    public void CharacterOrderWithinSameRosterIsNotAccountIdentity()
    {
        Complete(reverseSecond: true).Summarize().Last()
            .Should().Contain("status=roster-group-candidate");
    }

    [Fact]
    public void SameRosterOnDifferentOperatorAccountFailsNegativeControl()
    {
        string summary = Complete(sameRosterOnB: true).Summarize().Last();
        summary.Should().Contain("status=pattern-not-confirmed");
        summary.Should().Contain("cross-account-distinct=no");
        summary.Should().Contain("cross-account-disjoint=no");
    }

    [Fact]
    public void OverlappingAccountRostersFailConservativeIsolation()
    {
        string summary = Complete(overlappingB: true).Summarize().Last();
        summary.Should().Contain("status=pattern-not-confirmed");
        summary.Should().Contain("cross-account-distinct=yes");
        summary.Should().Contain("cross-account-disjoint=no");
    }

    [Fact]
    public void SameSelectedXandYFailsPositiveDistinctCharacterControl()
    {
        CharacterRosterContinuityStudy study = new();
        Phase(study, 0, 2, Ksc(true), X);
        Phase(study, 1, 4, Ksc(true), X);
        Phase(study, 2, 6, Ksc(false), Z);
        Phase(study, 3, 8, Ksc(true), X);

        string summary = study.Summarize().Last();
        summary.Should().Contain("status=pattern-not-confirmed");
        summary.Should().Contain("selected-x-y-distinct=no");
    }

    [Fact]
    public void ReturnToDifferentCharacterFailsStabilityControl()
    {
        string summary = Complete(finalSelected: Y).Summarize().Last();
        summary.Should().Contain("status=pattern-not-confirmed");
        summary.Should().Contain("selected-x-return-equal=no");
    }

    [Fact]
    public void MissingKsbKeepsEntireExperimentIncomplete()
    {
        CharacterRosterContinuityStudy study = new();
        study.StartPhase(0);
        study.ObserveOpenedSession(2);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(true)).Should().Contain("status=pending");

        study.Summarize()[0].Should().Contain("status=incomplete");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void KsbNotInKscFailsClosed()
    {
        CharacterRosterContinuityStudy study = new();
        study.StartPhase(0);
        study.ObserveOpenedSession(2);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(true));
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(Z)).Should().Contain("status=conflicted");

        study.Summarize()[0].Should().Contain("reason=selection-not-in-roster");
    }

    [Fact]
    public void MalformedListCannotBeReplacedByAnotherTcpGame()
    {
        CharacterRosterContinuityStudy study = new();
        study.StartPhase(0);
        study.ObserveOpenedSession(2);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", [0x80]).Should().Contain("status=conflicted");

        study.ObserveOpenedSession(3);
        study.Observe(3, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(true)).Should().Contain("status=conflicted");
        study.Summarize()[0].Should().Contain("reason=layout-rejected");
    }

    [Fact]
    public void TwoGameSessionsCannotCombineRosterAndSelectedCharacter()
    {
        CharacterRosterContinuityStudy study = new();
        study.StartPhase(0);
        study.ObserveOpenedSession(2);
        study.ObserveOpenedSession(3);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(true)).Should().Contain("status=pending");
        study.Observe(3, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(X)).Should().Contain("status=conflicted");
        study.Summarize()[0].Should().Contain("reason=multiple-game-sessions");
    }

    [Fact]
    public void ClientToServerAndUnrelatedOpcodesNeverCreateRoster()
    {
        CharacterRosterContinuityStudy study = new();
        study.StartPhase(0);
        study.ObserveOpenedSession(2);
        study.Observe(2, CharacterCaptureDirection.ClientToServer,
            "ksc", Ksc(true)).Should().BeNull();
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "knc", Ksc(true)).Should().BeNull();
        study.Summarize()[0].Should().Contain("status=incomplete");
    }

    [Fact]
    public void PreviousPhaseGameCannotWriteIntoNewPhase()
    {
        CharacterRosterContinuityStudy study = new();
        Phase(study, 0, 2, Ksc(true), X);
        study.StartPhase(1);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(false)).Should().BeNull();
        study.Summarize()[1].Should().Contain("status=incomplete");
    }

    [Fact]
    public void SecondRosterWithDifferentMembersIsRejectedWithinGame()
    {
        CharacterRosterContinuityStudy study = new();
        study.StartPhase(0);
        study.ObserveOpenedSession(2);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(true)).Should().Contain("status=pending");
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(false)).Should().Contain("status=conflicted");
        study.Summarize()[0].Should().Contain("reason=roster-changed");
    }

    [Fact]
    public void NewStudyHasNoReusedAccountRoster()
    {
        Complete().Summarize().Last().Should().Contain("roster-group-candidate");
        new CharacterRosterContinuityStudy().Summarize().Last()
            .Should().Contain("status=incomplete");
    }

    [Fact]
    public void ResearchModeIsExclusiveFromAllOtherIdentificationModes()
    {
        CharacterRosterContinuityStudy study = new();
        Action mixed = () => new CharacterCaptureFlowTracker(
            accountWireStudy: new AccountWireFieldComparisonStudy(),
            rosterStudy: study);
        mixed.Should().Throw<ArgumentException>();

        Action mixedProfessions = () => new CharacterCaptureFlowTracker(
            studyProfessionContinuity: true, rosterStudy: study);
        mixedProfessions.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RealTrackerPropagatesMatchedKscAndKsbOnSameTcpSession()
    {
        CharacterRosterContinuityStudy study = new();
        study.StartPhase(0);
        CharacterCaptureFlowTracker tracker = new(rosterStudy: study);
        CharacterCaptureFlowKey flow = new(
            "127.0.0.1", 40101, "10.0.0.1", 5555);
        DateTime time = new(2026, 10, 23, 13, 30, 0, DateTimeKind.Utc);
        byte[] roster = Frame(Any("ksc", Ksc(true)));
        byte[] selected = Frame(Any("ksb", Ksb(X)));

        CharacterCaptureEvent first = tracker.Observe(
            flow, CharacterCaptureDirection.ServerToClient,
            100, roster, time)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        CharacterCaptureEvent second = tracker.Observe(
            flow, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + roster.Length), selected, time.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        first.RosterContinuitySummary.Should().Contain("status=pending");
        second.RosterContinuitySummary.Should().Contain("status=linked");
        second.RosterContinuitySummary.Should().NotContain("9000101");
        study.Summarize()[0].Should().Contain("roster-rows=2");
    }
}
