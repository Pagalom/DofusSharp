using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Services;

public sealed record PetFoodEvidence(int Meals,double Minimum,double Maximum,bool Consistent,bool? Matches,
    string Build,DateTime ObservedAtUtc);

internal static class PetFoodEvidenceReader
{
    internal static PetFoodEvidence Summarize(IEnumerable<PetFeedingObservation> input,double? candidate)
    {
        var meals=input.ToArray();
        double min=meals.Max(m=>(double)(m.ExperienceAfter-m.ExperienceBefore)/m.Quantity);
        double max=meals.Min(m=>(double)(m.ExperienceAfter-m.ExperienceBefore+1)/m.Quantity);
        bool consistent=min<max;
        bool? matches=candidate is >0 ? consistent && meals.All(m=>
            PetExperience.MealExperience(candidate.Value,m.Quantity)==m.ExperienceAfter-m.ExperienceBefore) : null;
        return new(meals.Length,min,max,consistent,matches,meals[0].ClientBuild,meals.Max(m=>m.ObservedAtUtc));
    }
    internal static async Task<IReadOnlyDictionary<long,PetFoodEvidence>> ReadAsync(BestCrushDbContext db,
        IReadOnlyList<PetFoodDefinition> foods,string? build,CancellationToken token)
    {
        var observations=await db.PetFeedingObservations.AsNoTracking().Where(m=>build==null || m.ClientBuild==build).ToArrayAsync(token);
        var definitions=foods.ToDictionary(f=>f.DofusDbId);
        return observations.GroupBy(m=>m.FoodItemId).Where(g=>definitions.ContainsKey(g.Key)).ToDictionary(g=>g.Key,g=>
        {
            string latestBuild=g.MaxBy(m=>m.ObservedAtUtc)!.ClientBuild;
            return Summarize(g.Where(m=>m.ClientBuild==latestBuild),definitions[g.Key].Experience);
        });
    }
}
