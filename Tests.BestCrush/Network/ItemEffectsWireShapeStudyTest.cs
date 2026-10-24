using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class ItemEffectsWireShapeStudyTest
{
    private static byte[] Instance(params byte[][] effects) =>
        Message(Unsigned(3, 1), Unsigned(4, 6924), Unsigned(5, 9022491),
            Message(effects.Select(effect => Bytes(7, effect)).ToArray()));

    private static byte[] Iqx(params byte[][] effects) =>
        Bytes(2, Bytes(1, Instance(effects)));

    private static byte[] Iqz(params byte[][] effects) =>
        Bytes(1, Bytes(1, Instance(effects)));

    [Fact]
    public void FindsTwoCandidateEffectsWithoutDisclosingTheirIdsOrValues()
    {
        byte[] vitality = Message(Unsigned(1, 118), Unsigned(2, 99));
        byte[] intelligence = Message(Unsigned(1, 119), Unsigned(2, 20));

        string? first = ItemEffectsWireShapeStudy.Describe("iqx",
            Iqx(vitality, intelligence));
        string? second = ItemEffectsWireShapeStudy.Describe("iqz",
            Iqz(vitality, intelligence));

        first.Should().NotBeNull();
        first.Should().Be(second);
        first.Should().Contain("effects=2");
        first.Should().Contain("root=[1:v 2:v]x2");
        first.Should().NotContain("6924");
        first.Should().NotContain("9022491");
        first.Should().NotContain("118");
        first.Should().NotContain("119");
    }

    [Fact]
    public void ReusesSameNestedCurrentItemCandidateFromKch()
    {
        byte[] item = Instance(Message(Unsigned(1, 999), Unsigned(2, 123)));
        string? summary = ItemEffectsWireShapeStudy.Describe("kch",
            Bytes(3, Bytes(1, item)));
        summary.Should().Contain("effects=1");
        summary.Should().Contain("root=[1:v 2:v]");
        summary.Should().NotContain("999");
        summary.Should().NotContain("123");
    }

    [Fact]
    public void EffectsAbsentAreDifferentFromAnUnrecognizedItem()
    {
        ItemEffectsWireShapeStudy.Describe("iqx", Iqx())
            .Should().Be("effects=0 wire-shapes=[none]");
        ItemEffectsWireShapeStudy.Describe("iqx", Bytes(2,
            Bytes(1, Message(Unsigned(3, 1), Unsigned(4, 6924)))))
            .Should().BeNull();
        ItemEffectsWireShapeStudy.Describe("unknown", Iqx())
            .Should().BeNull();
    }

    [Fact]
    public void MalformedAndOversizedFieldsFailClosed()
    {
        ItemEffectsWireShapeStudy.Describe("iqx", [0x00])
            .Should().BeNull();
        ItemEffectsWireShapeStudy.Describe("iqx", new byte[4097])
            .Should().BeNull();
        ItemEffectsWireShapeStudy.Describe("iqx",
            Iqx(new byte[1025]))
            .Should().Be("effects=too-large");
        byte[][] many = Enumerable.Range(0, 65)
            .Select(_ => Message(Unsigned(1, 777))).ToArray();
        ItemEffectsWireShapeStudy.Describe("iqx", Iqx(many))
            .Should().Be("effects=over-limit");
    }

    [Fact]
    public void TextInsideOpaqueEffectsAndScalarVariantsNeverEscape()
    {
        byte[] text = Message(Unsigned(1, 12345678),
            Bytes(2, Encoding.UTF8.GetBytes("SecretCharacterName")));
        string? summary = ItemEffectsWireShapeStudy.Describe("iqx", Iqx(text));
        summary.Should().Contain("effects=1");
        summary.Should().NotContain("SecretCharacterName");
        summary.Should().NotContain("12345678");
        summary.Should().NotContain("6924");
    }

    [Fact]
    public void TrackerOnlyReportsCurrentServerToClientCandidateMessages()
    {
        CharacterCaptureFlowTracker tracker = new(itemEffectsShape: true);
        CharacterCaptureFlowKey flow = new(
            "127.0.0.1", 40001, "10.0.0.10", 5555);
        DateTime time = new(2026, 10, 24, 3, 45, 0, DateTimeKind.Utc);
        byte[] item = Iqx(Message(Unsigned(1, 118), Unsigned(2, 99)));

        CharacterCaptureEvent incoming = tracker.Observe(
            flow, CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("iqx", item)), time)
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        incoming.ItemEffectsShape.Should().Contain("effects=1");

        CharacterCaptureEvent outgoing = tracker.Observe(
            flow, CharacterCaptureDirection.ClientToServer,
            100, Frame(Any("iqx", item)), time.AddSeconds(1))
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        outgoing.ItemEffectsShape.Should().BeNull();

        CharacterCaptureEvent other = tracker.Observe(
            flow, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + Frame(Any("iqx", item)).Length),
            Frame(Any("unrelated", item)), time.AddSeconds(2))
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);
        other.ItemEffectsShape.Should().BeNull();

        string serialized = System.Text.Json.JsonSerializer.Serialize(incoming);
        serialized.Should().NotContain("9022491");
        serialized.Should().NotContain("6924");
        serialized.Should().NotContain("127.0.0.1");
    }

    [Fact]
    public void DefaultTrackerDoesNotAnalyzeCandidateEffects()
    {
        CharacterCaptureFlowTracker tracker = new();
        CharacterCaptureFlowKey flow = new(
            "127.0.0.1", 40001, "10.0.0.10", 5555);
        CharacterCaptureEvent message = tracker.Observe(flow,
            CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("iqx", Iqx(Message(Unsigned(1, 118))))),
            new DateTime(2026, 10, 24, 4, 0, 0, DateTimeKind.Utc))
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);

        message.ItemEffectsShape.Should().BeNull();
    }

    [Fact]
    public void LocalReferencesSelectF10WithoutExposingEffectValues()
    {
        byte[] first = Message(Unsigned(5, 118), Unsigned(10, 99));
        byte[] second = Message(Unsigned(5, 119), Unsigned(10, 20));

        string? iqx = ItemEffectsWireShapeStudy.Describe(
            "iqx", Iqx(first, second), [20, 99]);
        string? iqz = ItemEffectsWireShapeStudy.Describe(
            "iqz", Iqz(second, first), [99, 20]);

        iqx.Should().Be(iqz);
        iqx.Should().Contain("effects=2");
        iqx.Should().Contain("match=[f5:no f10:yes]");
        foreach (string sensitive in new[] { "118", "119", "9022491", "6924" })
            iqx.Should().NotContain(sensitive);
        iqx.Should().NotContain("99");
        iqx.Should().NotContain("20");
    }

    [Fact]
    public void LocalComparisonPreservesDuplicateAndNegativeValues()
    {
        byte[] first = Message(Unsigned(5, 118),
            Unsigned(10, unchecked((ulong)-2L)));
        byte[] second = Message(Unsigned(5, 119),
            Unsigned(10, unchecked((ulong)-2L)));
        ItemEffectsWireShapeStudy.Describe("iqx",
            Iqx(first, second), [-2, -2])
            .Should().Contain("match=[f5:no f10:yes]");

        ItemEffectsWireShapeStudy.Describe("iqx",
            Iqx(first, second), [-2, -1])
            .Should().Contain("match=[f5:no f10:no]");
    }

    [Fact]
    public void LocalComparisonDoesNotReportSuccessWhenEffectMissingOrMalformed()
    {
        byte[] effect = Message(Unsigned(5, 118), Unsigned(10, 99));
        ItemEffectsWireShapeStudy.Describe("iqx",
            Iqx(effect), [99, 20])
            .Should().Contain("match=[f5:incomplete f10:incomplete]");

        ItemEffectsWireShapeStudy.Describe("iqx",
            Iqx(Message(Unsigned(5, 118))), [99])
            .Should().Contain("match=[f5:no f10:unrecognized]");

        ItemEffectsWireShapeStudy.Describe("iqx",
            Iqx(Message(Bytes(5, [1, 2, 3]))), [99])
            .Should().Contain("match=[f5:unrecognized f10:unrecognized]");
    }

    [Fact]
    public void ExpectedReferencesCannotActivateStudyWithoutOptIn()
    {
        Action create = () => new CharacterCaptureFlowTracker(
            expectedEffectValues: [99, 20]);
        create.Should().Throw<ArgumentException>();

        Action empty = () => new CharacterCaptureFlowTracker(
            itemEffectsShape: true, expectedEffectValues: []);
        empty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComparisonModeDoesNotPrintReferencesThroughTrackerEvent()
    {
        CharacterCaptureFlowTracker tracker = new(
            itemEffectsShape: true, expectedEffectValues: [99, 20]);
        CharacterCaptureFlowKey flow = new(
            "127.0.0.1", 40001, "10.0.0.10", 5555);
        CharacterCaptureEvent message = tracker.Observe(
            flow, CharacterCaptureDirection.ServerToClient, 100,
            Frame(Any("iqx", Iqx(
                Message(Unsigned(5, 118), Unsigned(10, 99)),
                Message(Unsigned(5, 119), Unsigned(10, 20))))),
            new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc))
            .Single(x => x.Kind == CharacterCaptureEventKind.Message);

        message.ItemEffectsShape.Should().Contain("match=[f5:no f10:yes]");
        string report = System.Text.Json.JsonSerializer.Serialize(message);
        report.Should().NotContain("9022491");
        report.Should().NotContain("6924");
        report.Should().NotContain("118");
        report.Should().NotContain("119");
    }
}
