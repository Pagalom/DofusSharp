using System.Text;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

/// <summary>P3a synthetic-only frames: no captured private identifiers or XP.</summary>
public sealed class ProfessionFlowStudyTest
{
    private const ulong A = 9000123;
    private const ulong B = 9000789;

    private static byte[] Leaf(string name, int level, ulong professionClass) =>
        Message(Unsigned(3, (ulong)level),
            Bytes(5, Unsigned(3, professionClass)),
            Bytes(6, Encoding.UTF8.GetBytes(name)));

    private static byte[] Row(ulong id, string name, int level, ulong professionClass) =>
        Message(Unsigned(3, id), Bytes(4, Leaf(name, level, professionClass)));

    private static byte[] Ksc() =>
        Message(Bytes(2, Row(A, "Sable-Ciel", 137, 6)),
            Bytes(2, Row(B, "Roche-Lune", 2, 17)));

    private static byte[] Ksb(bool first = true, ulong? overrideClass = null) =>
        Bytes(1, Bytes(1, first
            ? Row(A, "Sable-Ciel", 137, overrideClass ?? 6)
            : Row(B, "Roche-Lune", 2, overrideClass ?? 17)));

    private static byte[] Ipz(bool duplicateF5 = false, bool omitF5 = false,
        int rows = 20) =>
        Message(Enumerable.Range(0, rows).Select(i =>
            Bytes(1, Message(
                Unsigned(1, (ulong)(i + 1)),
                Unsigned(2, (ulong)(i * 13)),
                omitF5 && i == 4 ? [] :
                    Unsigned(5, (ulong)(duplicateF5 && i == 4 ? 301 : 301 + i)))))
            .ToArray());

    [Fact]
    public void FullSelectedLoadedProfessionFlowIsConsistentButNeverVerified()
    {
        var study = new ProfessionFlowStudy();
        study.Observe("ksc", Ksc()).Should().Be(
            "p3a=ksc decoded=yes rows=2 selection-link=pending identity=unverified");
        study.Observe("ksb", Ksb()).Should().Be(
            "p3a=ksb decoded=yes selection-link=yes class-link=yes identity=unverified");
        study.Observe("ipz", Ipz()).Should().Be(
            "p3a=ipz decoded=yes rows=20 f5-unique=yes selection-link=yes class-link=yes owner=unverified mapping=unverified");
    }

    [Fact]
    public void IpzBeforeCharacterLoadingDoesNotIdentifyOwner()
    {
        var study = new ProfessionFlowStudy();
        string result = study.Observe("ipz", Ipz());
        result.Should().Contain("selection-link=pending class-link=unavailable");
        result.Should().Contain("owner=unverified mapping=unverified");
    }

    [Fact]
    public void KsbWithoutSelectionIsNotEnoughToAttributeProfessions()
    {
        var study = new ProfessionFlowStudy();
        study.Observe("ksb", Ksb());
        study.Observe("ipz", Ipz()).Should().Contain(
            "selection-link=unavailable class-link=unavailable owner=unverified");
    }

    [Fact]
    public void AlternatingAtoBtoAResetsLoadedCandidateEachTime()
    {
        var study = new ProfessionFlowStudy();
        study.Observe("ksc", Ksc());
        study.Observe("ksb", Ksb());
        study.Observe("ipz", Ipz()).Should().Contain(
            "selection-link=yes class-link=yes owner=unverified");

        study.Observe("ksc", Ksc());
        study.Observe("ipz", Ipz()).Should().Contain(
            "selection-link=pending class-link=unavailable owner=unverified");

        study.Observe("ksb", Ksb(first: false));
        study.Observe("ipz", Ipz()).Should().Contain(
            "selection-link=yes class-link=yes owner=unverified");

        study.Observe("ksc", Ksc());
        study.Observe("ksb", Ksb());
        study.Observe("ipz", Ipz()).Should().Contain(
            "selection-link=yes class-link=yes owner=unverified");
    }

    [Fact]
    public void MalformedNewKsbInvalidatesPreviousLoad()
    {
        var study = new ProfessionFlowStudy();
        study.Observe("ksc", Ksc());
        study.Observe("ksb", Ksb());
        study.Observe("ksb", [0x80]).Should().Contain(
            "decoded=no selection-link=unavailable");
        study.Observe("ipz", Ipz()).Should().Contain(
            "selection-link=pending class-link=unavailable owner=unverified");
    }

    [Fact]
    public void ContradictoryClassCannotReportClassLinkYes()
    {
        var study = new ProfessionFlowStudy();
        study.Observe("ksc", Ksc());
        study.Observe("ksb", Ksb(overrideClass: 17));
        study.Observe("ipz", Ipz()).Should().Contain(
            "selection-link=yes class-link=no owner=unverified");
    }

    [Fact]
    public void DifferentCharacterNotInSelectionNeverMatches()
    {
        var study = new ProfessionFlowStudy();
        study.Observe("ksc", Ksc());
        // Third fabricated character (not in list).
        byte[] third = Bytes(1, Bytes(1,
            Row(9000999, "Hiver-Rouge", 83, 6)));
        study.Observe("ksb", third);
        study.Observe("ipz", Ipz()).Should().Contain(
            "selection-link=no class-link=unavailable owner=unverified");
    }

    [Fact]
    public void InvalidIpzDoesNotInventProfessionMapping()
    {
        var study = new ProfessionFlowStudy();
        study.Observe("ksc", Ksc());
        study.Observe("ksb", Ksb());
        study.Observe("ipz", Ipz(rows: 19)).Should().Be(
            "p3a=ipz decoded=no selection-link=yes class-link=yes owner=unverified mapping=unverified");
    }

    [Fact]
    public void AbsentOrDuplicatedF5FailsGlobalCandidateUniqueness()
    {
        foreach (byte[] body in new[] { Ipz(duplicateF5: true), Ipz(omitF5: true) })
        {
            var study = new ProfessionFlowStudy();
            study.Observe("ksc", Ksc());
            study.Observe("ksb", Ksb());
            study.Observe("ipz", body).Should().Contain(
                "f5-unique=no selection-link=yes class-link=yes owner=unverified mapping=unverified");
        }
    }

    [Fact]
    public void NoOpcodeOrBodyDetailsEscapeDefaultDiagnostic()
    {
        var study = new ProfessionFlowStudy();
        string report = study.Observe("ksc", Ksc()) +
            study.Observe("ksb", Ksb()) +
            study.Observe("ipz", Ipz());
        report.Should().NotContain("Sable-Ciel");
        report.Should().NotContain("Roche-Lune");
        report.Should().NotContain("9000123");
        report.Should().NotContain("137");
        report.Should().NotContain("301");
    }

    [Fact]
    public void TrackerDoesNotCarrySelectionToAnotherTcpConnection()
    {
        var tracker = new CharacterCaptureFlowTracker(studyProfessionFlow: true);
        DateTime now = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        CharacterCaptureFlowKey first = new("127.0.0.1", 40001, "10.0.0.1", 5555);
        CharacterCaptureFlowKey second = new("127.0.0.1", 40002, "10.0.0.1", 5555);

        tracker.Observe(first, CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("ksc", Ksc())), now);
        tracker.Observe(first, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + Frame(Any("ksc", Ksc())).Length),
            Frame(Any("ksb", Ksb())), now.AddSeconds(1));

        var result = tracker.Observe(second,
            CharacterCaptureDirection.ServerToClient,
            100, Frame(Any("ipz", Ipz())), now.AddSeconds(2))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        result.ProfessionFlowSummary.Should().Contain(
            "selection-link=pending class-link=unavailable owner=unverified");
    }

    [Fact]
    public void TrackerProcessesKscKsbIpzOnOneTcpSession()
    {
        var tracker = new CharacterCaptureFlowTracker(studyProfessionFlow: true);
        DateTime now = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        CharacterCaptureFlowKey key = new("127.0.0.1", 40001, "10.0.0.1", 5555);
        byte[] list = Frame(Any("ksc", Ksc()));
        byte[] loaded = Frame(Any("ksb", Ksb()));
        byte[] professions = Frame(Any("ipz", Ipz()));

        var one = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            100, list, now).Single(e => e.Kind == CharacterCaptureEventKind.Message);
        var two = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + list.Length), loaded, now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        var three = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + list.Length + loaded.Length), professions, now.AddSeconds(2))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);

        one.ProfessionFlowSummary.Should().Contain("p3a=ksc decoded=yes");
        two.ProfessionFlowSummary.Should().Contain("p3a=ksb decoded=yes selection-link=yes");
        three.ProfessionFlowSummary.Should().Contain(
            "p3a=ipz decoded=yes rows=20 f5-unique=yes selection-link=yes class-link=yes owner=unverified");
        three.ProfessionFlowSummary.Should().NotContain("Sable-Ciel");
        three.ProfessionFlowSummary.Should().NotContain("9000123");
    }

    [Fact]
    public void FinAndNextSynCannotReusePriorProfessionFlow()
    {
        var tracker = new CharacterCaptureFlowTracker(studyProfessionFlow: true);
        DateTime now = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        CharacterCaptureFlowKey key = new("127.0.0.1", 40001, "10.0.0.1", 5555);
        byte[] list = Frame(Any("ksc", Ksc()));
        tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            100, list, now);
        tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            (uint)(100 + list.Length), Frame(Any("ksb", Ksb())), now.AddSeconds(1));
        tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            400, [], now.AddSeconds(2), fin: true);
        tracker.Observe(key, CharacterCaptureDirection.ClientToServer,
            800, [], now.AddSeconds(3), syn: true);
        var result = tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            1000, Frame(Any("ipz", Ipz())), now.AddSeconds(4))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        result.ProfessionFlowSummary.Should().Contain(
            "selection-link=pending class-link=unavailable owner=unverified");
    }

    [Fact]
    public void ClientToServerLookalikeDoesNotSupplyProfessionData()
    {
        var tracker = new CharacterCaptureFlowTracker(studyProfessionFlow: true);
        var key = new CharacterCaptureFlowKey(
            "127.0.0.1", 40001, "10.0.0.1", 5555);
        DateTime now = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        var message = tracker.Observe(
            key, CharacterCaptureDirection.ClientToServer,
            100, Frame(Any("ipz", Ipz())), now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        message.ProfessionFlowSummary.Should().BeNull();
    }

    [Fact]
    public void ResearchModeCannotBeMixedWithProductionOrIdentityDiagnostics()
    {
        Action bad = () => new CharacterCaptureFlowTracker(
            studyProfessionFlow: true,
            identityFieldsReference: new CharacterNameLevelReference("Sable-Ciel", 137));
        bad.Should().Throw<ArgumentException>();
    }
}
