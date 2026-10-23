using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3c.8 isolated RAM-only staging. No real character/account identifiers,
/// OS handles, player names, authentication data, or network capture.
/// </summary>
public sealed class EphemeralCandidateRosterGroupsStateTest
{
    private static readonly DofusClientWindowKey A = new(101, 1101);
    private static readonly DofusClientWindowKey B = new(102, 1102);
    private static readonly DofusClientWindowKey C = new(103, 1103);
    private static readonly NetworkCaptureLease Lease = new("ManualServer", 1);

    private static bool Remember(EphemeralCandidateRosterGroupsState state,
        DofusClientWindowKey window, long connectionId, ulong selected,
        ulong[] members, NetworkCaptureLease? lease = null,
        long captureEpoch = 10) =>
        state.Observe(window, lease ?? Lease, captureEpoch,
            connectionId, selected, members);

    [Fact]
    public void TwoWindowsWithDisjointCandidateRostersRemainDistinct()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]).Should().BeTrue();
        Remember(state, B, 4, 201, [201]).Should().BeTrue();

        state.LiveWindowCount.Should().Be(2);
        state.Compare(A, B, Lease, 10).Should().Be(
            EphemeralCandidateRosterGroupsState.Comparison.DisjointRosterCandidates);
        state.IsCurrent(A, Lease, 10, 2).Should().BeTrue();
        state.IsCurrent(B, Lease, 10, 4).Should().BeTrue();
    }

    [Fact]
    public void TwoDifferentCharactersInIdenticalRosterYieldOnlyCandidateEquality()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]).Should().BeTrue();
        Remember(state, B, 4, 102, [102, 101]).Should().BeTrue();

        state.Compare(A, B, Lease, 10).Should().Be(
            EphemeralCandidateRosterGroupsState.Comparison.SameRosterCandidate);
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
        new CurrentCharacterState().Characters.Should().BeEmpty();
    }

    [Fact]
    public void NamesOrSelectionFocusAreNotRequiredForCandidateComparisons()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]).Should().BeTrue();
        Remember(state, B, 4, 201, [201, 202]).Should().BeTrue();
        state.Compare(B, A, Lease, 10).Should().Be(
            EphemeralCandidateRosterGroupsState.Comparison.DisjointRosterCandidates);
        state.Compare(A, C, Lease, 10).Should().Be(
            EphemeralCandidateRosterGroupsState.Comparison.Unavailable);
    }

    [Fact]
    public void NewConnectionMustProvideFreshRosterAndInvalidatesPreviousToken()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]).Should().BeTrue();
        Remember(state, A, 4, 101, [101, 102]).Should().BeTrue();
        state.IsCurrent(A, Lease, 10, 2).Should().BeFalse();
        state.IsCurrent(A, Lease, 10, 4).Should().BeTrue();
        Remember(state, A, 2, 101, [101, 102]).Should().BeFalse();
    }

    [Fact]
    public void LateDisconnectForPreviousTcpTokenDoesNotDisconnectNewConnection()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]);
        Remember(state, A, 4, 101, [101, 102]);
        state.Disconnect(A, Lease, 10, 2);
        state.IsCurrent(A, Lease, 10, 4).Should().BeTrue();
        state.Disconnect(A, Lease, 10, 4);
        state.IsCurrent(A, Lease, 10, 4).Should().BeFalse();
        Remember(state, A, 4, 101, [101, 102]).Should().BeFalse();
        Remember(state, A, 6, 101, [101, 102]).Should().BeTrue();
    }

    [Fact]
    public void ContradictoryCharacterOnSameConnectionQuarantinesWindow()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]).Should().BeTrue();
        Remember(state, A, 2, 102, [101, 102]).Should().BeFalse();

        state.LiveWindowCount.Should().Be(0);
        Remember(state, A, 3, 102, [101, 102]).Should().BeFalse();
        state.Compare(A, B, Lease, 10).Should().Be(
            EphemeralCandidateRosterGroupsState.Comparison.Unavailable);
    }

    [Fact]
    public void IdenticalRepeatedObservationIsIdempotent()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]).Should().BeTrue();
        Remember(state, A, 2, 101, [102, 101]).Should().BeTrue();
        state.LiveWindowCount.Should().Be(1);
    }

    [Fact]
    public void PartialRosterOverlapFailsClosedForBothWindows()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]).Should().BeTrue();
        Remember(state, B, 4, 102, [102, 201]).Should().BeFalse();

        state.LiveWindowCount.Should().Be(0);
        Remember(state, A, 3, 101, [101, 102]).Should().BeFalse();
        Remember(state, B, 5, 201, [201]).Should().BeFalse();
    }

    [Fact]
    public void ClosingBRemovesOnlyItsSnapshotAndQuarantinesReusedWindowKey()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]).Should().BeTrue();
        Remember(state, B, 4, 201, [201]).Should().BeTrue();
        state.RemoveMissingWindows([A]);

        state.LiveWindowCount.Should().Be(1);
        state.IsCurrent(A, Lease, 10, 2).Should().BeTrue();
        state.IsCurrent(B, Lease, 10, 4).Should().BeFalse();
        Remember(state, B, 6, 201, [201]).Should().BeFalse();
        // A fresh HWND/PID, when independently observed, may join.
        Remember(state, C, 6, 201, [201]).Should().BeTrue();
    }

    [Fact]
    public void CaptureRestartErasesCandidatesAndRejectsOldEpoch()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]).Should().BeTrue();

        state.Reset(11).Should().BeTrue();
        state.Reset(11).Should().BeFalse();
        state.LiveWindowCount.Should().Be(0);
        Remember(state, A, 4, 101, [101, 102], captureEpoch: 10)
            .Should().BeFalse();
        Remember(state, A, 4, 101, [101, 102], captureEpoch: 11)
            .Should().BeTrue();
        state.Compare(A, B, Lease, 10).Should().Be(
            EphemeralCandidateRosterGroupsState.Comparison.Unavailable);
    }

    [Fact]
    public void ManualServerSelectionGenerationSwitchClearsAllCandidates()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]);
        NetworkCaptureLease changed = new("OtherManuallyChosenServer", 2);

        Remember(state, B, 4, 201, [201], changed).Should().BeTrue();
        state.LiveWindowCount.Should().Be(1);
        state.IsCurrent(A, Lease, 10, 2).Should().BeFalse();
        Remember(state, A, 2, 101, [101, 102]).Should().BeFalse();
        state.Compare(A, B, changed, 10).Should().Be(
            EphemeralCandidateRosterGroupsState.Comparison.Unavailable);
    }

    [Fact]
    public void SameGenerationWithDifferentServerNameIsRejectedRatherThanValidated()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101, 102]).Should().BeTrue();
        Remember(state, B, 4, 201, [201],
            new NetworkCaptureLease("AnotherServer", 1)).Should().BeFalse();
        state.IsCurrent(A, Lease, 10, 2).Should().BeTrue();
    }

    [Fact]
    public void OldServerGenerationCannotBeRevivedByNewCaptureEpoch()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, [101], new NetworkCaptureLease("A", 2), 10)
            .Should().BeTrue();
        Remember(state, B, 4, 201, [201],
            new NetworkCaptureLease("A", 1), 11).Should().BeFalse();
        state.IsCurrent(A, new NetworkCaptureLease("A", 2), 10, 2)
            .Should().BeTrue();
    }

    [Fact]
    public void InvalidCandidateInputCanNeverCreateARosterGroup()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 101, []).Should().BeFalse();
        Remember(state, A, 2, 101, [101, 101]).Should().BeFalse();
        Remember(state, A, 2, 101, [201]).Should().BeFalse();
        Remember(state, A, 2, 101, [0, 101]).Should().BeFalse();
        Remember(state, A, 2, 101, Enumerable.Range(1, 17)
            .Select(n => (ulong)n).ToArray()).Should().BeFalse();
        Remember(state, A, 0, 101, [101]).Should().BeFalse();
        Remember(state, A, 2, 0, [101]).Should().BeFalse();
        Remember(state, new DofusClientWindowKey(0, 11),
            2, 101, [101]).Should().BeFalse();
        state.LiveWindowCount.Should().Be(0);
    }

    [Fact]
    public void CandidateWorkspaceRejectsSeventeenthSimultaneousWindow()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        for (int i = 0; i < 16; i++)
            Remember(state, new DofusClientWindowKey(200 + i, 1200 + i),
                i + 1, (ulong)(1000 + i), [(ulong)(1000 + i)])
                .Should().BeTrue();

        Remember(state, new DofusClientWindowKey(300, 1300),
            30, 2000, [2000]).Should().BeFalse();
        state.LiveWindowCount.Should().Be(16);
    }

    [Fact]
    public void DebugStringCannotExposeIdsWindowsNamesOrCredentials()
    {
        var state = new EphemeralCandidateRosterGroupsState();
        Remember(state, A, 2, 987654321, [987654321]);
        string label = state.ToString();

        label.Should().Contain("redacted");
        label.Should().Contain("account=unverified");
        label.Should().NotContain("987654321");
        label.Should().NotContain("ManualServer");
        label.Should().NotContain("1101");
        label.Should().NotContain("101");
        new CurrentAccountProfessionsState().ActiveSnapshot.Should().BeNull();
    }

    [Fact]
    public void FreshInstanceHasNoProfilesOrAccountAssociationsFromOldWorkspace()
    {
        var previous = new EphemeralCandidateRosterGroupsState();
        Remember(previous, A, 2, 101, [101]);
        var next = new EphemeralCandidateRosterGroupsState();

        next.LiveWindowCount.Should().Be(0);
        next.Compare(A, B, Lease, 10).Should().Be(
            EphemeralCandidateRosterGroupsState.Comparison.Unavailable);
        new CurrentCharacterState().Characters.Should().BeEmpty();
    }
}
