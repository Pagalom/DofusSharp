using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>Only synthetic values. No actual character data or raw captures.</summary>
public sealed class ProfessionIpzStudyTest
{
    private static readonly ProfessionStudyReference[] References =
    [
        new("Alchimiste", 82, 30000, 150, 1640),
        new("Tailleur", 63, 19000, 245, 1260),
        new("Bûcheron", 44, 9000, 190, 880),
        new("Paysan", 33, 5000, 120, 660),
        new("Chasseur", 22, 2800, 90, 440),
        new("Éleveur", 17, 1400, 55, 340)
    ];

    private static byte[] Row(int index, byte[]? extra = null, bool wrongLower = false)
    {
        int level;
        ulong total;
        ulong lower;
        ulong upper;
        if (index < References.Length)
        {
            ProfessionStudyReference reference = References[index];
            level = reference.Level;
            total = reference.TotalExperience;
            lower = reference.LowerThreshold + (wrongLower ? 1UL : 0UL);
            upper = reference.UpperThreshold;
        }
        else if (index < 14)
        {
            level = index + 100;
            total = (ulong)(70000 + index * 1000);
            lower = total - 10;
            upper = total + 2000;
        }
        else
        {
            level = 1;
            total = index == 14 ? 2UL : 0UL;
            lower = 0;
            upper = 20;
        }

        return Message(
            Unsigned(1, (ulong)level),
            total == 0 ? [] : Unsigned(2, total),
            lower == 0 ? [] : Unsigned(3, lower),
            extra ?? [],
            Unsigned(5, (ulong)(1000 + index)),
            Unsigned(6, upper));
    }

    private static byte[] Sample(byte[]? f4 = null, bool wrongLower = false)
    {
        return Message(Enumerable.Range(0, 20)
            .Select(index => Bytes(1, Row(index,
                index == 0 ? f4 : null,
                wrongLower && index == 0))).ToArray());
    }

    [Fact]
    public void ParsesNamedReferencesAndRejectsBadInput()
    {
        ProfessionStudyReferenceInput.TryParse(
            "Alchimiste:82:30000:150:1640,Tailleur:63:19000:245:1260",
            out var references).Should().BeTrue();
        references.Should().HaveCount(2);
        references[0].LowerThreshold.Should().Be(29850);
        references[0].UpperThreshold.Should().Be(31490);
        references[0].ToString().Should().NotContain("30000");

        ProfessionStudyReferenceInput.TryParse(
            "Alchimiste:82:30000:150:1640,Alchimiste:63:19000:245:1260",
            out _).Should().BeFalse();
        ProfessionStudyReferenceInput.TryParse(
            "Alchimiste:82:30000:1640:1640,Tailleur:63:19000:245:1260",
            out _).Should().BeFalse();
        ProfessionStudyReferenceInput.TryParse(
            "Unknown:82:30000:150:1640,Tailleur:63:19000:245:1260",
            out _).Should().BeFalse();
    }

    [Fact]
    public void ValidatesSixDistinctPairsAndBothThresholdHypotheses()
    {
        string result = ProfessionIpzStudy.Analyze(Sample(), References);

        result.Should().Contain("rows=20 pairs=6/6 ambiguous=0");
        result.Should().Contain("f3-lower=6/6");
        result.Should().Contain("f6-upper=6/6");
        result.Should().Contain("f4=rows:0/20,v:0,len:0,32:0,64:0");
        result.Should().Contain("f5=present:20/20,distinct:20");
        result.Should().Contain("candidate-links=6/6");
        result.Should().Contain("mapping=unverified");
        result.Should().NotContain("Alchimiste:");
        result.Should().NotContain("30000");
        result.Should().NotContain("1000");
    }

    [Fact]
    public void F4IsInspectedAcrossEveryWireType()
    {
        byte[] withLength = Sample(Bytes(4, [0x08, 0x01]));
        ProfessionIpzStudy.Analyze(withLength, References)
            .Should().Contain("f4=rows:1/20,v:0,len:1,32:0,64:0");

        byte[] withVarint = Sample(Unsigned(4, 77));
        ProfessionIpzStudy.Analyze(withVarint, References)
            .Should().Contain("f4=rows:1/20,v:1,len:0,32:0,64:0");

        byte[] withFixed = Sample(Float(4, 3.75f));
        ProfessionIpzStudy.Analyze(withFixed, References)
            .Should().Contain("f4=rows:1/20,v:0,len:0,32:1,64:0");
    }

    [Fact]
    public void CandidateNumbersRequireExplicitOptInAndVerifiedThresholds()
    {
        string hidden = ProfessionIpzStudy.Analyze(Sample(), References);
        hidden.Should().NotContain("CANDIDATE_IDS");
        hidden.Should().NotContain("f5=1000");

        string shown = ProfessionIpzStudy.Analyze(Sample(), References, true);
        shown.Should().Contain("CANDIDATE_IDS=[Alchimiste:f5=1000;");
        shown.Should().Contain("Tailleur:f5=1001");
        shown.Should().Contain("Éleveur:f5=1005");
        shown.Should().Contain("mapping=unverified");
        shown.Should().NotContain("30000");
    }

    [Fact]
    public void WrongF3PreventsAnIndividualCandidateIdFromBeingShown()
    {
        string output = ProfessionIpzStudy.Analyze(
            Sample(wrongLower: true), References, true);

        output.Should().Contain("f3-lower=5/6");
        output.Should().Contain("f6-upper=6/6");
        output.Should().Contain("candidate-links=5/6");
        output.Should().NotContain("Alchimiste:f5=");
        output.Should().Contain("Tailleur:f5=1001");
    }

    [Fact]
    public void RepeatedLevelOneZeroXpRowsStayAmbiguousNotInvented()
    {
        string result = ProfessionIpzStudy.Analyze(Sample(), References, true);

        result.Should().Contain("candidate-links=6/6");
        result.Should().NotContain("f5=1015");
        result.Should().NotContain("f5=1019");
    }

    [Fact]
    public void MalformedWireYieldsOnlySafeRejectionReason()
    {
        string result = ProfessionIpzStudy.Analyze([0x00, 0x00], References, true);
        result.Should().Be("rejected-layout:invalid-root");
    }

    [Fact]
    public void InvalidReferenceNameCannotBeInjectedIntoCandidateOutput()
    {
        var unsafeRefs = new ProfessionStudyReference[]
        {
            new("Alchimiste\nINJECT", 82, 30000, 150, 1640),
            References[1]
        };
        Action action = () =>
            ProfessionIpzStudy.Analyze(Sample(), unsafeRefs, true);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackerIsIsolatedPerSessionAndCannotMixCandidateIds()
    {
        var tracker = new CharacterCaptureFlowTracker(
            studyReferences: References, showCandidateJobIds: false);
        DateTime now = new(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        byte[] frame = Frame(Any("ipz", Sample()));
        CharacterCaptureFlowKey first = new(
            "127.0.0.1", 40001, "10.0.0.10", 5555);
        CharacterCaptureFlowKey second = new(
            "127.0.0.1", 40002, "10.0.0.10", 5555);

        var a = tracker.Observe(first,
            CharacterCaptureDirection.ServerToClient, 100, frame, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        var b = tracker.Observe(second,
            CharacterCaptureDirection.ServerToClient, 100, frame, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        a.SessionNumber.Should().NotBe(b.SessionNumber);
        a.ProfessionStudySummary.Should().Contain("candidate-links=6/6");
        b.ProfessionStudySummary.Should().Contain("candidate-links=6/6");
        a.ProfessionStudySummary.Should().NotContain("Alchimiste:");
        a.ProfessionIpzSummary.Should().BeNull();
        a.ProfessionCandidates.Should().BeNull();
    }

    [Fact]
    public void ClientDirectionAndOtherOpcodesNeverProduceStudyOutput()
    {
        var tracker = new CharacterCaptureFlowTracker(
            studyReferences: References);
        DateTime now = new(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        CharacterCaptureFlowKey flow = new(
            "127.0.0.1", 40001, "10.0.0.10", 5555);
        byte[] first = Frame(Any("ipz", Sample()));
        byte[] second = Frame(Any("other", Sample()));

        var client = tracker.Observe(flow,
            CharacterCaptureDirection.ClientToServer, 100, first, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        var other = tracker.Observe(flow,
            CharacterCaptureDirection.ServerToClient, 100, second, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        client.ProfessionStudySummary.Should().BeNull();
        other.ProfessionStudySummary.Should().BeNull();
    }
}
