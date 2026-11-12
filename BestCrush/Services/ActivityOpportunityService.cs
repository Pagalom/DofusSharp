using System.Collections.Concurrent;
using System.Numerics;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

/// <summary>Independent DB contexts and bounded report cache; UI filters never perform a scan.</summary>
public sealed class ActivityOpportunityService : IDisposable
{
    private static readonly TimeSpan MaximumSalesHistoryAge = TimeSpan.FromDays(2);
    private static readonly int[] ResaleLotSizes = [1, 10, 100, 1000];
    private const long ResaleDiscountPerLot = 1;
    private readonly IDbContextFactory<BestCrushDbContext> _database;
    private readonly IDataPriorityProvider _priority;
    private readonly GlobalSalesRepository _sales;
    private readonly ActivityCatalogService _catalog;
    private readonly MarketDataChangeNotifier _changes;
    private readonly ILogger<ActivityOpportunityService>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, long> _revisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ScanKey, ActivityReport> _cache = [];
    private sealed record ScanKey(string Module, string Server, LiveStockSource? Stock, int Volume, long Revision, DataPriority Priority);

    public ActivityOpportunityService(IDbContextFactory<BestCrushDbContext> database, IDataPriorityProvider priority,
        GlobalSalesRepository sales, ActivityCatalogService catalog, MarketDataChangeNotifier changes, ILogger<ActivityOpportunityService>? logger = null)
    {
        _logger = logger; _database = database; _priority = priority; _sales = sales; _catalog = catalog; _changes = changes;
        _changes.Changed += MarketChanged; _sales.Changed += SalesChanged;
    }
    private void SalesChanged(string server) => _revisions.AddOrUpdate(server, 1, (_, n) => n + 1);
    private void MarketChanged(object? sender, MarketDataChangedEventArgs e) => SalesChanged(e.ServerName);

    public async Task<ActivityReport> ScanAsync(string module, string server, LiveStockSource? stock, int volume,
        CancellationToken token, bool force = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);
        // Trading values the entire available stock; quantity is only a scenario input in other modules.
        if (module == "achat-vente") volume = 1;
        if (volume is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(volume));
        if (module is not ("achat-vente" or "farming" or "pierres-ame" or "")) throw new ArgumentException("Module inconnu.");
        server = server.Trim();
        if (!StockOpportunityService.CanUse(stock, server)) stock = null;
        var key = new ScanKey(module, server.ToUpperInvariant(), stock, volume, _revisions.GetValueOrDefault(server), _priority.Priority);
        await _gate.WaitAsync(token);
        try
        {
            if (!force && _cache.TryGetValue(key, out var cached) && DateTime.UtcNow-cached.CalculatedUtc<TimeSpan.FromMinutes(10)) return cached;
            var report = await Task.Run(() => CalculateAsync(module, server, stock, volume, token), token);
            if (key.Revision == _revisions.GetValueOrDefault(server))
            {
                _cache[key] = report;
                while (_cache.Count > 8) _cache.Remove(_cache.Keys.First());
            }
            return report;
        }
        finally { _gate.Release(); }
    }

    private async Task<ActivityReport> CalculateAsync(string module, string server, LiveStockSource? source,
        int volume, CancellationToken token)
    {
        await using var db = await _database.CreateDbContextAsync(token);
        var prices = new MarketPriceService(db, _priority);
        bool trading = module is "" or "achat-vente";
        var market = new EquipmentProfitabilityContext
        {
            EquipmentPrices = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Equipment, server, token),
            ResourcePrices = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Resource, server, token, excludeUnavailableLots: trading),
            RunePrices = await prices.GetLatestObservationsForServerAsync(MarketObjectType.Rune, server, token, excludeUnavailableLots: trading),
            Coefficients = new Dictionary<long, CoefficientObservation>()
        };
        var resources = await db.Resources.AsNoTracking().ToArrayAsync(token);
        var runes = await db.Runes.AsNoTracking().ToArrayAsync(token);
        var equipment = await db.Equipments.AsNoTracking().Include(x => x.Recipe).ThenInclude(x => x.Resource)
            .Include(x => x.EquipmentRecipe).ThenInclude(x => x.IngredientEquipment).ToArrayAsync(token);
        var items = resources.Select(x => new MarketItem(x.DofusDbId, x.Name, x.Level, MarketObjectType.Resource))
            .Concat(runes.Select(x => new MarketItem(x.DofusDbId, x.Name, x.Level, MarketObjectType.Rune)))
            .Concat(equipment.Select(x => new MarketItem(x.DofusDbId, x.Name, x.Level, MarketObjectType.Equipment)))
            .DistinctBy(x => (x.Id, x.Type)).ToArray();
        var salesHistories = _sales.ReadHistories(server);
        var histories = salesHistories.Where(x => x.Market.Variant.Length == 0)
            .ToDictionary(x => (x.Market.ItemId, x.Market.ObjectType));
        var stock = source?.Containers.Where(x => x.Complete && !x.Stale && x.Container is "inventory" or "bank")
            .SelectMany(x => x.Items).Where(x => x.ItemId <= long.MaxValue).GroupBy(x => (long)x.ItemId)
            .ToDictionary(x => x.Key, x => x.Aggregate(BigInteger.Zero, (sum, item) => sum + item.Quantity)) ?? [];
        long OwnedUnits(long id) => checked((long)(module == "achat-vente" ? stock[id] : BigInteger.Min(stock[id], volume)));
        List<string> notices = [];
        List<ActivityRecommendation> rows = [];
        if (source is null) notices.Add("Stocks indisponibles pour ce serveur : la revente de votre inventaire ne peut pas encore être évaluée. Les autres analyses restent accessibles.");
        if (source is not null && module is "" or "achat-vente")
        {
            var ownedIds = stock.Keys.ToArray();
            ActivityItem[] metadata = [];
            try { metadata = await _catalog.GetItemsAsync(ownedIds, token); }
            catch (Exception ex) when (!token.IsCancellationRequested) { _logger?.LogWarning(ex,"Métadonnées des stocks indisponibles."); }
            var byId = metadata.ToDictionary(x => x.Id);
            items = items.Select(x => byId.TryGetValue(x.Id,out var meta)
                ? x with { VariantSensitive = meta.TypeId is 85 or 308 or 309 or 327 } : x).ToArray();
            var knownIds = items.Select(x => x.Id).ToHashSet();
            foreach (long id in ownedIds.Where(x => !knownIds.Contains(x)))
            {
                byId.TryGetValue(id,out var meta);
                var type = meta?.TypeId is 78 ? MarketObjectType.Rune : meta?.TypeId is { } typeId &&
                    EquipmentTypeExtensions.EquipmentTypeFromDofusDbTypeId(typeId) is not null ? MarketObjectType.Equipment : MarketObjectType.Resource;
                items = items.Append(new MarketItem(id,meta?.Name ?? $"Objet #{id}",meta?.Level ?? 0,type,
                    meta?.TypeId is 85 or 308 or 309 or 327)).ToArray();
            }
        }
        var alternatives = new Dictionary<long, List<CraftAlternative>>();
        if (module is "" or "achat-vente" or "farming")
        {
            var planner = new StockCraftPlanner(prices);
            var profitable = new List<(StockCraftPlan Plan, double Gain)>();
            foreach (var item in equipment.Where(x => market.EquipmentPrices.Keys.Any(p => p.DofusDbId == x.DofusDbId)))
            {
                token.ThrowIfCancellationRequested();
                if (item.Recipe.Count + item.EquipmentRecipe.Count == 0) continue;
                var plan = planner.Calculate(item, 1, stock, market);
                var sale = prices.CalculateValue(item.DofusDbId, 1, market.EquipmentPrices);
                if (plan.HasCompleteValuation && sale is { } saleValue && plan.EconomicCost is { } economicCost && saleValue.Value > economicCost)
                    profitable.Add((plan, saleValue.Value - economicCost));
            }
            IReadOnlyDictionary<long, RecipeProfession> professions = new Dictionary<long, RecipeProfession>();
            try { professions = await _catalog.GetRecipeProfessionsAsync(profitable.Select(x => x.Plan.Product.DofusDbId).ToArray(), token); }
            catch (Exception ex) when (!token.IsCancellationRequested) { _logger?.LogWarning(ex,"Catalogue public indisponible pour {Module}",module); notices.Add("Métiers des crafts non disponibles : leur niveau reste à vérifier."); }
            foreach (var (plan, gain) in profitable)
            {
                if (plan.Product is not Equipment resultEquipment) continue;
                professions.TryGetValue(resultEquipment.DofusDbId, out var profession);
                var alternative = new CraftAlternative(resultEquipment.DofusDbId, resultEquipment.Name,
                    string.Join("\n", plan.Ingredients.Select(x => $"{x.Required} × {x.Name}")), gain, profession?.Job, profession?.Level)
                {
                    IconId = resultEquipment.DofusDbIconId,
                    Ingredients = plan.Ingredients.Select(x => new TradeRecipeIngredient(x.ItemId, x.Name, x.Required, x.IconId)).ToArray()
                };
                foreach (long id in plan.Ingredients.Select(x => x.ItemId).Distinct())
                {
                    if (!alternatives.TryGetValue(id, out var list)) alternatives[id] = list = [];
                    list.Add(alternative);
                }
            }
        }

        if (trading)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                if (item.VariantSensitive)
                {
                    if (stock.GetValueOrDefault(item.Id)>0)
                        rows.Add(Build($"owned-capture-{item.Id}","achat-vente","Revendre mon stock",item,
                            OwnedUnits(item.Id),null,0,null,server,
                            ["Le contenu de cette capture doit être identifié. Le prix global d’une pierre générique ne peut pas valoriser le boss qu’elle contient."]));
                    continue;
                }
                var lots = PricesFor(item.Type, market).Values.Where(x => x.DofusDbId == item.Id).ToArray();
                histories.TryGetValue((item.Id, item.Type), out var history);
                var demand = TradingResaleDemand.FromHistory(history, now, MaximumSalesHistoryAge);
                var median = lots.Length > 0 || stock.GetValueOrDefault(item.Id) > 0
                    ? history?.SevenDayMedian()
                    : null;
                AddTradingPurchases(rows, item, lots, history, median, demand, server);
                if (stock.GetValueOrDefault(item.Id) > 0)
                {
                    long units = OwnedUnits(item.Id);
                    var resalePrices = lots.Where(lot => item.Type == MarketObjectType.Equipment ||
                            demand is null || demand.CompatibleUnits(lot.Quantity, ResalePrice(lot.Price)) > 0)
                        .ToDictionary(lot => (lot.DofusDbId, lot.Quantity));
                    var sale = prices.CalculateValue(item.Id, units, resalePrices, ResaleDiscountPerLot);
                    double? revenue = sale?.Value ?? (median is { } reference ? ResalePrice(reference.UnitPrice) * units : null);
                    var row = Build($"owned-{item.Type}-{item.Id}", "achat-vente", "Revendre mon stock", item, units,
                        revenue, 0, history, server, [
                            $"{stock[item.Id]:N0} possédé(s), dont {units:N0} valorisé(s). Montant encaissable ; le coût de votre acquisition passée n’est pas connu.",
                            sale is null ? "Valeur indicative d’après la médiane des ventes sur 7 jours." : "Valeur indicative aux prix des lots relevés en HDV."],
                        sale?.UsedObservations.Select(x => (DateTime?)x.ObservedAtUtc).Min() ?? history?.ObservedAtUtc.UtcDateTime)
                        with
                        {
                            TradingLots = new(HistoricalResale: sale is null && median is not null),
                            StockResaleTargets = StockTargets(item, units, lots, median?.UnitPrice, demand),
                            Crafts = alternatives.GetValueOrDefault(item.Id)?.OrderByDescending(x => x.AdditionalGain).Take(5).ToArray() ?? []
                        };
                    rows.Add(row);
                }
            }
        }

        if (module is "" or "farming")
        {
            try
            {
                var harvests = await _catalog.GetHarvestsAsync(token);
                var knownResourceIds = resources.Select(x => x.DofusDbId).ToHashSet();
                foreach (var harvest in harvests.DistinctBy(x => x.Item.Id).Where(x => !knownResourceIds.Contains(x.Item.Id)))
                    db.Resources.Add(new Resource(harvest.Item.Id) { Name = harvest.Item.Name, Level = harvest.Item.Level });
                if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(token);
                var metadata = await _catalog.GetItemsAsync(resources.Select(x => x.DofusDbId).Concat(harvests.Select(x => x.Item.Id)).Distinct().ToArray(), token);
                var harvestsByItem = harvests.Select(harvest => harvest.Job == "Base"
                    ? harvest with { Job = "Sans métier", RequiredLevel = null }
                    : harvest).ToLookup(harvest => harvest.Item.Id);
                foreach (var item in metadata)
                {
                    var itemHarvests = harvestsByItem[item.Id];
                    long? iconId = item.IconId is > 0 ? item.IconId
                        : itemHarvests.Select(harvest => harvest.Item.IconId).FirstOrDefault(id => id is > 0);
                    histories.TryGetValue((item.Id, MarketObjectType.Resource), out var history);
                    var directSale = prices.CalculateValue(item.Id, volume, market.ResourcePrices);
                    double? revenue = directSale?.Value ?? history?.WeightedUnitPrice(7) * volume;
                    DateTime? priceAt = directSale?.UsedObservations.Select(x => (DateTime?)x.ObservedAtUtc).Min() ?? history?.ObservedAtUtc.UtcDateTime;
                    var marketItem = new MarketItem(item.Id, item.Name, item.Level, MarketObjectType.Resource);
                    var crafts = alternatives.GetValueOrDefault(item.Id)?.OrderByDescending(x => x.AdditionalGain).Take(5).ToArray() ?? [];
                    foreach (var harvest in itemHarvests)
                        rows.Add(Build($"harvest-{item.Id}-{harvest.Job}", "farming", "Récolte", marketItem, volume, revenue, 0, history, server,
                            [$"Valeur de {volume:N0} ressources récoltées, avant taxes. Aucune cadence de récolte n’est supposée."], priceAt)
                            with { Job = harvest.Job, RequiredJobLevel = harvest.RequiredLevel, Crafts = crafts,
                                IconId = iconId });
                    if (item.DropMonsterIds.Count > 0)
                        rows.Add(Build($"drop-{item.Id}", "farming", "Ressource de combat", marketItem, volume, revenue, 0, history, server,
                            [$"Valeur de {volume:N0} ressources obtenues. Ce montant ne représente pas le gain d’un combat ; aucun temps, taux de drop ou bonus de prospection n’est supposé."], priceAt)
                            with { MonsterIds = item.DropMonsterIds, Crafts = crafts, IconId = iconId });
                    // Compare the SAME resource volume through a full batch of
                    // crafts. Other ingredients keep their economic value;
                    // the selected resource is the quantity to gather/loot.
                    foreach (var craft in crafts)
                    {
                        var target = equipment.First(x => x.DofusDbId == craft.ItemId);
                        int required = target.Recipe.Where(x => x.Resource.DofusDbId == item.Id).Sum(x => x.Count);
                        if (required <= 0) continue;
                        int count = Math.Min(1000, volume / required);
                        int maximumPerIngredient = target.Recipe.Select(x => x.Count).Concat(target.EquipmentRecipe.Select(x => x.Count)).DefaultIfEmpty(1).Max();
                        count = Math.Min(count, 250000 / Math.Max(1, maximumPerIngredient));
                        if (count == 0) continue;
                        var batchStock = stock.ToDictionary(x => x.Key, x => x.Value);
                        batchStock[item.Id] = volume;
                        var plan = new StockCraftPlanner(prices).Calculate(target, count, batchStock, market);
                        var sale = prices.CalculateValue(target.DofusDbId, count, market.EquipmentPrices);
                        var used = plan.Ingredients.SingleOrDefault(x => x.ItemId == item.Id);
                        if (!plan.HasCompleteValuation || sale is null || used?.OwnedValue is null || plan.EconomicCost is null) continue;
                        int remaining = volume - used.Used;
                        var rest = prices.CalculateValue(item.Id, remaining, market.ResourcePrices);
                        if (rest is null) continue;
                        double otherCost = Math.Max(0, plan.EconomicCost.Value - used.OwnedValue.Value.Value);
                        double batchRevenue = sale.Value.Value + rest.Value.Value;
                        if (revenue is { } direct && batchRevenue - otherCost <= direct) continue;
                        histories.TryGetValue((target.DofusDbId, MarketObjectType.Equipment), out var craftHistory);
                        var craftMarket = new MarketItem(target.DofusDbId, target.Name, target.Level, MarketObjectType.Equipment);
                        var derived = Build($"craft-value-{item.Id}-{target.DofusDbId}", "farming", "Valoriser en craft", craftMarket,
                            count, batchRevenue, otherCost, craftHistory, server,
                            [$"Transformer {used.Used} des {volume} ressources en {count} × {target.Name} ; {remaining} ressource(s) restante(s) valorisée(s) à la revente.",
                             $"Les autres ingrédients sont valorisés à {otherCost:N0} k, qu’ils soient possédés ou à acheter. La recette n’est pas présentée comme gratuite.",
                             "Cette valeur concerne un volume de ressources, sans gain par combat ni estimation de temps."],
                            sale.Value.UsedObservations.Concat(plan.Observations).Select(x => (DateTime?)x.ObservedAtUtc).Min())
                            with { ItemId = item.Id, Name = item.Name + " → " + target.Name, Quantity = volume, Level = item.Level,
                                IconId = iconId, FarmingCraft = new(item.Name, used.Used, count),
                                CraftJob = craft.Job, RequiredCraftLevel = craft.RequiredLevel, Crafts = [craft], MonsterIds = item.DropMonsterIds, CashRequired = plan.CashRequired };
                        // A harvest route and a combat route remain separate:
                        // profession filters must not hide combat resources.
                        foreach (var harvest in itemHarvests)
                            rows.Add(derived with { Id = derived.Id + "-harvest-" + harvest.Job, Strategy = "Récolte puis craft",
                                Job = harvest.Job, RequiredJobLevel = harvest.RequiredLevel, MonsterIds = [] });
                        if (item.DropMonsterIds.Count > 0)
                            rows.Add(derived with { Id = derived.Id + "-combat", Strategy = "Ressource de combat puis craft" });
                    }
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested) { _logger?.LogWarning(ex,"Catalogue public indisponible pour {Module}",module); notices.Add("Catalogue des récoltes et monstres indisponible. Réessayez lorsque DofusDB est accessible."); }
        }

        if (module is "" or "pierres-ame")
        {
            try
            {
                var bosses = await _catalog.GetBossesAsync(token);
                // These public, named market items must be known before a future
                // passive HDV capture can classify their prices correctly.
                var stoneIds = bosses.SelectMany(x => x.Stones).DistinctBy(x => x.Id).ToArray();
                var known = resources.Select(x => x.DofusDbId).ToHashSet();
                foreach (var stone in stoneIds.Where(x => !known.Contains(x.Id))) db.Resources.Add(new Resource(stone.Id) { Name = stone.Name });
                if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(token);
                var quotes = _sales.ReadSoulQuotes(server).ToDictionary(x => x.BossId);
                foreach (var boss in bosses)
                {
                    var eligible = boss.Stones.Select(x => new { Stone = x, Cost = prices.CalculateValue(x.Id, 1, market.ResourcePrices) })
                        .Where(x => x.Cost is not null).OrderBy(x => x.Cost!.Value.Value).FirstOrDefault();
                    quotes.TryGetValue(boss.Id, out var quote);
                    var item = new MarketItem(boss.Id, boss.Name, boss.Level ?? 0, MarketObjectType.Resource);
                    List<string> details = [boss.Dungeon];
                    var aggregate = salesHistories.FirstOrDefault(x => x.Market.Variant == GlobalSalesRepository.BossHistoryVariant(boss.Id));
                    if (quote is { LotQuantity: > 1 })
                        details.Add($"Prix unitaire estimé : {quote.Price:N2} k, d’après un lot de {quote.LotQuantity} à {quote.LotPrice:N0} k.");
                    details.Add(eligible is not null ? $"Pierre utilisée : {eligible.Stone.Name}, capacité {eligible.Stone.Capacity}." :
                        "Prix de la pierre vide manquant : " + (boss.Stones.FirstOrDefault()?.Name ?? "pierre adaptée au boss"));
                    if (eligible?.Cost is { IsEstimated: true } estimated)
                    {
                        var lot = estimated.UsedObservations.OrderBy(x => x.Quantity).FirstOrDefault();
                        if (lot is not null) details.Add($"Coût par pierre estimé depuis un lot de {lot.Quantity} à {lot.Price:N0} k. Le budget d’achat tient compte du lot entier si vous ne possédez pas de pierre.");
                    }
                    DateTime? oldestPrice = quote is null ? null : eligible?.Cost?.UsedObservations
                        .Select(x => (DateTime?)x.ObservedAtUtc).Append(quote.ObservedAtUtc.UtcDateTime).Min() ?? quote.ObservedAtUtc.UtcDateTime;
                    var row = Build($"soul-{boss.Id}", "pierres-ame", "Capturer puis revendre", item, 1, quote?.Price,
                        eligible?.Cost?.Value, aggregate, server, details, oldestPrice) with
                    {
                        BossId = boss.Id,
                        SoulCapture = new(boss.Dungeon, eligible?.Stone ?? boss.Stones.FirstOrDefault(),
                            eligible?.Cost?.IsEstimated == true, quote is { LotQuantity: > 1 }),
                        CashRequired = eligible is null ? null : stock.GetValueOrDefault(eligible.Stone.Id) > 0
                            ? 0 : prices.CalculateMinimumPurchaseCost(eligible.Stone.Id, 1, market.ResourcePrices)?.TotalCost
                    };
                    rows.Add(row);
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested) { _logger?.LogWarning(ex,"Catalogue public indisponible pour {Module}",module); notices.Add("Catalogue des boss capturables indisponible. Réessayez lorsque DofusDB est accessible."); }
        }
        // Reuse the already loaded local catalog before resolving artwork missing from older activity caches.
        var artwork = resources.Cast<IItem>().Concat(runes).Concat(equipment)
            .GroupBy(x => x.DofusDbId).ToDictionary(x => x.Key, x => x.First().DofusDbIconId);
        var missingFarmingIcons = rows.Where(row => row.Module == "farming" && row.IconId is not > 0 &&
            artwork.GetValueOrDefault(row.ItemId) is not > 0).Select(row => row.ItemId).Distinct().ToArray();
        if (missingFarmingIcons.Length > 0)
        {
            try
            {
                foreach (var (id, iconId) in await _catalog.GetItemIconIdsAsync(missingFarmingIcons, token))
                    artwork[id] = iconId;
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _logger?.LogWarning(ex, "Images manquantes du catalogue Farming indisponibles.");
            }
        }
        rows = rows.Select(row => (row.Module is "achat-vente" or "farming") && row.IconId is not > 0
            ? row with { IconId = artwork.GetValueOrDefault(row.ItemId) } : row).ToList();
        return new(server, source?.Id, DateTime.UtcNow, rows, notices);
    }

    private static void AddTradingPurchases(List<ActivityRecommendation> rows, MarketItem item,
        IReadOnlyList<MarketPriceObservation> lots, GlobalSalesHistory? history,
        (double UnitPrice, DateTimeOffset? LastAtOrAboveUtc)? median, TradingResaleDemand? demand, string server)
    {
        var recomposableLots = item.Type == MarketObjectType.Equipment ? [] : lots;
        foreach (var buy in lots)
        {
            foreach (var sell in recomposableLots.Where(x => x.Quantity != buy.Quantity))
            {
                int units = Lcm(buy.Quantity, sell.Quantity);
                if (units <= 0 || units > 10000)
                    continue;

                double cost = (double)buy.Price * (units / buy.Quantity);
                double sellPrice = ResalePrice(sell.Price);
                double revenue = sellPrice * (units / sell.Quantity);
                long? compatibleDailyUnits = demand?.CompatibleUnits(sell.Quantity, sellPrice);
                if (revenue <= cost || compatibleDailyUnits < units)
                    continue;

                rows.Add(Build($"lots-{item.Type}-{item.Id}-{buy.Quantity}-{sell.Quantity}", "achat-vente", "Recomposer des lots", item,
                    units, revenue, cost, history, server, [
                        $"Acheter {units / buy.Quantity} lot(s) de {buy.Quantity} à {buy.Price:N0} k ; revendre {units / sell.Quantity} lot(s) de {sell.Quantity} à {sellPrice:N0} k.",
                        "Revente à 1 K sous le prix du lot relevé en HDV. Vérifiez les quantités disponibles dans Dofus."],
                    buy.ObservedAtUtc <= sell.ObservedAtUtc ? buy.ObservedAtUtc : sell.ObservedAtUtc, compatibleDailyUnits) with
                    { TradingLots = new(buy.Quantity, buy.Price, sell.Quantity, sellPrice) });
            }

            if (median is not { } comparison)
                continue;

            double medianLotPrice = comparison.UnitPrice * buy.Quantity;
            double sellPriceForLot = ResalePrice(medianLotPrice);
            long? compatibleUnits = demand?.CompatibleUnits(buy.Quantity, sellPriceForLot);
            if (sellPriceForLot <= buy.Price || compatibleUnits < buy.Quantity)
                continue;

            rows.Add(Build($"history-{item.Type}-{item.Id}-{buy.Quantity}", "achat-vente", "Prix sous l’historique", item,
                buy.Quantity, sellPriceForLot, buy.Price, history, server,
                ["Achat et revente au même format. Le seuil de revente est calculé à partir de la médiane sur 7 jours, avec 1 K déduit par lot."],
                buy.ObservedAtUtc, compatibleUnits) with
                {
                    TradingLots = new(buy.Quantity, buy.Price, buy.Quantity, sellPriceForLot, HistoricalResale: true),
                    HistoricalPrice = new(buy.Price, medianLotPrice, comparison.LastAtOrAboveUtc)
                });
        }
    }

    private static IReadOnlyList<StockResaleTarget> StockTargets(MarketItem item, long units,
        IReadOnlyList<MarketPriceObservation> lots, double? medianUnitPrice, TradingResaleDemand? demand)
    {
        List<StockResaleTarget> targets = [];
        foreach (int size in ResaleLotSizes)
        {
            if (size > units || (item.Type == MarketObjectType.Equipment && size != 1))
                continue;

            double? reference = medianUnitPrice * size ?? lots.FirstOrDefault(lot => lot.Quantity == size)?.Price;
            if (reference is not { } price)
                continue;

            double target = ResalePrice(price);
            if (demand?.CompatibleUnits(size, target) >= size || demand is null)
                targets.Add(new(size, target, medianUnitPrice.HasValue));
        }

        return targets;
    }

    private static double ResalePrice(double reference) => Math.Max(1, Math.Floor(reference) - ResaleDiscountPerLot);

    private sealed record MarketItem(long Id, string Name, int Level, MarketObjectType Type, bool VariantSensitive = false);
    private static ActivityRecommendation Build(string id, string module, string strategy, MarketItem item,
        long quantity, double? revenue, double? cost, GlobalSalesHistory? history, string server,
        IReadOnlyList<string> details, DateTime? priceAt = null, long? compatibleDailyUnits = null)
    {
        var key = module == "pierres-ame" && history is not null ? history.Market
            : new SalesMarketKey(server, item.Type, item.Id, module == "pierres-ame" ? GlobalSalesRepository.BossHistoryVariant(item.Id) : "");
        var now = DateTimeOffset.UtcNow;
        var weekly = history?.Period(7);
        var profitability = SalesKineticsCalculator.EvaluateProfitability(key, quantity, revenue, cost,
            compatibleDailyUnits.HasValue ? history?.Period(1) : weekly, now, MaximumSalesHistoryAge);
        ResaleQuota? quota = weekly is not null && weekly.Market == key && weekly.ObservedAtUtc <= now
            ? new(weekly.UnitsSold, weekly.ObservedAtUtc + MaximumSalesHistoryAge)
            : null;
        if (compatibleDailyUnits is > 0 && profitability.Kinetics.HasMeasuredDemand)
        {
            double days = (double)quantity / compatibleDailyUnits.Value;
            profitability = profitability with
            {
                Kinetics = profitability.Kinetics with { UnitsPerDay = compatibleDailyUnits, MarketAbsorptionDays = days },
                MarketDailyGainCapacity = profitability.Gain / days
            };
        }
        List<string> missing = [];
        if (revenue is null) missing.Add(module == "pierres-ame" ? "Prix de revente de cette capture manquant." : "Prix de revente manquant.");
        if (cost is null) missing.Add(module == "pierres-ame" ? "Prix de la pierre d’âme vide manquant." : "Prix d’achat manquant.");
        if (profitability.Kinetics.Status == SalesKineticsStatus.Missing) missing.Add(module == "pierres-ame"
            ? "Historique des ventes de cette âme manquant : cinétique inconnue."
            : "Historique global des ventes manquant : ouvrez le Cours du marché de cet objet dans Dofus.");
        else if (profitability.Kinetics.Status == SalesKineticsStatus.Outdated) missing.Add("Historique global trop ancien : actualisez le Cours du marché.");
        else if (profitability.Kinetics.Status == SalesKineticsStatus.NoObservedSales) missing.Add("Aucune vente observée sur les 7 derniers jours.");
        return new(id, module, strategy, item.Id, item.Name, item.Level, item.Type, quantity,
            revenue, cost, profitability, missing, details)
            { PriceUpdatedUtc = priceAt, UsesResaleVolumeCeiling = compatibleDailyUnits.HasValue, ResaleQuota = quota };
    }
    private static IReadOnlyDictionary<(long DofusDbId, int Quantity), MarketPriceObservation> PricesFor(MarketObjectType type, EquipmentProfitabilityContext market) =>
        type switch { MarketObjectType.Equipment => market.EquipmentPrices, MarketObjectType.Rune => market.RunePrices, _ => market.ResourcePrices };
    private static int Lcm(int a, int b)
    {
        if (a <= 0 || b <= 0) return 0;
        int x = a, y = b;
        while (y != 0) (x, y) = (y, x % y);
        long result = (long)(a / x) * b;
        return result > int.MaxValue ? 0 : (int)result;
    }
    public void Dispose() { _changes.Changed -= MarketChanged; _sales.Changed -= SalesChanged; }
}
