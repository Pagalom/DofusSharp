using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class CharacterIdentityCorrelatorTest
{
    private static readonly DateTime T0 = new(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);

    private static CharacterConnectionScope Scope(
        string flow = "tcp:A:5555-B:45000", long serverGeneration = 1,
        long captureEpoch = 1, long connectionEpoch = 1, string server = "Kourial") =>
        new(server, flow, serverGeneration, captureEpoch, connectionEpoch);

    private static CharacterIdentityEvidence Signal(
        CharacterConnectionScope scope, CharacterIdentityEvidenceSource source,
        ulong characterId = 123, string name = "Alice", int? classId = 4,
        int? level = 150, int offsetSeconds = 0) =>
        new(scope, source, characterId, name, classId, level, T0.AddSeconds(offsetSeconds));

    [Fact]
    public void OneIdentityMessageOrRepeatsFromSameFamilyNeverConfirm()
    {
        CharacterIdentityCorrelator tracker = new();
        CharacterConnectionScope lease = Scope();

        tracker.Observe(Signal(lease, CharacterIdentityEvidenceSource.CharacterSelection))
            .Status.Should().Be(CharacterIdentityStatus.Pending);
        tracker.Observe(Signal(lease, CharacterIdentityEvidenceSource.CharacterSelection, offsetSeconds: 1))
            .Status.Should().Be(CharacterIdentityStatus.Pending);
        tracker.SoleConfirmedSession().Should().BeNull();
    }

    [Fact]
    public void TwoIndependentMatchingFamiliesConfirmStableIdentity()
    {
        CharacterIdentityCorrelator tracker = new();
        CharacterConnectionScope lease = Scope();

        tracker.Observe(Signal(lease, CharacterIdentityEvidenceSource.CharacterSelection));
        CharacterIdentityAssessment state = tracker.Observe(
            Signal(lease, CharacterIdentityEvidenceSource.CharacterCharacteristics, offsetSeconds: 2));

        state.Status.Should().Be(CharacterIdentityStatus.Confirmed);
        state.Character!.CharacterId.Should().Be(123);
        state.Character.Name.Should().Be("Alice");
        tracker.SoleConfirmedSession()!.Character!.CharacterId.Should().Be(123);
    }

    [Fact]
    public void LevelIncreaseDoesNotChangeConfirmedIdentity()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope scope);
        CharacterIdentityAssessment state = tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterCharacteristics,
            level: 151, offsetSeconds: 4));

        state.Status.Should().Be(CharacterIdentityStatus.Confirmed);
        state.Character!.Level.Should().Be(151);
    }

    [Fact]
    public void DifferentCharacterOnSameConnectionImmediatelyRevokesOldIdentityAndJobs()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope scope);
        tracker.TryRecordProfession(scope, 123, 2, 200, T0.AddSeconds(3))
            .Should().BeTrue();

        CharacterIdentityAssessment pending = tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterSelection, 456,
            "Bob", 2, 42, 5));

        pending.Status.Should().Be(CharacterIdentityStatus.Pending);
        pending.Character.Should().BeNull();
        pending.ProfessionLevels.Should().BeEmpty();
        tracker.TryRecordProfession(scope, 123, 2, 200, T0.AddSeconds(6))
            .Should().BeFalse();

        CharacterIdentityAssessment confirmed = tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterDetails, 456,
            "Bob", 2, 42, 7));

        confirmed.Character!.CharacterId.Should().Be(456);
        confirmed.ProfessionLevels.Should().BeEmpty();
    }

    [Fact]
    public void ContradictoryCharacterNameBlocksAttributionUntilNewSession()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope scope);
        CharacterIdentityAssessment state = tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterDetails,
            name: "NotAlice", offsetSeconds: 4));

        state.Status.Should().Be(CharacterIdentityStatus.Ambiguous);
        state.Character.Should().BeNull();
        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterSelection,
            offsetSeconds: 5)).Status.Should().Be(CharacterIdentityStatus.Ambiguous);
        tracker.TryRecordProfession(scope, 123, 3, 100, T0.AddSeconds(6))
            .Should().BeFalse();
    }

    [Fact]
    public void ContradictoryClassBlocksAttributionEvenWithSameIdAndName()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope scope);
        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterDetails,
            classId: 12, offsetSeconds: 4))
            .Status.Should().Be(CharacterIdentityStatus.Ambiguous);
    }

    [Fact]
    public void DisconnectInvalidatesIdentityAndDelayedMessagesCannotRestoreIt()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope scope);
        tracker.Disconnect(scope, T0.AddSeconds(5))
            .Status.Should().Be(CharacterIdentityStatus.Disconnected);

        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterSelection,
            offsetSeconds: 6)).Status.Should().Be(CharacterIdentityStatus.Disconnected);
        tracker.SoleConfirmedSession().Should().BeNull();
    }

    [Fact]
    public void NewConnectionEpochCanConfirmAFreshSessionAfterDisconnect()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope first);
        tracker.Disconnect(first, T0.AddSeconds(5));

        CharacterConnectionScope second = first with { ConnectionEpoch = 2 };
        tracker.Observe(Signal(
            second, CharacterIdentityEvidenceSource.CharacterSelection,
            456, "Bob", 2, 42, 6)).Status.Should().Be(CharacterIdentityStatus.Pending);
        tracker.Observe(Signal(
            second, CharacterIdentityEvidenceSource.CharacterDetails,
            456, "Bob", 2, 42, 7)).Status.Should().Be(CharacterIdentityStatus.Confirmed);

        tracker.Read(first).Status.Should().Be(CharacterIdentityStatus.Unknown);
        tracker.SoleConfirmedSession()!.Character!.CharacterId.Should().Be(456);
    }

    [Fact]
    public void OldServerLeaseCannotChangeTheNewSession()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope old);
        CharacterConnectionScope next = old with { ServerGeneration = 2, ServerName = "Other" };

        tracker.Observe(Signal(
            next, CharacterIdentityEvidenceSource.CharacterSelection,
            987, "Carol", 3, 99, 9));
        tracker.Observe(Signal(
            old, CharacterIdentityEvidenceSource.CharacterDetails,
            123, "Alice", 4, 150, 10))
            .Status.Should().Be(CharacterIdentityStatus.Stale);
        tracker.Read(next).Status.Should().Be(CharacterIdentityStatus.Pending);
        tracker.Read(old).Status.Should().Be(CharacterIdentityStatus.Unknown);
    }

    [Fact]
    public void OldCaptureEpochCannotOverwriteNewConnection()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope old);
        CharacterConnectionScope next = old with { CaptureEpoch = 2 };
        tracker.Observe(Signal(
            next, CharacterIdentityEvidenceSource.CharacterSelection,
            345, "New", 4, 78, 5));

        tracker.Observe(Signal(
            old, CharacterIdentityEvidenceSource.CharacterDetails,
            offsetSeconds: 10))
            .Status.Should().Be(CharacterIdentityStatus.Stale);
        tracker.Read(next).Status.Should().Be(CharacterIdentityStatus.Pending);
    }

    [Fact]
    public void EarlierPacketCannotOverrideLaterIdentity()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope scope);
        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterSelection,
            456, "Bob", 2, 42, 10));
        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterCharacteristics,
            123, "Alice", 4, 150, 3));
        tracker.Read(scope).Status.Should().Be(CharacterIdentityStatus.Pending);
    }

    [Fact]
    public void ProfessionUpdateRequiresConfirmedIdentityAndMatchingScopedFlow()
    {
        CharacterIdentityCorrelator tracker = new();
        CharacterConnectionScope scope = Scope();
        tracker.Observe(Signal(scope, CharacterIdentityEvidenceSource.CharacterSelection));
        tracker.TryRecordProfession(scope, 123, 5, 180, T0.AddSeconds(1))
            .Should().BeFalse();

        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterDetails,
            offsetSeconds: 2));
        tracker.TryRecordProfession(scope, 999, 5, 180, T0.AddSeconds(3))
            .Should().BeFalse();
        tracker.TryRecordProfession(scope with { ConnectionEpoch = 2 },
            123, 5, 180, T0.AddSeconds(3)).Should().BeFalse();
        tracker.TryRecordProfession(scope, 123, 5, 180, T0.AddSeconds(3))
            .Should().BeTrue();
        tracker.Read(scope).ProfessionLevels[5].Should().Be(180);
    }

    [Fact]
    public void IndependentConcurrentConfirmedFlowsMustNeverChooseOneAutomatically()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope first);
        CharacterConnectionScope second = Scope(flow: "tcp:B:5555-C:45000");

        tracker.Observe(Signal(
            second, CharacterIdentityEvidenceSource.CharacterSelection,
            456, "Bob", 8, 200));
        tracker.Observe(Signal(
            second, CharacterIdentityEvidenceSource.CharacterDetails,
            456, "Bob", 8, 200, 2));

        tracker.Read(first).Character!.Name.Should().Be("Alice");
        tracker.Read(second).Character!.Name.Should().Be("Bob");
        tracker.SoleConfirmedSession().Should().BeNull();
    }

    [Fact]
    public void IncompleteOrInvalidIdentityCannotBeConfirmed()
    {
        CharacterIdentityCorrelator tracker = new();
        CharacterConnectionScope scope = Scope();

        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterSelection, 0))
            .Status.Should().Be(CharacterIdentityStatus.Unknown);
        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterSelection, name: ""))
            .Status.Should().Be(CharacterIdentityStatus.Unknown);
        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterDetails, classId: 0))
            .Status.Should().Be(CharacterIdentityStatus.Unknown);
    }

    [Fact]
    public void SessionCanBeRevokedBeforeIdentityWasObserved()
    {
        CharacterIdentityCorrelator tracker = new();
        CharacterConnectionScope scope = Scope();

        tracker.Disconnect(scope, T0).Status
            .Should().Be(CharacterIdentityStatus.Disconnected);
        tracker.Observe(Signal(scope, CharacterIdentityEvidenceSource.CharacterSelection))
            .Status.Should().Be(CharacterIdentityStatus.Disconnected);
    }

    [Fact]
    public void SameServerGenerationCannotBeReinterpretedAsDifferentServer()
    {
        CharacterIdentityCorrelator tracker = Confirmed(out CharacterConnectionScope original);
        CharacterConnectionScope falseReassignment = original with
        {
            ServerName = "Other",
            CaptureEpoch = original.CaptureEpoch + 1
        };

        tracker.Observe(Signal(
            falseReassignment,
            CharacterIdentityEvidenceSource.CharacterSelection,
            456, "Bob", 4, 100, 9))
            .Status.Should().Be(CharacterIdentityStatus.Stale);
        tracker.Read(original).Status.Should().Be(CharacterIdentityStatus.Confirmed);
    }

    [Fact]
    public void MissingClassOrLevelNeedsFurtherCharacterEvidence()
    {
        CharacterIdentityCorrelator tracker = new();
        CharacterConnectionScope scope = Scope();

        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterSelection,
            classId: null, level: null));
        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterDetails,
            classId: null, level: null, offsetSeconds: 1))
            .Status.Should().Be(CharacterIdentityStatus.Pending);

        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterCharacteristics,
            classId: 4, level: 150, offsetSeconds: 2))
            .Status.Should().Be(CharacterIdentityStatus.Confirmed);
    }

    private static CharacterIdentityCorrelator Confirmed(out CharacterConnectionScope scope)
    {
        CharacterIdentityCorrelator tracker = new();
        scope = Scope();
        tracker.Observe(Signal(scope, CharacterIdentityEvidenceSource.CharacterSelection));
        tracker.Observe(Signal(
            scope, CharacterIdentityEvidenceSource.CharacterDetails,
            offsetSeconds: 2));
        return tracker;
    }
}
