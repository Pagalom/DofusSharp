using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>All ipz fixtures are fabricated; no real user XP or IDs.</summary>
public sealed class ProfessionIpzDecoderTest
{
    private static readonly ProfessionReference[] RefPairs =
        [new(2, 11), new(3, 22), new(4, 33)];

    private static byte[] Row(int index, bool includeExtras = true) =>
        Message(
            Unsigned(1, (ulong)(index + 1)),
            Unsigned(2, (ulong)(index * 11)),
            includeExtras ? Unsigned(3, (ulong)(300 + index)) : [],
            includeExtras ? Unsigned(5, 77) : [],
            includeExtras ? Unsigned(6, (ulong)(900 + index)) : []);

    private static byte[] Ipz(int count = 20, bool includeExtras = true) =>
        Message(Enumerable.Range(0, count)
            .Select(index => Bytes(1, Row(index, includeExtras))).ToArray());

    [Fact]
    public void DecodesTwentyRowsAndKeepsLevelsAndExperienceInRamOnly()
    {
        ProfessionIpzDecoder.TryDecode(Ipz(), out var snapshot).Should().BeTrue();
        snapshot.Should().NotBeNull();
        snapshot!.Rows.Should().HaveCount(20);
        snapshot.Rows[0].Level.Should().Be(1);
        snapshot.Rows[1].Level.Should().Be(2);
        snapshot.Rows[2].ExperienceCounter.Should().Be(22);
        snapshot.Rows[19].UnidentifiedVarints[3].Should().Be(319);
        snapshot.Rows[19].UnidentifiedVarints[5].Should().Be(77);

        snapshot.ToString().Should().NotContain("319");
        snapshot.Rows[19].ToString().Should().NotContain("319");
    }

    [Fact]
    public void AnonymousSummaryReportsMatchesAndUnknownFieldStatistics()
    {
        ProfessionIpzDecoder.TryDecode(Ipz(), out var snapshot).Should().BeTrue();

        string summary = ProfessionIpzDecoder.DescribeAnonymously(
            snapshot!, RefPairs);

        summary.Should().Contain("rows=20 matched=3/3");
        summary.Should().Contain("f3:present=20/20,distinct=20");
        summary.Should().Contain("f5:present=20/20,distinct=1");
        summary.Should().Contain("f6:present=20/20,distinct=20");
        summary.Should().Contain("identity=unverified");
        summary.Should().NotContain("900");
        summary.Should().NotContain("319");
    }

    [Fact]
    public void MissingOrExtraRootRowRejectsSnapshot()
    {
        ProfessionIpzDecoder.TryDecode(Ipz(19), out _).Should().BeFalse();
        ProfessionIpzDecoder.TryDecode(Ipz(21), out _).Should().BeFalse();
        ProfessionIpzDecoder.TryDecode(
            Message(Ipz(), Unsigned(4, 42)), out _).Should().BeFalse();
    }

    [Fact]
    public void MissingOrDuplicateRequiredFieldsRejectsWholeBatch()
    {
        byte[][] rows = Enumerable.Range(0, 20)
            .Select(i => Bytes(1, i == 5
                ? Message(Unsigned(1, 6), Unsigned(1, 6), Unsigned(2, 55))
                : Row(i))).ToArray();
        ProfessionIpzDecoder.TryDecode(Message(rows), out _).Should().BeFalse();

        rows[5] = Bytes(1, Message(Unsigned(1, 6), Unsigned(2, 55), Unsigned(2, 55)));
        ProfessionIpzDecoder.TryDecode(Message(rows), out _).Should().BeFalse();
    }

    [Fact]
    public void InvalidLevelsAndUnparseableWireAreRejected()
    {
        byte[][] rows = Enumerable.Range(0, 20)
            .Select(i => Bytes(1, i == 4
                ? Message(Unsigned(1, 201), Unsigned(2, 44))
                : Row(i))).ToArray();
        ProfessionIpzDecoder.TryDecode(Message(rows), out _).Should().BeFalse();

        rows[4] = Bytes(1, [0x08, 0x80]);
        ProfessionIpzDecoder.TryDecode(Message(rows), out _).Should().BeFalse();
    }

    [Fact]
    public void UnknownRepeatedScalarIsExcludedFromIdFieldCandidates()
    {
        byte[][] rows = Enumerable.Range(0, 20)
            .Select(i => Bytes(1, i == 3
                ? Message(Row(i), Unsigned(3, 8888))
                : Row(i))).ToArray();

        ProfessionIpzDecoder.TryDecode(Message(rows), out var snapshot)
            .Should().BeTrue();
        snapshot!.Rows[3].UnidentifiedVarints.ContainsKey(3).Should().BeFalse();

        string summary = ProfessionIpzDecoder.DescribeAnonymously(snapshot, RefPairs);
        summary.Should().Contain("f3:present=19/20,distinct=19");
    }

    [Fact]
    public void BareMinimumRowsStillDecodeWithoutInventingProfessionIds()
    {
        ProfessionIpzDecoder.TryDecode(Ipz(includeExtras: false), out var snap)
            .Should().BeTrue();

        ProfessionIpzDecoder.DescribeAnonymously(snap!, RefPairs)
            .Should().Contain("unknown=[none] identity=unverified");
    }

    [Fact]
    public void OversizedAndRandomBodiesAreRejected()
    {
        ProfessionIpzDecoder.TryDecode(new byte[16 * 1024 + 1], out _)
            .Should().BeFalse();
        ProfessionIpzDecoder.TryDecode([0x00, 0x01], out _)
            .Should().BeFalse();
        ProfessionIpzDecoder.TryDecode([], out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TrackerReportsIpzOnlyInTheNewExplicitModeAndOnlyServerToClient()
    {
        byte[] content = Frame(Any("ipz", Ipz()));
        CharacterCaptureFlowKey key = new(
            "127.0.0.1", 40001, "10.0.0.2", 5555);
        DateTime now = new(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc);

        CharacterCaptureFlowTracker selected = new(
            professionReferences: RefPairs, decodeIpz: true);
        CharacterCaptureEvent reported = selected.Observe(
            key, CharacterCaptureDirection.ServerToClient,
            100, content, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        reported.ProfessionIpzSummary.Should().Contain("matched=3/3");
        reported.ProfessionCandidates.Should().BeNull();

        CharacterCaptureFlowTracker original = new();
        original.Observe(key, CharacterCaptureDirection.ServerToClient,
            100, content, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message)
            .ProfessionIpzSummary.Should().BeNull();

        CharacterCaptureFlowTracker client = new(
            professionReferences: RefPairs, decodeIpz: true);
        client.Observe(key, CharacterCaptureDirection.ClientToServer,
            100, content, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message)
            .ProfessionIpzSummary.Should().BeNull();
    }

    [Fact]
    public void IpzSummaryCannotRevealRawTcpEndpointOrValues()
    {
        CharacterCaptureFlowTracker tracker = new(
            professionReferences: RefPairs, decodeIpz: true);
        var results = tracker.Observe(
            new("127.0.0.1", 40001, "10.0.0.2", 5555),
            CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("ipz", Ipz())),
            new DateTime(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc));

        string text = System.Text.Json.JsonSerializer.Serialize(
            results.Single(e => e.Kind == CharacterCaptureEventKind.Message));
        text.Should().NotContain("127.0.0.1");
        text.Should().NotContain("10.0.0.2");
        text.Should().NotContain("900");
        text.Should().NotContain("319");
    }

    [Fact]
    public void DifferentConnectionsRemainIndependent()
    {
        CharacterCaptureFlowTracker tracker = new(
            professionReferences: RefPairs, decodeIpz: true);
        DateTime now = new(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc);
        var a = tracker.Observe(
            new("127.0.0.1", 40001, "10.0.0.2", 5555),
            CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("ipz", Ipz())), now);
        var b = tracker.Observe(
            new("127.0.0.1", 40002, "10.0.0.2", 5555),
            CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("ipz", Ipz())), now);

        a.Single(e => e.Kind == CharacterCaptureEventKind.Message)
            .SessionNumber.Should().NotBe(
                b.Single(e => e.Kind == CharacterCaptureEventKind.Message)
                    .SessionNumber);
    }
}
