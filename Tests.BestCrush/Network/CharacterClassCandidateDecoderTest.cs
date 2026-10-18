using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>Research-only P2j synthetic wire layouts; no real player data.</summary>
public sealed class CharacterClassCandidateDecoderTest
{
    private const ulong FirstId = 9000123;
    private const ulong SecondId = 9000789;
    private static readonly CharacterNameLevelReference Reference =
        new("Sable-Ciel", 137);

    private static byte[] Leaf(
        string name, int level, byte[]? classNode = null) =>
        Message(
            Unsigned(3, (ulong)level),
            classNode is null ? [] : Bytes(5, classNode),
            Bytes(6, Encoding.UTF8.GetBytes(name)));

    private static byte[] Row(ulong id, string name, int level, byte[]? classNode) =>
        Message(Unsigned(3, id), Bytes(4, Leaf(name, level, classNode)));

    private static byte[] Breed(ulong id) => Message(Unsigned(3, id));

    private static byte[] Ksb(byte[]? classNode = null) =>
        Bytes(1, Bytes(1, Row(FirstId, "Sable-Ciel", 137, classNode)));

    private static byte[] Ksc(byte[]? selectedClass = null,
        byte[]? otherClass = null) =>
        Message(
            Bytes(2, Row(SecondId, "Roche-Lune", 2, otherClass)),
            Bytes(2, Row(FirstId, "Sable-Ciel", 137, selectedClass)));

    [Fact]
    public void KsbExtractsCandidateClassWithoutConfirmingClassOrIdentity()
    {
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", Ksb(Breed(6)), out var snapshot).Should().BeTrue();

        var candidate = snapshot!.Candidates.Single();
        candidate.CharacterIdCandidate.Should().Be(FirstId);
        candidate.ClassIdCandidate.Should().Be(6);
        candidate.ClassId.Should().BeNull();
        snapshot.IdentityConfirmed.Should().BeFalse();
        candidate.ToString().Should().Be("[character candidate: redacted]");
        candidate.ToString().Should().NotContain("Sable-Ciel");
    }

    [Fact]
    public void KscExtractsTwoDifferentClassCandidatesWithoutSelecting()
    {
        CharacterWireCandidateDecoder.TryDecode(
            "ksc", Ksc(Breed(6), Breed(17)),
            out var snapshot).Should().BeTrue();

        snapshot!.Candidates.Select(c => c.ClassIdCandidate)
            .Should().Equal(17, 6);
        snapshot.Candidates.Should().OnlyContain(c => c.ClassId == null);
        snapshot.IdentityConfirmed.Should().BeFalse();
    }

    [Fact]
    public void SameCharacterInBothFamiliesCanCorrelateCandidateClass()
    {
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", Ksb(Breed(6)), out var loaded);
        CharacterWireCandidateDecoder.TryDecode(
            "ksc", Ksc(Breed(6), Breed(17)), out var listed);

        CharacterWireCandidateDecoder.HasMatchingCandidate(
            loaded!, listed!).Should().BeTrue();
        CharacterWireCandidateDecoder.CompareClassCandidates(
            loaded!, listed!).Should().BeTrue();
    }

    [Fact]
    public void SameIdentityWithConflictingClassesDoesNotCorroborate()
    {
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", Ksb(Breed(17)), out var loaded);
        CharacterWireCandidateDecoder.TryDecode(
            "ksc", Ksc(Breed(6), Breed(17)), out var listed);

        CharacterWireCandidateDecoder.HasMatchingCandidate(
            loaded!, listed!).Should().BeTrue();
        CharacterWireCandidateDecoder.CompareClassCandidates(
            loaded!, listed!).Should().BeFalse();
        loaded!.Candidates.Single().ClassId.Should().BeNull();
    }

    [Fact]
    public void DifferentIdentityCannotProduceClassCorroboration()
    {
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", Ksb(Breed(6)), out var loaded);
        // Deliberately excludes the loaded ID.
        byte[] different = Message(
            Bytes(2, Row(SecondId, "Roche-Lune", 2, Breed(6))));
        CharacterWireCandidateDecoder.TryDecode(
            "ksc", different, out var listed);

        CharacterWireCandidateDecoder.HasMatchingCandidate(
            loaded!, listed!).Should().BeFalse();
        CharacterWireCandidateDecoder.CompareClassCandidates(
            loaded!, listed!).Should().BeNull();
    }

    [Fact]
    public void MissingOrMalformedClassNeverSuppliesClassId()
    {
        byte[] duplicated = Message(Unsigned(3, 6), Unsigned(3, 17));
        byte[] nonVarint = Message(Bytes(3, [0x31]));

        foreach (byte[]? input in new byte[]?[]
        {
            null, [], Breed(0), Breed(20), duplicated, nonVarint, [0xff]
        })
        {
            CharacterWireCandidateDecoder.TryDecode(
                "ksb", Ksb(input), out var decoded).Should().BeTrue();
            decoded!.Candidates.Single().ClassIdCandidate.Should().BeNull();
            decoded.Candidates.Single().ClassId.Should().BeNull();
        }
    }

    [Fact]
    public void MissingClassMakesCrossFamilyClassMatchUnavailable()
    {
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", Ksb(), out var loaded);
        CharacterWireCandidateDecoder.TryDecode(
            "ksc", Ksc(Breed(6), Breed(17)), out var listed);
        CharacterWireCandidateDecoder.CompareClassCandidates(
            loaded!, listed!).Should().BeNull();
    }

    [Fact]
    public void TrackerReportsClassEqualityOnlyOnSameTcpConnection()
    {
        var tracker = new CharacterCaptureFlowTracker(
            identityFieldsReference: Reference);
        var key = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        var now = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        byte[] list = Frame(Any("ksc", Ksc(Breed(6), Breed(17))));
        byte[] character = Frame(Any("ksb", Ksb(Breed(6))));

        var first = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient, 100, list, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        first.CharacterIdentityFieldSummary.Should().Contain(
            "p2j=class-candidate=all class-link=pending class=unverified");

        var loaded = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + list.Length), character, now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        loaded.CharacterIdentityFieldSummary.Should().Contain(
            "p2j=class-candidate=all class-link=yes class=unverified");
        loaded.CharacterIdentityFieldSummary.Should().NotContain("Sable-Ciel");
        loaded.CharacterIdentityFieldSummary.Should().NotContain("9000123");
        loaded.CharacterIdentityFieldSummary.Should().NotContain("137");
        loaded.CharacterIdentityFieldSummary.Should().Contain("identity=unverified");
    }

    [Fact]
    public void TrackerDoesNotLinkClassesFromDifferentTcpSessions()
    {
        var tracker = new CharacterCaptureFlowTracker(
            identityFieldsReference: Reference);
        var now = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var first = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        var second = new CharacterCaptureFlowKey(
            "127.0.0.1", 40002, "10.0.0.1", 5555);

        tracker.Observe(first, CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("ksc", Ksc(Breed(6), Breed(17)))), now);

        var output = tracker.Observe(second,
            CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("ksb", Ksb(Breed(6)))), now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        output.CharacterIdentityFieldSummary.Should().Contain(
            "p2j=class-candidate=all class-link=pending class=unverified");
    }

    [Fact]
    public void TrackerReportsContradictoryClassLinkWithoutConfirmingIdentity()
    {
        var tracker = new CharacterCaptureFlowTracker(
            identityFieldsReference: Reference);
        var now = new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var scope = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        byte[] list = Frame(Any("ksc", Ksc(Breed(6), Breed(17))));
        tracker.Observe(scope,
            CharacterCaptureDirection.ServerToClient, 100, list, now);
        var contradiction = tracker.Observe(scope,
            CharacterCaptureDirection.ServerToClient,
            (uint)(100 + list.Length), Frame(Any("ksb", Ksb(Breed(17)))),
            now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        contradiction.CharacterIdentityFieldSummary.Should().Contain(
            "p2j=class-candidate=all class-link=no class=unverified");
        contradiction.CharacterIdentityFieldSummary.Should().Contain(
            "identity=unverified");
    }
}
