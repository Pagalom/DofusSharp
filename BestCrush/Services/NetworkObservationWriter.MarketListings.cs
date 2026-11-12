using BestCrush.Network.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

internal sealed partial class NetworkObservationWriter
{
    internal void PersistMarketListing(MarketListingCreatedObservation listing, DateTime observedAtUtc,
        NetworkCaptureLease lease)
    {
        if (!currentServerState.IsCaptureLeaseActive(lease))
            return;
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            scope.ServiceProvider.GetService<GlobalSalesRepository>()?
                .SaveListing(lease.ServerName, listing, new DateTimeOffset(observedAtUtc));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mise en vente non enregistrée pour l’objet {ItemId}.", listing.ItemId);
        }
    }
}
