using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class CandidateCharacterSheetVisibilityTest
{
    private static readonly DofusClientWindowKey A = new(101, 1101);
    private static readonly DofusClientWindowKey B = new(102, 1102);

    [Fact]
    public void SelectionScreenTitleHidesPreviouslyLinkedCandidateCard()
    {
        CandidateWindowRosterCard[] candidates = [new(A, 2, 57)];
        DofusClientWindow[] windows =
        [
            new(A, "Client Dofus", null, null, true)
        ];

        CandidateCharacterSheetVisibility.Visible(candidates, windows)
            .Should().BeEmpty("selection screen is not a loaded character");
    }

    [Fact]
    public void TwoLoadedCharacterWindowsShowOwnCandidateWithoutNameMatching()
    {
        CandidateWindowRosterCard[] candidates =
        [
            new(A, 2, 57),
            new(B, 1, 2)
        ];
        DofusClientWindow[] windows =
        [
            new(A, "SameName", "Iop", "3.6", true),
            new(B, "SameName", "Eniripsa", "3.6", false)
        ];

        var sheets = CandidateCharacterSheetVisibility.Visible(candidates, windows);
        sheets.Should().HaveCount(2);
        sheets[0].Candidate.Window.Should().Be(A);
        sheets[0].Candidate.SelectedLevelCandidate.Should().Be(57);
        sheets[1].Candidate.Window.Should().Be(B);
        sheets[1].Candidate.SelectedLevelCandidate.Should().Be(2);
    }

    [Fact]
    public void MissingOrDuplicatedOsWindowCannotCreateAnExtraVisibleCard()
    {
        CandidateWindowRosterCard[] candidates = [new(A, 1, 2)];
        DofusClientWindow[] noWindows = [];
        CandidateCharacterSheetVisibility.Visible(candidates, noWindows)
            .Should().BeEmpty();

        var duplicates = new[]
        {
            new DofusClientWindow(A, "SameName", "Iop", null, true),
            new DofusClientWindow(A, "SameName", "Iop", null, false)
        };
        CandidateCharacterSheetVisibility.Visible(candidates, duplicates)
            .Should().BeEmpty("an ambiguous local HWND inventory is not proof");
    }
}
