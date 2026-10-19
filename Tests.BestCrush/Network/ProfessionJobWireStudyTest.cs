using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3f fixtures are exclusively synthetic; no account, player, ID or packet
/// from a real session appears in the tests.
/// </summary>
public sealed class ProfessionJobWireStudyTest
{
    private static byte[] Single(
        ulong xp, int level, ulong candidate,
        ulong lower = 100, ulong upper = 900) =>
        Message(Bytes(1, Message(
            Unsigned(1, (ulong)level),
            Unsigned(2, xp),
            Unsigned(3, lower),
            Unsigned(5, candidate),
            Unsigned(6, upper))));

    private static ProfessionJobReference[] References =>
    [
        new(1, "Sculpteur", 6, 370),
        new(2, "Bricoleur", 12, 800),
        new(3, "Sculpteur", 6, 400),
        new(4, "Bricoleur", 12, 850)
    ];

    [Fact]
    public void DifferentJobsMayHaveNonMonotoneGlobalXp()
    {
        ProfessionJobReferenceInput.TryParse(
            "1:Bricoleur:12:800,2:Sculpteur:6:370,3:Bricoleur:12:850,4:Sculpteur:6:400",
            out IReadOnlyList<ProfessionJobReference> refs).Should().BeTrue();
        refs.Should().HaveCount(4);
        refs[0].ToString().Should().Be("[local profession reference: redacted]");
        ProfessionXpReferenceInput.TryParse(
            "1:12:800,2:6:370", out _).Should().BeFalse();
    }

    [Fact]
    public void FourAlternatingCraftsCanIdentifyAStableDifferentF5()
    {
        ProfessionXpWireStudy study = new();
        study.Observe(1, Single(370, 6, 301, 100, 900));
        study.Observe(1, Single(800, 12, 302, 200, 950));
        study.Observe(1, Single(400, 6, 301, 100, 900));
        study.Observe(1, Single(850, 12, 302, 200, 950));

        string result = study.CompareJobs(References);
        result.Should().Contain("p3f=compared references=4 professions=2");
        result.Should().Contain("level-match=yes xp-match=yes");
        result.Should().Contain("f1.f5-stable-distinct=yes");
        result.Should().Contain("candidate-paths=[f1.f3,f1.f5,f1.f6]");
        result.Should().Contain("candidate-status=ambiguous");
        result.Should().Contain("mapping=unverified");
        result.Should().NotContain("Sculpteur");
        result.Should().NotContain("Bricoleur");
        result.Should().NotContain("301");
        result.Should().NotContain("302");
        result.Should().NotContain("370");
        result.Should().NotContain("850");
    }

    [Fact]
    public void LevelChangeAcrossSameJobCanEliminateThresholdFields()
    {
        ProfessionXpWireStudy study = new();
        study.Observe(1, Single(370, 6, 301, 100, 900));
        study.Observe(1, Single(800, 12, 302, 200, 950));
        study.Observe(1, Single(400, 7, 301, 300, 1000));
        study.Observe(1, Single(850, 13, 302, 400, 1100));

        ProfessionJobReference[] refs =
        [
            new(1, "Sculpteur", 6, 370),
            new(2, "Bricoleur", 12, 800),
            new(3, "Sculpteur", 7, 400),
            new(4, "Bricoleur", 13, 850)
        ];

        string result = study.CompareJobs(refs);
        result.Should().Contain("candidate-paths=[f1.f5]");
        result.Should().Contain("candidate-status=single-candidate");
        result.Should().Contain("f1.f5-stable-distinct=yes");
    }

    [Fact]
    public void DifferentJobsWithSameF5AreNotConsideredDistinctIdentifiers()
    {
        ProfessionXpWireStudy study = new();
        study.Observe(1, Single(370, 6, 301, 100, 900));
        study.Observe(1, Single(800, 12, 301, 100, 900));
        study.Observe(1, Single(400, 7, 301, 300, 1000));
        study.Observe(1, Single(850, 13, 301, 400, 1100));

        ProfessionJobReference[] refs =
        [
            new(1, "Sculpteur", 6, 370),
            new(2, "Bricoleur", 12, 800),
            new(3, "Sculpteur", 7, 400),
            new(4, "Bricoleur", 13, 850)
        ];
        study.CompareJobs(refs)
            .Should().Contain("candidate-paths=[none]")
            .And.Contain("f1.f5-stable-distinct=no");
    }

    [Fact]
    public void XpMismatchRejectsJobMappingEvenIfF5Matches()
    {
        ProfessionXpWireStudy study = new();
        study.Observe(1, Single(370, 6, 301));
        study.Observe(1, Single(800, 12, 302));
        study.Observe(1, Single(400, 6, 301));
        study.Observe(1, Single(850, 12, 302));

        ProfessionJobReference[] mismatch =
        [
            References[0], References[1],
            References[2],
            new(4, "Bricoleur", 12, 851)
        ];
        study.CompareJobs(mismatch).Should()
            .Contain("p3f=references-not-confirmed")
            .And.Contain("xp-match=no")
            .And.Contain("candidate-paths=[none]")
            .And.NotContain("301");
    }

    [Fact]
    public void InvalidInputIsRejectedBeforeMapping()
    {
        ProfessionJobReferenceInput.TryParse(
            "1:Sculpteur:6:370,2:Bricoleur:12:800,3:Sculpteur:6:400",
            out _).Should().BeFalse();
        ProfessionJobReferenceInput.TryParse(
            "1:Sculpteur:6:370,2:Bricoleur:12:800,3:Sculpteur:6:400,4:Bricoleur:12:850",
            out _).Should().BeTrue();
        ProfessionJobReferenceInput.TryParse(
            "1:Sculpteur:6:370,2:Bricoleur:12:800,3:Sculpteur:6:300,4:Bricoleur:12:850",
            out _).Should().BeFalse();
        ProfessionJobReferenceInput.TryParse(
            "1:Sculpteur:6:370,2:RootAdmin:12:800,3:Sculpteur:6:400,4:RootAdmin:12:850",
            out _).Should().BeFalse();
        ProfessionJobReferenceInput.TryParse(
            "1:Sculpteur:6:370,2:Bricoleur:12:800,2:Sculpteur:6:400,4:Bricoleur:12:850",
            out _).Should().BeFalse();
        ProfessionJobReferenceInput.TryParse(
            "1:Sculpteur:6:370,2:Bricoleur:12:800,3:Sculpteur:5:400,4:Bricoleur:12:850",
            out _).Should().BeFalse();
    }

    [Fact]
    public void TypoAndUncapturedIndexMustBeCorrectableBeforeComparison()
    {
        // Fictional input: doubled separator must be rejected.
        ProfessionJobReferenceInput.TryParse(
            "1:Sculpteur:7:111,2:Sculpteur:7::121,3:Cordonnier:3:222,4:Cordonnier:3:233",
            out _).Should().BeFalse();

        // The input parser allows indexes up to 16, but the prompt must
        // also verify that the given index was captured in this run.
        ProfessionJobReferenceInput.TryParse(
            "1:Sculpteur:7:111,2:Sculpteur:7:121,3:Cordonnier:3:222,9:Cordonnier:4:233",
            out IReadOnlyList<ProfessionJobReference> references).Should().BeTrue();
        ProfessionJobReferenceInput.Validate(references, 7).Should().BeFalse();

        // The known captured indexes remain valid without a new capture.
        ProfessionJobReferenceInput.TryParse(
            "1:Sculpteur:7:111,2:Sculpteur:7:121,3:Cordonnier:3:222,4:Cordonnier:4:233",
            out references).Should().BeTrue();
        ProfessionJobReferenceInput.Validate(references, 7).Should().BeTrue();
    }

    [Fact]
    public void TwoCraftsPerProfessionAreMandatory()
    {
        ProfessionJobReference[] onlyOneJobObservedOnce =
        [
            new(1, "Sculpteur", 6, 370),
            new(2, "Bricoleur", 12, 800),
            new(3, "Bricoleur", 12, 850),
            new(4, "Bricoleur", 12, 900)
        ];
        ProfessionJobReferenceInput.Validate(onlyOneJobObservedOnce, 16)
            .Should().BeFalse();
    }

    [Fact]
    public void ComparisonRequiresCapturedIndexesAndPreservesPrivacy()
    {
        ProfessionXpWireStudy study = new();
        study.Observe(1, Single(370, 6, 301));
        study.Observe(1, Single(800, 12, 302));

        study.CompareJobs(References)
            .Should().Contain("invalid-local-references");
        study.Count.Should().Be(2);
    }

    [Fact]
    public void TcpCollectorRejectsClientAndNonIpzMessages()
    {
        ProfessionXpWireStudy study = new();
        CharacterCaptureFlowTracker tracker =
            new(professionXpStudy: study);
        CharacterCaptureFlowKey key =
            new("127.0.0.1", 41000, "10.0.0.1", 5555);
        DateTime now = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);
        byte[] frame = Frame(Any("ipz", Single(370, 6, 301)));

        tracker.Observe(key, CharacterCaptureDirection.ClientToServer,
            100, frame, now).Single(e => e.Kind == CharacterCaptureEventKind.Message)
            .ProfessionXpStudySummary.Should().BeNull();

        byte[] wrongOpcode = Frame(Any("kba", Single(800, 12, 302)));
        tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            100, wrongOpcode, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message)
            .ProfessionXpStudySummary.Should().BeNull();
        study.Count.Should().Be(0);
    }

    [Fact]
    public void FreshStudyNeverInheritsAnyPreviousCandidates()
    {
        ProfessionXpWireStudy a = new();
        a.Observe(1, Single(370, 6, 301));
        a.Count.Should().Be(1);
        new ProfessionXpWireStudy().Count.Should().Be(0);
    }
}
