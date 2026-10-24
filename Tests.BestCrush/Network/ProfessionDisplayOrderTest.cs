using BestCrush.Services;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class ProfessionDisplayOrderTest
{
    [Fact]
    public void ProfessionsAreSortedByDescendingCumulativeXp()
    {
        AccountProfessionProgress[] source =
        [
            new("Bijoutier", 8, 708),
            new("Alchimiste", 195, 379581),
            new("Bûcheron", 69, 47043),
            new("Chasseur", 1, 0)
        ];

        ProfessionDisplayOrder.ByCumulativeExperience(source)
            .Select(job => job.Name)
            .Should().Equal("Alchimiste", "Bûcheron", "Bijoutier", "Chasseur");

        source.Select(job => job.Name)
            .Should().Equal("Bijoutier", "Alchimiste", "Bûcheron", "Chasseur");
    }

    [Fact]
    public void EqualTotalXpUsesNameAsDeterministicSecondarySort()
    {
        AccountProfessionProgress[] source =
        [
            new("Sculpteur", 1, 0),
            new("Bijoutier", 1, 0),
            new("Alchimiste", 1, 0),
            new("Mineur", 2, 30),
            new("Pêcheur", 2, 30)
        ];

        ProfessionDisplayOrder.ByCumulativeExperience(source)
            .Select(job => job.Name)
            .Should().Equal("Mineur", "Pêcheur", "Alchimiste", "Bijoutier", "Sculpteur");
    }

    [Fact]
    public void SortingUsesRawCumulativeXpNotCurrentLevelProgressPercent()
    {
        AccountProfessionProgress[] source =
        [
            new("Alchimiste", 195, 379581, 378300, 382200),
            new("Bijoutier", 8, 708, 560, 720)
        ];

        ProfessionDisplayOrder.ByCumulativeExperience(source)
            .Select(job => job.Name)
            .Should().Equal("Alchimiste", "Bijoutier");
    }
}
