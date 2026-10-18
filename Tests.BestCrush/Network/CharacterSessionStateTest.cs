using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class CharacterSessionStateTest
{
    private static readonly DateTime BaseTime =
        new(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);

    private static CharacterConnectionScope Scope(
        string server = "Kourial", string connection = "flow-A",
        long serverGeneration = 1, long captureEpoch = 1,
        long connectionEpoch = 1) =>
        new(server, connection, serverGeneration, captureEpoch, connectionEpoch);

    private static CharacterIdentityAssessment Confirmed(
        CharacterConnectionScope scope, ulong id, string name, int level = 150,
        int classId = 4)
    {
        CharacterIdentityCorrelator correlator = new();
        correlator.Observe(new CharacterIdentityEvidence(
            scope, CharacterIdentityEvidenceSource.CharacterSelection,
            id, name, classId, level, BaseTime));
        return correlator.Observe(new CharacterIdentityEvidence(
            scope, CharacterIdentityEvidenceSource.CharacterDetails,
            id, name, classId, level, BaseTime.AddSeconds(1)));
    }

    [Fact]
    public void AccountProfessionWorkspaceStartsUnobserved()
    {
        CurrentAccountProfessionsState accountProfessions = new();
        accountProfessions.ActiveSnapshot.Should().BeNull();

        // P3d prepares UI and types, but never manufactures an account
        // or a profession profile from a character or a TCP session.
    }

    [Fact]
    public void StartupNeverRestoresCharacterOrRecreatesCachedProfile()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");

        session.ActiveProfile.Should().BeNull();
        session.Characters.Should().BeEmpty();
        session.CharacterName.Should().BeEmpty();

        // Another MAUI process would receive a fresh singleton instance.
        CurrentCharacterState restart = new();
        restart.SelectServer("Kourial");

        restart.ActiveProfile.Should().BeNull();
        restart.Characters.Should().BeEmpty();
    }

    [Fact]
    public void UnverifiedCharacterCannotCreateSessionProfile()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");

        CharacterConnectionScope scope = Scope();
        CharacterIdentityCorrelator correlator = new();
        CharacterIdentityAssessment pending = correlator.Observe(
            new CharacterIdentityEvidence(
                scope, CharacterIdentityEvidenceSource.CharacterSelection,
                123, "Alice", 4, 150, BaseTime));

        session.ObserveConfirmedCharacter(pending, true).Should().BeFalse();
        session.Characters.Should().BeEmpty();
    }

    [Fact]
    public void TwoVerifiedCharactersOnSameServerGetDistinctRuntimeCards()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");
        CharacterConnectionScope aScope = Scope(connection: "flow-A");
        CharacterConnectionScope bScope = Scope(connection: "flow-B");

        session.ObserveConfirmedCharacter(Confirmed(aScope, 123, "Alice"), true)
            .Should().BeTrue();
        session.ObserveConfirmedCharacter(Confirmed(bScope, 456, "Bob"), false)
            .Should().BeTrue();

        session.Characters.Should().HaveCount(2);
        session.ActiveProfile!.Name.Should().Be("Alice");
        session.SelectCharacter(new CharacterKey("Kourial", 456))
            .Should().BeTrue();
        session.ActiveProfile!.Name.Should().Be("Bob");

        // Verified focus takes precedence over a previous manual choice.
        session.ObserveConfirmedCharacter(Confirmed(aScope, 123, "Alice"), true);
        session.ActiveProfile!.Name.Should().Be("Alice");
    }

    [Fact]
    public void NamesAloneCannotCreateOrSelectAnUnknownCharacter()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");

        session.SelectCharacter(new CharacterKey("Kourial", 777))
            .Should().BeFalse();
        session.Characters.Should().BeEmpty();
    }

    [Fact]
    public void SwitchingServersClearsTargetButKeepsOnlyEphemeralCards()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");
        session.ObserveConfirmedCharacter(Confirmed(Scope(), 123, "Alice"), true);

        session.SelectServer("Other");
        session.ActiveCharacterKey.Should().BeNull();
        session.Characters.Should().BeEmpty();
        session.ObserveConfirmedCharacter(Confirmed(
            Scope(server: "Other", serverGeneration: 2),
            456, "Bob"), true);
        session.Characters.Should().HaveCount(1);
        session.ActiveProfile!.Name.Should().Be("Bob");

        session.SelectServer("Kourial");
        session.ActiveCharacterKey.Should().BeNull(
            "returning to a server does not reactivate the previous character");
        session.Characters.Should().ContainSingle(p => p.Name == "Alice");

        new CurrentCharacterState().Characters.Should().BeEmpty();
    }

    [Fact]
    public void CharacterProfilesRetainTheirOwnInventoriesKamasAndBank()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");
        CharacterConnectionScope a = Scope(connection: "flow-A");
        CharacterConnectionScope b = Scope(connection: "flow-B");
        CharacterIdentityAssessment alice = Confirmed(a, 123, "Alice");
        CharacterIdentityAssessment bob = Confirmed(b, 456, "Bob", 200, 8);

        session.ObserveConfirmedCharacter(alice, true);
        session.ObserveConfirmedCharacter(bob, false);

        session.ApplyObservedData(alice, new CharacterDataObservation(
            a, 123,
            PocketKamas: 25000,
            BankKamas: 100000,
            Inventory: [new CharacterInventoryItem(111, 99, 3)],
            BankInventory: [new CharacterInventoryItem(222, null, 10)]))
            .Should().BeTrue();

        session.ApplyObservedData(bob, new CharacterDataObservation(
            b, 456,
            PocketKamas: 500,
            BankKamas: 2000,
            Inventory: [],
            BankInventory: []))
            .Should().BeTrue();

        CharacterSessionProfile first = session.Characters.Single(p => p.Name == "Alice");
        CharacterSessionProfile second = session.Characters.Single(p => p.Name == "Bob");

        first.PocketKamas.Should().Be(25000);
        first.BankKamas.Should().Be(100000);
        first.Inventory.Should().ContainSingle(x => x.ItemId == 111 && x.Quantity == 3);
        first.BankInventory.Should().ContainSingle(x => x.ItemId == 222);

        second.PocketKamas.Should().Be(500);
        second.BankKamas.Should().Be(2000);
        second.Inventory.Should().BeEmpty();
        second.BankInventory.Should().BeEmpty();
    }

    [Fact]
    public void UnknownAndObservedEmptyInventoriesRemainDistinct()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");
        CharacterConnectionScope scope = Scope();
        CharacterIdentityAssessment alice = Confirmed(scope, 123, "Alice");
        session.ObserveConfirmedCharacter(alice, true);

        session.ActiveProfile!.Inventory.Should().BeNull();
        session.ActiveProfile!.BankInventory.Should().BeNull();
        session.ActiveProfile!.PocketKamas.Should().BeNull();

        session.ApplyObservedData(alice, new CharacterDataObservation(
            scope, 123, Inventory: []));
        session.ActiveProfile!.Inventory.Should().BeEmpty();
        session.ActiveProfile!.BankInventory.Should().BeNull();
        session.ActiveProfile!.PocketKamas.Should().BeNull();
    }

    [Fact]
    public void CrossCharacterOrCrossConnectionObservationsAreRefused()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");
        CharacterConnectionScope a = Scope(connection: "flow-A");
        CharacterConnectionScope b = Scope(connection: "flow-B");
        CharacterIdentityAssessment alice = Confirmed(a, 123, "Alice");
        session.ObserveConfirmedCharacter(alice, true);

        session.ApplyObservedData(alice, new CharacterDataObservation(
            a, 456, PocketKamas: 9999)).Should().BeFalse();
        session.ApplyObservedData(alice, new CharacterDataObservation(
            b, 123, PocketKamas: 9999)).Should().BeFalse();

        session.ActiveProfile!.PocketKamas.Should().BeNull();
    }

    [Fact]
    public void NegativeCashOrInventoryAmountsCannotBeRecorded()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");
        CharacterConnectionScope scope = Scope();
        CharacterIdentityAssessment alice = Confirmed(scope, 123, "Alice");
        session.ObserveConfirmedCharacter(alice, true);

        session.ApplyObservedData(alice, new CharacterDataObservation(
            scope, 123, PocketKamas: -1)).Should().BeFalse();
        session.ApplyObservedData(alice, new CharacterDataObservation(
            scope, 123,
            Inventory: [new CharacterInventoryItem(111, 1, -2)]))
            .Should().BeFalse();
        session.ActiveProfile!.PocketKamas.Should().BeNull();
        session.ActiveProfile!.Inventory.Should().BeNull();
    }

    [Fact]
    public void DisconnectRevokesActiveCharacterAndBlocksOldFlow()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");
        CharacterConnectionScope old = Scope();
        CharacterIdentityAssessment alice = Confirmed(old, 123, "Alice");
        session.ObserveConfirmedCharacter(alice, true);

        session.InvalidateConnection(old);
        session.ActiveCharacterKey.Should().BeNull();
        session.IsCharacterConnected(new CharacterKey("Kourial", 123)).Should().BeFalse();
        session.SelectCharacter(new CharacterKey("Kourial", 123)).Should().BeFalse();
        session.Characters.Should().ContainSingle(
            "discovered cards remain only until BestCrush closes");

        session.ApplyObservedData(alice, new CharacterDataObservation(
            old, 123, BankKamas: 100)).Should().BeFalse();
        session.ObserveConfirmedCharacter(alice, true).Should().BeFalse();

        CharacterConnectionScope reconnected = old with { ConnectionEpoch = 2 };
        session.ObserveConfirmedCharacter(
            Confirmed(reconnected, 123, "Alice"), true).Should().BeTrue();
    }

    [Fact]
    public void ReconnectionClearsOldFinancialAndInventorySnapshots()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");
        CharacterConnectionScope old = Scope();
        CharacterIdentityAssessment alice = Confirmed(old, 123, "Alice");
        session.ObserveConfirmedCharacter(alice, true);
        session.ApplyObservedData(alice, new CharacterDataObservation(
            old, 123, PocketKamas: 123, Inventory: []));

        CharacterConnectionScope reconnected = old with { ConnectionEpoch = 2 };
        session.ObserveConfirmedCharacter(
            Confirmed(reconnected, 123, "Alice"), true);

        session.ActiveProfile!.PocketKamas.Should().BeNull();
        session.ActiveProfile!.Inventory.Should().BeNull();
    }

    [Fact]
    public void SwitchingCharacterOnTheSameGameConnectionRevokesTheOldOwner()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");
        CharacterConnectionScope flow = Scope();
        CharacterIdentityAssessment alice = Confirmed(flow, 123, "Alice");
        CharacterIdentityAssessment bob = Confirmed(flow, 456, "Bob", 170, 2);

        session.ObserveConfirmedCharacter(alice, true);
        session.IsCharacterConnected(new CharacterKey("Kourial", 123)).Should().BeTrue();

        session.ObserveConfirmedCharacter(bob, true);
        session.Characters.Should().HaveCount(2);
        session.ActiveProfile!.Name.Should().Be("Bob");
        session.IsCharacterConnected(new CharacterKey("Kourial", 123)).Should().BeFalse();
        session.SelectCharacter(new CharacterKey("Kourial", 123)).Should().BeFalse();
        session.ApplyObservedData(alice, new CharacterDataObservation(
            flow, 123, BankKamas: 9999)).Should().BeFalse();
    }

    [Fact]
    public void NewTargetFromAnotherServerIsIgnoredUntilGlobalServerMatches()
    {
        CurrentCharacterState session = new();
        session.SelectServer("Kourial");

        session.ObserveConfirmedCharacter(Confirmed(
            Scope(server: "Other"), 456, "Bob"), true).Should().BeFalse();
        session.Characters.Should().BeEmpty();
        session.ActiveProfile.Should().BeNull();
    }
}
