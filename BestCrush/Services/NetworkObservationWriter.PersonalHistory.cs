using BestCrush.Network.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

internal sealed partial class NetworkObservationWriter
{
    internal void PersistPersonalMarketHistory(byte[] body, DofusClientWindowKey? window,
        DateTime observedAt, NetworkCaptureLease lease)
    {
        if (window is null || !currentServerState.IsCaptureLeaseActive(lease)) return;
        try
        {
            using var services = serviceScopeFactory.CreateScope();
            var clients = services.ServiceProvider.GetService<CurrentDofusClientsState>();
            if (clients is null || ProductionTrackingScope.For(clients, window, lease.ServerName) is not { } scope ||
                PersonalMarketHistoryDecoder.Decode(body, observedAt) is not { } history ||
                !currentServerState.IsCaptureLeaseActive(lease)) return;
            services.ServiceProvider.GetService<GlobalSalesRepository>()?.MatchProductionHistory(lease.ServerName, scope, history, observedAt);
        }
        catch (Exception error)
        { logger.LogWarning(error, "Historique personnel des ventes indisponible pour le suivi des objectifs."); }
    }
}
