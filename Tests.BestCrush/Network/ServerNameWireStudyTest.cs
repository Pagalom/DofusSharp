using System.Text;
using BestCrush.Network.Capture;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>Public server labels and synthetic protocol fixtures only.</summary>
public sealed class ServerNameWireStudyTest
{
    private static readonly DateTime T0 = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
    private static CharacterCaptureEvent E(int session, CharacterCaptureEventKind kind,
        string? opcode = null) =>
        new(T0, session, CharacterCaptureDirection.ServerToClient, kind, opcode);

    private static byte[] Name(string value) => Encoding.UTF8.GetBytes(value);
    private static byte[] Nested(string value) =>
        Message(Bytes(2, Message(Bytes(3, Name(value)))));
    private static ServerNameWireStudy Create() => new("Dakal", "Imagiro");

    private static void Stage(ServerNameWireStudy study, int stage,
        int session, string value, string opcode = "srv")
    {
        study.StartStage(stage).Should().BeTrue();
        study.ObserveEvents([E(session, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(session, CharacterCaptureDirection.ServerToClient,
            opcode, Nested(value));
        study.ObserveEvents([
            E(session, CharacterCaptureEventKind.Message, "ksc"),
            E(session, CharacterCaptureEventKind.Message, "ksb"),
            E(session, CharacterCaptureEventKind.Closed)
        ]);
    }

    [Fact]
    public void AToBToAExactServerNamesAtSamePathBecomeOnlyCandidate()
    {
        ServerNameWireStudy study = Create();
        Stage(study, 0, 2, "Dakal");
        Stage(study, 1, 4, "Imagiro");
        Stage(study, 2, 6, "Dakal");
        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("p3m9=summary comparable=yes any-name-match=yes")
            .And.Contain("scoped-candidate-count=1")
            .And.Contain("scoped-candidate-paths=[S-C:srv:f2.f3]")
            .And.Contain("semantic=unverified server=unverified")
            .And.Contain("name=A,mode=exact")
            .And.Contain("name=B,mode=exact")
            .And.NotContain("Dakal").And.NotContain("Imagiro");
    }

    [Fact]
    public void CatalogListingBothNamesThroughoutCannotBeSelectedServerEvidence()
    {
        ServerNameWireStudy study = Create();
        for (int stage = 0; stage < 3; stage++)
        {
            int n = (stage + 1) * 2;
            study.StartStage(stage);
            study.ObserveEvents([E(n, CharacterCaptureEventKind.Opened)]);
            study.ObserveMessage(n, CharacterCaptureDirection.ServerToClient,
                "catalog", Nested("Dakal"));
            study.ObserveMessage(n, CharacterCaptureDirection.ServerToClient,
                "catalog", Nested("Imagiro"));
            study.ObserveEvents([
                E(n, CharacterCaptureEventKind.Message, "ksc"),
                E(n, CharacterCaptureEventKind.Message, "ksb"),
                E(n, CharacterCaptureEventKind.Closed)
            ]);
        }
        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("scoped-candidate-count=0")
            .And.Contain("any-name-match=yes")
            .And.Contain("other-name-seen=yes");
    }

    [Fact]
    public void EmbeddedNameInsideUtf8JsonWithoutProtoStillMatches()
    {
        ServerNameWireStudy study = Create();
        Stage(study, 0, 2, "Dakal", "srv");
        study.StartStage(1);
        study.ObserveMessage(4, CharacterCaptureDirection.ClientToServer,
            "sel", Name("{\"serverName\":\"Imagiro\",\"mode\":1}"));
        study.ObserveEvents([
            E(4, CharacterCaptureEventKind.Message, "ksc"),
            E(4, CharacterCaptureEventKind.Message, "ksb"),
            E(4, CharacterCaptureEventKind.Closed)
        ]);
        Stage(study, 2, 6, "Dakal", "srv");
        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("C-S:sel:root{name=B,mode=embedded}")
            .And.Contain("any-name-match=yes")
            .And.Contain("scoped-candidate-count=0");
    }

    [Fact]
    public void LongerCharacterNameDoesNotCreateSpuriousServerMatch()
    {
        ServerNameWireStudy study = Create();
        Stage(study, 0, 2, "Dakalite");
        Stage(study, 1, 4, "ImagiroExtra");
        Stage(study, 2, 6, "Dakalite");
        string result = study.Summarize().Last();
        result.Should().Contain("comparable=yes any-name-match=no")
            .And.Contain("scoped-candidate-count=0");
    }

    [Fact]
    public void MatchingIsCaseInsensitiveIncludingFrenchContext()
    {
        ServerNameWireStudy study = new("Brâkmar", "Bonta");
        Stage(study, 0, 2, "BRÂKMAR");
        Stage(study, 1, 4, "bonta");
        Stage(study, 2, 6, "Brâkmar");
        study.Summarize().Last().Should().Contain("scoped-candidate-count=1");
    }

    [Fact]
    public void NumericOnlyOrUnrelatedMessagesGiveNoNameHits()
    {
        ServerNameWireStudy study = Create();
        for (int stage = 0; stage < 3; stage++)
        {
            int n = (stage + 1) * 2;
            study.StartStage(stage);
            study.ObserveMessage(n, CharacterCaptureDirection.ServerToClient,
                "srv", Message(Unsigned(1, (ulong)(stage + 100))));
            study.ObserveEvents([
                E(n, CharacterCaptureEventKind.Message, "ksc"),
                E(n, CharacterCaptureEventKind.Message, "ksb"),
                E(n, CharacterCaptureEventKind.Closed)
            ]);
        }
        study.Summarize().Last().Should()
            .Contain("comparable=yes any-name-match=no scoped-candidate-count=0");
    }

    [Fact]
    public void NoCompletedGameSessionDoesNotYieldComparableResult()
    {
        ServerNameWireStudy study = Create();
        Stage(study, 0, 2, "Dakal");
        study.StartStage(1);
        study.ObserveMessage(4, CharacterCaptureDirection.ServerToClient,
            "srv", Nested("Imagiro"));
        study.ObserveEvents([E(4, CharacterCaptureEventKind.Closed)]);
        Stage(study, 2, 6, "Dakal");
        study.Summarize().Last().Should().Contain("comparable=no")
            .And.Contain("reason=incomplete-or-ambiguous-stage");
    }

    [Fact]
    public void SecondCompleteGameSessionInStageMustFailClosed()
    {
        ServerNameWireStudy study = Create();
        Stage(study, 0, 2, "Dakal");
        study.StartStage(1);
        foreach (int n in new[] { 4, 5 })
        {
            study.ObserveMessage(n, CharacterCaptureDirection.ServerToClient,
                "srv", Nested("Imagiro"));
            study.ObserveEvents([
                E(n, CharacterCaptureEventKind.Message, "ksc"),
                E(n, CharacterCaptureEventKind.Message, "ksb"),
                E(n, CharacterCaptureEventKind.Closed)
            ]);
        }
        Stage(study, 2, 6, "Dakal");
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    [Fact]
    public void OversizedTextMarksPartialCoverageButDoesNotRevealIt()
    {
        ServerNameWireStudy study = Create();
        study.StartStage(0);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "srv", Bytes(1, new byte[4097]));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "srv", Nested("Dakal"));
        study.ObserveEvents([
            E(2, CharacterCaptureEventKind.Message, "ksc"),
            E(2, CharacterCaptureEventKind.Message, "ksb"),
            E(2, CharacterCaptureEventKind.Closed)
        ]);
        Stage(study, 1, 4, "Imagiro");
        Stage(study, 2, 6, "Dakal");
        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("coverage=partial")
            .And.Contain("scoped-candidate-count=1")
            .And.NotContain("000000000");
    }

    [Fact]
    public void MessagesAfterKsbInSameSocketAreExcluded()
    {
        ServerNameWireStudy study = Create();
        study.StartStage(0);
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveEvents([
            E(2, CharacterCaptureEventKind.Message, "ksc"),
            E(2, CharacterCaptureEventKind.Message, "ksb")
        ]);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "late", Nested("Dakal"));
        study.ObserveEvents([E(2, CharacterCaptureEventKind.Closed)]);
        Stage(study, 1, 4, "Imagiro");
        Stage(study, 2, 6, "Dakal");
        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("stage-A1")
            .And.Contain("expected-name-seen=no");
    }

    [Fact]
    public void SessionCapacityOverflowFailsClosed()
    {
        ServerNameWireStudy study = Create();
        study.StartStage(0);
        for (int i = 1; i <= 25; i++)
            study.ObserveEvents([E(i, CharacterCaptureEventKind.Opened)]);
        study.StartStage(1);
        study.StartStage(2);
        study.Summarize().Last().Should()
            .Contain("comparable=no").And.Contain("reason=session-capacity");
    }

    [Fact]
    public void TrackerReallyFeedsDecodedMessagesInBothDirections()
    {
        ServerNameWireStudy study = Create();
        study.StartStage(0);
        CharacterCaptureFlowTracker tracker = new(serverNameStudy: study);
        CharacterCaptureFlowKey key = new("192.0.2.9", 48000,
            "198.51.100.9", 5555);
        byte[] c = Frame(Any("srv", Nested("Dakal")));
        byte[] ksc = Frame(Any("ksc", Message(Unsigned(2, 10))));
        byte[] ksb = Frame(Any("ksb", Message(Unsigned(1, 10))));
        var first = tracker.Observe(key, CharacterCaptureDirection.ClientToServer,
            100, c, T0);
        study.ObserveEvents(first);
        var second = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            200, ksc, T0.AddSeconds(1));
        study.ObserveEvents(second);
        var third = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            (uint)(200 + ksc.Length), ksb, T0.AddSeconds(2));
        study.ObserveEvents(third);
        study.ObserveEvents(tracker.Observe(key,
            CharacterCaptureDirection.ClientToServer,
            (uint)(100 + c.Length), [], T0.AddSeconds(3), fin: true));
        string stage = study.Summarize()[0];
        stage.Should().Contain("stage-A1 tcp-sessions=1 completed-game=1")
            .And.Contain("C-S:srv:f2.f3{name=A,mode=exact}");
    }

    [Fact]
    public void ExclusiveStudyRejectsOtherCaptureModes()
    {
        Action make = () => new CharacterCaptureFlowTracker(
            serverNameStudy: Create(), studyProfessionFlow: true);
        make.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RejectsSameOrInvalidServerNames()
    {
        Action same = () => new ServerNameWireStudy("Dakal", "dAKal");
        Action empty = () => new ServerNameWireStudy(" ", "Bonta");
        same.Should().Throw<ArgumentException>();
        empty.Should().Throw<ArgumentException>();
    }
}
