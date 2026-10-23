using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>P3b: exclusively synthetic profession data. No user information.</summary>
public sealed class ProfessionContinuityStudyTest
{
    private static byte[] Ipz(
        int changedRow = -1, string change = "", bool reverse = false)
    {
        IEnumerable<int> order = Enumerable.Range(0, 20);
        if (reverse)
            order = order.Reverse();

        byte[][] entries = order.Select(i =>
        {
            ulong id = (ulong)(301 + i);
            int level = 5 + i;
            ulong xp = (ulong)(1000 + i * 17);
            if (i == changedRow)
            {
                if (change == "id") id += 100;
                if (change == "duplicate-id") id = 301;
                if (change == "level") level++;
                if (change == "xp") xp++;
            }

            return Bytes(1, Message(
                Unsigned(1, (ulong)level),
                Unsigned(2, xp),
                i == changedRow && change == "missing-id"
                    ? [] : Unsigned(5, id)));
        }).ToArray();
        return Message(entries);
    }

    [Fact]
    public void FirstValidSnapshotCreatesOnlyAnAnonymousEphemeralBaseline()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz()).Should().Be(
            "p3b=ipz decoded=yes rows=20 f5-valid=yes baseline=created " +
            "scope=account-server account=unverified server=unverified mapping=unverified");
    }

    [Fact]
    public void SequentialAtoBtoAEqualProfessionsNeverConfirmAccount()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz()).Should().Contain("baseline=created");

        string comparison = study.Observe(Ipz());
        comparison.Should().Contain(
            "baseline=compared f5-set-equal=yes levels-equal=yes xp-equal=yes");
        comparison.Should().Contain("account=unverified server=unverified");

        study.Observe(Ipz(reverse: true)).Should().Contain(
            "baseline=compared f5-set-equal=yes levels-equal=yes xp-equal=yes");
    }

    [Fact]
    public void ChangedExperienceIsDetectedWithoutPrintingIt()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz());
        string result = study.Observe(Ipz(5, "xp"));
        result.Should().Contain(
            "f5-set-equal=yes levels-equal=yes xp-equal=no");
        result.Should().NotContain("1100");
    }

    [Fact]
    public void ChangedProfessionLevelDoesNotRequireChangedIds()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz());
        study.Observe(Ipz(5, "level")).Should().Contain(
            "f5-set-equal=yes levels-equal=no xp-equal=yes");
    }

    [Fact]
    public void ChangedProfessionIdSetCannotBeMisreportedAsSameAccount()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz());
        study.Observe(Ipz(5, "id")).Should().Contain(
            "f5-set-equal=no levels-equal=unavailable xp-equal=unavailable " +
            "scope=account-server account=unverified");
    }

    [Fact]
    public void MissingOrDuplicateIdsCannotCreateOrReplaceBaseline()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz(5, "missing-id")).Should().Contain(
            "f5-valid=no baseline=unchanged");
        study.Observe(Ipz(5, "duplicate-id")).Should().Contain(
            "f5-valid=no baseline=unchanged");
        study.Observe(Ipz()).Should().Contain("baseline=created");
        study.Observe(Ipz(6, "missing-id")).Should().Contain(
            "f5-valid=no baseline=unchanged");
        study.Observe(Ipz()).Should().Contain(
            "baseline=compared f5-set-equal=yes");
    }

    [Fact]
    public void MalformedBodyDoesNotResetBaseline()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz());
        study.Observe([0x80]).Should().Contain(
            "decoded=no baseline=unchanged");
        study.Observe(Ipz()).Should().Contain(
            "baseline=compared f5-set-equal=yes");
    }

    [Fact]
    public void NewStudyHasNoPreviousComparisonOrPersistedState()
    {
        new ProfessionContinuityStudy().Observe(Ipz())
            .Should().Contain("baseline=created");
        new ProfessionContinuityStudy().Observe(Ipz())
            .Should().Contain("baseline=created");
    }

    [Fact]
    public void TwoDifferentConnectionsCompareAnonymouslyButRemainUnverified()
    {
        var tracker = new CharacterCaptureFlowTracker(studyProfessionContinuity: true);
        DateTime now = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var first = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        var second = new CharacterCaptureFlowKey(
            "127.0.0.1", 40002, "10.0.0.1", 5555);

        var baseline = tracker.Observe(
            first, CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("ipz", Ipz())), now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        baseline.ProfessionContinuitySummary.Should().Contain("baseline=created");

        var comparison = tracker.Observe(
            second, CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("ipz", Ipz())), now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        comparison.ProfessionContinuitySummary.Should().Contain(
            "baseline=compared f5-set-equal=yes levels-equal=yes xp-equal=yes");
        comparison.ProfessionContinuitySummary.Should().Contain(
            "account=unverified server=unverified");
        comparison.ProfessionContinuitySummary.Should().NotContain("1000");
        comparison.ProfessionContinuitySummary.Should().NotContain("301");
    }

    [Fact]
    public void ClientDirectionAndOtherOpCodesCannotChangeBaseline()
    {
        var tracker = new CharacterCaptureFlowTracker(studyProfessionContinuity: true);
        DateTime now = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var key = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        byte[] forged = Frame(Any("ipz", Ipz(5, "xp")));
        byte[] normal = Frame(Any("ipz", Ipz()));
        var fake = tracker.Observe(key, CharacterCaptureDirection.ClientToServer,
            100, forged, now).Single(e => e.Kind == CharacterCaptureEventKind.Message);
        fake.ProfessionContinuitySummary.Should().BeNull();

        var first = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("ksb", [1, 2, 3])), now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        first.ProfessionContinuitySummary.Should().BeNull();

        // Continue server-to-client framing at the correct TCP sequence.
        var baseline = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + Frame(Any("ksb", [1, 2, 3])).Length),
            normal, now.AddSeconds(2))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        baseline.ProfessionContinuitySummary.Should().Contain("baseline=created");
    }

    [Fact]
    public void AtoBtoAWhenFinalActuallyMatchesBIsReportedAsSuch()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz()).Should().Contain("baseline=created");

        string b = study.Observe(Ipz(5, "level"));
        b.Should().Contain("levels-equal=no xp-equal=yes");
        b.Should().Contain("previous-levels-equal=no previous-xp-equal=yes");

        string a2 = study.Observe(Ipz(5, "level"));
        a2.Should().Contain("levels-equal=no xp-equal=yes");
        a2.Should().Contain("previous-f5-set-equal=yes");
        a2.Should().Contain("previous-levels-equal=yes previous-xp-equal=yes");
        a2.Should().Contain("account=unverified server=unverified");
    }

    [Fact]
    public void AtoBtoAWhenFinalReturnsToBaselineDetectsRecovery()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz());
        study.Observe(Ipz(5, "xp"));

        string a2 = study.Observe(Ipz());
        a2.Should().Contain("levels-equal=yes xp-equal=yes");
        a2.Should().Contain("previous-levels-equal=yes previous-xp-equal=no");
        a2.Should().Contain("account=unverified");
    }

    [Fact]
    public void MalformedOrNonUniqueIntermediateIpzCannotBecomePrevious()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz());
        study.Observe([0x80]).Should().Contain("baseline=unchanged");
        study.Observe(Ipz(4, "duplicate-id"))
            .Should().Contain("baseline=unchanged");

        string b = study.Observe(Ipz(5, "xp"));
        b.Should().Contain("previous-levels-equal=yes previous-xp-equal=no");
        string a2 = study.Observe(Ipz(5, "xp"));
        a2.Should().Contain("previous-levels-equal=yes previous-xp-equal=yes");
    }

    [Fact]
    public void DifferentCandidateIdsMakePreviousLevelAndXpUnavailable()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz());
        string b = study.Observe(Ipz(5, "id"));
        b.Should().Contain("previous-f5-set-equal=no");
        b.Should().Contain("previous-levels-equal=unavailable previous-xp-equal=unavailable");

        string a2 = study.Observe(Ipz(5, "id"));
        a2.Should().Contain("previous-f5-set-equal=yes");
        a2.Should().Contain("previous-levels-equal=yes previous-xp-equal=yes");
        a2.Should().Contain("f5-set-equal=no levels-equal=unavailable xp-equal=unavailable");
    }

    [Fact]
    public void PreviousComparisonDoesNotExposeProfessionValuesOrAccountIds()
    {
        var study = new ProfessionContinuityStudy();
        study.Observe(Ipz());
        string report = study.Observe(Ipz(5, "level"));
        report.Should().Contain("previous-levels-equal=no");
        report.Should().NotContain("1100");
        report.Should().NotContain("301");
        report.Should().NotContain("account-A");
        report.Should().Contain("account=unverified server=unverified mapping=unverified");
    }

    [Fact]
    public void StandaloneContinuityRejectsMixedDiagnosticModes()
    {
        Action mixed = () => new CharacterCaptureFlowTracker(
            studyProfessionFlow: true, studyProfessionContinuity: true);
        mixed.Should().Throw<ArgumentException>();
    }
}
