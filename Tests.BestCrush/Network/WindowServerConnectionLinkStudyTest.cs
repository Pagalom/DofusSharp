using System.Net;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class WindowServerConnectionLinkStudyTest
{
    private static readonly DateTime At = new(2026, 10, 22, 20, 0, 0, DateTimeKind.Utc);

    private static TcpSocketTuple Socket(int num) =>
        new(IPAddress.Parse("192.0.2.42"), (ushort)(48000 + num),
            IPAddress.Parse("198.51.100.9"), 5555);

    private static CharacterCaptureEvent Event(int n, CharacterCaptureEventKind kind,
        string? op = null) =>
        new(At, n, CharacterCaptureDirection.ServerToClient, kind, op);

    private static (int ProcessId, int WindowSlot)[] Windows() =>
        [(11, 1), (22, 2)];

    private static void Session(WindowServerConnectionLinkStudy study, int n,
        int pid, bool selection, int id = 0, bool game = false,
        bool loaded = false, bool closed = true)
    {
        var tuple = Socket(n);
        study.ObserveOwner(tuple, [new TcpSocketOwner(tuple, pid)], Windows());
        List<CharacterCaptureEvent> events = [Event(n, CharacterCaptureEventKind.Opened)];
        if (selection)
            study.ObserveSelectedId(n, id);
        if (game)
            events.Add(Event(n, CharacterCaptureEventKind.Message, "ksc"));
        if (loaded)
            events.Add(Event(n, CharacterCaptureEventKind.Message, "ksb"));
        if (closed)
            events.Add(Event(n, CharacterCaptureEventKind.Closed));
        study.ObserveEvents(tuple, events);
    }

    [Fact]
    public void InterleavedTwoClientsLinkToTheirOwnWindowsNotToGlobalAdjacency()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295);
        Session(study, 2, 22, selection: true, id: 355);
        Session(study, 3, 22, selection: false, game: true, loaded: true);
        Session(study, 4, 11, selection: false, game: true, loaded: true);

        string output = string.Join("\n", study.Summarize());
        output.Should().Contain("p3m13=loaded window-index=1 connection-session=1 game-session=4 reference=Draconiros")
            .And.Contain("p3m13=loaded window-index=2 connection-session=2 game-session=3 reference=Kourial")
            .And.Contain("p3m13=summary flows=4 windows-linked=2 windows-ready=2 loaded-pairs=2 references-matched=2")
            .And.Contain("status=two-window-links-candidate")
            .And.NotContain("198.51.100.9")
            .And.NotContain("48001")
            .And.NotContain("pid=11");
    }

    [Fact]
    public void SameServerForTwoClientsIsNotAWindowCollision()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295);
        Session(study, 2, 22, selection: true, id: 295);
        Session(study, 3, 11, selection: false, game: true, loaded: true);
        Session(study, 4, 22, selection: false, game: true, loaded: true);
        string[] result = study.Summarize().ToArray();
        result.Count(l => l.StartsWith("p3m13=loaded ")).Should().Be(2);
        result.Last().Should().Contain("two-window-links-candidate");
    }

    [Fact]
    public void DifferentProcessSelectionIsNeverJoinedToOtherWindowsGame()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295);
        Session(study, 2, 22, selection: false, game: true, loaded: true);
        study.Summarize().Last().Should().Contain("status=not-confirmed");
        study.Summarize().Should().NotContain(l => l.StartsWith("p3m13=loaded "));
    }

    [Fact]
    public void OwnerFoundBeforeWindowAppearsCanBeResolvedLater()
    {
        WindowServerConnectionLinkStudy study = new();
        var c = Socket(1);
        var g = Socket(2);
        study.ObserveOwner(c, [new TcpSocketOwner(c, 11)], []);
        study.ObserveOwner(g, [new TcpSocketOwner(g, 11)], []);
        study.ObserveSelectedId(1, 295);
        study.ObserveEvents(c, [
            Event(1, CharacterCaptureEventKind.Opened),
            Event(1, CharacterCaptureEventKind.Closed)]);
        study.ObserveEvents(g, [
            Event(2, CharacterCaptureEventKind.Opened),
            Event(2, CharacterCaptureEventKind.Message, "ksc"),
            Event(2, CharacterCaptureEventKind.Message, "ksb"),
            Event(2, CharacterCaptureEventKind.Closed)]);
        study.ObserveWindows([(11, 1)]);
        string[] output = study.Summarize().ToArray();
        output.Should().Contain(l => l.StartsWith(
            "p3m13=loaded window-index=1 connection-session=1 game-session=2"));
        output.Last().Should().Contain("windows-ready=1");
    }

    [Fact]
    public void TwoWindowsBelongingToSamePidCannotBeBound()
    {
        WindowServerConnectionLinkStudy study = new();
        var c = Socket(1);
        study.ObserveOwner(c, [new TcpSocketOwner(c, 11)],
            [(11, 1), (11, 2)]);
        study.ObserveSelectedId(1, 295);
        study.ObserveEvents(c, [Event(1, CharacterCaptureEventKind.Opened)]);
        study.Summarize().Last().Should()
            .Contain("window-missing=1").And.Contain("not-confirmed");
    }

    [Fact]
    public void DuplicateOSSocketOwnersFailClosed()
    {
        WindowServerConnectionLinkStudy study = new();
        var c = Socket(1);
        study.ObserveOwner(c, [
            new TcpSocketOwner(c, 11), new TcpSocketOwner(c, 22)
        ], Windows());
        study.ObserveSelectedId(1, 295);
        study.ObserveEvents(c, [Event(1, CharacterCaptureEventKind.Opened)]);
        study.Summarize().Last().Should()
            .Contain("owner-missing=1").And.Contain("not-confirmed");
    }

    [Fact]
    public void ConflictingOwnerForSameTupleInvalidatesFlow()
    {
        WindowServerConnectionLinkStudy study = new();
        var c = Socket(1);
        study.ObserveOwner(c, [new TcpSocketOwner(c, 11)], Windows());
        study.ObserveOwner(c, [new TcpSocketOwner(c, 22)], Windows());
        study.ObserveSelectedId(1, 295);
        study.ObserveEvents(c, [Event(1, CharacterCaptureEventKind.Opened)]);
        study.Summarize().Last().Should()
            .Contain("conflicts=1").And.Contain("not-confirmed");
    }

    [Fact]
    public void MissingEstablishedSocketCannotBeGuessedFromVisibleWindow()
    {
        WindowServerConnectionLinkStudy study = new();
        var c = Socket(1);
        study.ObserveOwner(c, [], Windows());
        study.ObserveSelectedId(1, 295);
        study.ObserveEvents(c, [Event(1, CharacterCaptureEventKind.Opened)]);
        study.NeedsOwner(c).Should().BeTrue();
        study.Summarize().Last().Should().Contain("owner-missing=1");
    }

    [Fact]
    public void LiveGameWithKscAndKsbLinksWithoutClosingTcpSocket()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295);
        Session(study, 2, 22, selection: true, id: 355);
        Session(study, 3, 11, selection: false, game: true,
            loaded: true, closed: false);
        Session(study, 4, 22, selection: false, game: true,
            loaded: true, closed: false);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("p3m13=loaded window-index=1 connection-session=1 game-session=3 reference=Draconiros")
            .And.Contain("p3m13=loaded window-index=2 connection-session=2 game-session=4 reference=Kourial")
            .And.Contain("game-state=loaded-live")
            .And.Contain("windows-ready=2 loaded-pairs=2 references-matched=2")
            .And.Contain("status=two-window-links-candidate");
    }

    [Fact]
    public void LiveGameWithOnlyKscIsIncompleteNotLoaded()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295);
        Session(study, 2, 11, selection: false, game: true,
            loaded: false, closed: false);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("game-state=ksc-only-live")
            .And.Contain("reason=game-incomplete")
            .And.Contain("loaded-pairs=0")
            .And.Contain("status=not-confirmed");
    }

    [Fact]
    public void TwoSelectorsOneLiveGameStayUnverifiedAndExplainDifference()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295);
        Session(study, 2, 11, selection: true, id: 355);
        Session(study, 3, 11, selection: false, game: true,
            loaded: true, closed: false);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("game-state=loaded-live")
            .And.Contain("preceding-selections=2 distinct-selection-values=2")
            .And.Contain("reason=multiple-distinct-selections")
            .And.Contain("status=not-confirmed");
        report.Should().NotContain("p3m13=loaded");
    }

    [Fact]
    public void TwoSameIdSelectorsStillDoNotProveWhichConnectionBelongsToGame()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295);
        Session(study, 2, 11, selection: true, id: 295);
        Session(study, 3, 11, selection: false, game: true,
            loaded: true, closed: false);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("preceding-selections=2 distinct-selection-values=1")
            .And.Contain("reason=multiple-selections")
            .And.Contain("status=not-confirmed");
        report.Should().NotContain("p3m13=loaded");
    }

    [Fact]
    public void P3m13RealisticTwoWindowsOneWithRepeatedSelectionsGetsPartialResult()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295);
        Session(study, 2, 11, selection: true, id: 295);
        Session(study, 3, 22, selection: true, id: 355);
        Session(study, 4, 11, selection: false, game: true,
            loaded: true, closed: false);
        Session(study, 5, 22, selection: false, game: true,
            loaded: true, closed: false);

        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("p3m13=window window-index=1 connection-selections=2 game-attempts=1 linked=0 loaded=0")
            .And.Contain("reason=multiple-selections")
            .And.Contain("p3m13=window window-index=2 connection-selections=1 game-attempts=1 linked=1 loaded=1 references-matched=1 status=linked-candidate")
            .And.Contain("windows-ready=1 loaded-pairs=1 references-matched=1")
            .And.Contain("status=not-confirmed");
    }

    [Fact]
    public void SelectorNotClosedDoesNotLinkToGame()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295, closed: false);
        Session(study, 2, 11, selection: false, game: true, loaded: true);
        study.Summarize().Last().Should().Contain("not-confirmed");
    }

    [Fact]
    public void TwoSelectionsBeforeSingleGameAreAmbiguous()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295);
        Session(study, 2, 11, selection: true, id: 355);
        Session(study, 3, 11, selection: false, game: true, loaded: true);
        study.Summarize().Last().Should().Contain("not-confirmed");
    }

    [Fact]
    public void TyleziaReferenceIsNotMistakenForKourialOnTheOtherAccount()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 293);
        Session(study, 2, 11, selection: false, game: true, loaded: true);
        string report = string.Join("\n", study.Summarize());
        report.Should().Contain("reference=unknown")
            .And.Contain("status=not-confirmed")
            .And.NotContain("reference=Kourial");
    }

    [Fact]
    public void UnrecognizedServerIdCannotBePresentedAsRecognizedServer()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 999);
        Session(study, 2, 11, selection: false, game: true, loaded: true);
        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("reference=unknown")
            .And.Contain("references-matched=0")
            .And.Contain("not-confirmed")
            .And.NotContain("999");
    }

    [Fact]
    public void TwoConsecutiveGamesInOneWindowGetDistinctSelections()
    {
        WindowServerConnectionLinkStudy study = new();
        Session(study, 1, 11, selection: true, id: 295);
        Session(study, 2, 11, selection: false, game: true, loaded: true);
        Session(study, 3, 11, selection: true, id: 355);
        Session(study, 4, 11, selection: false, game: true, loaded: true);
        string result = string.Join("\n", study.Summarize());
        result.Should().Contain("reference=Draconiros")
            .And.Contain("reference=Kourial")
            .And.Contain("loaded=2 references-matched=2 status=linked-candidate");
    }
}
