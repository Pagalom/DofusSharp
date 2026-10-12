using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class ProtoShapeSummarizerTest
{
    [Fact]
    public void DescribesOnlyFieldNumbersAndWireTypesWithoutScalarValues()
    {
        byte[] bytes = Message(
            Unsigned(1, 7654321),
            Float(2, 4.5f),
            Bytes(3, Encoding.UTF8.GetBytes("SecretCharacterName")),
            Unsigned(1, 99887766));

        string shape = ProtoShapeSummarizer.Describe(bytes);

        shape.Should().Contain("1:vx2");
        shape.Should().Contain("2:32");
        shape.Should().Contain("3:len");
        shape.Should().NotContain("7654321");
        shape.Should().NotContain("99887766");
        shape.Should().NotContain("SecretCharacterName");
    }

    [Fact]
    public void NestedFieldsAreMarkedOnlyAsUnverifiedCandidates()
    {
        byte[] bytes = Message(
            Unsigned(1, 150),
            Bytes(2, Message(Unsigned(1, 140), Unsigned(3, 2345))));

        string shape = ProtoShapeSummarizer.Describe(bytes);

        shape.Should().Contain("1:v");
        shape.Should().Contain("2:len");
        shape.Should().Contain("?2.1:v");
        shape.Should().Contain("?2.3:v");
        shape.Should().NotContain("2345");
    }

    [Fact]
    public void MalformedProtobufIsExplicitlyUnrecognized()
    {
        ProtoShapeSummarizer.Describe([0x00]).Should().Be("unrecognized");
        ProtoShapeSummarizer.Describe([0x08, 0x80]).Should().Be("unrecognized");
        ProtoShapeSummarizer.Describe([0x0b]).Should().Be("unrecognized");
        ProtoShapeSummarizer.Describe([0x80, 0x80, 0x80, 0x80, 0x80,
            0x80, 0x80, 0x80, 0x80, 0x02]).Should().Be("unrecognized");
        ProtoShapeSummarizer.Describe([]).Should().Be("empty");
    }

    [Fact]
    public void MaximumMessageSizeIsBounded()
    {
        ProtoShapeSummarizer.Describe(new byte[256 * 1024 + 1])
            .Should().Be("too-large");
    }

    [Fact]
    public void RootFieldCountHasAnIndependentUpperBound()
    {
        byte[] body = Message(Enumerable.Range(1, 32780)
            .Select(_ => Unsigned(1, 999999)).ToArray());

        string shape = ProtoShapeSummarizer.Describe(body);

        shape.Should().Contain("root=[1:vx32768]");
        shape.Should().Contain("[partial-root]");
        shape.Should().NotContain("999999");
        shape.Length.Should().BeLessThan(200);
    }

    [Fact]
    public void DoesNotTreatArbitraryTextAsVerifiedNestedProtobuf()
    {
        byte[] body = Bytes(3, Encoding.UTF8.GetBytes("classified bank token"));

        string shape = ProtoShapeSummarizer.Describe(body);

        shape.Should().Contain("3:len");
        shape.Should().NotContain("?3.");
        shape.Should().NotContain("classified");
    }

    [Fact]
    public void DifferentPrivateValuesWithSameWireLayoutGetSameShape()
    {
        byte[] alice = Message(Unsigned(1, 200),
            Bytes(2, Encoding.UTF8.GetBytes("Alice")));
        byte[] bob = Message(Unsigned(1, 123456),
            Bytes(2, Encoding.UTF8.GetBytes("Bobby")));

        ProtoShapeSummarizer.Describe(alice)
            .Should().Be(ProtoShapeSummarizer.Describe(bob));
    }

    [Fact]
    public void TrackerShapesOnlyExplicitlySelectedKeysAndKeepsClientIsolation()
    {
        CharacterCaptureFlowTracker tracker = new(shapeKeys:
            new HashSet<string>(StringComparer.Ordinal) { "irl" });

        CharacterCaptureFlowKey a = new("127.0.0.1", 40001, "10.0.0.10", 5555);
        CharacterCaptureFlowKey b = new("127.0.0.1", 40002, "10.0.0.10", 5555);
        DateTime t = new(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);

        var alice = tracker.Observe(a, CharacterCaptureDirection.ServerToClient,
            10, Frame(Any("irl", Unsigned(1, 7654321))), t);
        var bob = tracker.Observe(b, CharacterCaptureDirection.ServerToClient,
            10, Frame(Any("irl", Unsigned(1, 1234567))), t);
        var other = tracker.Observe(a, CharacterCaptureDirection.ServerToClient,
            10 + (uint)Frame(Any("irl", Unsigned(1, 7654321))).Length,
            Frame(Any("other", Unsigned(1, 3333))), t.AddSeconds(1));

        CharacterCaptureEvent first = alice.Single(x => x.Kind == CharacterCaptureEventKind.Message);
        CharacterCaptureEvent second = bob.Single(x => x.Kind == CharacterCaptureEventKind.Message);
        first.SessionNumber.Should().NotBe(second.SessionNumber);
        first.Shape.Should().Be("root=[1:v]");
        second.Shape.Should().Be("root=[1:v]");
        other.Single(x => x.Kind == CharacterCaptureEventKind.Message)
            .Shape.Should().BeNull();
    }

    [Fact]
    public void RepeatedRootRecordsAreCountedExactlyEvenIfNestedSamplingStops()
    {
        byte[][] records = Enumerable.Range(0, 172)
            .Select(index => Bytes(2, Message(
                Unsigned(1, (ulong)index + 100),
                Unsigned(4, 123456789),
                Bytes(5, Message(Unsigned(3, 999999))))))
            .ToArray();

        string shape = ProtoShapeSummarizer.Describe(Message(records));

        shape.Should().Contain("root=[2:lenx172]");
        shape.Should().Contain("?2.1:vx3");
        shape.Should().Contain("?2.4:vx3");
        shape.Should().NotContain("[partial-root]");
        shape.Should().NotContain("123456789");
        shape.Should().NotContain("999999");
    }

    [Fact]
    public void NestedFieldLimitNeverTruncatesIndependentRootCounts()
    {
        byte[] hugeNestedMessage = Message(Enumerable.Range(0, 600)
            .Select(_ => Unsigned(1, 5)).ToArray());
        byte[] body = Message(
            Bytes(2, hugeNestedMessage),
            Unsigned(7, 555),
            Unsigned(7, 666));

        string shape = ProtoShapeSummarizer.Describe(body);

        shape.Should().Contain("root=[2:len 7:vx2]");
        shape.Should().NotContain("[partial-root]");
        shape.Should().NotContain("sampled=[");
    }

    [Fact]
    public void RepeatedTextAndBinaryDataAreNotEmittedAsValues()
    {
        byte[][] strings = Enumerable.Range(0, 172)
            .Select(_ => Bytes(9,
                Encoding.UTF8.GetBytes("SensitiveCharacterName")))
            .ToArray();

        string shape = ProtoShapeSummarizer.Describe(Message(strings));

        shape.Should().Be("root=[9:lenx172]");
        shape.Should().NotContain("SensitiveCharacterName");
    }

    [Fact]
    public void DefaultTrackerNeverComputesOrReportsShapes()
    {
        CharacterCaptureFlowTracker tracker = new();
        var events = tracker.Observe(
            new CharacterCaptureFlowKey("127.0.0.1", 40001, "10.0.0.10", 5555),
            CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("irl", Unsigned(1, 7654321))),
            new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc));

        events.Single(x => x.Kind == CharacterCaptureEventKind.Message)
            .Shape.Should().BeNull();
    }

    [Fact]
    public void ShapeSelectionRejectsUnsafeOpcodeInput()
    {
        Action create = () => new CharacterCaptureFlowTracker(shapeKeys:
            new HashSet<string> { "private\nvalue" });
        create.Should().Throw<ArgumentException>();
    }
}
