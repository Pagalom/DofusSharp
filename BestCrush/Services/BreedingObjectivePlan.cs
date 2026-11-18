using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public enum BreedingObjectiveAction { Capture, Buy, Prepare, Cross, Train, Sell }

public sealed record BreedingObjectiveStep(BreedingObjectiveAction Action, string Name, Guid? OwnedId = null,
    BreedingMountSex? Sex = null, int? Level = null, string? Zone = null, string? First = null, string? Second = null,
    long? SalePrice = null, bool Ready = false);

internal static class BreedingObjectivePlan
{
    internal static IReadOnlyList<BreedingObjectiveStep> Build(BreedingMarketObjective objective,
        IReadOnlyList<BreedingProduction> routes, IReadOnlyDictionary<int, BreedingRide> rides,
        IReadOnlyDictionary<long, string> names, IReadOnlyList<BreedingPairParent> owned,
        IReadOnlyDictionary<Guid, long?> preparationCosts, IReadOnlyDictionary<int, long> experienceCosts,
        IReadOnlyDictionary<Guid, BreedingResaleOption> sellable, long? fullPreparation,
        IReadOnlyDictionary<string, string> captureZones)
    {
        var availableSale = owned.Where(parent => parent.Mount.SpeciesId == objective.RideId && sellable.ContainsKey(parent.Mount.Id))
            .OrderByDescending(parent => sellable[parent.Mount.Id].Net).FirstOrDefault();
        if (availableSale is not null)
        {
            var sale = sellable[availableSale.Mount.Id];
            var sell = new BreedingObjectiveStep(BreedingObjectiveAction.Sell, objective.Name, availableSale.Mount.Id,
                Level: sale.Level, SalePrice: sale.SalePrice);
            return sale.Level > availableSale.Mount.Level
                ? [new(BreedingObjectiveAction.Train, objective.Name, availableSale.Mount.Id, Level: sale.Level), sell]
                : [sell];
        }

        var best = routes.Select(route =>
        {
            var used = new HashSet<Guid>();
            var steps = new List<BreedingObjectiveStep>();
            decimal savings = Prepare(route, null, 1, false, used, steps);
            if (objective.Sale.Level > 1)
                steps.Add(new(BreedingObjectiveAction.Train, objective.Name, Level: objective.Sale.Level));
            steps.Add(new(BreedingObjectiveAction.Sell, objective.Name, Level: objective.Sale.Level,
                SalePrice: objective.Sale.SalePrice));
            return (Steps: steps, EstimatedCost: route.Cost - savings, Used: used.Count);
        }).OrderBy(result => result.EstimatedCost).ThenByDescending(result => result.Used).First();
        return best.Steps;

        string Name(int id) => names.GetValueOrDefault(rides[id].ItemId, $"Monture #{id}");

        (BreedingPairParent? Parent, decimal Saving) Available(BreedingProduction route, BreedingMountSex? sex, int level,
            HashSet<Guid> used) =>
            owned.Where(parent => parent.Mount.SpeciesId == route.RideId && parent.Mount.Sex == sex &&
                    parent.Mount.Fertility is BreedingMountFertility.Fertile or BreedingMountFertility.Ready &&
                    !used.Contains(parent.Mount.Id) && preparationCosts.GetValueOrDefault(parent.Mount.Id).HasValue)
                .Select(parent => (Parent: parent, Saving: Saving(parent, route, level)))
                .Where(candidate => candidate.Saving >= 0).OrderByDescending(candidate => candidate.Saving)
                .ThenByDescending(candidate => candidate.Parent.Mount.Level).FirstOrDefault();

        decimal Saving(BreedingPairParent parent, BreedingProduction route, int level)
        {
            int xp = BreedingResaleAdvisor.Need(parent.Mount, level);
            int fullXp = BreedingMountExperience.RequiredAtLevel(level);
            long remainingXp = experienceCosts.GetValueOrDefault(xp, experienceCosts.GetValueOrDefault(fullXp));
            return route.CostForParent + (fullPreparation ?? 0) + experienceCosts.GetValueOrDefault(fullXp)
                - preparationCosts[parent.Mount.Id]!.Value - remainingXp
                - (sellable.GetValueOrDefault(parent.Mount.Id)?.Net ?? 0);
        }

        decimal Prepare(BreedingProduction route, BreedingMountSex? sex, int level, bool forCross,
            HashSet<Guid> used, List<BreedingObjectiveStep> steps)
        {
            string name = Name(route.RideId);
            if (forCross && Available(route, sex, level, used) is { Parent: { } parent, Saving: var oneTimeSaving })
            {
                used.Add(parent.Mount.Id);
                steps.Add(new(BreedingObjectiveAction.Prepare, name, parent.Mount.Id, sex,
                    Math.Max(level, parent.Mount.Level),
                    Ready: parent.Mount.Fertility == BreedingMountFertility.Ready && parent.Mount.Level >= level));
                return oneTimeSaving;
            }

            decimal savings = 0;
            switch (route.Kind)
            {
                case BreedingSupplyKind.Capture:
                    steps.Add(new(BreedingObjectiveAction.Capture, name, Sex: sex, Zone: captureZones.GetValueOrDefault(name)));
                    break;
                case BreedingSupplyKind.Purchase:
                    steps.Add(new(BreedingObjectiveAction.Buy, name, Sex: sex, Level: 1));
                    break;
                case BreedingSupplyKind.Cross:
                    var first = route.First!;
                    var second = route.Second!;
                    const BreedingMountSex female = BreedingMountSex.Female, male = BreedingMountSex.Male;
                    var firstFemale = Available(first, female, route.FirstLevel, used);
                    var secondMale = Available(second, male, route.SecondLevel, used);
                    var firstMale = Available(first, male, route.FirstLevel, used);
                    var secondFemale = Available(second, female, route.SecondLevel, used);
                    var direct = (firstFemale.Saving + secondMale.Saving,
                        Convert.ToInt32(firstFemale.Parent is not null) + Convert.ToInt32(secondMale.Parent is not null));
                    var reverse = (firstMale.Saving + secondFemale.Saving,
                        Convert.ToInt32(firstMale.Parent is not null) + Convert.ToInt32(secondFemale.Parent is not null));
                    bool swap = reverse.CompareTo(direct) > 0;
                    savings += Prepare(first, swap ? male : female, route.FirstLevel, true, used, steps);
                    savings += Prepare(second, swap ? female : male, route.SecondLevel, true, used, steps);
                    steps.Add(new(BreedingObjectiveAction.Cross, name, Sex: sex, First: Name(first.RideId), Second: Name(second.RideId)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(route));
            }
            if (forCross)
                steps.Add(new(BreedingObjectiveAction.Prepare, name, Sex: sex, Level: level));
            return savings;
        }
    }
}
