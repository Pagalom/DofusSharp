using System.Text;
using BestCrush.Network.Capture;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>All message bodies, names and candidate IDs are synthetic.</summary>
public sealed class KtaSelectedCharacterStudyTest
{
    private static readonly DateTime T0 = new(2026, 10, 22, 11, 0, 0, DateTimeKind.Utc);
    private static CharacterCaptureEvent Ev(int n, CharacterCaptureEventKind kind) =>
        new(T0, n, CharacterCaptureDirection.ServerToClient, kind);
    private static byte[] Row(ulong id, string name, ulong level = 90) =>
        Message(Unsigned(3, id), Bytes(4, Message(
            Unsigned(3, level),
            Bytes(6, Encoding.UTF8.GetBytes(name)))));
    private static byte[] Ksc(ulong id, string name) =>
        Message(Bytes(2, Row(id, name)));
    private static byte[] Ksb(ulong id, string name) =>
        Message(Bytes(1, Message(Bytes(1, Row(id, name)))));
    private static byte[] Kta(ulong id) => Message(Unsigned(1, id));

    private static void Phase(KtaSelectedCharacterStudy study, int stage,
        int number, ulong kta, ulong selected, string name)
    {
        study.StartStage(stage).Should().BeTrue();
        study.ObserveEvents([Ev(number, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(number, CharacterCaptureDirection.ClientToServer,
            "kta", Kta(kta));
        study.ObserveMessage(number, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(selected, name));
        study.ObserveMessage(number, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(selected, name));
        study.ObserveEvents([Ev(number, CharacterCaptureEventKind.Closed)]);
    }

    [Fact]
    public void TwoDifferentCharactersMatchingThreeFamiliesProducePositiveEvidence()
    {
        KtaSelectedCharacterStudy study = new();
        Phase(study, 0, 2, 987001, 987001, "Synthetic-X");
        Phase(study, 1, 4, 987002, 987002, "Synthetic-Y");
        string summary = string.Join("\n", study.Summarize());

        summary.Should().Contain("p3m8=phase-X")
            .And.Contain("roster-linked=yes valid=yes kta-vs-ksb=equal")
            .And.Contain("p3m8=summary comparable=yes both-match=yes selected-f3-distinct=yes")
            .And.Contain("identity=unverified");
        summary.Should().NotContain("987001").And.NotContain("987002")
            .And.NotContain("Synthetic-X").And.NotContain("Synthetic-Y");
    }

    [Fact]
    public void DifferentKtaValueCannotBeMistakenForIdEquality()
    {
        KtaSelectedCharacterStudy study = new();
        Phase(study, 0, 2, 123000, 987001, "Synthetic-X");
        Phase(study, 1, 4, 987002, 987002, "Synthetic-Y");
        study.Summarize().Last().Should()
            .Contain("comparable=yes both-match=no selected-f3-distinct=yes");
    }

    [Fact]
    public void SameF3ForTwoDeclaredCharactersCannotProveDiscrimination()
    {
        KtaSelectedCharacterStudy study = new();
        Phase(study, 0, 2, 77, 77, "Synthetic-X");
        Phase(study, 1, 4, 77, 77, "Synthetic-X");
        study.Summarize().Last().Should().Contain("selected-f3-distinct=no");
    }

    [Fact]
    public void KtaMayAppearOnAuthenticationTcpSessionOfSameManualPhase()
    {
        KtaSelectedCharacterStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([Ev(1, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(1, CharacterCaptureDirection.ClientToServer,
            "kta", Kta(1001));
        study.ObserveEvents([Ev(1, CharacterCaptureEventKind.Closed)]);
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(1001, "Synthetic-X"));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(1001, "Synthetic-X"));
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 1002, 1002, "Synthetic-Y");
        string summary = string.Join("\n", study.Summarize());
        summary.Should().Contain("phase-X tcp-sessions=2 kta-messages=1")
            .And.Contain("comparable=yes both-match=yes");
    }

    [Fact]
    public void KscRosterWithMultipleCharactersUsesKsbSelectionNotFirstEntry()
    {
        KtaSelectedCharacterStudy study = new();
        study.StartStage(0);
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "kta", Kta(1002));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Message(
                Bytes(2, Row(1001, "Synthetic-X")),
                Bytes(2, Row(1002, "Synthetic-Y"))));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(1002, "Synthetic-Y"));
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 1001, 1001, "Synthetic-X");

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("roster-linked=yes valid=yes kta-vs-ksb=equal")
            .And.Contain("both-match=yes selected-f3-distinct=yes");
    }

    [Fact]
    public void KtaPointingAtDifferentRosterCharacterDoesNotMatchSelectedKsb()
    {
        KtaSelectedCharacterStudy study = new();
        study.StartStage(0);
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "kta", Kta(1001));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Message(
                Bytes(2, Row(1001, "Synthetic-X")),
                Bytes(2, Row(1002, "Synthetic-Y"))));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(1002, "Synthetic-Y"));
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 1001, 1001, "Synthetic-X");

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("phase-X")
            .And.Contain("roster-linked=yes valid=yes kta-vs-ksb=different")
            .And.Contain("both-match=no");
    }

    [Fact]
    public void KscCandidateMustAgreeWithKsbIdNameAndLevel()
    {
        KtaSelectedCharacterStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "kta", Kta(1001));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(1001, "Synthetic-X"));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(1002, "Synthetic-Y"));
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 1002, 1002, "Synthetic-Y");
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    [Fact]
    public void MultipleKtaValuesInPhaseAreAmbiguous()
    {
        KtaSelectedCharacterStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "kta", Kta(1001));
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "kta", Kta(1002));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(1001, "Synthetic-X"));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(1001, "Synthetic-X"));
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 1002, 1002, "Synthetic-Y");
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    [Fact]
    public void RepeatedIdenticalKtaInPhaseIsAccepted()
    {
        KtaSelectedCharacterStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Opened)]);
        for (int i = 0; i < 3; i++)
            study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
                "kta", Kta(1001));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(1001, "Synthetic-X"));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(1001, "Synthetic-X"));
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 1002, 1002, "Synthetic-Y");
        study.Summarize().Last().Should().Contain("both-match=yes");
    }

    [Theory]
    [MemberData(nameof(BadKtaFixtures))]
    public void MalformedOrDuplicateKtaF1FailsClosed(byte[] body)
    {
        KtaSelectedCharacterStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer, "kta", body);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(1001, "Synthetic-X"));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(1001, "Synthetic-X"));
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 1002, 1002, "Synthetic-Y");
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    public static IEnumerable<object[]> BadKtaFixtures()
    {
        yield return [Message(Unsigned(2, 333))];
        yield return [Message(Unsigned(1, 333), Unsigned(1, 333))];
        yield return [Message(Bytes(1, [0x41]))];
        yield return [Array.Empty<byte>()];
        yield return [new byte[8193]];
    }

    [Fact]
    public void KtaInWrongDirectionOrAfterSelectedCharacterDoesNotCount()
    {
        KtaSelectedCharacterStudy study = new();
        study.StartStage(0);
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Opened)]);
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "kta", Kta(1001));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksc", Ksc(1001, "Synthetic-X"));
        study.ObserveMessage(2, CharacterCaptureDirection.ServerToClient,
            "ksb", Ksb(1001, "Synthetic-X"));
        study.ObserveMessage(2, CharacterCaptureDirection.ClientToServer,
            "kta", Kta(1001)); // ignored after ksb
        study.ObserveEvents([Ev(2, CharacterCaptureEventKind.Closed)]);
        Phase(study, 1, 4, 1002, 1002, "Synthetic-Y");
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    [Fact]
    public void MultipleCompletedGameSessionsCannotProveSingleSelection()
    {
        KtaSelectedCharacterStudy study = new();
        study.StartStage(0);
        foreach (int n in new[] { 2, 3 })
        {
            study.ObserveMessage(n, CharacterCaptureDirection.ClientToServer,
                "kta", Kta(1001));
            study.ObserveMessage(n, CharacterCaptureDirection.ServerToClient,
                "ksc", Ksc(1001, "Synthetic-X"));
            study.ObserveMessage(n, CharacterCaptureDirection.ServerToClient,
                "ksb", Ksb(1001, "Synthetic-X"));
            study.ObserveEvents([Ev(n, CharacterCaptureEventKind.Closed)]);
        }
        Phase(study, 1, 4, 1002, 1002, "Synthetic-Y");
        study.Summarize().Last().Should().Contain("comparable=no");
    }

    [Fact]
    public void RealTrackerFeedsTwoDirectionFramesIntoStudy()
    {
        KtaSelectedCharacterStudy study = new();
        study.StartStage(0);
        CharacterCaptureFlowTracker tracker = new(ktaCharacterStudy: study);
        CharacterCaptureFlowKey key = new("192.0.2.4", 45000, "198.51.100.1", 5555);
        byte[] req = Frame(Any("kta", Kta(1001)));
        byte[] ksc = Frame(Any("ksc", Ksc(1001, "Synthetic-X")));
        byte[] ksb = Frame(Any("ksb", Ksb(1001, "Synthetic-X")));
        var sent = tracker.Observe(key, CharacterCaptureDirection.ClientToServer,
            100, req, T0);
        study.ObserveEvents(sent);
        var a = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            200, ksc, T0.AddSeconds(1));
        study.ObserveEvents(a);
        var b = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            (uint)(200 + ksc.Length), ksb, T0.AddSeconds(2));
        study.ObserveEvents(b);
        study.ObserveEvents(tracker.Observe(key,
            CharacterCaptureDirection.ClientToServer,
            (uint)(100 + req.Length), [], T0.AddSeconds(3), fin: true));
        string summary = string.Join("\n", study.Summarize());
        summary.Should().Contain("phase-X tcp-sessions=1 kta-messages=1")
            .And.Contain("roster-linked=yes valid=yes kta-vs-ksb=equal");
    }

    [Fact]
    public void StudyCannotBeCombinedWithOtherDiagnosticModes()
    {
        KtaSelectedCharacterStudy study = new();
        Action run = () => new CharacterCaptureFlowTracker(
            ktaCharacterStudy: study, studyProfessionFlow: true);
        run.Should().Throw<ArgumentException>();
    }
}
