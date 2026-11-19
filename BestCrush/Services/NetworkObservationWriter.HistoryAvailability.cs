using BestCrush.Network.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace BestCrush.Services;

internal sealed partial class NetworkObservationWriter
{
    internal void PersistHistoryAvailability(byte[] body, DateTime observedAtUtc, NetworkCaptureLease lease)
    {
        if (!settings.ResourceCaptureEnabled || !currentServerState.IsCaptureLeaseActive(lease) ||
            MarketMeanPriceDecoder.Decode(body) is not { } snapshot) return;
        using var scope = serviceScopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetService<GlobalSalesRepository>();
        if (currentServerState.IsCaptureLeaseActive(lease))
            repository?.SaveHistoryAvailability(lease.ServerName, snapshot, new DateTimeOffset(observedAtUtc));
    }
}
