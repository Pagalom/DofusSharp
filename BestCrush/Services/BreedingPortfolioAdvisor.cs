using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed record BreedingCaptureOption(string Name, string Zone, BreedingResaleOption? Resale, int Owned,
    BreedingCaptureCross? Cross)
{
    internal long CaptureExpense { get; init; }
    internal bool PreferCross => Cross is { } cross && cross.Net > (Resale?.Net ?? 0);
    internal decimal Net => (PreferCross ? Cross!.Net : Resale?.Net ?? 0) - CaptureExpense;
}

public sealed record BreedingCaptureCross(BreedingPairParent Partner, IReadOnlyList<string> Offspring,
    int Generation, decimal Net);

internal sealed record DirectCaptureCross(BreedingPairParent Partner, BreedingRide[] Offspring);

public sealed record BreedingPairParent(PaddockMountReading Mount, string Name, long? Family,
    BreedingResaleOption? Resale, BreedingLevelPrice? SterilePrice);

public sealed record BreedingPairOption(BreedingPairParent First, BreedingPairParent Second,
    IReadOnlyList<string> Offspring, int? DirectGeneration, decimal? FertilityCost, long? BestKnownOffspring,
    int? MissingTrainingPoints)
{
    internal long? PreparationCost { get; init; }
    internal bool Ready => BreedingDecisions.CanPair(First.Mount, Second.Mount);
    internal decimal? PotentialGain => BestKnownOffspring - FertilityCost - (PreparationCost ?? 0);
    internal bool Contains(Guid mountId) => First.Mount.Id == mountId || Second.Mount.Id == mountId;
    internal bool Matches(Guid first, Guid second) =>
        First.Mount.Id == first && Second.Mount.Id == second || First.Mount.Id == second && Second.Mount.Id == first;
}

internal static class BreedingPortfolioAdvisor
{
    internal static IEnumerable<DirectCaptureCross> CaptureCrosses(BreedingRide capture,
        IEnumerable<BreedingPairParent> parents)
    {
        foreach (var parent in parents)
        {
            if (parent.Family != capture.Family || !parent.Mount.Sex.HasValue ||
                parent.Mount.Fertility is not (BreedingMountFertility.Ready or BreedingMountFertility.Fertile) ||
                parent.Mount.SpeciesId is not { } id ||
                !BreedingLineage.Rides.TryGetValue(id, out var ride))
                continue;
            var offspring = BreedingLineage.DirectCrosses(capture.Id, id)
                .Where(child => child.Generation > Math.Max(capture.Generation, ride.Generation)).ToArray();
            if (offspring.Length > 0)
                yield return new(parent, offspring);
        }
    }

    internal static BreedingCaptureCross? EstimateCaptureCross(DirectCaptureCross cross, string capturedName,
        BreedingLevelPrice? sterileCapture, IReadOnlyDictionary<Guid, long?> preparation,
        IReadOnlyDictionary<long, string> marketNames, IReadOnlyDictionary<string, BreedingResaleOption> resale)
    {
        var parent = cross.Partner;
        if (sterileCapture is null || parent.SterilePrice is not { } sterilePartner || parent.Resale is not { } partnerValue ||
            preparation.GetValueOrDefault(Guid.Empty) is not { } captureCost ||
            preparation.GetValueOrDefault(parent.Mount.Id) is not { } parentCost ||
            !resale.TryGetValue(parent.Name, out var parentColour) || !resale.TryGetValue(capturedName, out var capturedColour))
            return null;

        int generation = cross.Offspring.Max(ride => ride.Generation);
        var outcomes = cross.Offspring.Select(ride => (
            ride.Generation, Name: marketNames.GetValueOrDefault(ride.ItemId))).ToArray();
        if (outcomes.Any(outcome => outcome.Name is null || !resale.ContainsKey(outcome.Name)))
            return null;
        var targets = outcomes.Where(outcome => outcome.Generation == generation).Select(outcome => outcome.Name!).ToArray();
        long targetValue = targets.Min(name => resale[name].Net);
        long otherValue = Math.Min(parentColour.Net, capturedColour.Net);
        foreach (var outcome in outcomes.Where(outcome => outcome.Generation < generation))
            otherValue = Math.Min(otherValue, resale[outcome.Name!].Net);

        // The individual odds within a generation come from the parents' genealogy.
        // Use the least valuable priced result within each group for this projection.
        decimal chance = BreedingDecisions.TargetGenerationChance(1, parent.Mount.Level).Base / 100m;
        decimal childValue = targetValue * chance + otherValue * (1m - chance);
        decimal net = childValue + sterileCapture.SuggestedPrice + sterilePartner.SuggestedPrice
            - partnerValue.Net - captureCost - parentCost;
        return new(parent, targets, generation, net);
    }

    internal static IReadOnlyList<BreedingPairOption> PairOptions(IReadOnlyList<BreedingPairParent> parents,
        IReadOnlyDictionary<string, BreedingResaleOption> prices, IReadOnlyDictionary<long, string> marketNames)
    {
        var eligible = parents.Where(parent => parent.Family.HasValue && parent.Mount.SpeciesId.HasValue &&
            parent.Mount.Sex.HasValue && parent.Mount.Fertility is BreedingMountFertility.Fertile or BreedingMountFertility.Ready);
        var pairs = new List<BreedingPairOption>();
        foreach (var family in eligible.GroupBy(parent => parent.Family))
        {
            var males = family.Where(parent => parent.Mount.Sex == BreedingMountSex.Male).ToArray();
            foreach (var first in family.Where(parent => parent.Mount.Sex == BreedingMountSex.Female))
            foreach (var second in males)
            {
                var crosses = BreedingLineage.DirectCrosses(first.Mount.SpeciesId!.Value, second.Mount.SpeciesId!.Value).ToArray();
                var outcomes = crosses.Select(ride => marketNames.GetValueOrDefault(ride.ItemId)).OfType<string>()
                    .Concat([first.Name, second.Name]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                long? child = outcomes.Max(name => prices.GetValueOrDefault(name)?.Net);
                decimal? fertilityCost = first.Resale is { } a && second.Resale is { } b &&
                    first.SterilePrice is { } sterileA && second.SterilePrice is { } sterileB
                    ? (decimal)a.Net + b.Net - sterileA.SuggestedPrice - sterileB.SuggestedPrice : null;
                pairs.Add(new(first, second, outcomes, crosses.Max(ride => (int?)ride.Generation), fertilityCost, child,
                    BreedingDecisions.MissingBreedingPoints(first.Mount) + BreedingDecisions.MissingBreedingPoints(second.Mount)));
            }
        }
        return RankPairs(pairs);
    }

    internal static IReadOnlyList<BreedingPairOption> RankPairs(IEnumerable<BreedingPairOption> pairs) =>
        pairs.OrderByDescending(pair => (pair.Ready || pair.PreparationCost.HasValue) && pair.PotentialGain is > 0)
            .ThenByDescending(pair => pair.PotentialGain)
            .ThenByDescending(pair => pair.Ready)
            .ThenBy(pair => pair.MissingTrainingPoints ?? int.MaxValue)
            .ThenByDescending(pair => pair.DirectGeneration)
            .ToArray();

    internal static Dictionary<PaddockGauge, int>? PreparationNeeds(PaddockMountReading mount)
    {
        if (mount.Fertility == BreedingMountFertility.Ready) return [];
        var demands = new Dictionary<PaddockGauge, int>();
        foreach (var gauge in BreedingTrainingRules.Gauges.Where(gauge => gauge != PaddockGauge.Experience))
        {
            if (BreedingTrainingRules.PointsNeeded(mount, gauge, mount.Level) is not { } points) return null;
            if (points > 0) demands[gauge] = BreedingTrainingRules.FuelNeeded(mount, gauge, points);
        }
        int serenity = mount.Serenity;
        if (demands.ContainsKey(PaddockGauge.Maturity)) MoveTo(Math.Clamp(serenity, -2_000, 2_000));
        if (serenity < 0)
        {
            if (demands.ContainsKey(PaddockGauge.Love)) MoveTo(0);
        }
        else if (demands.ContainsKey(PaddockGauge.Endurance))
        {
            MoveTo(-1);
        }
        return demands;

        void MoveTo(int target)
        {
            if (target == serenity) return;
            var gauge = target > serenity ? PaddockGauge.SerenityGain : PaddockGauge.SerenityLoss;
            demands[gauge] = demands.GetValueOrDefault(gauge) + Math.Abs(target - serenity);
            serenity = target;
        }
    }

    internal static decimal? ConfirmedGain(BreedingPairOption pair,
        IEnumerable<(BreedingResaleOption? Option, decimal Chance)> outcomes, long preparationCost)
    {
        if (pair.FertilityCost is not { } opportunity || preparationCost < 0) return null;
        var observed = outcomes.Where(outcome => outcome.Chance != 0).ToArray();
        var total = observed.Sum(outcome => outcome.Chance);
        // Dofus rounds each displayed percentage individually.
        if (observed.Length == 0 || Math.Abs(total - 100m) > .1m ||
            observed.Any(outcome => outcome.Option is null || outcome.Chance < 0))
            return null;
        return observed.Sum(outcome => outcome.Option!.Net * outcome.Chance / total) - opportunity - preparationCost;
    }
}
