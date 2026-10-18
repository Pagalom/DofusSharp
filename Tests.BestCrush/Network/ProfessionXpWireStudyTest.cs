using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3e: all packet bodies and references are synthetic; no real accounts,
/// client data or multiclient isolation experiments in this phase.
/// </summary>
public sealed class ProfessionXpWireStudyTest
{
    private static byte[] Single(ulong xp, int level = 3, ulong professionCandidate = 8) =>
        Message(Bytes(1, Message(
            Unsigned(1, (ulong)level),
            Unsigned(2, xp),
            Unsigned(3, 5),
            Unsigned(5, professionCandidate),
            Unsigned(6, 130))));

    [Fact]
    public void SingleRowReproducesTheOneByteVarintBoundary()
    {
        Single(124).Length.Should().Be(13);
        Single(138).Length.Should().Be(14);
        Single(166).Length.Should().Be(14);
    }

    [Fact]
    public void ComparisonFindsOnlyTheMatchingXpFieldPath()
    {
        ProfessionXpWireStudy study = new();
        string first = study.Observe(1, Single(124))!;
        string second = study.Observe(1, Single(138))!;
        string third = study.Observe(1, Single(166))!;

        first.Should().Contain("index=1");
        second.Should().Contain("index=2");
        third.Should().Contain("index=3");
        first.Should().NotContain("124");
        second.Should().NotContain("138");
        third.Should().NotContain("166");

        string result = study.Compare([
            new ProfessionXpReference(1, 3, 124),
            new ProfessionXpReference(2, 3, 138),
            new ProfessionXpReference(3, 3, 166)
        ]);

        result.Should().Contain("xp-field-candidates=[f1.f2]");
        result.Should().Contain("level-field-candidates=[f1.f1]");
        result.Should().Contain("xp-status=single-candidate");
        result.Should().Contain("account=unverified server=unverified");
        result.Should().NotContain("124");
        result.Should().NotContain("138");
        result.Should().NotContain("166");
    }

    [Fact]
    public void WrongReferenceDoesNotCreateAFalsePositive()
    {
        ProfessionXpWireStudy study = new();
        study.Observe(1, Single(124));
        study.Observe(1, Single(138));

        study.Compare([
            new ProfessionXpReference(1, 3, 124),
            new ProfessionXpReference(2, 3, 150)
        ]).Should().Contain("xp-field-candidates=[none]")
            .And.Contain("xp-status=none");
    }

    [Fact]
    public void CanSkipUnrelatedObservationsByIndex()
    {
        ProfessionXpWireStudy study = new();
        study.Observe(1, Single(4));
        study.Observe(1, Single(124));
        study.Observe(1, Single(138));

        study.Compare([
            new ProfessionXpReference(2, 3, 124),
            new ProfessionXpReference(3, 3, 138)
        ]).Should().Contain("xp-field-candidates=[f1.f2]");
    }

    [Fact]
    public void InvalidAndFullSnapshotsAreIgnored()
    {
        ProfessionXpWireStudy study = new();
        study.Observe(1, [0xff]).Should().BeNull();
        study.Observe(1, Message(
            Bytes(1, Message(Unsigned(1, 3))),
            Bytes(1, Message(Unsigned(1, 3))))).Should().BeNull();
        study.Observe(1, Message(Bytes(1, Message(
            Unsigned(1, 3), Unsigned(2, 123),
            Unsigned(2, 124))))).Should().BeNull();
        study.Count.Should().Be(0);
    }

    [Fact]
    public void ReferenceInputIsStrictAndNeverFormattedAsPrivateValues()
    {
        ProfessionXpReferenceInput.TryParse(
            "1:3:124,2:3:138", out var points).Should().BeTrue();
        points.Should().HaveCount(2);
        points[0].ToString().Should().NotContain("124");

        ProfessionXpReferenceInput.TryParse("1:3:124", out _).Should().BeFalse();
        ProfessionXpReferenceInput.TryParse("2:3:138,1:3:124", out _).Should().BeFalse();
        ProfessionXpReferenceInput.TryParse("1:3:124,2:3:124", out _).Should().BeFalse();
        ProfessionXpReferenceInput.TryParse("1:0:124,2:3:138", out _).Should().BeFalse();
        ProfessionXpReferenceInput.TryParse("1:3:124,2:3:138\nLEAK", out _)
            .Should().BeFalse();
    }

    [Fact]
    public void AtLeastTwoDistinctInRangeReferencesAreRequired()
    {
        ProfessionXpWireStudy study = new();
        study.Observe(1, Single(124));
        study.Observe(1, Single(138));

        study.Compare([new ProfessionXpReference(1, 3, 124)])
            .Should().Contain("invalid-local-references");
        study.Compare([
            new ProfessionXpReference(1, 3, 124),
            new ProfessionXpReference(3, 3, 138)
        ]).Should().Contain("invalid-local-references");
    }

    [Fact]
    public void BoundedInMemoryStoreDoesNotRetainMoreThanSixteenRows()
    {
        ProfessionXpWireStudy study = new();
        foreach (int i in Enumerable.Range(0, 16))
            study.Observe(1, Single((ulong)(200 + i))).Should().Contain("accepted=yes");

        study.Observe(1, Single(216)).Should().Contain("reason=limit");
        study.Count.Should().Be(16);
        new ProfessionXpWireStudy().Count.Should().Be(0);
    }

    [Fact]
    public void TcpTrackerOnlyEmitsResultsForServerToClientMonoIpz()
    {
        ProfessionXpWireStudy study = new();
        CharacterCaptureFlowTracker tracker = new(professionXpStudy: study);
        CharacterCaptureFlowKey key =
            new("127.0.0.1", 40001, "10.0.0.1", 5555);
        DateTime t = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

        byte[] a = Frame(Any("ipz", Single(124)));
        byte[] b = Frame(Any("ipz", Single(138)));
        byte[] unrelated = Frame(Any("kba", Single(166)));

        CharacterCaptureEvent client = tracker.Observe(
            key, CharacterCaptureDirection.ClientToServer, 100, a, t)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        client.ProfessionXpStudySummary.Should().BeNull();

        CharacterCaptureEvent other = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient, 100, unrelated, t)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        other.ProfessionXpStudySummary.Should().BeNull();

        CharacterCaptureEvent first = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + unrelated.Length), a, t.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        first.ProfessionXpStudySummary.Should().Contain("index=1");
        CharacterCaptureEvent second = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + unrelated.Length + a.Length), b, t.AddSeconds(2))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        second.ProfessionXpStudySummary.Should().Contain("index=2");
        study.Count.Should().Be(2);
    }

    [Fact]
    public void XpModeRejectsOtherResearchModes()
    {
        Action mixed = () => new CharacterCaptureFlowTracker(
            professionXpStudy: new ProfessionXpWireStudy(),
            studyProfessionContinuity: true);
        mixed.Should().Throw<ArgumentException>();

        Action mixedShape = () => new CharacterCaptureFlowTracker(
            professionXpStudy: new ProfessionXpWireStudy(),
            shapeKeys: new HashSet<string> { "ipz" });
        mixedShape.Should().Throw<ArgumentException>();
    }
}
