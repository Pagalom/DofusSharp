using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Network.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

internal sealed partial class NetworkObservationWriter
{
    private async Task<MarketObjectType?> ResolveEmptySoulStoneAsync(long itemId, BestCrushDbContext context,
        NetworkCaptureLease lease, CancellationToken token)
    {
        if (!settings.ResourceCaptureEnabled || !currentServerState.IsCaptureLeaseActive(lease)) return null;
        using var scope = serviceScopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetService<ActivityCatalogService>();
        if (catalog is null) return null;
        try
        {
            var stone = (await catalog.GetEmptySoulStonesAsync(token)).SingleOrDefault(x => x.Id == itemId);
            if (stone is null || !currentServerState.IsCaptureLeaseActive(lease)) return null;
            context.Resources.Add(new Resource(stone.Id) { Name = stone.Name });
            await context.SaveChangesAsync(token);
            _marketObjectTypes[itemId] = MarketObjectType.Resource;
            return MarketObjectType.Resource;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogDebug(ex,"Identification de la pierre vide {ItemId} indisponible.",itemId); return null; }
    }

    private async Task<bool> PersistBossSoulHistoryAsync(GlobalMarketHistory history, DateTime observedAtUtc,
        NetworkCaptureLease lease, CancellationToken token)
    {
        if (!settings.ResourceCaptureEnabled || !currentServerState.IsCaptureLeaseActive(lease)) return false;
        using var scope = serviceScopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetService<ActivityCatalogService>();
        var repository = scope.ServiceProvider.GetService<GlobalSalesRepository>();
        if (catalog is null || repository is null) return false;
        try
        {
            var item = await ResolveBossSoulItemAsync(catalog, repository, lease.ServerName, history.ItemId, token);
            if (item is null || !(await catalog.GetBossesAsync(token)).Any(x => x.Id == item.BossId) ||
                !currentServerState.IsCaptureLeaseActive(lease)) return false;
            var key = new SalesMarketKey(lease.ServerName,MarketObjectType.Resource,item.ItemId,
                GlobalSalesRepository.BossHistoryVariant(item.BossId));
            var snapshot = new GlobalSalesHistory(key,new DateTimeOffset(observedAtUtc),
                history.Last24Hours.Select(x => new GlobalSalePoint(x.UnitPrice,x.UnitsSold,x.RecordedAtUtc)).ToArray(),
                history.Last30Days.Select(x => new GlobalSalePoint(x.UnitPrice,x.UnitsSold,x.RecordedAtUtc)).ToArray());
            if (repository.SaveHistory(snapshot)) await debugWriter.WriteEventDebugAsync(observedAtUtc,
                $"[SOUL-HISTORY] ItemId={item.ItemId} BossId={item.BossId} units7d={snapshot.Period(7).UnitsSold}",token);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogDebug(ex,"Historique agrégé de l’âme {ItemId} indisponible.",history.ItemId); return false; }
    }
}
