using System.Net;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static BestCrush.Network.Protocol.ConcurrentRosterWindowEvidenceStudy;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3c.7: all window, PID and endpoint values are synthetic.
/// No protocol, OS or account identity may be inferred from test fixtures.
/// </summary>
public sealed class ConcurrentRosterWindowEvidenceStudyTest
{
    private static readonly TcpSocketTuple SocketA = new(
        IPAddress.Parse("192.0.2.10"), 40101,
        IPAddress.Parse("198.51.100.40"), 5555);
    private static readonly TcpSocketTuple SocketB = SocketA with { LocalPort = 40102 };
    private static readonly TcpSocketTuple SocketB2 = SocketA with { LocalPort = 40103 };

    private static readonly WindowCandidate A = new(1001, 101);
    private static readonly WindowCandidate B = new(2002, 202);
    private static readonly WindowCandidate B2 = new(3003, 303);

    private static readonly TcpSocketOwner[] Sockets =
    [
        new(SocketA, 101), new(SocketB, 202), new(SocketB2, 303)
    ];
    private static readonly WindowCandidate[] Windows = [A, B, B2];

    private static string Observe(
        ConcurrentRosterWindowEvidenceStudy study,
        int stage, int game, TcpSocketTuple tuple,
        IReadOnlyList<TcpSocketOwner>? owners = null,
        IReadOnlyList<WindowCandidate>? windows = null) =>
        study.ObserveLinked(stage, game, tuple,
            owners ?? Sockets, windows ?? Windows);

    private static ConcurrentRosterWindowEvidenceStudy Complete()
    {
        ConcurrentRosterWindowEvidenceStudy study = new();
        Observe(study, 0, 2, SocketA).Should().Contain("status=os-matched");
        Observe(study, 1, 4, SocketB).Should().Contain("status=os-matched");
        study.ObserveClosed(4);
        Observe(study, 2, 6, SocketB2).Should().Contain("status=os-matched");
        return study;
    }

    [Fact]
    public void TwoWindowsWithBReconnectAndAOwnerStillPresentPassLocalJoin()
    {
        string summary = Complete().Summarize().Last();
        summary.Should().Contain("status=window-roster-link-candidate");
        summary.Should().Contain("A-window-stable-across-B=yes");
        summary.Should().Contain("A-B-distinct-windows=yes");
        summary.Should().Contain(
            "account=unverified server=unverified semantic=unverified");
    }

    [Fact]
    public void SummariesAndToStringRedactAllPrivateWindowAndEndpointDetails()
    {
        var study = Complete();
        string result = string.Join("\n", study.Summarize());
        result.Should().NotContain("192.0.2.10");
        result.Should().NotContain("198.51.100.40");
        result.Should().NotContain("40101");
        result.Should().NotContain("1001");
        result.Should().NotContain("2002");
        result.Should().NotContain("303");
        result.Should().NotContain("account=verified");
        study.ToString().Should().Contain("redacted");
        A.ToString().Should().Contain("redacted");
    }

    [Fact]
    public void MissingOsSocketOwnerFailsClosedWithoutGuessingWindow()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA, owners: [])
            .Should().Contain("status=os-unavailable");
        study.Summarize()[0].Should().Contain("status=unavailable");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void MissingOsEvidenceCanBeRetriedForSameGameSocket()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA, owners: [])
            .Should().Contain("os-unavailable");
        Observe(study, 0, 2, SocketA)
            .Should().Contain("os-matched");
    }

    [Fact]
    public void DuplicateOsOwnersOrMultipleWindowsOnSamePidAreRejected()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA,
            owners: [new(SocketA, 101), new(SocketA, 202)])
            .Should().Contain("os-unavailable");
        Observe(study, 0, 2, SocketA,
            windows: [A, new WindowCandidate(1002, 101), B])
            .Should().Contain("os-unavailable");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void AccountBWindowCannotBeSameAsAWindowEvenOnDistinctSocket()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA).Should().Contain("os-matched");
        TcpSocketOwner[] sameOwner =
        [
            new(SocketA, 101), new(SocketB, 101)
        ];
        Observe(study, 1, 4, SocketB, owners: sameOwner,
            windows: [A]).Should().Contain("status=conflicted");
        study.Summarize()[1].Should().Contain("reason=A-B-same-window");
    }

    [Fact]
    public void LosingAOwnerWhileBLinksCannotEstablishConcurrentEvidence()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA);
        Observe(study, 1, 4, SocketB,
            owners: [new(SocketB, 202)])
            .Should().Contain("status=conflicted");
        study.Summarize()[1].Should().Contain("reason=A-owner-not-current");
    }

    [Fact]
    public void ChangedWindowOwnershipForSameGameSessionIsQuarantined()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA).Should().Contain("os-matched");
        TcpSocketOwner[] changed = [new(SocketA, 202)];
        Observe(study, 0, 2, SocketA, owners: changed)
            .Should().Contain("status=conflicted");
        study.Summarize()[0].Should().Contain("reason=owner-changed");
    }

    [Fact]
    public void ReusedSessionOrdinalOrTupleCannotSwitchToAnotherClient()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA);
        Observe(study, 0, 2, SocketB)
            .Should().Contain("status=conflicted");
        study.Summarize()[0].Should().Contain("session-or-tuple-change");
    }

    [Fact]
    public void ClosingABeforeBReconnectMakesReportIncomplete()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA);
        Observe(study, 1, 4, SocketB);
        study.ObserveClosed(4);
        study.ObserveClosed(2);
        Observe(study, 2, 6, SocketB2).Should().Contain("A-unavailable");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void AWindowMayCloseAfterBothOverlapsWereProved()
    {
        var study = Complete();
        study.ObserveClosed(2);
        study.ObserveClosed(6);
        study.Summarize().Last()
            .Should().Contain("status=window-roster-link-candidate");
    }

    [Fact]
    public void B1MustHaveClosedBeforeBReconnectProvesDistinctGame()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA);
        Observe(study, 1, 4, SocketB);
        Observe(study, 2, 6, SocketB2)
            .Should().Contain("status=B-reconnect-not-distinct");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }

    [Fact]
    public void StaleLateB1EvidenceAfterCloseCannotModifyItsWindow()
    {
        var study = Complete();
        Observe(study, 1, 4, SocketB)
            .Should().Contain("status=conflicted");
        study.Summarize().Last()
            .Should().Contain("status=window-roster-link-candidate");
    }

    [Fact]
    public void SameSessionCanBeReevaluatedOnlyWithSameOwnerWindow()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA).Should().Contain("os-matched");
        Observe(study, 0, 2, SocketA).Should().Contain("os-matched");
        study.Summarize()[0].Should().Contain("status=os-matched");
    }

    [Fact]
    public void MissingAAtFirstBObservationCannotBeCircumventedByOtherWindow()
    {
        var study = new ConcurrentRosterWindowEvidenceStudy();
        Observe(study, 0, 2, SocketA, owners: []).Should().Contain("os-unavailable");
        Observe(study, 1, 4, SocketB).Should().Contain("status=A-unavailable");
        study.Summarize().Last().Should().Contain("status=incomplete");
    }
}
