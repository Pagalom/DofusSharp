using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// P2i class-hypothesis fixtures are entirely synthetic. There are no
/// real player characters, network captures or private references here.
/// </summary>
public sealed class CharacterBreedHypothesisStudyTest
{
    private static readonly CharacterNameLevelReference Reference =
        new("Sable-Ciel", 137);

    private static byte[] Leaf(string name, int level, ulong? breed) =>
        Message(
            Unsigned(3, (ulong)level),
            breed.HasValue
                ? Bytes(5, Message(Unsigned(3, breed.Value)))
                : [],
            Bytes(6, Encoding.UTF8.GetBytes(name)));

    private static byte[] Row(ulong id, string name, int level, ulong? breed) =>
        Message(Unsigned(3, id), Bytes(4, Leaf(name, level, breed)));

    private static byte[] Ksb(ulong? breed = 6) =>
        Bytes(1, Bytes(1, Row(9000123, "Sable-Ciel", 137, breed)));

    private static byte[] Ksc(ulong? firstClass = 6, ulong? secondClass = 17) =>
        Message(
            Bytes(2, Row(9000789, "Roche-Lune", 2, secondClass)),
            Bytes(2, Row(9000123, "Sable-Ciel", 137, firstClass)));

    [Fact]
    public void KnownBreedReferenceCanMatchWithoutConfirmingClass()
    {
        string result = CharacterBreedHypothesisStudy.Describe(
            "ksc", Ksc(), Reference, expectedBreedId: 6);
        result.Should().Be(
            "p2i=f5-f3-check reference-equal=yes peers-differ=yes class=unverified");
        result.Should().NotContain("Sable-Ciel");
        result.Should().NotContain("9000123");
    }

    [Fact]
    public void WrongBreedReferenceIsRejectedByComparison()
    {
        CharacterBreedHypothesisStudy.Describe(
            "ksb", Ksb(), Reference, expectedBreedId: 17)
            .Should().Be(
                "p2i=f5-f3-check reference-equal=no peers-differ=unavailable class=unverified");
    }

    [Fact]
    public void SameBreedForTwoCharactersDoesNotProveDistinctClass()
    {
        CharacterBreedHypothesisStudy.Describe(
            "ksc", Ksc(firstClass: 6, secondClass: 6), Reference, 6)
            .Should().Be(
                "p2i=f5-f3-check reference-equal=yes peers-differ=no class=unverified");
    }

    [Fact]
    public void BothFamiliesCanIndependentlyDecodeCandidateValue()
    {
        CharacterBreedHypothesisStudy.Describe(
            "ksb", Ksb(), Reference, 6)
            .Should().Contain("reference-equal=yes");
        CharacterBreedHypothesisStudy.Describe(
            "ksc", Ksc(), Reference, 6)
            .Should().Contain("reference-equal=yes");
    }

    [Fact]
    public void DistinctSecondCharacterCanBeCheckedWithIndependentLocalReference()
    {
        var secondReference = new CharacterNameLevelReference("Roche-Lune", 2);
        CharacterBreedHypothesisStudy.Describe(
            "ksc", Ksc(), secondReference, 17)
            .Should().Contain("reference-equal=yes peers-differ=yes");
    }

    [Fact]
    public void MissingOrZeroClassFieldFailsClosed()
    {
        CharacterBreedHypothesisStudy.Describe(
            "ksb", Ksb(null), Reference, 6)
            .Should().Be("p2i=unavailable class=unverified");
        CharacterBreedHypothesisStudy.Describe(
            "ksc", Ksc(firstClass: 0), Reference, 6)
            .Should().Be("p2i=unavailable class=unverified");
    }

    [Fact]
    public void WrongNameAndLevelNeverMatchReference()
    {
        var wrong = new CharacterNameLevelReference("Other-Ciel", 137);
        CharacterBreedHypothesisStudy.Describe(
            "ksc", Ksc(), wrong, 6)
            .Should().Be("p2i=reference-not-unique class=unverified");
    }

    [Fact]
    public void UnsupportedMalformedAndOversizedBodiesFailClosed()
    {
        CharacterBreedHypothesisStudy.Describe(
            "unknown", Ksb(), Reference, 6)
            .Should().Be("p2i=unavailable class=unverified");
        CharacterBreedHypothesisStudy.Describe(
            "ksb", [0xff], Reference, 6)
            .Should().Be("p2i=unavailable class=unverified");
        CharacterBreedHypothesisStudy.Describe(
            "ksc", new byte[8193], Reference, 6)
            .Should().Be("p2i=unavailable class=unverified");
    }

    [Fact]
    public void InvalidExpectedClassIdCannotStartStudy()
    {
        Action zero = () => CharacterBreedHypothesisStudy.Describe(
            "ksb", Ksb(), Reference, 0);
        Action tooHigh = () => CharacterBreedHypothesisStudy.Describe(
            "ksb", Ksb(), Reference, 20);
        zero.Should().Throw<ArgumentOutOfRangeException>();
        tooHigh.Should().Throw<ArgumentOutOfRangeException>();
        Action trackerWithoutName = () =>
            new CharacterCaptureFlowTracker(expectedClassId: 6);
        trackerWithoutName.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackerAddsOnlyAnonymousOptionalClassComparison()
    {
        var tracker = new CharacterCaptureFlowTracker(
            identityFieldsReference: Reference, expectedClassId: 6);
        var key = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        var now = new DateTime(2026, 10, 18, 13, 0, 0, DateTimeKind.Utc);
        byte[] frame = Frame(Any("ksc", Ksc()));

        var result = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient, 100, frame, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        result.CharacterIdentityFieldSummary.Should().Contain(
            "p2i=f5-f3-check reference-equal=yes peers-differ=yes class=unverified");
        result.CharacterIdentityFieldSummary.Should().Contain(
            "identity=unverified");
        result.CharacterIdentityFieldSummary.Should().NotContain("Sable-Ciel");
        result.CharacterIdentityFieldSummary.Should().NotContain("Roche-Lune");
        result.CharacterIdentityFieldSummary.Should().NotContain("9000123");
    }

    [Fact]
    public void ExistingModeDoesNotEnableP2iUnlessExplicitlyRequested()
    {
        var tracker = new CharacterCaptureFlowTracker(
            identityFieldsReference: Reference);
        var key = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        var now = new DateTime(2026, 10, 18, 13, 0, 0, DateTimeKind.Utc);
        var result = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("ksc", Ksc())), now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        result.CharacterIdentityFieldSummary.Should().NotContain("p2i=");
    }
}
