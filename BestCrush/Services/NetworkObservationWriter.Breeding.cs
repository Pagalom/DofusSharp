using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Network.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

internal sealed partial class NetworkObservationWriter
{
    private readonly HashSet<long> _mountMarketItems = [];

    internal void PersistBreedingMarketLevels(byte[] body, DateTime observedAtUtc, NetworkCaptureLease lease)
    {
        if (!settings.ResourceCaptureEnabled || !currentServerState.IsCaptureLeaseActive(lease) ||
            BreedingMarketDecoder.Decode(body) is not { } snapshot)
            return;
        using var scope = serviceScopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetService<GlobalSalesRepository>();
        if (currentServerState.IsCaptureLeaseActive(lease))
            repository?.SaveBreedingPrices(lease.ServerName, snapshot, new DateTimeOffset(observedAtUtc));
    }

    private async Task<bool> IsBreedingMountMarketAsync(long id, CancellationToken token)
    {
        if (_mountMarketItems.Contains(id)) return true;
        return (await BreedingMarketMetadataAsync(id, token))?.TypeId is 331 or 332 or 333;
    }

    private async Task<MarketObjectType?> ResolveBreedingResourceAsync(long id, BestCrushDbContext context,
        NetworkCaptureLease lease, CancellationToken token)
    {
        if (!settings.ResourceCaptureEnabled || !currentServerState.IsCaptureLeaseActive(lease))
            return null;
        var item = await BreedingMarketMetadataAsync(id, token);
        if (item is null || (item.TypeId is not (326 or 331 or 332 or 333) && item.Id != BreedingRefillService.CaptureNetId) ||
            !currentServerState.IsCaptureLeaseActive(lease))
            return null;

        context.Resources.Add(new Resource(item.Id) { Name = item.Name, Level = item.Level, DofusDbIconId = item.IconId });
        await context.SaveChangesAsync(token);
        _marketObjectTypes[id] = MarketObjectType.Resource;
        return MarketObjectType.Resource;
    }

    private async Task<ActivityItem?> BreedingMarketMetadataAsync(long id, CancellationToken token)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetService<ActivityCatalogService>();
        if (catalog is null) return null;
        try
        {
            var item = (await catalog.GetItemsAsync([id], token)).FirstOrDefault(item => item.Id == id);
            if (item?.TypeId is 331 or 332 or 333) _mountMarketItems.Add(id);
            return item;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Type de l’objet d’élevage {ItemId} indisponible.", id);
            return null;
        }
    }
}
