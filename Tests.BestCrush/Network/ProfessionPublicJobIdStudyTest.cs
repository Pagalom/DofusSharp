using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class ProfessionPublicJobIdStudyTest
{
    private static byte[] Craft(int level, ulong xp, ulong id) =>
        Message(Bytes(1, Message(
            Unsigned(1, (ulong)level),
            Unsigned(2, xp),
            Unsigned(5, id))));

    private static byte[] Roster(params ulong[] ids) =>
        Message(ids.Select(id => Bytes(1, Message(
            Unsigned(1, 1),
            Unsigned(5, id)))).ToArray());

    private static ProfessionJobReference[] References =>
    [
        new(1, "Cordonnier", 5, 230),
        new(2, "Sculpteur", 9, 758),
        new(3, "Cordonnier", 6, 302),
        new(4, "Sculpteur", 10, 903)
    ];

    private static ProfessionXpWireStudy Study(ulong cordonnier = 15, ulong sculpteur = 13)
    {
        ProfessionXpWireStudy study = new(checkPublicJobIds: true);
        study.Observe(1, Craft(5, 230, cordonnier));
        study.Observe(1, Craft(9, 758, sculpteur));
        study.Observe(1, Craft(6, 302, cordonnier));
        study.Observe(1, Craft(10, 903, sculpteur));
        return study;
    }

    [Fact]
    public void PublicReferenceHasTwentyPlusThreeAndCorrectCraftIds()
    {
        ProfessionPublicJobIdReference.ByName.Should().HaveCount(23);
        ProfessionPublicJobIdReference.ReferenceTwenty.Should().HaveCount(20);
        ProfessionPublicJobIdReference.ByName["Sculpteur"].Should().Be(13);
        ProfessionPublicJobIdReference.ByName["Cordonnier"].Should().Be(15);
        ProfessionPublicJobIdReference.ByName["Bricoleur"].Should().Be(65);
    }

    [Fact]
    public void VerifiedLocalLevelsAndXpCanMatchPublicIdsWithoutPrintingValues()
    {
        ProfessionXpWireStudy study = Study();
        study.CompareJobs(References).Should().Contain("candidate-paths=[f1.f5]");
        study.MatchPublicJobIds(References).Should().BeTrue();
        study.Count.Should().Be(4);

        study.DescribePublicRoster(false).Should().Contain("status=not-captured")
            .And.NotContain("230");
    }

    [Fact]
    public void MismatchedIdsAndLevelsNeverAuthorizeMapping()
    {
        ProfessionXpWireStudy study = Study(cordonnier: 65);
        study.MatchPublicJobIds(References).Should().BeFalse();
        ProfessionJobReference[] inaccurate = [.. References];
        inaccurate[0] = new(1, "Cordonnier", 6, 230);
        study.MatchPublicJobIds(inaccurate).Should().BeNull();
        ProfessionXpWireStudy regular = new();
        regular.Observe(1, Craft(5, 230, 15));
        regular.MatchPublicJobIds(References).Should().BeNull();
    }

    [Fact]
    public void FullTwentyRowsWithExactPublicIdsAreCompleteOnlyAfterIdCheck()
    {
        ProfessionXpWireStudy study = new(checkPublicJobIds: true);
        string? receipt = study.Observe(1,
            Roster([.. ProfessionPublicJobIdReference.ReferenceTwenty]));
        receipt.Should().Contain("p3g=roster-captured rows=20");
        study.Count.Should().Be(0);
        study.DescribePublicRoster(false).Should()
            .Contain("reference20=not-compared");
        string summary = study.DescribePublicRoster(true);
        summary.Should().Contain("status=reference20-complete")
            .And.Contain("reference20-present=20/20")
            .And.Contain("unique-f5=yes")
            .And.NotContain(" 13 ").And.NotContain(" 15 ");
    }

    [Fact]
    public void FullRosterReportsMissingAndExtraPublicNamesWithoutNetworkValues()
    {
        ProfessionXpWireStudy study = new(checkPublicJobIds: true);
        ulong[] ids = [.. ProfessionPublicJobIdReference.ReferenceTwenty];
        ids[0] = 79; // public extra Éleveur, replaces Bûcheron
        study.Observe(1, Roster(ids));
        string summary = study.DescribePublicRoster(true);
        summary.Should().Contain("status=reference20-differs")
            .And.Contain("reference20-present=19/20")
            .And.Contain("missing-public-names=[Bûcheron]")
            .And.Contain("extra-public-names=[Éleveur]")
            .And.NotContain(" 79 ");
    }

    [Fact]
    public void RosterRemainsScopedToFirstTcpSessionAndLimitsMemory()
    {
        ProfessionXpWireStudy study = new(checkPublicJobIds: true);
        study.Observe(1, Roster([.. ProfessionPublicJobIdReference.ReferenceTwenty]))
            .Should().Contain("roster-captured");
        study.Observe(2, Craft(5, 230, 15)).Should().BeNull();
        study.Count.Should().Be(0);
        study.Observe(1, Craft(5, 230, 15)).Should().Contain("index=1");
    }
}
