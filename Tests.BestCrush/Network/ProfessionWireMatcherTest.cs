using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class ProfessionWireMatcherTest
{
    private static readonly ProfessionReference[] References =
        [new(82, 150), new(63, 245), new(47, 20)];

    private static byte[] Record(int level, int xp) =>
        Message(Unsigned(1, (ulong)level), Unsigned(3, (ulong)xp));

    [Fact]
    public void ValidatesInput()
    {
        ProfessionReferenceInput.TryParse("82:150,63:245",
            out IReadOnlyList<ProfessionReference> values).Should().BeTrue();
        values.Should().HaveCount(2);
        ProfessionReferenceInput.TryParse("82:150",
            out _).Should().BeFalse();
        ProfessionReferenceInput.TryParse("82:150,82:150",
            out _).Should().BeFalse();
        ProfessionReferenceInput.TryParse("1:0,63:245",
            out _).Should().BeFalse();
    }

    [Fact]
    public void CorrelatesRepeatedRecordsAtCommonNode()
    {
        byte[] body = Message(
            Bytes(2, Record(82, 150)),
            Bytes(2, Record(63, 245)),
            Bytes(2, Record(47, 20)));

        ProfessionWireMatcher.Describe(body, References)
            .Should().Be("node=f2 level=f1 xp=f3 pairs=3");
    }

    [Fact]
    public void OnePairDoesNotQualify()
    {
        byte[] body = Message(
            Bytes(2, Record(82, 150)),
            Bytes(2, Record(17, 999)));
        ProfessionWireMatcher.Describe(body, References).Should().BeNull();
    }

    [Fact]
    public void DoesNotCombineDifferentNodes()
    {
        byte[] body = Message(
            Bytes(2, Unsigned(1, 82)),
            Bytes(3, Unsigned(3, 150)),
            Bytes(2, Unsigned(1, 63)),
            Bytes(3, Unsigned(3, 245)));

        ProfessionWireMatcher.Describe(body, References).Should().BeNull();
    }

    [Fact]
    public void DoesNotCombineNumbersInTheSameField()
    {
        byte[] body = Message(
            Bytes(1, Message(Unsigned(2, 82), Unsigned(2, 150))),
            Bytes(1, Message(Unsigned(2, 63), Unsigned(2, 245))));
        ProfessionWireMatcher.Describe(body, References).Should().BeNull();
    }

    [Fact]
    public void InvalidBodiesDoNotYieldMatches()
    {
        ProfessionWireMatcher.Describe([0x0a, 0x10, 0x01], References)
            .Should().BeNull();
        ProfessionWireMatcher.Describe(new byte[256 * 1024 + 1], References)
            .Should().BeNull();
    }

    [Fact]
    public void IndependentSessionsDoNotCombineReferences()
    {
        CharacterCaptureFlowTracker tracker = new(professionReferences: References);
        DateTime now = new(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc);
        var first = tracker.Observe(
            new("127.0.0.1", 40001, "10.0.0.1", 5555),
            CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("pft", Bytes(2, Record(82, 150)))), now);
        var second = tracker.Observe(
            new("127.0.0.1", 40002, "10.0.0.1", 5555),
            CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("pft", Bytes(2, Record(63, 245)))), now);

        first.Single(x => x.Kind == CharacterCaptureEventKind.Message)
            .ProfessionCandidates.Should().BeNull();
        second.Single(x => x.Kind == CharacterCaptureEventKind.Message)
            .ProfessionCandidates.Should().BeNull();
    }

    [Fact]
    public void ReportContainsOnlyStructuralHints()
    {
        CharacterCaptureFlowTracker tracker = new(professionReferences: References);
        byte[] body = Message(Bytes(3, Record(82, 150)),
            Bytes(3, Record(63, 245)));
        var events = tracker.Observe(
            new("127.0.0.1", 40001, "10.0.0.1", 5555),
            CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("pft", body)),
            new DateTime(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc));

        CharacterCaptureEvent entry = events.Single(x =>
            x.Kind == CharacterCaptureEventKind.Message);
        entry.ProfessionCandidates.Should().Be("node=f3 level=f1 xp=f3 pairs=2");
        string output = System.Text.Json.JsonSerializer.Serialize(entry);
        output.Should().NotContain("127.0.0.1");
        output.Should().NotContain("245");
        output.Should().NotContain("150");
    }

    [Fact]
    public void ClientDirectionAndOldModeRemainUnaffected()
    {
        byte[] body = Message(Bytes(3, Record(82, 150)),
            Bytes(3, Record(63, 245)));
        var route = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        DateTime now = new(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc);
        CharacterCaptureFlowTracker selected =
            new(professionReferences: References);
        CharacterCaptureFlowTracker original = new();

        selected.Observe(route, CharacterCaptureDirection.ClientToServer,
            100, Frame(Any("pft", body)), now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message)
            .ProfessionCandidates.Should().BeNull();
        original.Observe(route, CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("pft", body)), now)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message)
            .ProfessionCandidates.Should().BeNull();
    }
}
