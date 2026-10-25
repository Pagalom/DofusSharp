using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class CharacterScopedStockGroupsTest
{
    private static readonly DateTime BaseTime =
        new(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc);

    private static CharacterConnectionScope Scope(
        string server = "Kourial", string connection = "A",
        long connectionEpoch = 1) =>
        new(server, connection, 1, 1, connectionEpoch);

    private static CharacterIdentityAssessment Confirmed(
        CharacterConnectionScope scope, ulong id, string name)
    {
        CharacterIdentityCorrelator correlator = new();
        correlator.Observe(new CharacterIdentityEvidence(
            scope, CharacterIdentityEvidenceSource.CharacterSelection,
            id, name, 4, 100, BaseTime));
        return correlator.Observe(new CharacterIdentityEvidence(
            scope, CharacterIdentityEvidenceSource.CharacterDetails,
            id, name, 4, 100, BaseTime.AddSeconds(1)));
    }

    private static CharacterInventoryItem Item(
        ulong itemId, ulong? uid, long quantity, long value = 24) =>
        new(itemId, uid, quantity)
        {
            Effects = [new CharacterInventoryEffect(1001, value)]
        };

    [Fact]
    public void ConfirmedCharacterHasOnlyItsOwnGroupedInventory()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Kourial");
        var a = Scope(connection: "flow-A");
        var b = Scope(connection: "flow-B");
        var alice = Confirmed(a, 123, "SameName");
        var bob = Confirmed(b, 456, "SameName");
        state.ObserveConfirmedCharacter(alice, true).Should().BeTrue();
        state.ObserveConfirmedCharacter(bob, false).Should().BeTrue();

        state.ApplyObservedData(alice, new CharacterDataObservation(a, 123,
            Inventory: [Item(2427, 111, 2), Item(2427, 222, 3)],
            BankInventory: [Item(2418, 333, 999)]))
            .Should().BeTrue();
        state.ApplyObservedData(bob, new CharacterDataObservation(b, 456,
            Inventory: [Item(2427, 444, 7)],
            BankInventory: [Item(2418, 555, 123)]))
            .Should().BeTrue();

        CharacterScopedStockGroups first = state.GetScopedStockGroups(
            new CharacterKey("Kourial", 123))!;
        CharacterScopedStockGroups second = state.GetScopedStockGroups(
            new CharacterKey("Kourial", 456))!;

        first.Inventory.Should().ContainSingle().Which.Quantity.Should().Be(5);
        second.Inventory.Should().ContainSingle().Which.Quantity.Should().Be(7);
        first.Bank.Should().BeNull("bank ownership has not been proven");
        second.Bank.Should().BeNull();

        // Same display name and same type/jet are insufficient to merge owners.
        state.GetScopedStockGroups(new CharacterKey("Kourial", 999))
            .Should().BeNull();
    }

    [Fact]
    public void UnknownAndObservedEmptyInventoriesStayDistinct()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Kourial");
        var a = Scope();
        var alice = Confirmed(a, 123, "Alice");
        state.ObserveConfirmedCharacter(alice, true);

        state.GetScopedStockGroups(new CharacterKey("Kourial", 123))!
            .Inventory.Should().BeNull();

        state.ApplyObservedData(alice, new CharacterDataObservation(a, 123,
            Inventory: []));
        state.GetScopedStockGroups(new CharacterKey("Kourial", 123))!
            .Inventory.Should().BeEmpty();
    }

    [Fact]
    public void BankRequiresExplicitAccountServerProofAndMatchingServer()
    {
        CharacterInventoryItem[] bank = [Item(2427, 111, 3)];
        CharacterKey character = new("Kourial", 123);
        CharacterSessionProfile baseProfile =
            new(character, "Alice", 4, 100, BankInventory: bank);

        CharacterStockViewProjection.FromVerifiedProfile(baseProfile)
            .Bank.Should().BeNull();
        CharacterStockViewProjection.FromVerifiedProfile(
            baseProfile with
            {
                VerifiedAccountServerKey = new AccountServerKey("", "Kourial")
            }).Bank.Should().BeNull();
        CharacterStockViewProjection.FromVerifiedProfile(
            baseProfile with
            {
                VerifiedAccountServerKey = new AccountServerKey("account-X", "Tylezia")
            }).Bank.Should().BeNull();

        var verifiedProfile = baseProfile with
        {
            VerifiedAccountServerKey = new AccountServerKey("account-X", "Kourial")
        };
        CharacterStockViewProjection.FromVerifiedProfile(verifiedProfile)
            .Bank.Should().ContainSingle().Which.Quantity.Should().Be(3);

        // A different account must not be selected by a matching server/name:
        // this pure function uses only the *supplied verified profile*.
        var other = baseProfile with
        {
            VerifiedAccountServerKey = new AccountServerKey("account-Y", "Kourial"),
            BankInventory = [Item(2427, 222, 8)]
        };
        CharacterStockViewProjection.FromVerifiedProfile(other)
            .Bank.Should().ContainSingle().Which.Quantity.Should().Be(8);
        CharacterStockViewProjection.FromVerifiedProfile(verifiedProfile)
            .Bank.Should().ContainSingle().Which.Quantity.Should().Be(3);
    }

    [Fact]
    public void DisconnectSwitchServerAndReconnectionCannotReuseStaleStock()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Kourial");
        var flow = Scope();
        var alice = Confirmed(flow, 123, "Alice");
        var key = new CharacterKey("Kourial", 123);
        state.ObserveConfirmedCharacter(alice, true);
        state.ApplyObservedData(alice, new CharacterDataObservation(flow, 123,
            Inventory: [Item(2427, 111, 2)]));

        state.GetScopedStockGroups(key)!.Inventory.Should().ContainSingle();
        state.SelectServer("Tylezia");
        state.GetScopedStockGroups(key).Should().BeNull();
        state.SelectServer("Kourial");
        state.GetScopedStockGroups(key)!.Inventory.Should().ContainSingle();

        state.InvalidateConnection(flow);
        state.GetScopedStockGroups(key).Should().BeNull();
        state.ApplyObservedData(alice, new CharacterDataObservation(flow, 123,
            Inventory: [Item(2427, 111, 999)])).Should().BeFalse();

        var newFlow = flow with { ConnectionEpoch = 2 };
        state.ObserveConfirmedCharacter(Confirmed(newFlow, 123, "Alice"), true)
            .Should().BeTrue();
        state.GetScopedStockGroups(key)!.Inventory.Should().BeNull(
            "a reconnect cannot inherit earlier inventory observations");
    }

    [Fact]
    public void UnreadableEffectsPreventTheWholeInventoryFromBeingTrusted()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Kourial");
        var flow = Scope();
        var alice = Confirmed(flow, 123, "Alice");
        var key = new CharacterKey("Kourial", 123);
        state.ObserveConfirmedCharacter(alice, true);
        state.ApplyObservedData(alice, new CharacterDataObservation(flow, 123,
            Inventory:
            [
                Item(2427, 111, 1),
                new CharacterInventoryItem(2427, 222, 2)
            ])).Should().BeTrue();
        state.GetScopedStockGroups(key)!.Inventory.Should().BeNull(
            "an unknown real jet must not be grouped as an empty one");
    }
}
