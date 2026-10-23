using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3c.4: synthetic messages only. Numeric values are fixtures and never
/// real account IDs; matching a path never grants verified account identity.
/// </summary>
public sealed class AccountWireFieldComparisonStudyTest
{
    private static byte[] Krg(
        ulong character, ulong accountCandidate, ulong server = 295) =>
        Message(Unsigned(2, character),
            Unsigned(11, accountCandidate),
            Bytes(10, Message(Unsigned(3, server))));

    private static string Read(
        AccountWireFieldComparisonStudy study, int session,
        ulong character, ulong accountCandidate)
    {
        study.ObserveOpenedSession(session);
        return study.Observe(session,
            CharacterCaptureDirection.ServerToClient,
            "krg", Krg(character, accountCandidate))!;
    }

    private static AccountWireFieldComparisonStudy Complete()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0).Should().BeTrue();
        Read(study, 2, 101, 7301).Should().Contain("status=observed");
        study.StartPhase(1).Should().BeTrue();
        Read(study, 4, 102, 7301).Should().Contain("status=observed");
        study.StartPhase(2).Should().BeTrue();
        Read(study, 6, 201, 7402).Should().Contain("status=observed");
        study.StartPhase(3).Should().BeTrue();
        Read(study, 8, 101, 7301).Should().Contain("status=observed");
        return study;
    }

    [Fact]
    public void FourPhaseStudyReportsOnlyAccountShapedWirePaths()
    {
        string summary = Complete().Summarize().Last();
        summary.Should().Contain("status=compared");
        summary.Should().Contain("account-shaped-paths=[krg:f11]");
        summary.Should().NotContain("krg:f2");
        summary.Should().NotContain("krg:f10.f3");
        summary.Should().Contain("account=unverified server=unverified");
    }

    [Fact]
    public void NeverPrintsRawCandidateNumbersOrAuthenticators()
    {
        string text = string.Join("\n", Complete().Summarize());
        text.Should().NotContain("7301");
        text.Should().NotContain("7402");
        text.Should().NotContain("295");
        text.Should().NotContain("101");
        text.Should().NotContain("201");
        text.Should().NotContain("account=verified");
    }

    [Fact]
    public void RepeatedACharacterOnOneAccountCannotReplaceDifferentCharacterControl()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        Read(study, 2, 101, 7301);
        study.StartPhase(1);
        Read(study, 4, 101, 7301);
        study.StartPhase(2);
        Read(study, 6, 201, 7402);
        study.StartPhase(3);
        Read(study, 8, 101, 7301);

        // Equal paths are only wire candidates. The diagnostic cannot
        // know whether the operator actually changed characters.
        study.Summarize().Last().Should().Contain("semantic=unverified");
    }

    [Fact]
    public void MissingPhaseFailsClosedWithoutAccountCandidates()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        Read(study, 2, 101, 7301);

        study.Summarize().Last().Should().Contain("status=incomplete");
        study.StartPhase(3).Should().BeFalse();
        study.StartPhase(1).Should().BeTrue();
    }

    [Fact]
    public void OnlyNewlyOpenedTcpSessionInCurrentPhaseCanSupplyKrg()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        Read(study, 2, 101, 7301);
        study.StartPhase(1);
        study.Observe(2, CharacterCaptureDirection.ServerToClient, "krg",
            Krg(201, 7402)).Should().BeNull();
        Read(study, 4, 102, 7301).Should().Contain("status=observed");
        study.Summarize()[1].Should().Contain("status=recorded");
    }

    [Fact]
    public void ClientToServerPayloadAndUnrelatedOpCodesAreIgnored()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        study.ObserveOpenedSession(2);
        study.Observe(2, CharacterCaptureDirection.ClientToServer, "krg",
            Krg(101, 7301)).Should().BeNull();
        study.Observe(2, CharacterCaptureDirection.ServerToClient, "knc",
            Krg(101, 7301)).Should().BeNull();
        study.Summarize()[0].Should().Contain("status=unavailable");
    }

    [Fact]
    public void TwoGameSessionsInSamePhaseCauseConflict()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        Read(study, 2, 101, 7301);
        Read(study, 3, 102, 7301)
            .Should().Contain("status=multiple-game-sessions");
        study.Summarize()[0].Should().Contain("status=conflicted");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void ChangingNumericFieldIsExcludedButPhaseRemainsAvailable()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        Read(study, 2, 101, 7301);
        study.Observe(2, CharacterCaptureDirection.ServerToClient, "krg",
            Krg(101, 7402)).Should().Contain("status=observed");

        string phase = study.Summarize()[0];
        phase.Should().Contain("status=recorded");
        phase.Should().Contain("fields=2");
        phase.Should().Contain("krg=2 unstable-dropped=1");
        phase.Should().Contain("account=unverified");
    }

    [Fact]
    public void SameSessionLoadingStagesKeepOnlySharedStablePaths()
    {
        AccountWireFieldComparisonStudy study = new();
        for (int phase = 0; phase < 4; phase++)
        {
            study.StartPhase(phase).Should().BeTrue();
            int session = phase + 1;
            ulong character = phase switch { 0 or 3 => 101, 1 => 102, _ => 201 };
            ulong account = phase == 2 ? 7402UL : 7301UL;

            study.ObserveOpenedSession(session);
            byte[] first = Message(
                Unsigned(2, character),
                Unsigned(11, account),
                Unsigned(12, (ulong)(8000 + phase)),
                Bytes(10, Message(Unsigned(3, 295))));
            study.Observe(session, CharacterCaptureDirection.ServerToClient,
                "krg", first).Should().Contain("status=observed");
            study.Observe(session, CharacterCaptureDirection.ServerToClient,
                "krg", Krg(character, account))
                .Should().Contain("status=observed");

            study.Summarize()[phase].Should().Contain("unstable-dropped=1");
        }

        string summary = study.Summarize().Last();
        summary.Should().Contain("status=compared");
        summary.Should().Contain("account-shaped-paths=[krg:f11]");
        summary.Should().NotContain("krg:f12");
        summary.Should().Contain("semantic=unverified");
    }

    [Fact]
    public void OnceDroppedChangingPathIsNeverReintroducedByLaterUpdate()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        Read(study, 2, 101, 7301);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "krg", Krg(101, 7402)).Should().Contain("status=observed");
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "krg", Krg(101, 7301)).Should().Contain("status=observed");

        study.Summarize()[0].Should().Contain("fields=2");
        study.Summarize()[0].Should().Contain("krg=3 unstable-dropped=1");
    }

    [Fact]
    public void NoFieldStableAcrossLoadingStagesFailsClosed()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        Read(study, 2, 101, 7301);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "krg", Krg(102, 7402, server: 355))
            .Should().Contain("status=no-stable-fields");

        study.Summarize()[0].Should().Contain("status=conflicted");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void MalformedSecondKrgInvalidatesPreviouslyValidPhase()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        Read(study, 2, 101, 7301);
        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "krg", [0x80]).Should().Contain("status=layout-rejected");

        study.Summarize()[0].Should().Contain("status=conflicted");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void UnboundedKrgMessagesFailClosedInsteadOfEvictingOlderEvidence()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        Read(study, 2, 101, 7301);
        for (int i = 1; i < 8; i++)
            study.Observe(2, CharacterCaptureDirection.ServerToClient,
                "krg", Krg(101, 7301)).Should().Contain("status=observed");

        study.Observe(2, CharacterCaptureDirection.ServerToClient,
            "krg", Krg(101, 7301)).Should().Contain("status=krg-limit");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void IdenticalRepeatInSameTcpSessionIsIdempotent()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        Read(study, 2, 101, 7301);
        study.Observe(2, CharacterCaptureDirection.ServerToClient, "krg",
            Krg(101, 7301)).Should().Contain("status=observed");
        study.Summarize()[0].Should().Contain("status=recorded");
    }

    [Fact]
    public void MalformedKrgDoesNotCreateUsablePhase()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        study.ObserveOpenedSession(2);
        study.Observe(2, CharacterCaptureDirection.ServerToClient, "krg",
            [0x80]).Should().Contain("status=layout-rejected");
        study.Summarize()[0].Should().Contain("status=unavailable");
    }

    [Fact]
    public void DiagnosticDoesNotMixWithOtherStudyModes()
    {
        AccountWireFieldComparisonStudy study = new();
        Action conflicting = () => new CharacterCaptureFlowTracker(
            studyProfessionContinuity: true, accountWireStudy: study);
        conflicting.Should().Throw<ArgumentException>();
        Action shapeConflict = () => new CharacterCaptureFlowTracker(
            shapeKeys: new HashSet<string> { "krg" }, accountWireStudy: study);
        shapeConflict.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LiveCaptureTrackerRoutesOnlyServerToClientKrgIntoStudy()
    {
        AccountWireFieldComparisonStudy study = new();
        study.StartPhase(0);
        CharacterCaptureFlowTracker tracker = new(accountWireStudy: study);
        CharacterCaptureFlowKey tcp = new(
            "127.0.0.1", 40101, "10.0.0.1", 5555);
        DateTime now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

        CharacterCaptureEvent observed = tracker.Observe(
            tcp, CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("krg", Krg(101, 7301))), now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        observed.AccountWireFieldSummary.Should().Contain("status=observed");
        observed.AccountWireFieldSummary.Should().NotContain("7301");

        CharacterCaptureEvent ignored = tracker.Observe(
            new CharacterCaptureFlowKey(
                "127.0.0.1", 40102, "10.0.0.1", 5555),
            CharacterCaptureDirection.ClientToServer,
            100, Frame(Any("krg", Krg(101, 9999))), now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        ignored.AccountWireFieldSummary.Should().BeNull();
    }

    [Fact]
    public void NoDistinguishingFieldYieldsNoCandidateAndNeverVerified()
    {
        AccountWireFieldComparisonStudy study = new();
        for (int phase = 0; phase < 4; phase++)
        {
            study.StartPhase(phase);
            Read(study, phase + 1, (ulong)(100 + phase), 7301);
        }

        study.Summarize().Last()
            .Should().Contain("account-shaped-paths=[none]");
        study.Summarize().Last().Should().Contain("account=unverified");
    }
}
