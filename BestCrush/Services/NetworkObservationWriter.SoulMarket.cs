using BestCrush.Network.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

internal sealed partial class NetworkObservationWriter
{
    internal async Task PersistSoulMarketAsync(SoulMarketObservation market, DateTime observedAtUtc,
        NetworkCaptureLease lease, CancellationToken cancellationToken)
    {
        if (!settings.ResourceCaptureEnabled || !currentServerState.IsCaptureLeaseActive(lease) ||
            market.Offers.FirstOrDefault() is not { } first ||
            market.Offers.Any(offer => offer.BossId != first.BossId || offer.GroupId != first.GroupId)) return;
        using var scope = serviceScopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetService<ActivityCatalogService>();
        var repository = scope.ServiceProvider.GetService<GlobalSalesRepository>();
        if (catalog is null || repository is null) return;
        try
        {
            var item = await ResolveBossSoulItemAsync(catalog, repository, lease.ServerName, market.ItemId, cancellationToken);
            // The dedicated offer decoder already validates the item, boss and group fields in the game packet.
            // The capturable-boss catalogue still applies when the item catalogue omits a valid soul.
            if ((item is not null && (first.BossId != item.BossId || first.GroupId != item.GroupId)) ||
                !(await catalog.GetBossesAsync(cancellationToken)).Any(x => x.Id == first.BossId) ||
                !currentServerState.IsCaptureLeaseActive(lease)) return;
            if (repository.SaveSoulMarket(lease.ServerName, market, new DateTimeOffset(observedAtUtc)))
                await debugWriter.WriteEventDebugAsync(observedAtUtc,
                    $"[SOUL-MARKET] ItemId={market.ItemId} BossId={first.BossId} offers={market.Offers.Count} server={lease.ServerName}", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Prix de capture non enregistré pour l’objet {ItemId}.", market.ItemId); }
    }

    private async Task<BossSoulItem?> ResolveBossSoulItemAsync(ActivityCatalogService catalog,
        GlobalSalesRepository repository, string server, long itemId, CancellationToken token)
    {
        BossSoulItem? catalogItem = null;
        try
        {
            catalogItem = (await catalog.GetBossSoulItemsAsync(token)).SingleOrDefault(x => x.ItemId == itemId);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Catalogue des objets d’âme indisponible.");
        }
        return catalogItem ?? repository.ReadCapturedSoulItem(server, itemId);
    }
}
