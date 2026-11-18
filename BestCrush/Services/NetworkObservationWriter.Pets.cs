using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Network.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BestCrush.Services;

internal sealed partial class NetworkObservationWriter
{
    internal async Task PersistPetPricesAsync(byte[] body, DateTime at, NetworkCaptureLease lease, CancellationToken token)
    {
        if (!settings.EquipmentCaptureEnabled || !currentServerState.IsCaptureLeaseActive(lease) ||
            PetMarketDecoder.Decode(body) is not { } snapshot) return;
        using var scope = serviceScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BestCrushDbContext>();
        bool knownPet = await db.Equipments.AnyAsync(item => item.DofusDbId == snapshot.ItemId &&
            (item.Type == EquipmentType.Pet || item.Type == EquipmentType.Petsmount), token);
        if (knownPet && currentServerState.IsCaptureLeaseActive(lease))
            scope.ServiceProvider.GetService<GlobalSalesRepository>()?.SavePetPrices(lease.ServerName, snapshot, new(at));
    }
}
