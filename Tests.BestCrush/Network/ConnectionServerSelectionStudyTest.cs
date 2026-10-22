using System.Text;
using BestCrush.Network.Capture;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>Purely synthetic Connection and Game protobuf fixtures.</summary>
public sealed class ConnectionServerSelectionStudyTest
{
    private static readonly DateTime T0 = new(2026, 10, 22, 20, 0, 0, DateTimeKind.Utc);
    private static CharacterCaptureFlowKey Key(int session, string ip = "198.51.100.4") =>
        new("192.0.2.7", 49000 + session, ip, 5555);
    private static CharacterCaptureEvent E(int n, CharacterCaptureEventKind kind,
        string? opcode = null) =>
        new(T0, n, CharacterCaptureDirection.ServerToClient, kind, opcode);

    // Both documented Unity versions contain outer.f1.f4.f1, but use
    // UUID versus lang in outer.f1.f1. Neither is ever printed.
    private static byte[] Select(int id, string context = "fr") => Message(
        Bytes(1, Message(
            Bytes(1, Encoding.UTF8.GetBytes(context)),
            Bytes(4, Message(Unsigned(1, (ulong)id))))));

    private static void Phase(ConnectionServerSelectionStudy study, int stage,
        int authSession, int gameSession, int id, string ip = "198.51.100.4")
    {
        study.StartStage(stage).Should().BeTrue();
        var auth = Key(authSession, ip);
        study.ObserveEvents(auth, [E(authSession, CharacterCaptureEventKind.Opened)]);
        study.ObserveFrame(authSession, auth, CharacterCaptureDirection.ClientToServer,
            Select(id), hasGameAny: false);
        study.ObserveEvents(auth, [E(authSession, CharacterCaptureEventKind.Closed)]);
        var game = Key(gameSession, ip);
        study.ObserveEvents(game, [
            E(gameSession, CharacterCaptureEventKind.Opened),
            E(gameSession, CharacterCaptureEventKind.Message, "ksc"),
            E(gameSession, CharacterCaptureEventKind.Message, "ksb"),
            E(gameSession, CharacterCaptureEventKind.Closed)
        ]);
    }

    [Fact]
    public void FindsStableSelectionPatternAcrossIndependentAuthAndGameSockets()
    {
        ConnectionServerSelectionStudy study = new();
        Phase(study, 0, 1, 2, 12345, "198.51.100.4");
        Phase(study, 1, 3, 4, 67890, "198.51.100.5");
        Phase(study, 2, 5, 6, 12345, "198.51.100.6");

        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("stage-A1 tcp-sessions=2 completed-game=1")
            .And.Contain("select-candidates=1 shape-matches=1 distinct-values=1")
            .And.Contain("selector-session=1 selector-destination=198.51.100.4:5555")
            .And.Contain("p3m11=summary comparable=yes A1-vs-A2=equal A1-vs-B=different pattern=A-B-A")
            .And.Contain("meaning=structural-candidate-only server=unverified")
            .And.NotContain("12345").And.NotContain("67890");
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("64eb3871-fa39-4ffa-b9e0-a61fa145f598")]
    public void SupportsTwoPublishedContextShapesWithoutAssumingVersion(string context)
    {
        ConnectionServerSelectionStudy.TryGetSelectedServerCandidate(
            Select(108, context), out int candidate).Should().BeTrue();
        candidate.Should().Be(108);
    }

    [Fact]
    public void RefusesGameAnyFramesEvenIfNestedFieldsAccidentallyMatch()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        var key = Key(1);
        study.ObserveFrame(1, key, CharacterCaptureDirection.ClientToServer,
            Select(55555), hasGameAny: true);
        string stage = study.Summarize()[0];
        stage.Should().Contain("frames=1 non-game-frames=0")
            .And.Contain("select-candidates=0");
    }

    [Theory]
    [MemberData(nameof(InvalidFrames))]
    public void MalformedOrAmbiguousFramesAreNotSelectionCandidates(byte[] body)
    {
        ConnectionServerSelectionStudy.TryGetSelectedServerCandidate(
            body, out int candidate).Should().BeFalse();
        candidate.Should().Be(0);
    }

    public static IEnumerable<object[]> InvalidFrames()
    {
        yield return [Array.Empty<byte>()];
        yield return [Message(Unsigned(1, 100))];
        yield return [Message(Bytes(1, Message(Bytes(4, Message(Unsigned(1, 77))))))];
        yield return [Message(Bytes(1, Message(Bytes(1, Encoding.UTF8.GetBytes("fr")))))];
        yield return [Message(Bytes(1, Message(
            Bytes(1, Encoding.UTF8.GetBytes("fr")),
            Bytes(4, Message(Unsigned(2, 101))))))];
        yield return [Message(Bytes(1, Message(
            Bytes(1, Encoding.UTF8.GetBytes("fr")),
            Bytes(4, Message(Unsigned(1, 101), Unsigned(1, 101))))))];
        yield return [Message(Bytes(1, Message(
            Bytes(1, Encoding.UTF8.GetBytes("fr")),
            Bytes(4, Message(Unsigned(1, 0))))))];
        yield return [Message(Bytes(1, Message(
            Bytes(1, Encoding.UTF8.GetBytes("fr")),
            Bytes(4, Message(Unsigned(1, (ulong)int.MaxValue + 1))))))];
        yield return [new byte[] { 0x0a, 0x05, 0x0a, 0x02 }];
        yield return [new byte[8193]];
    }

    [Fact]
    public void ServerToClientFrameCannotBeMisinterpretedAsSelectionRequest()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        var key = Key(1);
        study.ObserveFrame(1, key, CharacterCaptureDirection.ServerToClient,
            Select(777), hasGameAny: false);
        string stage = study.Summarize().Single(line =>
            line.StartsWith("p3m11=stage-A1 ", StringComparison.Ordinal));
        stage.Should().Contain("non-game-frames=1")
            .And.Contain("non-game-c2s=0 non-game-s2c=1")
            .And.Contain("select-candidates=0");
    }

    [Fact]
    public void ConflictingIdsWithinStageMakeSummaryUnavailable()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        var key = Key(1);
        study.ObserveFrame(1, key, CharacterCaptureDirection.ClientToServer,
            Select(100), false);
        study.ObserveFrame(1, key, CharacterCaptureDirection.ClientToServer,
            Select(101), false);
        study.ObserveEvents(key, [E(1, CharacterCaptureEventKind.Closed)]);
        study.ObserveEvents(Key(2), [
            E(2, CharacterCaptureEventKind.Message, "ksc"),
            E(2, CharacterCaptureEventKind.Message, "ksb"),
            E(2, CharacterCaptureEventKind.Closed)
        ]);
        Phase(study, 1, 3, 4, 200);
        Phase(study, 2, 5, 6, 100);
        study.Summarize().Last().Should().Contain("comparable=no");
        study.Summarize()[0].Should()
            .Contain("select-candidates=2 shape-matches=2 distinct-values=2");
    }

    [Fact]
    public void SameNumericalValueAcrossAllServersDoesNotPretendServerDiscrimination()
    {
        ConnectionServerSelectionStudy study = new();
        Phase(study, 0, 1, 2, 42);
        Phase(study, 1, 3, 4, 42);
        Phase(study, 2, 5, 6, 42);
        study.Summarize().Last().Should()
            .Contain("comparable=yes").And.Contain("pattern=other");
    }

    [Fact]
    public void NonClosedGameSessionCannotMakeCompleteStage()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        var auth = Key(1);
        study.ObserveFrame(1, auth, CharacterCaptureDirection.ClientToServer,
            Select(10), false);
        study.ObserveEvents(Key(2), [
            E(2, CharacterCaptureEventKind.Message, "ksc"),
            E(2, CharacterCaptureEventKind.Message, "ksb")
        ]);
        Phase(study, 1, 3, 4, 20);
        Phase(study, 2, 5, 6, 10);
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    [Fact]
    public void EvictionAndSessionCapacityFailClosed()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        for (int i = 1; i <= 25; i++)
            study.ObserveEvents(Key(i), [E(i, CharacterCaptureEventKind.Opened)]);
        study.StartStage(1);
        study.StartStage(2);
        study.Summarize().Last().Should()
            .Contain("reason=session-capacity");
    }

    [Fact]
    public void TrackerReceivesConnectionFrameBeforeAnyFilter()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        CharacterCaptureFlowTracker tracker = new(connectionSelectionStudy: study);
        CharacterCaptureFlowKey key = Key(1);

        byte[] select = Frame(Select(765));
        var e1 = tracker.Observe(key, CharacterCaptureDirection.ClientToServer,
            100, select, T0);
        study.ObserveEvents(key, e1);

        byte[] game = Frame(Any("ksc", []));
        var e2 = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            200, game, T0.AddSeconds(1));
        study.ObserveEvents(key, e2);
        string result = study.Summarize()[0];
        result.Should().Contain("tcp-sessions=1")
            .And.Contain("frames=2 non-game-frames=1")
            .And.Contain("non-game-c2s=1 non-game-s2c=0")
            .And.Contain("select-candidates=1");
    }

    [Fact]
    public void P3m11bDescribesOnlyFieldNumbersAndWireTypes()
    {
        byte[] payload = Select(938274, "sensitive-uuid-test");
        string shape = ConnectionServerSelectionStudy
            .DescribeNonGameFrameShape(payload);

        shape.Should().Be("root(f1:L)/f1(f1:L.f4:L)/f4(f1:V)")
            .And.NotContain("sensitive-uuid-test")
            .And.NotContain("938274");
    }

    [Fact]
    public void NonGameClientFramesAreReportedPerSocketWithoutValues()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        CharacterCaptureFlowKey key = Key(1);
        study.ObserveFrame(1, key, CharacterCaptureDirection.ClientToServer,
            Select(871265, "some-uuid"), hasGameAny: false);
        study.ObserveFrame(1, key, CharacterCaptureDirection.ServerToClient,
            Message(Unsigned(2, 11)), hasGameAny: false);

        string[] lines = study.Summarize().ToArray();
        lines[0].Should().Contain("non-game-c2s=1 non-game-s2c=1");
        string raw = lines.Single(x => x.StartsWith("p3m11=socket stage=A1"));
        raw.Should().Contain("session=1 destination=198.51.100.4:5555")
            .And.Contain("root(f1:L)/f1(f1:L.f4:L)/f4(f1:V)*1")
            .And.NotContain("871265")
            .And.NotContain("some-uuid");
    }

    [Fact]
    public void WorldLoadGameBurstNoLongerExhaustsConnectionFramesBudget()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        var key = Key(1);
        for (int i = 0; i < 300; i++)
            study.ObserveFrame(1, key, CharacterCaptureDirection.ServerToClient,
                Message(Unsigned(1, 25)), hasGameAny: true);
        study.ObserveFrame(1, key, CharacterCaptureDirection.ClientToServer,
            Select(77), hasGameAny: false);
        string stage = study.Summarize()[0];
        stage.Should().Contain("frames=301 non-game-frames=1")
            .And.Contain("select-candidates=1")
            .And.Contain("truncated=no");
    }

    [Fact]
    public void AfterKsbPacketsDoNotConsumeConnectionBudget()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        var key = Key(1);
        study.ObserveEvents(key, [
            E(1, CharacterCaptureEventKind.Opened),
            E(1, CharacterCaptureEventKind.Message, "ksc"),
            E(1, CharacterCaptureEventKind.Message, "ksb")
        ]);
        for (int i = 0; i < 350; i++)
            study.ObserveFrame(1, key, CharacterCaptureDirection.ClientToServer,
                Select(71), hasGameAny: false);
        study.Summarize()[0].Should().Contain("frames=0 non-game-frames=0")
            .And.Contain("truncated=no");
    }

    [Fact]
    public void ConnectionRawFrameBudgetStillCapsActualNonGameFlood()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        var key = Key(1);
        for (int i = 0; i < 257; i++)
            study.ObserveFrame(1, key, CharacterCaptureDirection.ServerToClient,
                Message(Unsigned(1, 55)), hasGameAny: false);
        study.Summarize()[0].Should().Contain("truncated=yes")
            .And.Contain("non-game-frames=257");
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("64eb3871-fa39-4ffa-b9e0-a61fa145f598")]
    public void ModernOrLegacyContextIsOpaqueAndOptionalUtf8ValidationRemoved(
        string context)
    {
        byte[] frame = Select(412, context);
        ConnectionServerSelectionStudy.InspectSelectedServerCandidate(frame,
            out int selected).Should().Be(
                ConnectionServerSelectionStudy.SelectionCandidateStatus.Accepted);
        selected.Should().Be(412);
    }

    [Fact]
    public void BinaryOrOneByteContextCanStillCarryValidSelection()
    {
        byte[] opaqueContext = { 0x00, 0xff, 0xfe, 0x01 };
        byte[] frame = Message(Bytes(1, Message(
            Bytes(1, opaqueContext),
            Bytes(4, Message(Unsigned(1, 841))))));
        ConnectionServerSelectionStudy.InspectSelectedServerCandidate(
            frame, out int candidate).Should().Be(
                ConnectionServerSelectionStudy.SelectionCandidateStatus.Accepted);
        candidate.Should().Be(841);

        byte[] shortContext = Message(Bytes(1, Message(
            Bytes(1, [0xff]),
            Bytes(4, Message(Unsigned(1, 841))))));
        ConnectionServerSelectionStudy.TryGetSelectedServerCandidate(
            shortContext, out int oneByteCandidate).Should().BeTrue();
        oneByteCandidate.Should().Be(841);
    }

    [Fact]
    public void InvalidCandidateReportsOnlySafeReasonCodesAndNeverFieldValues()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        CharacterCaptureFlowKey key = Key(1);
        byte[] zeroServerId = Select(0, "unique-sensitive-context-value");
        study.ObserveFrame(1, key, CharacterCaptureDirection.ClientToServer,
            zeroServerId, hasGameAny: false);
        string stage = study.Summarize()[0];
        string socket = study.Summarize().Single(line =>
            line.StartsWith("p3m11=socket stage=A1", StringComparison.Ordinal));
        stage.Should().Contain("select-candidates=0 shape-matches=1");
        socket.Should().Contain("rejection-codes=[zeroid*1]")
            .And.NotContain("unique-sensitive-context-value");
        ConnectionServerSelectionStudy.InspectSelectedServerCandidate(
            zeroServerId, out int candidate).Should().Be(
                ConnectionServerSelectionStudy.SelectionCandidateStatus.ZeroId);
        candidate.Should().Be(0);
    }

    [Fact]
    public void NumericSelectionMustNotBeAcceptedWhenEnvelopeContainsExtraField()
    {
        byte[] extraField = Message(Bytes(1, Message(
            Bytes(1, Encoding.UTF8.GetBytes("fr")),
            Bytes(4, Message(Unsigned(1, 201))),
            Unsigned(8, 11))));
        ConnectionServerSelectionStudy.TryGetSelectedServerCandidate(
            extraField, out _).Should().BeFalse();
    }

    [Fact]
    public void NumericCandidateStillMatchesIndependentConnectionPhases()
    {
        ConnectionServerSelectionStudy study = new();
        Phase(study, 0, 1, 2, 121, "198.51.100.4");
        Phase(study, 1, 3, 4, 243, "198.51.100.5");
        Phase(study, 2, 5, 6, 121, "198.51.100.6");
        study.Summarize().Last().Should()
            .Contain("comparable=yes A1-vs-A2=equal A1-vs-B=different pattern=A-B-A");
    }

    [Fact]
    public void ExclusiveModeRejectsUnrelatedMetadataCollection()
    {
        Action make = () => new CharacterCaptureFlowTracker(
            connectionSelectionStudy: new ConnectionServerSelectionStudy(),
            studyProfessionFlow: true);
        make.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LatePacketKeepsOriginalSessionStage()
    {
        ConnectionServerSelectionStudy study = new();
        study.StartStage(0);
        var key = Key(1);
        study.ObserveEvents(key, [E(1, CharacterCaptureEventKind.Opened)]);
        study.StartStage(1);
        study.ObserveFrame(1, key, CharacterCaptureDirection.ClientToServer,
            Select(100), false);
        string[] lines = study.Summarize().ToArray();
        lines.Single(line => line.StartsWith(
                "p3m11=stage-A1 ", StringComparison.Ordinal))
            .Should().Contain("tcp-sessions=1")
            .And.Contain("select-candidates=1");
        lines.Single(line => line.StartsWith(
                "p3m11=stage-B ", StringComparison.Ordinal))
            .Should().Contain("tcp-sessions=0");
    }
}
