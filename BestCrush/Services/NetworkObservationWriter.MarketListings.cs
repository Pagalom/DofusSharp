using BestCrush.Network.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

internal sealed partial class NetworkObservationWriter
{
    internal ProductionListingEvidence? ListingEvidence(DofusClientWindowKey? window, ulong uid, NetworkCaptureLease lease)
    {
        if (window is null || !currentServerState.IsCaptureLeaseActive(lease)) return null;
        using var scope = serviceScopeFactory.CreateScope();
        var clients = scope.ServiceProvider.GetService<CurrentDofusClientsState>();
        var stock = scope.ServiceProvider.GetService<LiveStockService>()?.ForWindow(window.Value);
        if (clients is null || stock is null || !string.Equals(stock.ServerName, lease.ServerName, StringComparison.OrdinalIgnoreCase) ||
            ProductionTrackingScope.For(clients, window, lease.ServerName) is not { } player) return null;
        var items = stock.Containers.Where(c => c.Container == "inventory" && c.Complete && !c.Stale)
            .SelectMany(c => c.Items).Where(i => i.Uid == uid).ToArray();
        return items is [var item] ? ProductionListingReader.FromStock(player, item) : null;
    }

    internal void PersistMarketListing(MarketListingCreatedObservation listing, DateTime observedAtUtc,
        NetworkCaptureLease lease, ProductionListingEvidence? evidence = null)
    {
        if (!currentServerState.IsCaptureLeaseActive(lease))
            return;
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            scope.ServiceProvider.GetService<GlobalSalesRepository>()?
                .SaveListing(lease.ServerName, listing, new DateTimeOffset(observedAtUtc), evidence);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mise en vente non enregistrée pour l’objet {ItemId}.", listing.ItemId);
        }
    }
}
