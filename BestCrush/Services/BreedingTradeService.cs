using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

public sealed record BreedingTradeReport(string Server, IReadOnlyList<string> MarketNames,
    IReadOnlyDictionary<Guid, BreedingResaleAssessment> Resale, bool ExperiencePricingFailed,
    IReadOnlyList<BreedingPriceCheck> PriceChecks, IReadOnlyList<BreedingCaptureOption> Captures,
    IReadOnlyList<BreedingPairOption> Pairs, IReadOnlyDictionary<string, BreedingResaleOption> OffspringValues,
    BreedingMarketStrategyReport Strategy, IReadOnlyList<BreedingObjectiveStep> Plan,
    IReadOnlyDictionary<Guid, BreedingResaleOption> Sellable, IReadOnlyDictionary<Guid, BreedingMarketCapacity> OwnedCapacity,
    IReadOnlySet<string> SellableOffspring)
{
    public BreedingPlanningOptions Planning { get; init; } = new();
    public IReadOnlyDictionary<string, IReadOnlyList<BreedingObjectiveStep>> ObjectivePlans { get; init; } = new Dictionary<string, IReadOnlyList<BreedingObjectiveStep>>();
}

internal sealed class BreedingTradeService(CurrentServerState servers, ActivityCatalogService catalog, GlobalSalesRepository history,
    BreedingRefillService refill, ILogger<BreedingTradeService> logger, BestCrushSettingsService settings,
    CurrentWindowProfessionsState? professions = null)
{
    internal static IReadOnlyList<(string Zone, string[] Targets)> SearchAreas { get; } =
        [
            ("Territoire des Dragodindes sauvages", ["Dragodinde Amande", "Dragodinde Dorée", "Dragodinde Rousse"]),
            ("Bassin des Muldos", ["Muldo Doré", "Muldo Ébène", "Muldo Indigo", "Muldo Orchidée", "Muldo Pourpre"]),
            ("Haras de Brâkmar", ["Volkorne Ébène", "Volkorne Indigo", "Volkorne Orchidée", "Volkorne Pourpre"])
        ];

    internal async Task<BreedingTradeReport?> LoadAsync(DofusClientWindowKey? window, string? serverView,
        IReadOnlyList<PaddockMountReading> mounts, IReadOnlyDictionary<long, BreedingSpecies> species,
        CancellationToken token)
    {
        var planning = settings.BreedingPlanning;
        var lease = window is { } key ? servers.GetWindowCaptureLease(key) : null;
        string? server = lease?.ServerName ?? serverView;
        if (string.IsNullOrWhiteSpace(server))
            return null;
        if (planning.CapturesPerDay == 0)
            planning = planning with { CapturesPerDay = BreedingPlanningOptions.DefaultCaptures(
                professions?.ForWindow(window, server)?.FirstOrDefault(p => p.Name == "Éleveur")?.Level) };
        var items = await catalog.GetBreedingMarketItemsAsync(token);
        var marketItems = items.DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        var now = DateTimeOffset.UtcNow;
        var observations = history.ReadBreedingPrices(server, DateTimeOffset.MinValue, now).ToLookup(quote => quote.ItemId);
        var quotes = observations.SelectMany(group => BreedingResaleAdvisor.LatestSnapshot(group)
                .Where(price => BreedingResaleAdvisor.IsFresh(price, now)))
            .ToLookup(price => price.ItemId);
        var itemIds = marketItems.ToDictionary(item => item.Name, item => item.Id, StringComparer.OrdinalIgnoreCase);
        var marketNames = marketItems.ToDictionary(item => item.Id, item => item.Name);
        var listedToday = history.ReadListingTotals(server, now).ByItem;
        var listedThisWeek = history.ReadListingTotals(server, now, 7).ByItem;
        var histories = history.ReadHistories(server).Where(sales => sales.Market.ObjectType == MarketObjectType.Resource &&
                sales.Market.Variant.Length == 0 && marketNames.ContainsKey(sales.Market.ItemId) &&
                sales.ObservedAtUtc <= now).ToDictionary(sales => sales.Market.ItemId);
        var availability = history.ReadHistoryAvailability(server, now);
        // One demand calculation serves every species, stock sale and offspring. No species blacklist.
        var demand = marketNames.Keys.ToDictionary(id => id, id => BreedingMarketDemand.FromEvidence(id,
            histories.GetValueOrDefault(id), availability, listedToday.GetValueOrDefault(id), listedThisWeek.GetValueOrDefault(id), now));
        var captureZones = SearchAreas.SelectMany(area => area.Targets.Select(name => (Name: name, area.Zone)))
            .ToDictionary(entry => entry.Name, entry => entry.Zone, StringComparer.OrdinalIgnoreCase);
        var captureNames = SearchAreas.SelectMany(area => area.Targets).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var capturedMount = new PaddockMountReading(Guid.Empty, 1, 0, 0, 0)
        {
            Fertility = BreedingMountFertility.Fertile, Endurance = 0, Love = 0
        };
        var owned = mounts.Where(mount => mount.SpeciesId is { } id && species.ContainsKey(id))
            .Select(mount =>
            {
                string name = BreedingLineage.MarketName(species[mount.SpeciesId!.Value].Name);
                long itemId = BreedingLineage.Rides.GetValueOrDefault(mount.SpeciesId.Value)?.ItemId ?? itemIds.GetValueOrDefault(name);
                return (Mount: mount, Name: name, ItemId: itemId, Quotes: quotes[itemId].ToArray());
            }).ToArray();
        var needed = owned.SelectMany(row => BreedingResaleAdvisor.ComparableQuotes(row.Mount, row.Quotes)
                .Select(quote => BreedingResaleAdvisor.Need(row.Mount, quote.Level)))
            .Concat(marketItems.SelectMany(item =>
                BreedingResaleAdvisor.ComparableQuotes(capturedMount, quotes[item.Id])
                    .Select(quote => BreedingResaleAdvisor.Need(capturedMount, quote.Level))))
            .Concat(Enumerable.Range(2, 199).Select(BreedingMountExperience.RequiredAtLevel))
            .Concat(owned.Where(row => row.Mount.Fertility is BreedingMountFertility.Fertile or BreedingMountFertility.Ready)
                .SelectMany(row => Enumerable.Range(row.Mount.Level, 201 - row.Mount.Level)
                    .Select(level => Math.Max(0, BreedingMountExperience.RequiredAtLevel(level) - row.Mount.Experience))))
            .Where(points => points > 0).Distinct().ToArray();
        BreedingExperiencePricing experience = new([], []);
        bool experiencePricingFailed = false;
        try
        {
            experience = await refill.EstimateTrainingCostsAsync(window, server, PaddockGauge.Experience, needed, token);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            experiencePricingFailed = true;
            logger.LogWarning(ex, "Estimation du coût de l’XP des montures indisponible sur {Server}.", server);
        }
        var resale = owned.ToDictionary(row => row.Mount.Id, row =>
            BreedingResaleAdvisor.Compare(row.Mount, row.Quotes, experience.Costs) with
                { LatestSpeciesQuoteAt = observations[row.ItemId].Max(quote => (DateTimeOffset?)quote.ObservedAtUtc) });
        var newbornResale = marketItems.ToDictionary(item => item.Id,
            item => BreedingResaleAdvisor.Compare(capturedMount, quotes[item.Id].ToArray(), experience.Costs));
        var offspringValues = marketItems.Select(item => (item.Name, Value: newbornResale[item.Id].Best))
            .Where(entry => entry.Value is not null)
            .ToDictionary(entry => entry.Name, entry => entry.Value!, StringComparer.OrdinalIgnoreCase);
        var parents = owned.Select(row => new BreedingPairParent(row.Mount, row.Name,
            species[row.Mount.SpeciesId!.Value].FamilyId, resale[row.Mount.Id].Best,
            row.Quotes.FirstOrDefault(price => !price.Fertile && price.Level == row.Mount.Level))).ToArray();
        var pairs = BreedingPortfolioAdvisor.PairOptions(parents, offspringValues, marketNames);
        var captureRides = BreedingLineage.Rides.Values.Where(ride => ride.Generation == 1 &&
                marketNames.TryGetValue(ride.ItemId, out string? name) && captureNames.Contains(name))
            .ToDictionary(ride => marketNames[ride.ItemId], StringComparer.OrdinalIgnoreCase);
        var captureCrosses = captureRides.ToDictionary(entry => entry.Key,
            entry => BreedingPortfolioAdvisor.CaptureCrosses(entry.Value, parents).ToArray(), StringComparer.OrdinalIgnoreCase);
        var candidateNeeds = parents.Select(parent => parent.Mount).Where(mount =>
                mount.Fertility is BreedingMountFertility.Fertile or BreedingMountFertility.Ready)
            .Append(capturedMount).DistinctBy(mount => mount.Id)
            .ToDictionary(mount => mount.Id, BreedingPortfolioAdvisor.PreparationNeeds);
        var demands = candidateNeeds.Values.OfType<Dictionary<PaddockGauge, int>>().SelectMany(needs => needs)
            .Where(demand => demand.Value > 0).GroupBy(demand => demand.Key);
        var trainingPrices = await Task.WhenAll(demands.Select(async demand =>
        {
            try
            {
                return (Gauge: demand.Key, Pricing: await refill.EstimateTrainingCostsAsync(window, server, demand.Key,
                    demand.Select(entry => entry.Value).Distinct().ToArray(), token));
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Estimation du coût d’élevage indisponible pour {Gauge}.", demand.Key);
                return (Gauge: demand.Key, Pricing: new BreedingExperiencePricing([], []));
            }
        }));
        var preparationCosts = candidateNeeds.ToDictionary(entry => entry.Key, entry =>
        {
            if (entry.Value is not { } needs) return (long?)null;
            var amounts = needs.Select(need => trainingPrices.FirstOrDefault(price => price.Gauge == need.Key)
                .Pricing?.Costs.GetValueOrDefault(need.Value, -1) ?? -1).ToArray();
            return amounts.Any(value => value < 0) ? null : amounts.Sum();
        });
        pairs = BreedingPortfolioAdvisor.RankPairs(pairs.Select(pair => pair with
        {
            PreparationCost = preparationCosts.GetValueOrDefault(pair.First.Mount.Id) + preparationCosts.GetValueOrDefault(pair.Second.Mount.Id)
        }));
        var productionSales = newbornResale.Where(entry => entry.Value.Options.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value.Options);
        var purchases = quotes.SelectMany(group => group.Where(price => price is { Level: 1, Fertile: true }))
            .ToDictionary(price => price.ItemId, price => price.Price);
        // Capture equipment is excluded by user policy; no filet quote or recipe is required.
        var captureCosts = captureRides.Values.Select(ride => ride.Family).Distinct().ToDictionary(family => family, _ => 0L);
        long? fullPreparation = preparationCosts.GetValueOrDefault(Guid.Empty);
        var strategy = await Task.Run(() => BreedingMarketStrategy.Calculate(BreedingLineage.Rides, marketNames, purchases,
            captureCosts, fullPreparation, experience.Costs, productionSales, demand, token, planning), token);
        var objectives = strategy.Report.Objectives.Select(g => g with { OwnedTargets = owned.Count(row => row.Mount.SpeciesId == g.RideId &&
            resale[row.Mount.Id].Options.Any(o => o.Level == g.Sale.Level && o.Fertile == g.Sale.Fertile &&
                (planning.BudgetKamas is null || o.ExperienceCost <= planning.BudgetKamas))) })
            .OrderByDescending(g => g.WeeklyPotential).ThenByDescending(g => g.Margin).ToArray();
        strategy = strategy with { Report = strategy.Report with { Objectives = objectives, Recommended = objectives.FirstOrDefault(g => g.CanSell),
            Generations = strategy.Report.Generations.Select(g => g with { Best = objectives.FirstOrDefault(o => o.Generation == g.Generation) }).ToArray() } };
        var ownedCapacity = owned.Where(row => demand.ContainsKey(row.ItemId) && resale[row.Mount.Id].Best is not null)
            .ToDictionary(row => row.Mount.Id, row => demand[row.ItemId].AtPrice(resale[row.Mount.Id].Best!.SalePrice));
        var sellable = owned.Where(row => row.Mount.Fertility.HasValue && demand.ContainsKey(row.ItemId))
            .GroupBy(row => row.ItemId).SelectMany(group => demand[group.Key].Allocate(
                group.Select(row => (row.Mount.Id, resale[row.Mount.Id].Options))))
            .ToDictionary();
        var plans = await Task.Run(() => strategy.Report.Objectives.ToDictionary(BreedingObjectiveProgressStore.Key, objective =>
        {
            token.ThrowIfCancellationRequested();
            // A quota warning must not hide an already owned target from its route. The player may
            // inspect the route or explicitly override that warning; this does not allocate any sales.
            var planningSales = new Dictionary<Guid, BreedingResaleOption>(sellable);
            foreach (var row in owned.Where(row => row.Mount.SpeciesId == objective.RideId))
            {
                planningSales.Remove(row.Mount.Id);
                if (resale[row.Mount.Id].Options.FirstOrDefault(option => option.Level == objective.Sale.Level &&
                    option.Fertile == objective.Sale.Fertile) is { } comparable)
                    planningSales[row.Mount.Id] = comparable;
            }
            return BreedingObjectivePlan.Build(objective, strategy.Routes[objective.RideId], BreedingLineage.Rides, marketNames,
                parents, preparationCosts, experience.Costs, planningSales, fullPreparation, captureZones, planning);
        }), token);
        IReadOnlyList<BreedingObjectiveStep> plan = strategy.Report.Recommended is { } recommended
            ? plans.GetValueOrDefault(BreedingObjectiveProgressStore.Key(recommended), []) : [];

        var sellableOffspring = offspringValues.Where(entry =>
                demand.GetValueOrDefault(itemIds[entry.Key])?.AtPrice(entry.Value.SalePrice).Remaining > 0)
            .Select(entry => entry.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var captures = SearchAreas.SelectMany(area => area.Targets.Select(name =>
            new BreedingCaptureOption(name, area.Zone, DirectSale(name),
                owned.Count(row => string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    row.Mount.Fertility is BreedingMountFertility.Fertile or BreedingMountFertility.Ready), BestCaptureCross(name))
                { CaptureExpense = 0 }))
            .Where(option => option.Net > 0)
            .OrderByDescending(option => option.Net).ThenBy(option => option.PreferCross).ThenBy(option => option.Resale?.ExperienceCost)
            .ThenBy(option => option.Owned).ToArray();
        var checks = new List<BreedingPriceCheck>();
        foreach (var candidate in strategy.Report.Generations.Select(row => row.BestPriced).OfType<BreedingMarketObjective>()
                     .Where(row => row.Margin > 0 && row.Capacity?.Evidence != BreedingDemandEvidence.Observed &&
                         availability?.IsUnavailable(row.ItemId, histories.GetValueOrDefault(row.ItemId)?.ObservedAtUtc, now) != true)
                     .OrderByDescending(row => row.Margin))
            checks.Add(new(candidate.ItemId, candidate.Name, histories.GetValueOrDefault(candidate.ItemId)?.ObservedAtUtc,
                "Ouvrir le Cours du marché pour relever le volume des ventes"));
        foreach (var generation in strategy.Report.Generations.Where(row => row.WithoutPrice > 0).OrderByDescending(row => row.Generation))
        {
            var next = BreedingLineage.Rides.Values.Where(ride => ride.Generation == generation.Generation && !quotes[ride.ItemId].Any())
                .OrderBy(ride => strategy.Routes.GetValueOrDefault(ride.Id)?.FirstOrDefault()?.Cost ?? decimal.MaxValue)
                .FirstOrDefault(ride => marketNames.ContainsKey(ride.ItemId));
            if (next is not null && PriceCheck(marketNames[next.ItemId], $"Comparer un débouché de génération {generation.Generation}") is { } check)
                checks.Add(check);
        }
        foreach (var row in owned.DistinctBy(row => (row.ItemId, row.Mount.Level, row.Mount.Fertility)))
        {
            if (row.ItemId <= 0 || resale[row.Mount.Id].Current is not null) continue;
            if (quotes[row.ItemId].Any()) continue;
            checks.Add(new(row.ItemId, row.Name, observations[row.ItemId].Max(price => (DateTimeOffset?)price.ObservedAtUtc),
                $"Comparer la revente au niveau {row.Mount.Level}"));
        }
        checks.AddRange(pairs.Take(12).SelectMany(pair => pair.Offspring).Distinct().Where(name => !offspringValues.ContainsKey(name))
            .Select(name => PriceCheck(name, "Estimer la revente du petit")).OfType<BreedingPriceCheck>());
        checks.AddRange(captureCrosses.Values.SelectMany(crosses => crosses).SelectMany(cross => cross.Offspring)
            .Select(ride => marketNames.GetValueOrDefault(ride.ItemId)).OfType<string>().Distinct()
            .Where(name => !offspringValues.ContainsKey(name))
            .Select(name => PriceCheck(name, "Comparer le descendant avec la revente directe de la capture")).OfType<BreedingPriceCheck>());
        checks.AddRange(captureNames.Where(name => !offspringValues.ContainsKey(name))
            .Select(name => PriceCheck(name, "Comparer la capture")).OfType<BreedingPriceCheck>());
        if (needed.Length > 0 && experience.Costs.Count == 0) checks.InsertRange(0, experience.PriceChecks);
        foreach (var (_, pricing) in trainingPrices.Where(price => price.Pricing.Costs.Count == 0))
            checks.AddRange(pricing.PriceChecks);
        checks.AddRange(CompleteMarketChecks(BreedingLineage.Rides.Values, marketNames, observations, quotes,
            histories.Values.ToDictionary(sales => sales.Market.ItemId, sales => sales.ObservedAtUtc), now, availability));
        if (lease is { } confirmed && !servers.IsCaptureLeaseActive(confirmed))
            throw new InvalidOperationException("La session Dofus a changé pendant le calcul.");
        return new(server, itemIds.Keys.Order().ToArray(), resale, experiencePricingFailed, checks.DistinctBy(check => check.ItemId).ToArray(),
            captures, pairs, offspringValues, strategy.Report, plan, sellable, ownedCapacity, sellableOffspring) { ObjectivePlans = plans, Planning = planning };

        BreedingPriceCheck? PriceCheck(string name, string reason)
        {
            long id = itemIds.GetValueOrDefault(name);
            if (id <= 0 || quotes[id].Any()) return null;
            return new(id, name, observations[id].Max(price => (DateTimeOffset?)price.ObservedAtUtc), reason);
        }

        BreedingCaptureCross? BestCaptureCross(string name)
        {
            if (!captureRides.TryGetValue(name, out var ride) || !captureCrosses.TryGetValue(name, out var options))
                return null;
            var sterile = quotes[ride.ItemId].FirstOrDefault(price => price is { Level: 1, Fertile: false });
            return options.Where(cross => planning.Accepts(1, cross.Partner.Mount.Level)).Select(cross => BreedingPortfolioAdvisor.EstimateCaptureCross(cross, name, sterile,
                    preparationCosts, marketNames, offspringValues))
                .OfType<BreedingCaptureCross>()
                .Where(cross => cross.Offspring.All(sellableOffspring.Contains))
                .MaxBy(cross => cross.Net);
        }

        BreedingResaleOption? DirectSale(string name) => itemIds.TryGetValue(name, out long id) && demand.TryGetValue(id, out var market)
            ? newbornResale[id].Options.Where(option => market.AtPrice(option.SalePrice).Remaining > 0)
                .OrderByDescending(option => option.Net).FirstOrDefault()
            : null;
    }

    internal static IEnumerable<BreedingPriceCheck> CompleteMarketChecks(IEnumerable<BreedingRide> rides,
        IReadOnlyDictionary<long, string> marketNames, ILookup<long, BreedingLevelPrice> observations,
        ILookup<long, BreedingLevelPrice> currentQuotes, IReadOnlyDictionary<long, DateTimeOffset> lastHistories,
        DateTimeOffset now, MarketHistoryAvailability? availability = null)
    {
        foreach (var ride in rides.OrderByDescending(ride => ride.Generation))
        {
            if (!marketNames.TryGetValue(ride.ItemId, out var name)) continue;
            if (!currentQuotes[ride.ItemId].Any())
            {
                yield return new(ride.ItemId, name, observations[ride.ItemId].Max(price => (DateTimeOffset?)price.ObservedAtUtc),
                    $"Relever les prix · Génération {ride.Generation}");
            }
            else
            {
                DateTimeOffset? last = lastHistories.TryGetValue(ride.ItemId, out var date) ? date : null;
                if ((!last.HasValue || now - last.Value > BreedingMarketDemand.Lifetime) &&
                    availability?.IsUnavailable(ride.ItemId, last, now) != true)
                    yield return new(ride.ItemId, name, last, "Ouvrir le Cours du marché pour relever le volume des ventes");
            }
        }
    }
}
