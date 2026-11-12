using System.Collections.Concurrent;
using System.Numerics;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

public sealed record StockOpportunityReport(string SourceId, DateTime StockUpdatedUtc, string Server,
    int RecipeCount, IReadOnlyList<EconomicOpportunity> Candidates, DateTime CalculatedUtc)
{
    public bool IncludesBank { get; init; }
    public int Quantity { get; init; } = 1;
    internal long MarketRevision { get; init; }
    internal LiveStockSource? StockSnapshot { get; init; }
    public string OriginFilter { get; init; } = "";
    public bool ProductCatalogUnavailable { get; init; }
}

public sealed class StockOpportunityService : IDisposable
{
    private readonly ItemsService _items;
    private readonly EquipmentProfitabilityService _profitability;
    private readonly StockCraftPlanner _planner;
    private readonly IOpportunityProvider[] _providers;
    private readonly MarketDataChangeNotifier? _marketChanges;
    private readonly IBestCrushSettingsProvider? _settings;
    private readonly IDataPriorityProvider? _priority;
    private readonly ActivityCatalogService? _activityCatalog;
    private readonly ILogger<StockOpportunityService>? _logger;
    private volatile bool _craftUsesRunePrices;
    private readonly SemaphoreSlim _scanGate = new(1,1);
    private readonly ConcurrentDictionary<string,long> _marketRevisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string,long> _craftMarketRevisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ReportKey,StockOpportunityReport> _reports = new();
    private readonly Dictionary<MarketKey,EquipmentProfitabilityContext> _markets = new();
    private IReadOnlyCollection<Equipment>? _catalog;
    private const int CacheLimit = 8;

    private sealed record ReportKey(LiveStockSource Source,string Server,int Quantity,string Origin,long Revision,
        CrushYieldEstimationMode? Estimation,double? Discount,DataPriority? Priority);
    private sealed record MarketKey(string Server,long Revision,bool IncludesCrushing,bool IncludesRunePrices,DataPriority? Priority);

    public StockOpportunityService(ItemsService items, EquipmentProfitabilityService profitability,
        StockCraftPlanner planner, IEnumerable<IOpportunityProvider> providers,
        MarketDataChangeNotifier? marketChanges = null, IBestCrushSettingsProvider? settings = null,
        IDataPriorityProvider? priority = null, ActivityCatalogService? activityCatalog = null,
        ILogger<StockOpportunityService>? logger = null)
    {
        _items=items;_profitability=profitability;_planner=planner;_providers=providers.ToArray();
        _marketChanges=marketChanges;_settings=settings;_priority=priority;
        _activityCatalog = activityCatalog;
        _logger = logger;
        if (_marketChanges is not null) _marketChanges.Changed+=OnMarketChanged;
    }

    public static bool CanUse(LiveStockSource? source, string? server) => source is not null &&
        !string.IsNullOrWhiteSpace(server) && string.Equals(source.ServerName,server,StringComparison.OrdinalIgnoreCase) &&
        source.Containers.Any(x=>x.Container=="inventory" && x.Complete && !x.Stale);

    public static bool HasSynchronizedBank(LiveStockSource? source) => source is not null &&
        source.Containers.Any(x=>x.Container=="bank" && x.Complete && !x.Stale);

    public void InvalidateMarket(string server,MarketObjectType? objectType=null)
    {
        _marketRevisions.AddOrUpdate(server,1,(_,revision)=>revision+1);
        if (objectType!=MarketObjectType.Rune || CraftUsesRunePrices)
            _craftMarketRevisions.AddOrUpdate(server,1,(_,revision)=>revision+1);
    }
    public bool CraftUsesRunePrices => _craftUsesRunePrices;
    private long Revision(string server,string origin) => (origin=="craft" ? _craftMarketRevisions : _marketRevisions).GetValueOrDefault(server);
    internal bool IsMarketCurrent(StockOpportunityReport report) => report.MarketRevision==Revision(report.Server,report.OriginFilter);
    private void OnMarketChanged(object? sender,MarketDataChangedEventArgs args) => InvalidateMarket(args.ServerName,args.ObjectType);

    public async Task<StockOpportunityReport> ScanAsync(LiveStockSource source,string server,int quantity,
        CancellationToken cancellationToken,string originFilter="",bool useCache=false,bool forceRefresh=false)
    {
        if (!CanUse(source,server)) throw new InvalidOperationException("Inventaire synchronisé du même serveur requis.");
        if (quantity is <1 or >1000) throw new ArgumentOutOfRangeException(nameof(quantity));
        string origin=originFilter.Trim().ToLowerInvariant();
        string normalizedServer=server.ToUpperInvariant();
        // All SQL reads and CPU work run off the renderer. The shared scoped EF
        // context is still serialized across cancellation and module navigation.
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (forceRefresh) { _catalog=null;InvalidateMarket(server); }
            long revision=Revision(normalizedServer,origin);
            var key=new ReportKey(source,normalizedServer,quantity,origin,revision,
                origin=="craft" ? null : _settings?.CrushYieldEstimationMode,
                origin=="craft" ? null : _settings?.CrushValueDiscountPercent,_priority?.Priority);
            if (useCache && !forceRefresh && _reports.TryGetValue(key,out var cached)) return cached;
            return await Task.Run(async()=>
            {
                var catalog=useCache && _catalog is not null ? _catalog : await _items.GetEquipmentsAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (useCache) _catalog=catalog;
                bool includesCrushing=origin!="craft";
                ProductCraftRecipe[] productRecipes = [];
                bool productCatalogUnavailable = false;
                if (origin != "concassage" && _activityCatalog is not null)
                {
                    try { productRecipes = await _activityCatalog.GetProductCraftsAsync(cancellationToken); }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        productCatalogUnavailable = true;
                        _logger?.LogWarning(ex, "Catalogue des produits fabriqués indisponible.");
                    }
                }
                bool needsRunePrices = productRecipes.Any(recipe => recipe.Product.MarketType == MarketObjectType.Rune ||
                    recipe.Ingredients.Any(ingredient => ingredient.MarketType == MarketObjectType.Rune));
                if (needsRunePrices) _craftUsesRunePrices = true;
                var marketKey=new MarketKey(normalizedServer,revision,includesCrushing,needsRunePrices,_priority?.Priority);
                EquipmentProfitabilityContext market;
                if (!useCache || forceRefresh || !_markets.TryGetValue(marketKey,out market!))
                {
                    market=await _profitability.LoadContextAsync(server,cancellationToken,includesCrushing,needsRunePrices);
                    if (useCache) StoreBounded(_markets,marketKey,market);
                }
                // An absent/partial/stale bank never contributes retained rows.
                var stock=source.Containers.Where(x=>x.Complete && !x.Stale && x.Container is "bank" or "inventory")
                    .SelectMany(x=>x.Items).Where(x=>x.ItemId<=(ulong)long.MaxValue).GroupBy(x=>(long)x.ItemId)
                    .ToDictionary(x=>x.Key,x=>x.Aggregate(BigInteger.Zero,(sum,item)=>sum+item.Quantity));
                List<StockCraftPlan> plans=[];
                foreach(var equipment in catalog)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!equipment.Recipe.Any(x=>stock.GetValueOrDefault(x.Resource.DofusDbId)>0) &&
                        !equipment.EquipmentRecipe.Any(x=>stock.GetValueOrDefault(x.IngredientEquipment.DofusDbId)>0)) continue;
                    plans.Add(_planner.Calculate(equipment,quantity,stock,market));
                }
                foreach (var recipe in productRecipes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (recipe.Ingredients.GroupBy(ingredient => ingredient.ItemId)
                        .Any(group => group.Sum(ingredient => (long)ingredient.Count) * quantity > 250_000))
                        continue;
                    plans.Add(_planner.Calculate(recipe, quantity, stock, market));
                }
                var context=new OpportunityScanContext(server,plans,market);
                List<EconomicOpportunity> candidates=[];
                foreach(var provider in _providers.Where(x=>origin.Length==0 || x.Origin==origin))
                    candidates.AddRange(await provider.EvaluateAsync(context,cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                var report=new StockOpportunityReport(source.Id,source.UpdatedUtc,server,plans.Count,candidates,DateTime.UtcNow)
                { IncludesBank=HasSynchronizedBank(source),OriginFilter=origin,StockSnapshot=source,Quantity=quantity,
                    MarketRevision=revision,ProductCatalogUnavailable=productCatalogUnavailable };
                // Never cache a calculation overtaken by incoming market data.
                if (useCache && revision==Revision(normalizedServer,origin)) StoreBounded(_reports,key,report);
                return report;
            },cancellationToken);
        }
        finally { _scanGate.Release(); }
    }

    private static void StoreBounded<TKey,TValue>(Dictionary<TKey,TValue> cache,TKey key,TValue value) where TKey:notnull
    {
        cache[key]=value;
        while(cache.Count>CacheLimit)cache.Remove(cache.Keys.First());
    }

    public void Dispose()
    {
        if (_marketChanges is not null) _marketChanges.Changed-=OnMarketChanged;
    }
}
