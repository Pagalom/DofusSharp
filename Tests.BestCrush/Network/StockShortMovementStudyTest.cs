using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class StockShortMovementStudyTest
{
    private static byte[] Delta(ulong quantity, ulong type, ulong uid) =>
        Bytes(2, Bytes(1, Message(
            Unsigned(3, quantity), Unsigned(4, type), Unsigned(5, uid))));

    [Fact]
    public void ShortMessagesOnlyCompareTheirCandidatesToTheLastDelta()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "iqx", Delta(2, 2418, 818181));

        string? adjustment = study.Observe(1, "irx", Bytes(3,
            Message(Unsigned(1, 2), Unsigned(3, 818181))));
        adjustment.Should().Be(
            "short=irx shape=f3.f1+f3.f3 " +
            "f3.f1-match=[f3=same,f4=different,f5=different] " +
            "f3.f3-match=[f3=different,f4=different,f5=same]");

        string? remove = study.Observe(1, "iqm", Unsigned(1, 818181));
        remove.Should().Be("short=iqm shape=f1 " +
            "f1-match=[f3=different,f4=different,f5=same]");

        string? update = study.Observe(1, "iqq", Message(
            Unsigned(1, 2418), Unsigned(2, 2)));
        update.Should().Contain(
            "f1=[f3=different,f4=same,f5=different]");
        update.Should().Contain(
            "f2=[f3=same,f4=different,f5=different]");

        foreach (string report in new[] { adjustment!, remove!, update! })
        foreach (string sensitive in new[] { "2418", "818181" })
            report.Should().NotContain(sensitive);
    }

    [Fact]
    public void ShortSignalsCannotCreateOrModifyInventory()
    {
        StockSnapshotDeltaStudy study = new();
        string? withoutBaseline = study.Observe(2, "irx", Bytes(3,
            Message(Unsigned(1, 1), Unsigned(3, 99))));
        withoutBaseline.Should().Contain("no-delta-baseline");

        study.Observe(2, "iqx", Delta(1, 2418, 99))
            .Should().Contain("bank=[baseline-unobserved]");
        study.Observe(2, "iqm", Unsigned(1, 99))
            .Should().Contain("f5=same");

        // A short opcode is not a stock snapshot and must not change
        // the recorded delta nor invent a bank/inventory baseline.
        study.Observe(2, "iqx", Delta(1, 2418, 99))
            .Should().Contain("previous-delta=[opcode=iqx");
    }

    [Fact]
    public void TwoSessionsCannotShareShortSignalComparisons()
    {
        StockSnapshotDeltaStudy study = new();
        study.Observe(1, "iqx", Delta(1, 2418, 99));
        study.Observe(2, "iqm", Unsigned(1, 99))
            .Should().Contain("no-delta-baseline");
        study.DropSession(1);
        study.Observe(1, "iqm", Unsigned(1, 99))
            .Should().Contain("no-delta-baseline");
    }

    [Fact]
    public void CorruptedAndUnknownShortSignalsFailClosed()
    {
        StockShortMovementStudy.Describe("xx", Unsigned(1, 88), null)
            .Should().BeNull();
        StockShortMovementStudy.Describe("iqm", [0x00], null)
            .Should().Be("short=unrecognized");
        StockShortMovementStudy.Describe("irx", Bytes(3, [0x00]), null)
            .Should().Be("short=irx shape=unrecognized");
        StockShortMovementStudy.Describe("iqm", Message(
            Unsigned(1, 99), Unsigned(1, 100)), null)
            .Should().Be("short=iqm shape=unrecognized");
        StockShortMovementStudy.Describe("iqq", Message(
            Unsigned(1, 99), Bytes(2, [0x01])), null)
            .Should().Contain("f1=[no-delta-baseline] f2=[missing]");
        StockShortMovementStudy.Describe("irx",
            new byte[257], null)
            .Should().Be("short=unrecognized");
    }

    [Fact]
    public void TrackerOnlyPublishesShortSignalInOptInServerToClientMode()
    {
        byte[] delta = Frame(Any("iqx", Delta(1, 2418, 91119)));
        byte[] remove = Frame(Any("iqm", Unsigned(1, 91119)));
        CharacterCaptureFlowKey key =
            new("127.0.0.1", 44444, "10.0.0.10", 5555);
        DateTime now = new(2026, 10, 25, 2, 20, 0, DateTimeKind.Utc);
        CharacterCaptureFlowTracker tracker = new(stockSnapshotStudy: true);

        tracker.Observe(key, CharacterCaptureDirection.ServerToClient,
            100, delta, now);
        CharacterCaptureEvent message = tracker.Observe(
                key, CharacterCaptureDirection.ServerToClient,
                (uint)(100 + delta.Length), remove, now.AddSeconds(1))
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        message.StockSnapshotSummary.Should().Contain(
            "short=iqm shape=f1 f1-match=");
        string serialized = System.Text.Json.JsonSerializer.Serialize(message);
        serialized.Should().NotContain("91119");
        serialized.Should().NotContain("2418");

        CharacterCaptureFlowTracker noDiagnostic = new();
        CharacterCaptureEvent ignored = noDiagnostic.Observe(
                key, CharacterCaptureDirection.ServerToClient,
                100, remove, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        ignored.StockSnapshotSummary.Should().BeNull();

        CharacterCaptureFlowTracker outbound = new(stockSnapshotStudy: true);
        CharacterCaptureEvent request = outbound.Observe(
                key, CharacterCaptureDirection.ClientToServer,
                100, remove, now)
            .Single(e => e.Kind == CharacterCaptureEventKind.Message);
        request.StockSnapshotSummary.Should().BeNull();
    }
}
