using BestCrush.Network.Capture;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>All game routes and numeric fields are synthetic.</summary>
public sealed class KtaFieldScopeStudyTest
{
    private static readonly DateTime T0 =
        new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

    private static CharacterCaptureFlowKey Key(int session, string remote = "198.51.100.12") =>
        new("192.0.2.5", 48000 + session, remote, 5555);

    private static CharacterCaptureEvent Event(int session,
        CharacterCaptureEventKind kind, string? opcode = null) =>
        new(T0, session, CharacterCaptureDirection.ServerToClient, kind, opcode);

    private static void Phase(KtaFieldScopeStudy study, int phase, int session,
        ulong value, string remote = "198.51.100.12")
    {
        study.StartStage(phase).Should().BeTrue();
        var key = Key(session, remote);
        study.ObserveEvents(key, [Event(session, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(session, key, CharacterCaptureDirection.ClientToServer,
            "kta", Message(Unsigned(1, value)));
        study.ObserveEvents(key, [
            Event(session, CharacterCaptureEventKind.Message, "ksc"),
            Event(session, CharacterCaptureEventKind.Message, "ksb"),
            Event(session, CharacterCaptureEventKind.Closed)
        ]);
    }

    [Fact]
    public void FourPhaseStableOnServerAAndDifferentOnBIsOnlyScopeCandidate()
    {
        KtaFieldScopeStudy study = new();
        Phase(study, 0, 2, 13579);
        Phase(study, 1, 4, 13579);
        Phase(study, 2, 6, 24680, "198.51.100.22");
        Phase(study, 3, 8, 13579);
        string result = string.Join("\n", study.Summarize());

        result.Should().Contain("p3m7=summary comparable=yes")
            .And.Contain("reconnect-X=equal")
            .And.Contain("same-server-X-vs-Y=equal")
            .And.Contain("cross-server-X-vs-Z=different")
            .And.Contain("kta-f1-scope=server-or-account-server-candidate")
            .And.Contain("semantic=unverified server=unverified")
            .And.NotContain("13579")
            .And.NotContain("24680");
        result.Should().Contain("game-session=2 destination=198.51.100.12:5555")
            .And.Contain("game-session=6 destination=198.51.100.22:5555")
            .And.NotContain("48002");
    }

    [Fact]
    public void CharacterDependentPatternIsNotServerIdentity()
    {
        KtaFieldScopeStudy study = new();
        Phase(study, 0, 2, 100);
        Phase(study, 1, 4, 101);
        Phase(study, 2, 6, 200);
        Phase(study, 3, 8, 100);
        study.Summarize().Last().Should()
            .Contain("same-server-X-vs-Y=different")
            .And.Contain("kta-f1-scope=character-or-selection-candidate");
    }

    [Fact]
    public void AllSameValueSuggestsAccountOrConstantNotServer()
    {
        KtaFieldScopeStudy study = new();
        Phase(study, 0, 2, 42);
        Phase(study, 1, 4, 42);
        Phase(study, 2, 6, 42);
        Phase(study, 3, 8, 42);
        study.Summarize().Last().Should().Contain("cross-server-X-vs-Z=equal")
            .And.Contain("kta-f1-scope=account-wide-or-constant-candidate");
    }

    [Fact]
    public void ReconnectionChangingValueIsNotStable()
    {
        KtaFieldScopeStudy study = new();
        Phase(study, 0, 2, 21);
        Phase(study, 1, 4, 21);
        Phase(study, 2, 6, 12);
        Phase(study, 3, 8, 22);
        study.Summarize().Last().Should().Contain("reconnect-X=different")
            .And.Contain("kta-f1-scope=unstable-candidate");
    }

    [Fact]
    public void SameValueAcrossMultipleKtaMessagesIsAcceptable()
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        var key = Key(2);
        study.ObserveEvents(key, [Event(2, CharacterCaptureEventKind.Opened)]);
        for (int i = 0; i < 2; i++)
            study.ObserveMessage(2, key, CharacterCaptureDirection.ClientToServer,
                "kta", Message(Unsigned(1, 123)));
        study.ObserveEvents(key, [
            Event(2, CharacterCaptureEventKind.Message, "ksc"),
            Event(2, CharacterCaptureEventKind.Message, "ksb"),
            Event(2, CharacterCaptureEventKind.Closed)
        ]);
        Phase(study, 1, 4, 123);
        Phase(study, 2, 6, 234);
        Phase(study, 3, 8, 123);
        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("phase-A-X1 tcp-sessions=1 completed-game=1 kta-messages=2 kta-distinct-values=1 valid=yes");
        result.Should().Contain("comparable=yes");
    }

    [Fact]
    public void ConflictingValuesWithinPhaseInvalidateComparison()
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        var key = Key(2);
        study.ObserveMessage(2, key, CharacterCaptureDirection.ClientToServer,
            "kta", Message(Unsigned(1, 12)));
        study.ObserveMessage(2, key, CharacterCaptureDirection.ClientToServer,
            "kta", Message(Unsigned(1, 13)));
        study.ObserveEvents(key, [
            Event(2, CharacterCaptureEventKind.Message, "ksc"),
            Event(2, CharacterCaptureEventKind.Message, "ksb"),
            Event(2, CharacterCaptureEventKind.Closed)
        ]);
        Phase(study, 1, 4, 12);
        Phase(study, 2, 6, 13);
        Phase(study, 3, 8, 12);

        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("phase-A-X1 tcp-sessions=1 completed-game=1 kta-messages=2 kta-distinct-values=2 valid=no")
            .And.Contain("summary comparable=no kta-f1-scope=unavailable");
    }

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public void MissingDuplicateAndWrongWireFieldsFailClosed(byte[] body)
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        var key = Key(2);
        study.ObserveEvents(key, [Event(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, key, CharacterCaptureDirection.ClientToServer,
            "kta", body);
        study.ObserveEvents(key, [
            Event(2, CharacterCaptureEventKind.Message, "ksc"),
            Event(2, CharacterCaptureEventKind.Message, "ksb"),
            Event(2, CharacterCaptureEventKind.Closed)
        ]);
        Phase(study, 1, 4, 10);
        Phase(study, 2, 6, 20);
        Phase(study, 3, 8, 10);
        study.Summarize().Last().Should()
            .Contain("comparable=no kta-f1-scope=unavailable");
    }

    public static IEnumerable<object[]> InvalidFields()
    {
        yield return [Message(Unsigned(2, 22))];
        yield return [Message(Unsigned(1, 22), Unsigned(1, 22))];
        yield return [Message(Bytes(1, [10, 11]))];
        yield return [Array.Empty<byte>()];
        yield return [new byte[1025]];
        yield return [new byte[] { 0x08, 0x80 }];
    }

    [Fact]
    public void IncompleteGameSessionCannotSupportStage()
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        var key = Key(2);
        study.ObserveEvents(key, [Event(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, key, CharacterCaptureDirection.ClientToServer,
            "kta", Message(Unsigned(1, 10)));
        study.ObserveEvents(key, [
            Event(2, CharacterCaptureEventKind.Message, "ksc"),
            Event(2, CharacterCaptureEventKind.Closed)
        ]);
        Phase(study, 1, 4, 10);
        Phase(study, 2, 6, 20);
        Phase(study, 3, 8, 10);
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    [Fact]
    public void ASecondUnfinishedGameCandidateMakesStageAmbiguous()
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        var first = Key(2);
        var second = Key(3);
        study.ObserveMessage(2, first, CharacterCaptureDirection.ClientToServer,
            "kta", Message(Unsigned(1, 10)));
        study.ObserveEvents(first, [
            Event(2, CharacterCaptureEventKind.Message, "ksc"),
            Event(2, CharacterCaptureEventKind.Message, "ksb"),
            Event(2, CharacterCaptureEventKind.Closed)
        ]);
        study.ObserveEvents(second, [
            Event(3, CharacterCaptureEventKind.Opened),
            Event(3, CharacterCaptureEventKind.Message, "ksc"),
            Event(3, CharacterCaptureEventKind.Message, "ksb")
        ]);
        Phase(study, 1, 4, 10);
        Phase(study, 2, 6, 20);
        Phase(study, 3, 8, 10);
        study.Summarize().Last().Should()
            .Contain("comparable=no kta-f1-scope=unavailable");
    }

    [Fact]
    public void EvictedSessionInvalidatesItsStage()
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        var key = Key(2);
        study.ObserveMessage(2, key, CharacterCaptureDirection.ClientToServer,
            "kta", Message(Unsigned(1, 10)));
        study.ObserveEvents(key, [
            Event(2, CharacterCaptureEventKind.Message, "ksc"),
            Event(2, CharacterCaptureEventKind.Message, "ksb"),
            Event(2, CharacterCaptureEventKind.Evicted)
        ]);
        Phase(study, 1, 4, 10);
        Phase(study, 2, 6, 20);
        Phase(study, 3, 8, 10);
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    [Fact]
    public void ServerToClientKtaIsNeverTrustedAsClientSelection()
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        var key = Key(2);
        study.ObserveMessage(2, key, CharacterCaptureDirection.ServerToClient,
            "kta", Message(Unsigned(1, 10)));
        study.ObserveEvents(key, [
            Event(2, CharacterCaptureEventKind.Message, "ksc"),
            Event(2, CharacterCaptureEventKind.Message, "ksb"),
            Event(2, CharacterCaptureEventKind.Closed)
        ]);
        Phase(study, 1, 4, 10);
        Phase(study, 2, 6, 20);
        Phase(study, 3, 8, 10);
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    [Fact]
    public void StageIsBoundToSocketOpenAndLateMessagesCannotReattribute()
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        var key = Key(2);
        study.ObserveEvents(key, [Event(2, CharacterCaptureEventKind.Opened)]);
        study.StartStage(1);
        study.ObserveMessage(2, key, CharacterCaptureDirection.ClientToServer,
            "kta", Message(Unsigned(1, 10)));
        study.ObserveEvents(key, [
            Event(2, CharacterCaptureEventKind.Message, "ksc"),
            Event(2, CharacterCaptureEventKind.Message, "ksb"),
            Event(2, CharacterCaptureEventKind.Closed)
        ]);
        Phase(study, 2, 6, 20);
        Phase(study, 3, 8, 10);
        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("phase-A-X1 tcp-sessions=1 completed-game=1 kta-messages=1")
            .And.Contain("phase-A-Y tcp-sessions=0");
    }

    [Fact]
    public void IPv6DestinationsFormatAndSensitiveValuesRemainInternal()
    {
        KtaFieldScopeStudy study = new();
        Phase(study, 0, 2, 1, "2001:db8::4");
        Phase(study, 1, 4, 1, "2001:db8::4");
        Phase(study, 2, 6, 2, "2001:db8::8");
        Phase(study, 3, 8, 1, "2001:db8::4");

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("destination=[2001:db8::4]:5555")
            .And.Contain("destination=[2001:db8::8]:5555")
            .And.Contain("semantic=unverified");
    }

    [Fact]
    public void MoreThan32SessionsCannotProduceComparison()
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        for (int i = 1; i <= 33; i++)
            study.ObserveEvents(Key(i), [Event(i, CharacterCaptureEventKind.Opened)]);
        study.StartStage(1);
        study.StartStage(2);
        study.StartStage(3);
        study.Summarize().Last().Should()
            .Contain("comparable=no")
            .And.Contain("reason=session-limit");
    }

    [Fact]
    public void StandaloneModeReceivesSyntheticTcpFrames()
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        CharacterCaptureFlowTracker tracker = new(ktaScopeStudy: study);
        CharacterCaptureFlowKey key = Key(2);
        byte[] a = Frame(Any("kta", Message(Unsigned(1, 9876))));
        byte[] b = Frame(Any("ksc", Message(Unsigned(2, 1))));
        byte[] c = Frame(Any("ksb", Message(Unsigned(2, 1))));
        uint seq = 100;
        var sent = tracker.Observe(key, CharacterCaptureDirection.ClientToServer,
            seq, a, T0);
        study.ObserveEvents(key, sent);
        seq += (uint)a.Length;
        // ksc and ksb are received on the independent server→client stream.
        var first = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            200, b, T0.AddSeconds(1));
        study.ObserveEvents(key, first);
        var second = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            (uint)(200 + b.Length), c, T0.AddSeconds(2));
        study.ObserveEvents(key, second);
        study.ObserveEvents(key, tracker.Observe(key,
            CharacterCaptureDirection.ClientToServer, seq, [],
            T0.AddSeconds(3), fin: true));

        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("phase-A-X1 tcp-sessions=1")
            .And.Contain("kta-messages=1 kta-distinct-values=1");
    }

    [Fact]
    public void KtaMayComeFromPreloginTcpSessionSeparateFromGame()
    {
        KtaFieldScopeStudy study = new();
        study.StartStage(0);
        var auth = Key(1, "198.51.100.99");
        var game = Key(2);
        study.ObserveEvents(auth, [Event(1, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(1, auth, CharacterCaptureDirection.ClientToServer,
            "kta", Message(Unsigned(1, 867530)));
        study.ObserveEvents(auth, [Event(1, CharacterCaptureEventKind.Closed)]);
        study.ObserveEvents(game, [
            Event(2, CharacterCaptureEventKind.Opened),
            Event(2, CharacterCaptureEventKind.Message, "ksc"),
            Event(2, CharacterCaptureEventKind.Message, "ksb"),
            Event(2, CharacterCaptureEventKind.Closed)
        ]);
        Phase(study, 1, 4, 867530);
        Phase(study, 2, 6, 867531);
        Phase(study, 3, 8, 867530);

        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("phase-A-X1 tcp-sessions=2 completed-game=1")
            .And.Contain("game-session=2 destination=198.51.100.12:5555")
            .And.Contain("kta-f1-scope=server-or-account-server-candidate");
    }

    [Fact]
    public void KtaStudyIsExclusiveOfOtherTrackers()
    {
        KtaFieldScopeStudy study = new();
        Action act = () => new CharacterCaptureFlowTracker(
            ktaScopeStudy: study, studyProfessionFlow: true);
        act.Should().Throw<ArgumentException>();
    }
}
