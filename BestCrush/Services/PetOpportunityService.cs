using System.Numerics;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

public sealed record OwnedPet(PetSpecies Species,ulong Uid,ulong Quantity,string Container,PetItemReading? Reading);
public sealed record PetSalePlan(int Level,long SalePrice,PetNutritionPlan Nutrition,double? Margin,
    long? RemainingListings,bool GlobalDemandOnly);
public sealed record PetAssessment(PetSpecies Species,OwnedPet? Owned,PetMarketOffer? Purchase,
    long? CurrentResale,IReadOnlyList<PetSalePlan> Options,PetSalePlan? Best,string? Issue)
{
    public long AcquisitionCost => Purchase?.Price ?? 0;
}
public sealed record PetOpportunityReport(string Server,IReadOnlyList<PetAssessment> Owned,
    IReadOnlyList<PetAssessment> Purchases,IReadOnlyList<BreedingPriceCheck> Checks,
    IReadOnlyDictionary<long,BigInteger> Inventory,IReadOnlyDictionary<long,BigInteger> Bank,
    bool InventoryKnown,bool BankKnown,bool ProfessionsKnown,bool RecipesKnown,int FoodCount,int QuotedFoods)
{
    public LiveStockSource? Stock { get; init; }
    public long? BudgetKamas { get; init; }
    public IReadOnlyList<BreedingPriceCheck> OwnedChecks { get; init; } = [];
    public IReadOnlyList<BreedingPriceCheck> FoodPriceChecks {get;init;}=[];
    public IReadOnlyList<BreedingPriceCheck> PetPriceChecks {get;init;}=[];
    public IReadOnlySet<long> KnownFoodExperienceItems {get;init;}=new HashSet<long>();
    public PetFoodValue? BestFoodValue { get; init; }
    public IReadOnlyDictionary<long,string> FoodPriceTooltips { get; init; } = new Dictionary<long,string>();
    public string FoodPriceTooltip(long id) => FoodPriceTooltips.GetValueOrDefault(id) ?? "Aucun prix connu";
    public IReadOnlyList<PetAssessment> OverlayPurchases { get; init; } = [];
    public IReadOnlySet<long> RelevantPriceItems { get; init; } = new HashSet<long>();
    public bool AnalysisComplete { get; init; } = true;
    public bool OwnedAnalysisComplete { get; init; } = true;
}

internal sealed partial class PetOpportunityService(IDbContextFactory<BestCrushDbContext> database,IDataPriorityProvider priority,
    ActivityCatalogService catalog,LiveStockService liveStock,CurrentServerState servers,
    CurrentWindowProfessionsState professions,GlobalSalesRepository history,ILogger<PetOpportunityService> logger,
    PetFoodCatalogService? foodCatalog=null, BestCrushSettingsService? settings=null,
    PetFoodPreferences? foodPreferences=null)
{
    private string? _nutritionKey;
    private PetNutritionCache _nutritionCache=new();
    internal async Task<PetOpportunityReport?> LoadAsync(DofusClientWindowKey? window,string? viewServer,CancellationToken token,
        Func<PetOpportunityReport,Task>? publish=null)
    {
        var lease=window is { } key ? servers.GetWindowCaptureLease(key) : null;
        string? server=lease?.ServerName ?? viewServer;
        if (string.IsNullOrWhiteSpace(server)) return null;
        var foodSelection=foodPreferences?.Selection ?? PetFoodSelection.Default;
        var overview=await LoadOverviewAsync(window,server,token);
        if (publish is not null) await publish(overview);
        token.ThrowIfCancellationRequested();
        var species=(await catalog.GetPetSpeciesAsync(token)).ToDictionary(pet=>pet.Id);
        var foods=foodCatalog is null ? await catalog.GetPetFoodsAsync(token) : await foodCatalog.GetFoodsAsync(token);
        var knownFoodExperienceItems=foods.Where(food=>double.IsFinite(food.Experience) && food.Experience>0).Select(food=>food.Id).ToHashSet();
        foods=foods.Where(food=>!foodSelection.Skipped.Contains(food.Id)).ToArray();
        var source=window is { } stockWindow ? liveStock.ForWindow(stockWindow) : null;
        var containers=string.Equals(source?.ServerName,server,StringComparison.OrdinalIgnoreCase)
            ? source!.Containers.Where(c=>c.Complete && !c.Stale && c.Container is "inventory" or "bank").ToArray() : [];
        Dictionary<long,BigInteger> Stock(string container) => containers.Where(c=>c.Container==container).SelectMany(c=>c.Items)
            .Where(item=>item.ItemId<=long.MaxValue).GroupBy(item=>(long)item.ItemId)
            .ToDictionary(group=>group.Key,group=>group.Aggregate(BigInteger.Zero,(sum,item)=>sum+item.Quantity));
        var inventory=Stock("inventory");var bank=Stock("bank");
        var stock=foodSelection.PlanningStock(inventory.Concat(bank).GroupBy(pair=>pair.Key)
            .ToDictionary(group=>group.Key,group=>group.Aggregate(BigInteger.Zero,(sum,pair)=>sum+pair.Value)));
        var owned=containers.SelectMany(c=>c.Items.Where(item=>item.ItemId<=long.MaxValue && species.ContainsKey((long)item.ItemId))
                .Select(item=>new OwnedPet(species[(long)item.ItemId],item.Uid,item.Quantity,c.Container,
                    PetMarketDecoder.Read(item.EffectWire,(ulong)species[(long)item.ItemId].TypeId)))).ToArray();
        var levels=professions.ForWindow(window,server);
        bool recipesKnown=true;
        ProductCraftRecipe[] recipes=[];
        if (levels is {Count:>0})
        {
            try
            {
                var professionsByName=levels.GroupBy(level=>level.Name,StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group=>group.Key,group=>group.Max(level=>level.Level),StringComparer.OrdinalIgnoreCase);
                recipes=(await catalog.GetPetRecipesAsync(foods.Where(food=>food.Craftable).Select(food=>food.Id).ToArray(),token))
                    .Where(recipe=>recipe.Product.Job is { } job && recipe.Product.JobLevel is { } required &&
                        professionsByName.TryGetValue(job,out int actual) && actual>=required).ToArray();
            }
            catch(Exception ex) when (!token.IsCancellationRequested)
            {
                recipesKnown=false;
                logger.LogWarning(ex,"Recettes de nourriture indisponibles sur {Server}.",server);
            }
        }
        await using var db=await database.CreateDbContextAsync(token);
        var pricing=new MarketPriceService(db,priority);
        async Task<IReadOnlyDictionary<(long,int),MarketPriceObservation>> Quotes(MarketObjectType type) =>
            await pricing.GetLatestObservationsForServerAsync(type,server,token,excludeUnavailableLots:true);
        var all=new EquipmentProfitabilityContext{ResourcePrices=await Quotes(MarketObjectType.Resource),
            RunePrices=await Quotes(MarketObjectType.Rune),EquipmentPrices=await Quotes(MarketObjectType.Equipment),Coefficients=[]};
        var now=DateTimeOffset.UtcNow;
        Dictionary<(long,int),MarketPriceObservation> Usable(IReadOnlyDictionary<(long,int),MarketPriceObservation> quotes) =>
            quotes.Where(pair=>pair.Value.ObservedAtUtc<=now.UtcDateTime && pair.Value.Price>0 && !pair.Value.IsCleared).ToDictionary();
        var market=new EquipmentProfitabilityContext{ResourcePrices=Usable(all.ResourcePrices),RunePrices=Usable(all.RunePrices),
            EquipmentPrices=Usable(all.EquipmentPrices),Coefficients=[]};
        var viableRecipes=recipes.Where(recipe=>recipe.Ingredients.All(i=>stock.GetValueOrDefault(i.ItemId)>=i.Count ||
            StockCraftPlanner.PricesFor(market,i.MarketType).Keys.Any(key=>key.DofusDbId==i.ItemId))).ToArray();
        var allPetQuotes=history.ReadPetPrices(server,now).Where(reading=>species.ContainsKey(reading.Snapshot.ItemId)).ToArray();
        var petQuotes=allPetQuotes.ToDictionary(reading=>reading.Snapshot.ItemId,reading=>reading.Snapshot.Offers);
        var listedToday=history.ReadListingTotals(server,now).ByItem;
        var listedWeek=history.ReadListingTotals(server,now,7).ByItem;
        var demand=history.ReadHistories(server).Where(sales=>sales.Market.ObjectType==MarketObjectType.Equipment &&
                sales.Market.Variant.Length==0 && species.ContainsKey(sales.Market.ItemId) && sales.ObservedAtUtc<=now &&
                now-sales.ObservedAtUtc<=BreedingMarketDemand.Lifetime)
            .ToDictionary(sales=>sales.Market.ItemId,sales=>new BreedingMarketDemand(sales,
                listedToday.GetValueOrDefault(sales.Market.ItemId),listedWeek.GetValueOrDefault(sales.Market.ItemId)));
        var checks=new List<BreedingPriceCheck>();
        foreach(var pet in owned.Where(pet=>pet.Reading is not null && !
            (petQuotes.GetValueOrDefault(pet.Species.Id) ?? []).Any(offer=>offer.Level==pet.Reading.Level && offer.Variant==pet.Reading.Variant)))
            checks.Add(new(pet.Species.Id,pet.Species.Name,null,$"Comparer la vente immédiate au niveau {pet.Reading!.Level}"));
        foreach(var pet in species.Values)
        {
            var reading=allPetQuotes.FirstOrDefault(quote=>quote.Snapshot.ItemId==pet.Id);
            if (reading is null || PetDataFreshness.Price(reading.ObservedAtUtc.UtcDateTime,now.UtcDateTime)==DataFreshness.Stale) checks.Add(new(pet.Id,pet.Name,reading?.ObservedAtUtc,
                owned.Any(row=>row.Species.Id==pet.Id) ? "Comparer un familier possédé par niveau" : "Comparer achat et revente par niveau"));
            if (!demand.ContainsKey(pet.Id)) checks.Add(new(pet.Id,pet.Name,null,"Ouvrir le Cours du marché pour relever les volumes globaux"));
        }
        long? budget = settings?.ProductionBudgetKamas is > 0 and var amount ? amount : null;
        // Reuse the resolved J/M quotes already loaded for this server; no extra queries or nutrition calculations.
        var foodPriceTooltips=market.ResourcePrices.Values.Concat(market.RunePrices.Values).Concat(market.EquipmentPrices.Values)
            .Where(q=>knownFoodExperienceItems.Contains(q.DofusDbId) && q.Quantity is 1 or 10 or 100 or 1000)
            .GroupBy(q=>q.DofusDbId).ToDictionary(g=>g.Key,g=>string.Join(Environment.NewLine,
                g.OrderBy(q=>q.Quantity).Select(q=>$"×{q.Quantity:N0} : {q.Price:N0} K")));
        overview=overview with {BestFoodValue=BestFoodValue(foods,market,budget),FoodPriceTooltips=foodPriceTooltips};
        var priceItems=foods.Select(f=>(f.Id,f.Name,f.MarketType)).Concat(viableRecipes.SelectMany(r=>r.Ingredients)
            .Select(i=>(Id:i.ItemId,i.Name,i.MarketType))).DistinctBy(i=>(i.Id,i.MarketType));
        var foodPriceChecks=priceItems.Select(item=>
        {
            var quotes=StockCraftPlanner.PricesFor(market,item.MarketType).Values.Where(q=>q.DofusDbId==item.Id).ToArray();
            var old=quotes.Where(q=>PetDataFreshness.Price(q.ObservedAtUtc,now.UtcDateTime)==DataFreshness.Stale)
                .MinBy(q=>q.ObservedAtUtc);
            return old is null ? null : new BreedingPriceCheck(item.Id,item.Name,old.ObservedAtUtc,"Actualiser le prix · dernier relevé conservé dans le calcul");
        }).OfType<BreedingPriceCheck>().OrderByDescending(c=>stock.GetValueOrDefault(c.ItemId)>0)
            .ThenBy(c=>c.LastObserved).ToArray();
        string nutritionKey=PetNutritionCache.Fingerprint(foods,viableRecipes,stock,market);
        if (_nutritionKey!=nutritionKey) { _nutritionKey=nutritionKey;_nutritionCache=new(); }
        var planner=new PetFoodPlanner(pricing,foods,viableRecipes,stock,market,_nutritionCache);
        var own=await Task.Run(()=>owned.Select(pet=>Assess(pet.Species,pet,null,
            petQuotes.GetValueOrDefault(pet.Species.Id) ?? [],planner,demand.GetValueOrDefault(pet.Species.Id),token)).ToArray(),token);
        if (publish is not null) await publish(overview with { Owned=own,OwnedAnalysisComplete=true });
        var report=await Task.Run(()=>
        {
            var purchases=new List<PetAssessment>();
            var priceReviewPlans=new List<PetAssessment>();
            var overlayPurchases=new List<PetAssessment>();
            foreach(var pet in species.Values)
            {
                token.ThrowIfCancellationRequested();
                var offers=petQuotes.GetValueOrDefault(pet.Id) ?? [];
                bool hasDemand=demand.GetValueOrDefault(pet.Id) is {} petDemand &&
                    offers.Any(offer=>petDemand.AtPrice(Math.Max(1,offer.Price-1)).Remaining>0);
                bool reviewPrice=allPetQuotes.FirstOrDefault(q=>q.Snapshot.ItemId==pet.Id) is {} last &&
                    PetDataFreshness.Price(last.ObservedAtUtc.UtcDateTime,now.UtcDateTime)==DataFreshness.Stale;
                // Reuse these assessments for refresh priority even if market volume needs a new reading.
                if(!hasDemand && !reviewPrice)continue;
                var candidates=offers.Where(offer=>PetExperience.LevelAt(offer.Experience)==offer.Level)
                    .DistinctBy(offer=>(offer.Level,offer.Experience,offer.Price,offer.Variant))
                    .Select(offer=>Assess(pet,null,offer,offers,planner,demand.GetValueOrDefault(pet.Id),token)).ToArray();
                if(reviewPrice)priceReviewPlans.AddRange(candidates);
                if(!hasDemand)continue;
                var profitable=candidates.Where(option=>option.Best?.Margin>0 && option.Best.RemainingListings is >0 &&
                    (budget is null || option.AcquisitionCost + option.Best.Nutrition.CashRequired <= budget)).ToArray();
                overlayPurchases.AddRange(profitable.GroupBy(option=>option.Purchase!.Level).Select(group=>group.MaxBy(option=>option.Best!.Margin)!));
                if (profitable.MaxBy(option=>option.Best!.Margin) is { } best)
                    purchases.Add(best);
            }
            return new PetOpportunityReport(server,own.OrderByDescending(row=>row.Best?.Margin).ToArray(),
                purchases.OrderByDescending(row=>row.Best?.RemainingListings is > 0).ThenByDescending(row=>row.Best?.Margin).ThenBy(row=>row.AcquisitionCost).Take(5).ToArray(),checks.DistinctBy(check=>check.ItemId).ToArray(),
                inventory,bank,containers.Any(c=>c.Container=="inventory"),containers.Any(c=>c.Container=="bank"),
                levels is {Count:>0},recipesKnown,foods.Length,foods.Count(food=>
                    StockCraftPlanner.PricesFor(market,food.MarketType).Keys.Any(key=>key.DofusDbId==food.Id))) { Stock=source,
                    PetPriceChecks=PetPriceReviews(species.Values,allPetQuotes.ToDictionary(q=>q.Snapshot.ItemId,q=>q.ObservedAtUtc),priceReviewPlans.Concat(own),now.UtcDateTime),
                    OverlayPurchases=overlayPurchases.GroupBy(p=>p.Purchase!.Level).OrderBy(group=>group.Key==1?0:1)
                        .ThenByDescending(group=>group.Max(p=>p.Best!.Margin)).Take(3)
                        .SelectMany(group=>group.OrderByDescending(p=>p.Best!.Margin).ThenBy(p=>p.AcquisitionCost).Take(3)).ToArray() };
        },token);
        var relevantPets = report.Owned.Concat(report.Purchases).ToArray();
        var relevant = relevantPets.Select(p => p.Species.Id).ToHashSet();
        var ownedIds = report.Owned.Select(p => p.Species.Id).ToHashSet();

        var affordable = report.Purchases.Select(p =>
        {
            long perPet = p.AcquisitionCost + (p.Best?.Nutrition.CashRequired ?? 0);
            return budget is { } available && p.Best is { } best && perPet > 0
                ? p with { Best = best with { RemainingListings = Math.Min(best.RemainingListings ?? long.MaxValue, available / perPet) } } : p;
        }).Where(p => p.Best?.RemainingListings is > 0).ToArray();
        report = report with { Purchases = affordable, BudgetKamas = budget,
            BestFoodValue=overview.BestFoodValue,FoodPriceTooltips=overview.FoodPriceTooltips,
            FoodPriceChecks=foodPriceChecks,KnownFoodExperienceItems=knownFoodExperienceItems,
            OwnedChecks=overview.OwnedChecks,
            RelevantPriceItems=species.Keys.Concat(foods.Select(f=>f.Id)).Concat(recipes.SelectMany(r=>r.Ingredients).Select(i=>i.ItemId)).ToHashSet(),
            Checks = report.Checks.Where(c => relevant.Contains(c.ItemId))
            .OrderByDescending(c => ownedIds.Contains(c.ItemId)).ToArray() };
        if(lease is { } active && !servers.IsCaptureLeaseActive(active))
            throw new InvalidOperationException("La session Dofus a changé pendant le calcul.");
        return report;
    }

    internal static PetAssessment Assess(PetSpecies species,OwnedPet? owned,PetMarketOffer? purchase,
        IReadOnlyList<PetMarketOffer> quotes,PetFoodPlanner planner,BreedingMarketDemand? demand,CancellationToken token)
    {
        var reading=owned?.Reading;
        if (owned is not null && reading is not {Experience:{}})
            return new(species,owned,null,null,[],null,"XP non reconnue : synchroniser l’inventaire ou la banque.");
        if(reading?.TradeRestricted==true)
            return new(species,owned,null,null,[],null,"Restriction d’échange relevée : vérifier sa disponibilité à la vente en jeu.");
        int level=reading?.Level ?? purchase!.Level;
        long experience=reading?.Experience ?? purchase!.Experience;
        string variant=reading?.Variant ?? purchase!.Variant;
        if(PetExperience.LevelAt(experience)!=level)
            return new(species,owned,purchase,null,[],null,"Le niveau et l’XP relevés ne correspondent pas : consulter de nouveau le familier.");
        long? current=quotes.Where(offer=>offer.Level==level && offer.Variant==variant)
            .Select(offer=>(long?)Math.Max(1,offer.Price-1)).Min();
        var options=new List<PetSalePlan>();
        foreach(var target in quotes.Where(offer=>offer.Level>=level && offer.Variant==variant)
            .GroupBy(offer=>offer.Level).Select(group=>group.MinBy(offer=>offer.Price)!))
        {
            token.ThrowIfCancellationRequested();
            int needed=(int)Math.Max(0,PetExperience.RequiredAtLevel(target.Level)-experience);
            int maximum=target.Level==100 ? int.MaxValue : (int)(PetExperience.RequiredAtLevel(target.Level+1)-1-experience);
            if(planner.Calculate(needed,token,maximum) is not { } nutrition) continue;
            if(PetExperience.LevelAt(experience+nutrition.Experience)!=target.Level) continue;
            long sale=Math.Max(1,target.Price-1);
            double? margin=nutrition.EconomicCost is { } cost
                ? sale-cost-(purchase?.Price ?? current) : null;
            options.Add(new(target.Level,sale,nutrition,margin,demand?.AtPrice(sale).Remaining,true));
        }
        var actionable=options.Any(option=>option.RemainingListings!=0) ? options.Where(option=>option.RemainingListings!=0) : options;
        var best=actionable.Where(option=>option.Nutrition.EconomicCost.HasValue)
            .OrderByDescending(option=>option.SalePrice-option.Nutrition.EconomicCost)
            .ThenBy(option=>option.Nutrition.CashRequired).ThenBy(option=>option.Level).FirstOrDefault()
            ?? options.OrderByDescending(option=>option.SalePrice-option.Nutrition.CashRequired).FirstOrDefault();
        string? issue=quotes.Count==0 ? "Offres HDV par niveau à relever." :
            current is null && owned is not null ? "Prix au niveau actuel manquant : comparaison avec une vente immédiate à compléter." :
            level<100 && !quotes.Any(offer=>offer.Level>level && offer.Variant==variant)
                ? "Aucune offre comparable à un niveau supérieur : gain du nourrissage non chiffrable." :
            best is null ? "Prix de nourriture ou ingrédients à relever." :
            best.Nutrition.EconomicCost is null ? "Valeur d’une partie du stock inconnue : gain incomplet." : null;
        return new(species,owned,purchase,current,options,best,issue);
    }
}
