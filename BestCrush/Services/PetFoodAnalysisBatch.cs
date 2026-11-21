using BestCrush.Domain.Models;

namespace BestCrush.Services;

// A short, session-only form queue. Quantities and XP totals are never persisted.
public sealed class PetFoodAnalysisBatch(IEnumerable<PetFoodDefinition> foods)
{
    private readonly List<PetFoodDefinition> _remaining=foods
        .Where(f=>f.MarketType==MarketObjectType.Resource && !string.IsNullOrWhiteSpace(f.Name) &&
            PetDataFreshness.ExperienceNeedsReview(f.ExperienceObservedAtUtc))
        .DistinctBy(f=>f.DofusDbId).DistinctBy(f=>f.Name,StringComparer.CurrentCultureIgnoreCase).Take(50).ToList();
    private int _index;
    public IReadOnlyList<PetFoodDefinition> Remaining=>_remaining;
    public PetFoodDefinition? Current=>_remaining.Count>0 ? _remaining[_index] : null;
    public int Position=>_remaining.Count>0 ? _index+1 : 0;
    public void Move(int delta)
    {if(_remaining.Count>0)_index=(_index+delta%_remaining.Count+_remaining.Count)%_remaining.Count;}
    public bool Complete(long itemId)
    {
        int removed=_remaining.FindIndex(f=>f.DofusDbId==itemId);
        if(removed<0)return false;
        _remaining.RemoveAt(removed);
        if(removed<_index)_index--;
        _index=Math.Min(_index,Math.Max(0,_remaining.Count-1));
        return true;
    }
}
