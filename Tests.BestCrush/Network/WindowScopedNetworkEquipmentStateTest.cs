using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class WindowScopedNetworkEquipmentStateTest
{
    private static readonly DateTime T0 =
        new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DofusClientWindowKey First = new(101, 1201);
    private static readonly DofusClientWindowKey Second = new(102, 1202);
    private static readonly NetworkCaptureLease Draconiros =
        new("Draconiros", 5);

    [Fact]
    public void TwoWindowsCannotReplaceEachOthersLastEquipment()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0).Should().BeTrue();
        state.Remember(Second, Draconiros, 31, 43,
            T0.AddSeconds(1)).Should().BeTrue();

        state.Get(First, Draconiros, 31).Should().Be(
            new LastNetworkEquipmentSnapshot(42, "Draconiros", T0));
        state.Get(Second, Draconiros, 31).Should().Be(
            new LastNetworkEquipmentSnapshot(43, "Draconiros",
                T0.AddSeconds(1)));

        state.Remember(Second, Draconiros, 31, 44,
            T0.AddSeconds(2)).Should().BeTrue();
        state.Get(First, Draconiros, 31)!.DofusDbId.Should().Be(42);
    }

    [Fact]
    public void ClosedWindowLosesStateWhileOtherWindowRemains()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        state.Remember(Second, Draconiros, 31, 43, T0);

        state.Reconcile(Draconiros, 31, [Second]);

        state.Get(First, Draconiros, 31).Should().BeNull();
        state.Get(Second, Draconiros, 31)!.DofusDbId.Should().Be(43);
    }

    [Fact]
    public void NewServerLeaseOrNewCaptureDropsPriorEquipment()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);

        NetworkCaptureLease kourial = new("Kourial", 6);
        state.Get(First, kourial, 31).Should().BeNull();
        state.Remember(First, kourial, 31, 44, T0).Should().BeTrue();
        state.Get(First, Draconiros, 31).Should().BeNull();
        state.Get(First, kourial, 31)!.DofusDbId.Should().Be(44);

        state.Remember(Second, kourial, 32, 45, T0).Should().BeTrue();
        state.Get(First, kourial, 32).Should().BeNull();
        state.Get(Second, kourial, 32)!.DofusDbId.Should().Be(45);
    }

    [Fact]
    public void DelayedOldEpochAndLeaseCannotResurrectStaleEquipment()
    {
        WindowScopedNetworkEquipmentState state = new();
        NetworkCaptureLease kourial = new("Kourial", 6);
        state.Remember(First, Draconiros, 31, 42, T0);
        state.Remember(First, kourial, 31, 43, T0.AddSeconds(1));

        state.Remember(First, Draconiros, 31, 99,
            T0.AddSeconds(2)).Should().BeFalse();
        state.Get(First, kourial, 31)!.DofusDbId.Should().Be(43);

        state.Remember(First, kourial, 32, 44, T0.AddSeconds(3));
        state.Remember(First, kourial, 31, 99,
            T0.AddSeconds(4)).Should().BeFalse();
        state.Get(First, kourial, 32)!.DofusDbId.Should().Be(44);
    }

    [Fact]
    public void InvalidWindowAndEquipmentAreRejectedWithoutClearing()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        state.Remember(new DofusClientWindowKey(0, 1201),
            Draconiros, 31, 43, T0).Should().BeFalse();
        state.Remember(Second, Draconiros, 31, -1,
            T0).Should().BeFalse();
        state.Get(First, Draconiros, 31)!.DofusDbId.Should().Be(42);
        state.Get(Second, Draconiros, 31).Should().BeNull();
    }

    [Fact]
    public void IndependentWindowPollingRemovesClosedClientWithoutTraffic()
    {
        WindowScopedNetworkEquipmentState state = new();
        CurrentDofusClientsState clients = new();
        DofusWindowInfo first = new(new IntPtr(101), "Dofus", "Dofus",
            0, 0, 800, 600) { ProcessId = 1201 };
        DofusWindowInfo second = new(new IntPtr(102), "Dofus", "Dofus",
            0, 0, 800, 600) { ProcessId = 1202 };

        clients.Update([first, second]);
        state.Remember(First, Draconiros, 31, 42, T0);
        state.Remember(Second, Draconiros, 31, 43, T0);

        clients.Update([second]);
        state.RemoveMissingWindows(
            clients.Clients.Select(client => client.Key).ToArray());

        state.Get(First, Draconiros, 31).Should().BeNull();
        state.Get(Second, Draconiros, 31)!.DofusDbId.Should().Be(43);
    }

    [Fact]
    public void OldWindowHandleWithNewPidDoesNotInheritEquipment()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);

        var recycled = new DofusClientWindowKey(First.Handle, 9999);
        state.RemoveMissingWindows([recycled]);

        state.Get(First, Draconiros, 31).Should().BeNull();
        state.Get(recycled, Draconiros, 31).Should().BeNull();
    }

    [Fact]
    public void NoLiveWindowsRemoveAllEntriesWithoutChangingCaptureGeneration()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        state.Remember(Second, Draconiros, 31, 43, T0);

        state.RemoveMissingWindows([]);
        state.Get(First, Draconiros, 31).Should().BeNull();
        state.Get(Second, Draconiros, 31).Should().BeNull();

        state.Remember(First, Draconiros, 31, 44,
            T0.AddSeconds(1)).Should().BeTrue();
        state.Get(First, Draconiros, 31)!.DofusDbId.Should().Be(44);
    }

    [Fact]
    public void ClearRemovesAllClientStateWithoutPersistence()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        state.Remember(Second, Draconiros, 31, 43, T0);
        state.Clear();

        state.Get(First, Draconiros, 31).Should().BeNull();
        state.Get(Second, Draconiros, 31).Should().BeNull();
        new WindowScopedNetworkEquipmentState()
            .Get(First, Draconiros, 31).Should().BeNull();
    }

    [Fact]
    public void WheelTokenForOneWindowNeverReadsOtherWindowsLastEquipment()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        state.Remember(Second, Draconiros, 31, 43, T0.AddSeconds(1));

        WindowEquipmentFocusToken a =
            state.GetFocusToken(First, Draconiros)!;
        WindowEquipmentFocusToken b =
            state.GetFocusToken(Second, Draconiros)!;

        a.Window.Should().Be(First);
        a.Equipment.DofusDbId.Should().Be(42);
        b.Window.Should().Be(Second);
        b.Equipment.DofusDbId.Should().Be(43);
        state.IsCurrent(a).Should().BeTrue();
        state.IsCurrent(b).Should().BeTrue();
    }

    [Fact]
    public void MissingWindowHasNoWheelTokenEvenIfGlobalOrAnotherWindowHasEquipment()
    {
        WindowScopedNetworkEquipmentState state = new();
        LastNetworkEquipmentState legacy = new();
        legacy.Set(42, Draconiros.ServerName, T0);
        state.Remember(First, Draconiros, 31, 42, T0);

        state.GetFocusToken(Second, Draconiros).Should().BeNull();
        state.IsCurrent(null).Should().BeFalse();
        legacy.GetForServer(Draconiros.ServerName)
            .Should().NotBeNull();
    }

    [Fact]
    public void WheelTokenRejectsAnotherManualServerOrServerGeneration()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);

        state.GetFocusToken(First,
            new NetworkCaptureLease("Kourial", 5)).Should().BeNull();
        state.GetFocusToken(First,
            new NetworkCaptureLease("Draconiros", 6)).Should().BeNull();
        state.GetFocusToken(First, Draconiros).Should().NotBeNull();
    }

    [Fact]
    public void UpdatingOneWindowInvalidatesItsPendingWheelButNotOtherClient()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        state.Remember(Second, Draconiros, 31, 43, T0);
        WindowEquipmentFocusToken oldA =
            state.GetFocusToken(First, Draconiros)!;
        WindowEquipmentFocusToken oldB =
            state.GetFocusToken(Second, Draconiros)!;

        state.Remember(First, Draconiros, 31, 44, T0.AddSeconds(1));

        state.IsCurrent(oldA).Should().BeFalse();
        state.IsCurrent(oldB).Should().BeTrue();
        state.GetFocusToken(Second, Draconiros)!.Equipment.DofusDbId
            .Should().Be(43);
    }

    [Fact]
    public void WindowClosureInvalidatesPendingAsyncFocusToken()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        WindowEquipmentFocusToken token =
            state.GetFocusToken(First, Draconiros)!;

        state.RemoveMissingWindows([Second]);

        state.IsCurrent(token).Should().BeFalse();
        state.GetFocusToken(First, Draconiros).Should().BeNull();
    }

    [Fact]
    public void CaptureRestartInvalidatesTokenEvenForSameEquipmentAndTimestamp()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        WindowEquipmentFocusToken old =
            state.GetFocusToken(First, Draconiros)!;

        state.Remember(First, Draconiros, 32, 42, T0);

        state.IsCurrent(old).Should().BeFalse();
        state.GetFocusToken(First, Draconiros)!.CaptureEpoch.Should().Be(32);
    }

    [Fact]
    public void ClearForShutdownInvalidatesWheelTokenAndNewInstanceStartsEmpty()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        WindowEquipmentFocusToken token =
            state.GetFocusToken(First, Draconiros)!;
        state.Clear();

        state.IsCurrent(token).Should().BeFalse();
        new WindowScopedNetworkEquipmentState()
            .GetFocusToken(First, Draconiros).Should().BeNull();
    }

    [Fact]
    public void NewIdenticalObservationInvalidatesPendingFocusWithinSameCapture()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        WindowEquipmentFocusToken old =
            state.GetFocusToken(First, Draconiros)!;

        state.Remember(First, Draconiros, 31, 42, T0);

        state.IsCurrent(old).Should().BeFalse();
        state.GetFocusToken(First, Draconiros)!.ObservationVersion
            .Should().BeGreaterThan(old.ObservationVersion);
    }

    [Fact]
    public void ClosedThenRecreatedSameWindowCannotReviveOldFocusToken()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);
        WindowEquipmentFocusToken before =
            state.GetFocusToken(First, Draconiros)!;

        state.RemoveMissingWindows([]);
        state.Remember(First, Draconiros, 31, 42, T0);

        state.IsCurrent(before).Should().BeFalse();
        state.GetFocusToken(First, Draconiros)!.ObservationVersion
            .Should().BeGreaterThan(before.ObservationVersion);
    }

    [Fact]
    public void RecycledWindowKeyCannotReadPreviousClientsEquipment()
    {
        WindowScopedNetworkEquipmentState state = new();
        state.Remember(First, Draconiros, 31, 42, T0);

        DofusClientWindowKey reusedHandle = new(First.Handle, 9999);
        state.GetFocusToken(reusedHandle, Draconiros).Should().BeNull();
        state.GetFocusToken(new DofusClientWindowKey(0, 1201),
            Draconiros).Should().BeNull();
        state.GetFocusToken(new DofusClientWindowKey(101, 0),
            Draconiros).Should().BeNull();
    }

    [Fact]
    public void CapacityLimitCannotReplaceAnotherClientEquipment()
    {
        WindowScopedNetworkEquipmentState state = new();
        for (int i = 0; i < 16; i++)
            state.Remember(new DofusClientWindowKey(i + 1, i + 200),
                Draconiros, 31, i + 1, T0).Should().BeTrue();

        state.Remember(new DofusClientWindowKey(100, 999),
            Draconiros, 31, 99, T0).Should().BeFalse();
        state.Get(new DofusClientWindowKey(1, 200),
            Draconiros, 31)!.DofusDbId.Should().Be(1);
    }
}
