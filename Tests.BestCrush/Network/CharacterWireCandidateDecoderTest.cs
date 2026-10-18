using System.Text;
using BestCrush.Network.Protocol;
using BestCrush.Network.Capture;
using FluentAssertions;
using System;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// P2f synthetic protobuf fixtures only, never recorded account/character data.
/// </summary>
public sealed class CharacterWireCandidateDecoderTest
{
    private const ulong FirstId = 9000123;
    private const ulong SecondId = 9000789;

    private static byte[] Leaf(string name, int level) => Message(
        Unsigned(3, (ulong)level),
        Bytes(6, Encoding.UTF8.GetBytes(name)));

    private static byte[] Row(ulong id, string name, int level) =>
        Message(Unsigned(3, id), Bytes(4, Leaf(name, level)));

    private static byte[] Ksb(ulong id = FirstId, string name = "Sable-Ciel",
        int level = 137) =>
        Bytes(1, Bytes(1, Row(id, name, level)));

    private static byte[] Ksc(ulong id = FirstId,
        string name = "Sable-Ciel", int level = 137) =>
        Message(
            Bytes(2, Row(SecondId, "Roche-Lune", 2)),
            Bytes(2, Row(id, name, level)));

    [Fact]
    public void KsbDecodesOneTransientCandidateWithUnknownClass()
    {
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", Ksb(), out var decoded).Should().BeTrue();

        decoded.Should().NotBeNull();
        decoded!.Opcode.Should().Be("ksb");
        decoded.Candidates.Should().HaveCount(1);
        decoded.Candidates[0].CharacterIdCandidate.Should().Be(FirstId);
        decoded.Candidates[0].Name.Should().Be("Sable-Ciel");
        decoded.Candidates[0].Level.Should().Be(137);
        decoded.Candidates[0].ClassId.Should().BeNull();
        decoded.IdentityConfirmed.Should().BeFalse();
        decoded.ToString().Should().Be("[character candidate snapshot: redacted]");
        decoded.Candidates[0].ToString().Should().Be("[character candidate: redacted]");
    }

    [Fact]
    public void KscDecodesTwoDistinctSelectableCandidatesWithoutSelectingOne()
    {
        CharacterWireCandidateDecoder.TryDecode(
            "ksc", Ksc(), out var decoded).Should().BeTrue();
        decoded.Should().NotBeNull();
        decoded!.Opcode.Should().Be("ksc");
        decoded.Candidates.Should().HaveCount(2);
        decoded.Candidates[0].CharacterIdCandidate.Should().Be(SecondId);
        decoded.Candidates[1].CharacterIdCandidate.Should().Be(FirstId);
        decoded.Candidates[0].Level.Should().Be(2);
        decoded.IdentityConfirmed.Should().BeFalse();
    }

    [Fact]
    public void KsbAndKscHaveOneMatchingCandidateWithoutConfirmingIdentity()
    {
        CharacterWireCandidateDecoder.TryDecode("ksb", Ksb(), out var loaded);
        CharacterWireCandidateDecoder.TryDecode("ksc", Ksc(), out var list);
        CharacterWireCandidateDecoder.HasMatchingCandidate(
            loaded!, list!).Should().BeTrue();
        loaded!.IdentityConfirmed.Should().BeFalse();
        list!.IdentityConfirmed.Should().BeFalse();
    }

    [Fact]
    public void WrongCandidateIdOrNameOrLevelDoesNotMatch()
    {
        CharacterWireCandidateDecoder.TryDecode(
            "ksc", Ksc(), out var list);

        foreach (var packet in new[]
        {
            Ksb(9000555), Ksb(name: "Sable-Soleil"),
            Ksb(level: 136)
        })
        {
            CharacterWireCandidateDecoder.TryDecode(
                "ksb", packet, out var loaded).Should().BeTrue();
            CharacterWireCandidateDecoder.HasMatchingCandidate(
                loaded!, list!).Should().BeFalse();
        }
    }

    [Fact]
    public void SwappedMessageFamilyCannotCountAsConfirmation()
    {
        CharacterWireCandidateDecoder.TryDecode("ksb", Ksb(), out var loaded);
        CharacterWireCandidateDecoder.TryDecode("ksc", Ksc(), out var list);
        CharacterWireCandidateDecoder.HasMatchingCandidate(
            list!, loaded!).Should().BeFalse();
        CharacterWireCandidateDecoder.HasMatchingCandidate(
            loaded!, loaded!).Should().BeFalse();
    }

    [Fact]
    public void DuplicateKscCandidateIdsAreRejected()
    {
        byte[] duplicate = Message(
            Bytes(2, Row(FirstId, "Roche-Lune", 2)),
            Bytes(2, Row(FirstId, "Sable-Ciel", 137)));

        CharacterWireCandidateDecoder.TryDecode(
            "ksc", duplicate, out var decoded).Should().BeFalse();
        decoded.Should().BeNull();
    }

    [Fact]
    public void DuplicateKscCandidateNamesAreRejected()
    {
        byte[] duplicate = Message(
            Bytes(2, Row(SecondId, "Sable-Ciel", 2)),
            Bytes(2, Row(FirstId, "Sable-Ciel", 137)));
        CharacterWireCandidateDecoder.TryDecode(
            "ksc", duplicate, out var decoded).Should().BeFalse();
        decoded.Should().BeNull();
    }

    [Fact]
    public void DuplicateIdOrLevelFieldsInOneCandidateAreRejected()
    {
        byte[] duplicatedId = Bytes(1, Bytes(1,
            Message(Unsigned(3, FirstId), Unsigned(3, FirstId),
                Bytes(4, Leaf("Sable-Ciel", 137)))));
        byte[] duplicatedLevel = Bytes(1, Bytes(1,
            Message(Unsigned(3, FirstId), Bytes(4,
                Message(Unsigned(3, 137), Unsigned(3, 137),
                    Bytes(6, Encoding.UTF8.GetBytes("Sable-Ciel")))))));

        CharacterWireCandidateDecoder.TryDecode(
            "ksb", duplicatedId, out _).Should().BeFalse();
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", duplicatedLevel, out _).Should().BeFalse();
    }

    [Fact]
    public void EmptyIdOrOutOfRangeLevelFailClosed()
    {
        foreach (byte[] packet in new[]
        {
            Ksb(id: 0), Ksb(level: 0), Ksb(level: 201)
        })
            CharacterWireCandidateDecoder.TryDecode(
                "ksb", packet, out _).Should().BeFalse();
    }

    [Fact]
    public void MissingRequiredNameOrParentFieldsFailClosed()
    {
        byte[] absentName = Bytes(1, Bytes(1, Message(
            Unsigned(3, FirstId), Bytes(4, Unsigned(3, 137)))));
        byte[] absentParentId = Bytes(1, Bytes(1,
            Bytes(4, Leaf("Sable-Ciel", 137))));

        CharacterWireCandidateDecoder.TryDecode(
            "ksb", absentName, out _).Should().BeFalse();
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", absentParentId, out _).Should().BeFalse();
    }

    [Fact]
    public void InvalidUtf8AndControlCharactersFailClosed()
    {
        byte[] badUtf8 = Bytes(1, Bytes(1, Message(
            Unsigned(3, FirstId), Bytes(4, Message(
                Unsigned(3, 137), Bytes(6, [0xc3, 0x28, 0x44])))));
        byte[] control = Ksb(name: "Bad\nName");

        CharacterWireCandidateDecoder.TryDecode(
            "ksb", badUtf8, out _).Should().BeFalse();
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", control, out _).Should().BeFalse();
    }

    [Fact]
    public void UnsupportedOrMalformedOrOversizedBodiesFailClosed()
    {
        CharacterWireCandidateDecoder.TryDecode(
            "unknown", Ksb(), out _).Should().BeFalse();
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", [], out _).Should().BeFalse();
        CharacterWireCandidateDecoder.TryDecode(
            "ksb", [0], out _).Should().BeFalse();
        CharacterWireCandidateDecoder.TryDecode(
            "ksc", new byte[8193], out _).Should().BeFalse();
    }

    [Fact]
    public void KscWithIncompleteRowCannotCreatePartialCandidateList()
    {
        byte[] invalid = Message(
            Bytes(2, Row(FirstId, "Sable-Ciel", 137)),
            Bytes(2, Message(Unsigned(3, SecondId))));
        CharacterWireCandidateDecoder.TryDecode(
            "ksc", invalid, out var decoded).Should().BeFalse();
        decoded.Should().BeNull();
    }


    [Fact]
    public void RealProbePathProducesOnlyRedactedP2fConsistencyFlags()
    {
        var tracker = new CharacterCaptureFlowTracker(
            identityFieldsReference: new CharacterNameLevelReference("Sable-Ciel", 137));
        var key = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        DateTime now = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        byte[] list = Frame(Any("ksc", Ksc()));
        byte[] loaded = Frame(Any("ksb", Ksb()));

        var initial = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient,
            100, list, now).Single(e => e.Kind == CharacterCaptureEventKind.Message);
        initial.CharacterIdentityFieldSummary.Should().Contain(
            "p2f=decoded candidates=2 class=unknown candidate-link=pending identity=unverified");

        var corroboration = tracker.Observe(
            key, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + list.Length), loaded, now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        corroboration.CharacterIdentityFieldSummary.Should().Contain(
            "p2f=decoded candidates=1 class=unknown candidate-link=yes identity=unverified");
        corroboration.CharacterIdentityFieldSummary.Should().NotContain("Sable-Ciel");
        corroboration.CharacterIdentityFieldSummary.Should().NotContain("9000123");
        corroboration.CharacterIdentityFieldSummary.Should().NotContain("137");
    }

    [Fact]
    public void DifferentTcpSessionsNeverProduceP2fCandidateLink()
    {
        var tracker = new CharacterCaptureFlowTracker(
            identityFieldsReference: new CharacterNameLevelReference("Sable-Ciel", 137));
        DateTime now = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var first = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        var second = new CharacterCaptureFlowKey(
            "127.0.0.1", 40002, "10.0.0.1", 5555);

        var list = tracker.Observe(
            first, CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("ksc", Ksc())), now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        var loaded = tracker.Observe(
            second, CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("ksb", Ksb())), now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        list.CharacterIdentityFieldSummary.Should().Contain("candidate-link=pending");
        loaded.CharacterIdentityFieldSummary.Should().Contain("candidate-link=pending");
    }

    [Fact]
    public void ContradictoryKsbIsFailClosedAfterInitialDecode()
    {
        var tracker = new CharacterCaptureFlowTracker(
            identityFieldsReference: new CharacterNameLevelReference("Sable-Ciel", 137));
        var key = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        DateTime now = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        byte[] first = Frame(Any("ksb", Ksb()));
        byte[] changed = Frame(Any("ksb", Ksb(9000555)));

        tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            100, first, now);
        var conflict = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + first.Length), changed, now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        conflict.CharacterIdentityFieldSummary.Should().Be(
            "status=conflicted identity=unverified");
    }

    [Fact]
    public void CandidateIdAndNameNeverAppearInDefaultStringRendering()
    {
        CharacterWireCandidateDecoder.TryDecode("ksb", Ksb(), out var decoded);
        string text = decoded!.ToString() + decoded.Candidates[0].ToString();
        text.Should().NotContain("Sable-Ciel");
        text.Should().NotContain("9000123");
        text.Should().NotContain("137");
    }
}
