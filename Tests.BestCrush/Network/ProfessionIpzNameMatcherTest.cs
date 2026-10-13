using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class ProfessionIpzNameMatcherTest
{
    private static readonly ProfessionNamedReference[] Refs =
    [
        new("Cordomage", 12, 509),
        new("Façomage", 11, 468),
        new("Forgemage", 10, 367),
        new("Joaillomage", 11, 479),
        new("Sculptemage", 9, 302)
    ];

    // Pure synthetic wire structures; values are fabricated for the unit test.
    private static byte[] Body(
        bool duplicatePair = false, bool duplicateF5 = false,
        bool missingF5 = false)
    {
        byte[][] rows = Enumerable.Range(0, 20).Select(index =>
        {
            int level = index < 5 ? Refs[index].Level : 20 + index;
            ulong xp = index < 5 ? Refs[index].TotalExperience :
                (ulong)(1000 + index * 31);

            if (duplicatePair && index == 5)
            {
                level = 12;
                xp = 509;
            }

            ulong id = (ulong)(index + 101);
            if (duplicateF5 && index == 10)
                id = 101;

            return Bytes(1, Message(
                Unsigned(1, (ulong)level),
                Unsigned(2, xp),
                missingF5 && index == 10 ? [] : Unsigned(5, id),
                Unsigned(6, xp + 50)));
        }).ToArray();

        return Message(rows);
    }

    [Fact]
    public void FiveNamedPairsResolveToFiveDistinctCandidates()
    {
        string report = ProfessionIpzNameMatcher.Analyze(Body(), Refs, true);
        report.Should().Contain("rows=20 pairs=5/5");
        report.Should().Contain("f5-unique=true");
        report.Should().Contain("candidate-links=5/5");
        report.Should().Contain("Cordomage:f5=101");
        report.Should().Contain("Façomage:f5=102");
        report.Should().Contain("Forgemage:f5=103");
        report.Should().Contain("Joaillomage:f5=104");
        report.Should().Contain("Sculptemage:f5=105");
        report.Should().Contain("mapping=unverified");
    }

    [Fact]
    public void DefaultModeDoesNotRevealNamesOrIdentifiersOrXp()
    {
        string report = ProfessionIpzNameMatcher.Analyze(Body(), Refs);
        report.Should().Contain("pairs=5/5");
        report.Should().NotContain("CANDIDATE_IDS");
        report.Should().NotContain("Cordomage:");
        report.Should().NotContain("f5=101");
        report.Should().NotContain("509");
    }

    [Fact]
    public void DuplicatePairCannotBeMappedByGuessing()
    {
        string report = ProfessionIpzNameMatcher.Analyze(
            Body(duplicatePair: true), Refs, true);
        report.Should().Contain("pairs=4/5");
        report.Should().Contain("candidate-links=4/5");
        report.Should().NotContain("Cordomage:f5=");
    }

    [Fact]
    public void DuplicateOrMissingF5BlocksAllIdDisclosure()
    {
        foreach (byte[] body in new[]
        {
            Body(duplicateF5: true),
            Body(missingF5: true)
        })
        {
            string report = ProfessionIpzNameMatcher.Analyze(body, Refs, true);
            report.Should().Contain("f5-unique=false");
            report.Should().Contain("candidate-links=0/5");
            report.Should().NotContain("CANDIDATE_IDS");
        }
    }

    [Fact]
    public void InputRejectsDuplicateNamesAndLevelExperiencePairs()
    {
        ProfessionNamedReferenceInput.TryParse(
            "Cordomage:12:509,Façomage:11:468", out var parsed)
            .Should().BeTrue();
        parsed.Should().HaveCount(2);
        parsed[0].ToString().Should().NotContain("509");

        ProfessionNamedReferenceInput.TryParse(
            "Cordomage:12:509,Cordomage:11:468", out _)
            .Should().BeFalse();
        ProfessionNamedReferenceInput.TryParse(
            "Cordomage:12:509,Façomage:12:509", out _)
            .Should().BeFalse();
        ProfessionNamedReferenceInput.TryParse(
            "Injected:12:509,Façomage:11:468", out _)
            .Should().BeFalse();
    }

    [Fact]
    public void EmptyOrInvalidWireIsOnlyAnAnonymizedRejection()
    {
        ProfessionIpzNameMatcher.Analyze([], Refs, true)
            .Should().Be("rejected-layout:size");
        ProfessionIpzNameMatcher.Analyze([0, 1], Refs, true)
            .Should().Be("rejected-layout:invalid-root");
    }

    [Fact]
    public void TrackerAcceptsOnlyServerIpzAndIsolatesSessions()
    {
        var tracker = new CharacterCaptureFlowTracker(
            identifyReferences: Refs, showCandidateJobIds: true);
        DateTime now = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);
        byte[] frame = Frame(Any("ipz", Body()));
        CharacterCaptureFlowKey a = new("127.0.0.1", 40001, "10.0.0.10", 5555);
        CharacterCaptureFlowKey b = new("127.0.0.1", 40002, "10.0.0.10", 5555);

        CharacterCaptureEvent first = tracker.Observe(
            a, CharacterCaptureDirection.ServerToClient,
            100, frame, now).Single(e => e.Kind == CharacterCaptureEventKind.Message);
        CharacterCaptureEvent second = tracker.Observe(
            b, CharacterCaptureDirection.ServerToClient,
            100, frame, now).Single(e => e.Kind == CharacterCaptureEventKind.Message);
        CharacterCaptureEvent outgoing = tracker.Observe(
            a, CharacterCaptureDirection.ClientToServer,
            100, frame, now).Single(e => e.Kind == CharacterCaptureEventKind.Message);

        first.SessionNumber.Should().NotBe(second.SessionNumber);
        first.ProfessionIdentifySummary.Should().Contain("candidate-links=5/5");
        first.ProfessionIdentifySummary.Should().Contain("Cordomage:f5=101");
        first.ProfessionStudySummary.Should().BeNull();
        outgoing.ProfessionIdentifySummary.Should().BeNull();

        var other = tracker.Observe(
            a, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + frame.Length), Frame(Any("kxn", Body())), now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        other.ProfessionIdentifySummary.Should().BeNull();
    }

    [Fact]
    public void InvalidDirectReferencesCannotInjectTerminalOutput()
    {
        ProfessionNamedReference[] names =
        [
            new("Cordomage\nINJECTED", 12, 509),
            new("Façomage", 11, 468)
        ];
        Action action = () =>
            ProfessionIpzNameMatcher.Analyze(Body(), names, true);
        action.Should().Throw<ArgumentException>();
    }
}
