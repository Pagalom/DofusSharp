using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class CrushingProfessionFilterRulesTest
{
    [Theory]
    [InlineData(39, true, null, null, true)]
    [InlineData(39, true, 38, null, true)]
    [InlineData(39, true, 39, null, false)]
    [InlineData(39, true, 40, null, false)]
    [InlineData(39, true, null, 100, false)]
    [InlineData(39, true, 1, 100, false)]
    [InlineData(null, true, null, null, false)]
    [InlineData(39, false, null, null, false)]
    [InlineData(1, true, null, null, true)]
    [InlineData(1, true, 1, null, false)]
    [InlineData(39, true, 0, null, false)]
    [InlineData(0, true, null, null, false)]
    public void EligibilityRequiresLiveLevelsNoMaximumAndAStrictlyLowerMinimum(int? player, bool knownJobs, int? min, int? max, bool expected)
    {
        var jobs = knownJobs ? new Dictionary<string, int> { ["Bijoutier"] = 20 } : null;
        Assert.Equal(expected, CrushingProfessionFilterRules.CanEnable(player, jobs, min, max));
        Assert.Equal(!expected, CrushingProfessionFilterRules.UnavailableReason(player, jobs, min, max) is not null);
    }

    [Theory]
    [InlineData("Bijoutier", 20, 20, true)]
    [InlineData("Bijoutier", 21, 20, false)]
    [InlineData("Tailleur", 1, 200, false)]
    [InlineData(null, 1, 200, false)]
    [InlineData("Bijoutier", null, 200, false)]
    public void AccessUsesTheRecipeProfessionAndItsRequiredLevel(string? job, int? required, int actual, bool expected)
    {
        var levels = new Dictionary<string, int> { ["Bijoutier"] = actual };
        Assert.Equal(expected, CrushingProfessionFilterRules.CanCraft(new(100, job, required), levels));
        Assert.False(CrushingProfessionFilterRules.CanCraft(null, levels));
        Assert.False(CrushingProfessionFilterRules.CanCraft(new(100, job, required), null));
    }
}
