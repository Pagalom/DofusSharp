using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class CurrentCharacterWindowLifecycleTest
{
    private static readonly DofusClientWindowKey A = new(101, 1201);
    private static readonly DofusClientWindowKey B = new(102, 1202);

    private static CharacterIdentityAssessment Confirmed(
        ulong id, string name, long connection = 1)
    {
        CharacterConnectionScope scope = new(
            "Draconiros", $"flow-{connection}", 1, 1, connection);
        return new CharacterIdentityAssessment(
            CharacterIdentityStatus.Confirmed, scope,
            new ConfirmedCharacterIdentity(id, name, 4, 150));
    }

    [Fact]
    public void ClosingOneVerifiedWindowRemovesOnlyItsCharacterCard()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Draconiros");
        state.ObserveConfirmedCharacter(Confirmed(1, "SameName", 1),
            true, A).Should().BeTrue();
        state.ObserveConfirmedCharacter(Confirmed(2, "SameName", 2),
            false, B).Should().BeTrue();
        state.Characters.Should().HaveCount(2);

        state.RemoveMissingWindows([B]);
        state.Characters.Should().ContainSingle()
            .Which.Key.CharacterId.Should().Be(2);
        state.ActiveCharacterKey.Should().BeNull();
        state.IsCharacterConnected(new CharacterKey("Draconiros", 1))
            .Should().BeFalse();
    }

    [Fact]
    public void TcpDisconnectKeepsCardButRealWindowClosureDeletesIt()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Draconiros");
        CharacterIdentityAssessment identity = Confirmed(1, "Alice");
        state.ObserveConfirmedCharacter(identity, true, A)
            .Should().BeTrue();

        state.InvalidateConnection(identity.Scope);
        state.Characters.Should().ContainSingle();
        state.ActiveCharacterKey.Should().BeNull();

        state.RemoveMissingWindows([]);
        state.Characters.Should().BeEmpty();
        state.SelectCharacter(new CharacterKey("Draconiros", 1))
            .Should().BeFalse();
    }

    [Fact]
    public void ReusedWindowHandleWithAnotherPidCannotInheritProfile()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Draconiros");
        state.ObserveConfirmedCharacter(Confirmed(1, "Alice"), true, A)
            .Should().BeTrue();

        state.RemoveMissingWindows([new DofusClientWindowKey(101, 9999)]);
        state.Characters.Should().BeEmpty();
    }

    [Fact]
    public void ContradictoryVerifiedWindowOnSameConnectionIsRejected()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Draconiros");
        CharacterIdentityAssessment identity = Confirmed(1, "Alice");
        state.ObserveConfirmedCharacter(identity, true, A)
            .Should().BeTrue();
        state.ObserveConfirmedCharacter(identity, false, B)
            .Should().BeFalse();

        state.RemoveMissingWindows([A]);
        state.Characters.Should().ContainSingle();

        state.RemoveMissingWindows([B]);
        state.Characters.Should().BeEmpty();
    }

    [Fact]
    public void LateIdentityCannotRecreateProfileAfterWindowClose()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Draconiros");
        CharacterIdentityAssessment original = Confirmed(1, "Alice", 1);
        state.ObserveConfirmedCharacter(original, true, A).Should().BeTrue();

        state.RemoveMissingWindows([]);
        state.Characters.Should().BeEmpty();
        state.ObserveConfirmedCharacter(original, true, A).Should().BeFalse();
        state.Characters.Should().BeEmpty();

        // A separately re-confirmed new TCP connection may create a new
        // session card only for a currently present, independently proven
        // window; this is not reuse of the revoked old connection.
        state.ObserveConfirmedCharacter(Confirmed(1, "Alice", 2),
            true, B).Should().BeTrue();
        state.Characters.Should().ContainSingle();
    }

    [Fact]
    public void UnverifiedWindowCannotRemoveProfileByNameOrServerAlone()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Draconiros");
        state.ObserveConfirmedCharacter(Confirmed(1, "SameName"),
            false).Should().BeTrue();

        state.RemoveMissingWindows([]);
        state.Characters.Should().ContainSingle();
        new CurrentCharacterState().Characters.Should().BeEmpty();
    }

    [Fact]
    public void InvalidWindowProofCannotCreateOrBindCharacterCard()
    {
        CurrentCharacterState state = new();
        state.SelectServer("Draconiros");
        state.ObserveConfirmedCharacter(Confirmed(1, "Alice"),
            true, new DofusClientWindowKey(0, 1201)).Should().BeFalse();
        state.ObserveConfirmedCharacter(Confirmed(2, "Bob"),
            true, new DofusClientWindowKey(101, 0)).Should().BeFalse();
        state.Characters.Should().BeEmpty();
    }
}
