using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class LastNetworkEquipmentStateTest
{
    private static readonly DateTime ObservedAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StartsEmptyAndClearRemovesTheSnapshot()
    {
        LastNetworkEquipmentState state = new();
        state.GetForServer("TEST").Should().BeNull();
        state.Set(42, "TEST", ObservedAt);
        state.GetForServer("TEST").Should().Be(new LastNetworkEquipmentSnapshot(42, "TEST", ObservedAt));
        state.Clear();
        state.GetForServer("TEST").Should().BeNull();
    }

    [Theory]
    [InlineData(0, "TEST")]
    [InlineData(-1, "TEST")]
    [InlineData(43, "")]
    [InlineData(43, " ")]
    public void InvalidObservationDoesNotEraseTheLastValidEquipment(long id, string server)
    {
        LastNetworkEquipmentState state = new();
        state.Set(42, "TEST", ObservedAt);
        state.Set(id, server, ObservedAt.AddSeconds(1));
        state.GetForServer("TEST").Should().Be(new LastNetworkEquipmentSnapshot(42, "TEST", ObservedAt));
    }

    [Fact]
    public void ServerLookupIsOrdinalAndDoesNotTrimTheStoredName()
    {
        LastNetworkEquipmentState state = new();
        state.Set(42, "TEST", ObservedAt);
        state.GetForServer(null).Should().BeNull();
        state.GetForServer(" ").Should().BeNull();
        state.GetForServer("test").Should().BeNull();
        state.GetForServer(" TEST ").Should().BeNull();
        state.Set(43, " TEST ", ObservedAt);
        state.GetForServer(" TEST ")!.DofusDbId.Should().Be(43);
        state.GetForServer("TEST").Should().BeNull();
    }

    [Fact]
    public void KeepsOnlyOneSnapshotAndLookupForAnotherServerDoesNotClearIt()
    {
        LastNetworkEquipmentState state = new();
        state.Set(42, "FIRST", ObservedAt);
        state.GetForServer("SECOND").Should().BeNull();
        state.GetForServer("FIRST").Should().NotBeNull();
        state.Set(43, "SECOND", ObservedAt);
        state.GetForServer("FIRST").Should().BeNull();
        state.GetForServer("SECOND")!.DofusDbId.Should().Be(43);
    }

    [Fact]
    public void LastSetWinsEvenWithAnOlderTimestampAndDoesNotMutatePriorSnapshot()
    {
        LastNetworkEquipmentState state = new();
        state.Set(42, "TEST", ObservedAt);
        var previous = state.GetForServer("TEST");
        state.Set(43, "TEST", ObservedAt.AddHours(-1));
        state.GetForServer("TEST").Should().Be(new LastNetworkEquipmentSnapshot(43, "TEST", ObservedAt.AddHours(-1)));
        previous.Should().Be(new LastNetworkEquipmentSnapshot(42, "TEST", ObservedAt));
    }
}
