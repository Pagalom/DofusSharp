using BestCrush.Network.Capture;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>
/// Metadata-only P1 research. These synthetic frames contain no game account data.
/// </summary>
public sealed class CharacterCaptureFlowTrackerTest
{
    private static readonly DateTime T0 =
        new(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);

    private static CharacterCaptureFlowKey Flow(int clientPort = 40001) =>
        new("127.0.0.1", clientPort, "10.0.0.10", 5555);

    private static byte[] MessageFrame(string opcode = "abc") =>
        Frame(Any(opcode, Unsigned(1, 7654321)));

    [Fact]
    public void TwoClientsStayInSeparateSessionsDespiteMatchingTcpSequences()
    {
        CharacterCaptureFlowTracker tracker = new();
        var first = tracker.Observe(
            Flow(40001), CharacterCaptureDirection.ServerToClient,
            100, MessageFrame("aaa"), T0);
        var second = tracker.Observe(
            Flow(40002), CharacterCaptureDirection.ServerToClient,
            100, MessageFrame("bbb"), T0.AddSeconds(1));

        first.Should().ContainSingle(x =>
            x.Kind == CharacterCaptureEventKind.Message &&
            x.Opcode == "aaa" && x.SessionNumber == 1);
        second.Should().ContainSingle(x =>
            x.Kind == CharacterCaptureEventKind.Message &&
            x.Opcode == "bbb" && x.SessionNumber == 2);
        tracker.FlowCount.Should().Be(2);
    }

    [Fact]
    public void BothDirectionsShareSessionNumberButNotFramingBuffers()
    {
        CharacterCaptureFlowTracker tracker = new();
        var sent = tracker.Observe(Flow(),
            CharacterCaptureDirection.ClientToServer,
            10, MessageFrame("send"), T0);
        var received = tracker.Observe(Flow(),
            CharacterCaptureDirection.ServerToClient,
            10, MessageFrame("recv"), T0.AddSeconds(1));

        sent.Should().ContainSingle(x => x.Opcode == "send" && x.SessionNumber == 1);
        received.Should().ContainSingle(x => x.Opcode == "recv" && x.SessionNumber == 1);
    }

    [Fact]
    public void PartialTcpSegmentsDoNotProduceFalseMessages()
    {
        CharacterCaptureFlowTracker tracker = new();
        byte[] frame = MessageFrame("split");
        int cut = frame.Length / 2;

        var first = tracker.Observe(Flow(), CharacterCaptureDirection.ServerToClient,
            100, frame[..cut], T0);
        first.Should().NotContain(x => x.Kind == CharacterCaptureEventKind.Message);

        var second = tracker.Observe(Flow(), CharacterCaptureDirection.ServerToClient,
            (uint)(100 + cut), frame[cut..], T0.AddSeconds(1));

        second.Should().ContainSingle(x =>
            x.Kind == CharacterCaptureEventKind.Message && x.Opcode == "split");
    }

    [Fact]
    public void FinClosesSessionEvenWithoutPayloadAndLateDataIsDiscarded()
    {
        CharacterCaptureFlowTracker tracker = new();
        tracker.Observe(Flow(), CharacterCaptureDirection.ServerToClient,
            100, MessageFrame(), T0);

        tracker.Observe(Flow(), CharacterCaptureDirection.ServerToClient,
            200, [], T0.AddSeconds(1), fin: true)
            .Should().ContainSingle(x => x.Kind == CharacterCaptureEventKind.Closed);

        tracker.Observe(Flow(), CharacterCaptureDirection.ServerToClient,
            201, MessageFrame("late"), T0.AddSeconds(2))
            .Should().NotContain(x => x.Kind == CharacterCaptureEventKind.Message);
    }

    [Fact]
    public void RstClosesSessionWithoutContentAndNextSynGetsNewNumber()
    {
        CharacterCaptureFlowTracker tracker = new();
        tracker.Observe(Flow(), CharacterCaptureDirection.ServerToClient,
            100, MessageFrame(), T0);

        tracker.Observe(Flow(), CharacterCaptureDirection.ClientToServer,
            200, [], T0.AddSeconds(1), rst: true)
            .Should().ContainSingle(x => x.Kind == CharacterCaptureEventKind.Closed);

        var next = tracker.Observe(Flow(), CharacterCaptureDirection.ClientToServer,
            600, [], T0.AddSeconds(3), syn: true);
        next.Should().ContainSingle(x =>
            x.Kind == CharacterCaptureEventKind.Opened && x.SessionNumber == 2);
    }

    [Fact]
    public void NormalSynAndSynAckHandshakeSharesOneSession()
    {
        CharacterCaptureFlowTracker tracker = new();
        var syn = tracker.Observe(Flow(), CharacterCaptureDirection.ClientToServer,
            100, [], T0, syn: true);
        var synAck = tracker.Observe(Flow(), CharacterCaptureDirection.ServerToClient,
            500, [], T0.AddMilliseconds(5), syn: true);

        syn.Should().ContainSingle(x =>
            x.Kind == CharacterCaptureEventKind.Opened && x.SessionNumber == 1);
        synAck.Should().BeEmpty();

        var data = tracker.Observe(Flow(), CharacterCaptureDirection.ClientToServer,
            101, MessageFrame("login"), T0.AddSeconds(1));
        data.Should().ContainSingle(x => x.Opcode == "login" && x.SessionNumber == 1);
    }

    [Fact]
    public void ReusedTcpEndpointsAfterTrafficRequireNewConnectionState()
    {
        CharacterCaptureFlowTracker tracker = new();
        tracker.Observe(Flow(), CharacterCaptureDirection.ClientToServer,
            100, MessageFrame(), T0);

        var reopened = tracker.Observe(Flow(), CharacterCaptureDirection.ClientToServer,
            1000, [], T0.AddMinutes(1), syn: true);

        reopened.Should().Contain(x =>
            x.Kind == CharacterCaptureEventKind.Closed && x.SessionNumber == 1);
        reopened.Should().Contain(x =>
            x.Kind == CharacterCaptureEventKind.Opened && x.SessionNumber == 2);
    }

    [Fact]
    public void FlowCapacityIsBoundedAndEvictionsAreMetadataOnly()
    {
        CharacterCaptureFlowTracker tracker = new(maxFlows: 1);
        tracker.Observe(Flow(), CharacterCaptureDirection.ClientToServer,
            100, MessageFrame(), T0);

        var second = tracker.Observe(Flow(40002),
            CharacterCaptureDirection.ClientToServer,
            100, MessageFrame("next"), T0.AddSeconds(1));

        tracker.FlowCount.Should().Be(1);
        second.Should().Contain(x => x.Kind == CharacterCaptureEventKind.Evicted);
        second.Should().Contain(x =>
            x.Kind == CharacterCaptureEventKind.Message && x.SessionNumber == 2);
    }

    [Fact]
    public void IdleFlowIsExpiredBeforeAnyNewFramesAreRead()
    {
        CharacterCaptureFlowTracker tracker =
            new(idleTimeout: TimeSpan.FromSeconds(10));

        tracker.Observe(Flow(), CharacterCaptureDirection.ClientToServer,
            100, MessageFrame(), T0);

        var next = tracker.Observe(Flow(40002),
            CharacterCaptureDirection.ClientToServer,
            100, MessageFrame("new"), T0.AddSeconds(11));

        next.Should().Contain(x =>
            x.Kind == CharacterCaptureEventKind.Evicted && x.SessionNumber == 1);
        tracker.FlowCount.Should().Be(1);
    }

    [Fact]
    public void DiagnosticContainsNeitherRawPayloadNorEndpointAddresses()
    {
        CharacterCaptureFlowTracker tracker = new();
        byte[] privateBody = Unsigned(1, 7654321);
        var events = tracker.Observe(Flow(), CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("safe", privateBody)), T0);

        CharacterCaptureEvent message = events.Single(x =>
            x.Kind == CharacterCaptureEventKind.Message);

        message.Opcode.Should().Be("safe");
        message.BodyLength.Should().Be(privateBody.Length);
        string serialized = System.Text.Json.JsonSerializer.Serialize(message);
        serialized.Should().NotContain("127.0.0.1");
        serialized.Should().NotContain("10.0.0.10");
        serialized.Should().NotContain("7654321");
    }

    [Fact]
    public void MaliciousOrUnexpectedOpcodeIsNeverPrintedVerbatim()
    {
        CharacterCaptureFlowTracker tracker = new();
        var items = tracker.Observe(Flow(), CharacterCaptureDirection.ServerToClient,
            100, MessageFrame("name\nsecret"), T0);

        items.Should().ContainSingle(x =>
            x.Kind == CharacterCaptureEventKind.Message &&
            x.Opcode == "(invalid)");
    }

    [Fact]
    public void InvalidEndpointCannotCreateFlow()
    {
        CharacterCaptureFlowTracker tracker = new();

        Action action = () => tracker.Observe(
            new CharacterCaptureFlowKey("", 40000, "10.0.0.1", 5555),
            CharacterCaptureDirection.ClientToServer,
            100, MessageFrame(), T0);

        action.Should().Throw<ArgumentException>();
        tracker.FlowCount.Should().Be(0);
    }
}
