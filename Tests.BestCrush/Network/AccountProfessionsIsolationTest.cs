using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

/// <summary>
/// P3c.2: artificial identifiers only. The live account/profession decoder
/// remains disabled until account and server evidence is independently proven.
/// </summary>
public sealed class AccountProfessionsIsolationTest
{
    private static CharacterSessionProfile Character(
        string server, ulong characterId,
        AccountServerKey? verifiedAccount = null,
        string name = "SameName") =>
        new(new CharacterKey(server, characterId), name, 4, 100,
            VerifiedAccountServerKey: verifiedAccount);

    private static AccountProfessionsSnapshot Snapshot(
        string account, string server) =>
        new(new AccountServerKey(account, server),
            [new AccountProfessionProgress("SyntheticProfession", 25, 1234)]);

    [Fact]
    public void LiveWorkspaceNeverContainsSyntheticAccountProfessions()
    {
        CurrentAccountProfessionsState live = new();
        live.ActiveSnapshot.Should().BeNull();
        live.ForCharacter(Character("Draconiros", 1,
            new AccountServerKey("account-A", "Draconiros")))
            .Should().BeNull();
    }

    [Fact]
    public void DifferentAccountsOnSameServerMustNotBorrowProfessions()
    {
        AccountProfessionsSnapshot first =
            Snapshot("account-A", "Draconiros");

        CharacterSessionProfile a = Character("Draconiros", 1,
            new AccountServerKey("account-A", "Draconiros"));
        CharacterSessionProfile b = Character("Draconiros", 2,
            new AccountServerKey("account-B", "Draconiros"));

        CurrentAccountProfessionsState.ForVerifiedSnapshot(a, first)
            .Should().BeSameAs(first);
        CurrentAccountProfessionsState.ForVerifiedSnapshot(b, first)
            .Should().BeNull();
    }

    [Fact]
    public void SameAccountSameServerMayShareVerifiedProfessionsAcrossCharacters()
    {
        AccountProfessionsSnapshot shared =
            Snapshot("account-A", "Draconiros");

        CharacterSessionProfile first = Character("Draconiros", 11,
            new AccountServerKey("account-A", "Draconiros"));
        CharacterSessionProfile second = Character("Draconiros", 12,
            new AccountServerKey("account-A", "Draconiros"));

        CurrentAccountProfessionsState.ForVerifiedSnapshot(first, shared)
            .Should().BeSameAs(shared);
        CurrentAccountProfessionsState.ForVerifiedSnapshot(second, shared)
            .Should().BeSameAs(shared);
    }

    [Fact]
    public void SameAccountDifferentServerCannotShareProfessions()
    {
        AccountProfessionsSnapshot firstServer =
            Snapshot("account-A", "Draconiros");
        CharacterSessionProfile otherServer = Character("Kourial", 11,
            new AccountServerKey("account-A", "Kourial"));

        CurrentAccountProfessionsState.ForVerifiedSnapshot(
            otherServer, firstServer).Should().BeNull();
    }

    [Fact]
    public void UnverifiedCharacterNeverSeesSnapshotEvenWithSameNameAndServer()
    {
        AccountProfessionsSnapshot snapshot =
            Snapshot("account-A", "Draconiros");
        CharacterSessionProfile unverified =
            Character("Draconiros", 11, name: "SameName");

        CurrentAccountProfessionsState.ForVerifiedSnapshot(
            unverified, snapshot).Should().BeNull();
    }

    [Fact]
    public void CharacterKeyServerMustAgreeWithVerifiedAccountServer()
    {
        AccountProfessionsSnapshot snapshot =
            Snapshot("account-A", "Draconiros");
        CharacterSessionProfile inconsistent = Character("Kourial", 11,
            new AccountServerKey("account-A", "Draconiros"));

        CurrentAccountProfessionsState.ForVerifiedSnapshot(
            inconsistent, snapshot).Should().BeNull();
    }

    [Theory]
    [InlineData("", "Draconiros")]
    [InlineData("account-A", "")]
    [InlineData(" ", "Draconiros")]
    [InlineData("account-A", " ")]
    public void EmptyOrWhitespaceIdentityCannotUnlockProfessions(
        string account, string server)
    {
        AccountProfessionsSnapshot snapshot =
            Snapshot("account-A", "Draconiros");
        CharacterSessionProfile invalid = Character("Draconiros", 11,
            new AccountServerKey(account, server));

        CurrentAccountProfessionsState.ForVerifiedSnapshot(
            invalid, snapshot).Should().BeNull();
    }

    [Fact]
    public void AccountIdentityIsOrdinalNotCaseInsensitive()
    {
        AccountProfessionsSnapshot snapshot =
            Snapshot("account-A", "Draconiros");
        CharacterSessionProfile differentAccount = Character(
            "Draconiros", 1,
            new AccountServerKey("Account-A", "Draconiros"));

        CurrentAccountProfessionsState.ForVerifiedSnapshot(
            differentAccount, snapshot).Should().BeNull();
    }

    [Fact]
    public void NullSnapshotAndNewProcessNeverRestoreProfessions()
    {
        CharacterSessionProfile character = Character("Draconiros", 1,
            new AccountServerKey("account-A", "Draconiros"));
        CurrentAccountProfessionsState.ForVerifiedSnapshot(
            character, null).Should().BeNull();

        CurrentAccountProfessionsState restarted = new();
        restarted.ActiveSnapshot.Should().BeNull();
        restarted.ForCharacter(character).Should().BeNull();
    }
}
