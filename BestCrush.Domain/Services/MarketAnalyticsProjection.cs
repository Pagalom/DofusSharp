using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

/// <summary>Display-only transformations never read the database or recompute price histories.</summary>
public static class MarketAnalyticsProjection
{
    public static IReadOnlyList<MarketAnalyticsPlotSeries> Curves(MarketAnalyticsComparison comparison,
        IReadOnlyList<MarketAnalyticsSeriesDefinition> definitions)
    {
        var byId = definitions.ToDictionary(d => d.Id);
        List<MarketAnalyticsPlotSeries> curves = [];
        foreach (var group in comparison.Groups)
        {
            if (!byId.TryGetValue(group.DefinitionId,out var definition)) continue;
            if (definition.FuseMembers && definition.SelectionMode != MarketAnalyticsSelectionMode.Single)
                curves.Add(new($"{group.DefinitionId}:group",group.DefinitionId,group.Name,group.FusedSamples));
            else
                foreach (var member in group.Members)
                    curves.Add(new($"{group.DefinitionId}:{member.Member.ObjectType}:{member.Member.DofusDbId}",group.DefinitionId,
                        definition.SelectionMode == MarketAnalyticsSelectionMode.Single ? group.Name : member.Member.Name,member.Samples));
        }
        return curves;
    }
}
