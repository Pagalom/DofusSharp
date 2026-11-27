using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using BestCrush.Domain;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

public sealed partial class OverlayWorkspaceService
{
    public bool CanChooseInventoryRoll => Profitability is { Item: Equipment } focused &&
        InventoryCopyKeys(focused.Item.DofusDbId, focused.Server).Count > 0;

    private HashSet<string> InventoryCopyKeys(long? itemId, string? server)
    {
        if (TargetWindow is not { } window || stock?.ForWindow(window) is not { } source || server is null ||
            !string.Equals(source.ServerName, server, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ActiveGameServer, server, StringComparison.OrdinalIgnoreCase)) return [];
        return source.Containers.Where(c => c.Container == "inventory" && c.Complete && !c.Stale)
            .SelectMany(c => c.Items).Where(i => i.Uid > 0 && i.Quantity > 0 && i.ItemId is > 0 and <= long.MaxValue &&
                (itemId is null || i.ItemId == (ulong)itemId.Value))
            .Select(i => ForgemagedEquipmentStock.InstanceKey(server, (long)i.ItemId, i.Uid)).ToHashSet();
    }

    public IReadOnlyList<OverlayOwnedChoice> InventoryChoices(long? itemId = null)
    {
        var server = itemId is null ? OwnedServer : Profitability?.Server;
        if (!string.Equals(OwnedServer, server, StringComparison.OrdinalIgnoreCase)) return [];
        var keys = InventoryCopyKeys(itemId, server);
        return Owned.Where(choice => choice.Item.Container == "inventory" && keys.Contains(choice.Item.Key) &&
            (itemId is null || choice.Item.Equipment.DofusDbId == itemId)).ToArray();
    }

    internal void ObserveCrushedEquipment(IReadOnlyList<NetworkCrushResultLine> lines, NetworkCaptureLease lease,
        DofusClientWindowKey? verifiedWindow)
    {
        if (_item is null || Profitability is null || verifiedWindow is null || TargetWindow != verifiedWindow ||
            !servers.IsCaptureLeaseActive(lease) || !string.Equals(_server, lease.ServerName, StringComparison.OrdinalIgnoreCase)) return;
        if (!lines.Any(line => line.EquipmentDofusDbId == _item.DofusDbId &&
            (_instanceIdentity is null || line.ObservedItem is { ItemUid: > 0 } item &&
                ForgemagedEquipmentStock.InstanceKey(lease.ServerName, line.EquipmentDofusDbId, item.ItemUid) == _instanceIdentity))) return;
        _wasCrushed = true;
        Profitability = Profitability with { WasCrushed = true };
        Notify();
    }

    public async Task FocusItemAsync(long id,string? name=null,MarketObjectType? type=null,string? server=null)
    {
        var intent=Interlocked.Increment(ref _focusIntent);
        using var scope=scopes.CreateScope();var services=scope.ServiceProvider;
        IItem? item=await services.GetRequiredService<ItemsService>().GetEquipmentAsync(id,_stop.Token);
        if(item is null)
        {
            var db=services.GetRequiredService<BestCrushDbContext>();
            if(type==MarketObjectType.Rune)item=await db.Runes.AsNoTracking().FirstOrDefaultAsync(r=>r.DofusDbId==id,_stop.Token);
            else item=await db.Resources.AsNoTracking().FirstOrDefaultAsync(r=>r.DofusDbId==id,_stop.Token);
            item ??=new CraftedProduct(id,name??$"Objet {id}",0,null,type??MarketObjectType.Resource,0,"Objet",null,null);
        }
        if(intent==FocusVersion) await FocusAsync(item,server);
    }
    internal async Task FocusTooltipAsync(Equipment equipment)
    {
        var intent=Interlocked.Increment(ref _focusIntent);var window=TargetWindow;var server=ActiveGameServer;
        if(window is not { } target || server is null || stock.ForWindow(target) is not { } source ||
            !string.Equals(source.ServerName,server,StringComparison.OrdinalIgnoreCase))
        {await FocusAsync(equipment);return;}
        using var scope=scopes.CreateScope();var s=scope.ServiceProvider;
        var rows=await ForgemagedEquipmentStock.LoadAsync(source,server,s.GetRequiredService<ForgemagingSessionStore>(),
            s.GetRequiredService<ForgemagingDataService>(),s.GetRequiredService<ActivityCatalogService>(),
            s.GetRequiredService<CoefficientService>(),s.GetRequiredService<IBestCrushSettingsProvider>(),true,_stop.Token,equipment.DofusDbId);
        if(intent!=FocusVersion || TargetWindow!=window || ActiveGameServer!=server || !ReferenceEquals(source,stock.ForWindow(target)))return;
        if(ResolveTooltipInstance(rows,_instanceIdentity) is { } owned){await FocusOwnedAsync(owned,server);return;}
        if(rows.Length==0){await FocusAsync(equipment,server);return;}
        // Several copies cannot be identified from a tooltip title alone.
        Owned=rows.Select(row=>new OverlayOwnedChoice(row)).ToArray();OwnedServer=server;
        await FocusCoreAsync(equipment,server,null,null,null,"Jet réel indisponible",0,true);
    }
    internal static OwnedForgemagingEquipment? ResolveTooltipInstance(IReadOnlyList<OwnedForgemagingEquipment> rows,string? currentKey) =>
        rows.FirstOrDefault(row=>row.Key==currentKey) ?? (rows.Count==1 && rows[0].Quantity==1?rows[0]:null);
    private async Task<(double? Cost,IReadOnlyList<OverlayPriceLine> Lines)> ProductRecipeAsync(IItem item,
        EquipmentProfitabilityContext context,MarketPriceService pricing,IServiceProvider services)
    {
        var recipes=await services.GetRequiredService<ActivityCatalogService>().GetProductCraftsAsync([item.DofusDbId],_stop.Token);
        var recipe=recipes.FirstOrDefault(r=>r.Product.DofusDbId==item.DofusDbId);
        if(recipe is null)return(null,[]);
        var lines=recipe.Ingredients.Select(r=>{
            var purchase=pricing.CalculateMinimumPurchaseCost(r.ItemId,r.Count,StockCraftPlanner.PricesFor(context,r.MarketType));
            return new OverlayPriceLine(r.Name,r.Count,purchase?.TotalCost,purchase is {PurchasedQuantity:>0}?(double)purchase.TotalCost/purchase.PurchasedQuantity:null,
                purchase?.SurplusQuantity>0?$"{purchase.PurchasedQuantity} achetés · {purchase.SurplusQuantity} conservés":null,
                purchase is null || purchase.Freshness!=DataFreshness.Fresh,r.ItemId)
                {ObservedAtUtc=OverlayMarketFreshness.Oldest(purchase?.UsedObservations)};
        }).ToArray();
        return(lines.All(l=>l.Total.HasValue)?lines.Sum(l=>l.Total):null,lines);
    }
    private static EquipmentProfitabilityResult AddDisplayScenarios(EquipmentProfitabilityResult result,
        IReadOnlyDictionary<Characteristic,double> roll,EquipmentProfitabilityContext context,
        MarketPriceService pricing,IServiceProvider services)
    {
        if(result.Coefficient is not { } coefficient)return result;
        var crush=services.GetRequiredService<CrushService>();
        var settings=services.GetRequiredService<IBestCrushSettingsProvider>();
        var scenarios=result.Scenarios.ToList();
        foreach(var focus in new Characteristic?[]{null}.Concat(roll.Where(line=>line.Value>0).Select(line=>(Characteristic?)line.Key)))
        {
            if(scenarios.Any(s=>s.FocusedCharacteristic==focus))continue;
            var runes=focus is { } stat?crush.GetFocusedCrushResult(new(roll),stat,result.Equipment.Level,coefficient.CoefficientPercent/100):
                crush.GetCrushResult(new(roll),result.Equipment.Level,coefficient.CoefficientPercent/100);
            Dictionary<Rune,MarketValueResult> values=[];
            foreach(var rune in runes)
                if(pricing.CalculateValue(rune.Key.DofusDbId,rune.Value,context.RunePrices) is { } value)values[rune.Key]=value;
            scenarios.Add(new EquipmentProfitabilityScenario(result.Equipment,coefficient,result.EquipmentCost,result.CraftCost,
                null,null,null,null,focus,runes,values){AdjustedRuneValue=values.Values.Sum(v=>v.Value)*settings.CrushValueMultiplier,
                DiscountPercent=settings.CrushValueDiscountPercent});
        }
        return result with{Scenarios=scenarios};
    }

}
