using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

public sealed partial class OverlayWorkspaceService
{
    private readonly SemaphoreSlim _indicatorGate=new(1,1);
    private EquipmentProfitabilityContext? _indicatorMarket;
    private string? _indicatorServer;
    private long _indicatorRevision,_loadedIndicatorRevision=-1;
    private readonly Dictionary<(string Server,long Id),int> _indicatorPopups=[];
    internal void RetainIndicatorPopup(OverlayPopup popup)
    {
        if(popup.MarketItemId is not {} id || popup.MarketServer is not {} server)return;
        lock(_indicatorPopups)_indicatorPopups[(server,id)]=_indicatorPopups.GetValueOrDefault((server,id))+1;
        if(string.Equals(server,ActiveGameServer,StringComparison.OrdinalIgnoreCase))_=PrepareEquipmentIndicatorsAsync();
    }
    internal void ReleaseIndicatorPopup(OverlayPopup popup)
    {
        if(popup.MarketItemId is not {} id || popup.MarketServer is not {} server)return;
        lock(_indicatorPopups)
        {
            var key=(server,id);var count=_indicatorPopups.GetValueOrDefault(key);
            if(count<=1)_indicatorPopups.Remove(key);else _indicatorPopups[key]=count-1;
        }
    }
    private long[] MissingSearchIndicatorIds(string server)
    {
        long[] watched;
        lock(_indicatorPopups)watched=_indicatorPopups.Keys.Where(key=>string.Equals(key.Server,server,StringComparison.OrdinalIgnoreCase)).Select(key=>key.Id).ToArray();
        return EquipmentSearch.MissingIndicatorIds(watched);
    }

    public async Task PrepareEquipmentIndicatorsAsync()
    {
        if(ActiveGameServer is not {} server || EquipmentSearch.AnalysisPending || MissingSearchIndicatorIds(server).Length==0)return;
        bool changed=false;
        try
        {
            if(!EquipmentSearch.CatalogueReady)await PrepareEquipmentSearchAsync();
            await _indicatorGate.WaitAsync(_stop.Token);
            try
            {
                if(server!=ActiveGameServer || EquipmentSearch.AnalysisPending)return;
                var ids=MissingSearchIndicatorIds(server);
                if(ids.Length==0)return;
                var revision=Volatile.Read(ref _indicatorRevision);
                using var scope=scopes.CreateScope();
                var services=scope.ServiceProvider;
                await using var db=await services.GetRequiredService<IDbContextFactory<BestCrushDbContext>>().CreateDbContextAsync(_stop.Token);
                var items=await db.Equipments.AsNoTracking().Where(item=>ids.Contains(item.DofusDbId))
                    .Include(item=>item.Characteristics).Include(item=>item.Recipe).ThenInclude(line=>line.Resource)
                    .Include(item=>item.EquipmentRecipe).ThenInclude(line=>line.IngredientEquipment)
                    .AsSplitQuery().ToArrayAsync(_stop.Token);
                var market=_indicatorMarket;
                if(market is null || server!=_indicatorServer || revision!=_loadedIndicatorRevision)
                {
                    market=await services.GetRequiredService<EquipmentProfitabilityService>().LoadContextAsync(server,_stop.Token,gameRunePricesOnly:true);
                    if(server!=ActiveGameServer || revision!=Volatile.Read(ref _indicatorRevision))return;
                    _indicatorMarket=market;_indicatorServer=server;_loadedIndicatorRevision=revision;
                }
                var pricing=services.GetRequiredService<MarketPriceService>();
                var craft=services.GetRequiredService<CraftCostService>();
                var runes=EquipmentSearch.Runes;
                var updates=await Task.Run(()=>items.ToDictionary(item=>item.DofusDbId,item=>OverlayMarketFreshness.FromMarket(item,runes,market,pricing,craft)),_stop.Token);
                if(server==ActiveGameServer && revision==Volatile.Read(ref _indicatorRevision))
                {EquipmentSearch.MergeIndicators(updates);changed=true;}
            }
            finally{_indicatorGate.Release();}
        }
        catch(OperationCanceledException) when(_stop.IsCancellationRequested){}
        catch(Exception error)
        {EquipmentSearch.IndicatorError="Relevés indisponibles · réessayez la recherche.";changed=true;logger.LogWarning(error,"Search market indicators unavailable");}
        finally{if(changed)Notify();}
    }
    private void SearchIndicatorPricesChanged(MarketDataChangedEventArgs change)
    {
        if(!string.Equals(change.ServerName,ActiveGameServer,StringComparison.OrdinalIgnoreCase))return;
        Interlocked.Increment(ref _indicatorRevision);
        EquipmentSearch.InvalidateIndicators(change);
        _=PrepareEquipmentIndicatorsAsync();
    }
}
