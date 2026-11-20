using BestCrush.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

internal sealed partial class NetworkObservationWriter
{
    internal async Task PersistEquipmentAcquisitionAsync(ObservedEquipmentAcquisition observation,
        NetworkCaptureLease lease, CancellationToken token)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        if (!currentServerState.IsCaptureLeaseActive(lease) || services.GetService<ForgemagingSessionStore>() is not { } store)
            return;

        try
        {
            var db = services.GetRequiredService<BestCrushDbContext>();
            if (await db.Equipments.AsNoTracking().AnyAsync(item => item.DofusDbId == observation.ItemId, token) &&
                currentServerState.IsCaptureLeaseActive(lease))
            {
                var key = ForgemagedEquipmentStock.InstanceKey(lease.ServerName, observation.ItemId, observation.Uid);
                store.RecordAcquisition(lease.ServerName, observation.ItemId, key, observation.Acquisition);
                if (services.GetService<OverlayWorkspaceService>() is { } overlays)
                    _ = overlays.FocusAcquisitionAsync(observation, lease);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            logger.LogWarning(error, "Could not record the acquisition of equipment {ItemId}", observation.ItemId);
        }
    }
}
