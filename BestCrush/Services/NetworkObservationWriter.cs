using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

/// <summary>
/// Persists semantic network observations and updates application state.
/// Existing scope, cache, notification and ordering semantics are preserved.
/// </summary>
internal sealed class NetworkObservationWriter(
    IServiceScopeFactory serviceScopeFactory,
    CurrentServerState currentServerState,
    LastNetworkEquipmentState lastNetworkEquipmentState,
    IBestCrushSettingsProvider settings,
    MarketDataChangeNotifier marketDataChangeNotifier,
    NetworkDebugWriter debugWriter,
    Func<
        IReadOnlyList<NetworkCrushResultLine>,
        DateTime,
        string,
        CancellationToken,
        Task> applyNetworkCrushAsync,
    ILogger<DofusNetworkCaptureService> logger)
{
    private readonly Dictionary<
        long,
        MarketObjectType?> _marketObjectTypes = [];

    internal async Task PersistMarketAsync(
        MarketObservation market,
        DateTime observedAtUtc,
        NetworkCaptureLease lease,
        CancellationToken cancellationToken,
        bool allowEquipmentPriceRefresh = true)
    {
        string serverName = lease.ServerName;

        if (!currentServerState.IsCaptureLeaseActive(lease) ||
            market.ItemId == 0)
        {
            return;
        }

        using IServiceScope scope =
            serviceScopeFactory.CreateScope();

        BestCrushDbContext context =
            scope.ServiceProvider
                .GetRequiredService<BestCrushDbContext>();

        MarketObjectType? objectType =
            await ResolveMarketObjectTypeAsync(
                checked((long)market.ItemId),
                context,
                cancellationToken);

        if (!currentServerState.IsCaptureLeaseActive(lease))
            return;

        if (objectType ==
            MarketObjectType.Equipment)
        {
            lastNetworkEquipmentState.Set(
                checked((long)market.ItemId),
                serverName,
                observedAtUtc);

            if (!allowEquipmentPriceRefresh)
            {
                return;
            }
        }

        // Even an empty jzn is useful for focus: it identifies
        // the equipment currently consulted in the market.
        // Price persistence obviously requires an actual ladder.
        if (market.Offers.Count == 0 ||
            objectType is null ||
            !IsMarketCaptureEnabled(
                objectType.Value))
        {
            return;
        }

        MarketPriceService marketPriceService =
            scope.ServiceProvider
                .GetRequiredService<MarketPriceService>();

        int[] quantities = [1, 10, 100, 1000];
        int maximumLadderLength =
            market.Offers.Max(
                offer => offer.Ladder.Count);

        for (
            int index = 0;
            index < Math.Min(
                quantities.Length,
                maximumLadderLength);
            index++)
        {
            if (!currentServerState.IsCaptureLeaseActive(lease))
                return;

            ulong[] candidates =
                market.Offers
                    .Where(
                        offer =>
                            offer.Ladder.Count > index &&
                            offer.Ladder[index] > 0)
                    .Select(
                        offer =>
                            offer.Ladder[index])
                    .ToArray();

            if (candidates.Length == 0)
                continue;

            ulong rawPrice = candidates.Min();

            if (rawPrice > long.MaxValue)
                continue;

            int quantity = quantities[index];

            await marketPriceService
                .AddObservationAsync(
                    objectType.Value,
                    checked((long)market.ItemId),
                    serverName,
                    checked((long)rawPrice),
                    quantity,
                    MarketPriceSource.InGameAutomatic,
                    cancellationToken);

            if (!currentServerState.IsCaptureLeaseActive(lease))
                return;

            marketDataChangeNotifier.Notify(
                objectType.Value,
                checked((long)market.ItemId),
                serverName,
                quantity);
        }
    }

    internal async Task PersistCrushAsync(
        CrushObservation crush,
        IReadOnlyDictionary<
            ulong,
            ItemDetailObservation> itemDetails,
        DateTime observedAtUtc,
        NetworkCaptureLease lease,
        CancellationToken cancellationToken)
    {
        string serverName = lease.ServerName;

        if (!currentServerState.IsCaptureLeaseActive(lease))
            return;

        CoefficientService? coefficientService =
            null;

        IServiceScope? scope =
            null;

        if (settings.CoefficientCaptureEnabled)
        {
            scope =
                serviceScopeFactory.CreateScope();

            coefficientService =
                scope.ServiceProvider
                    .GetRequiredService<CoefficientService>();
        }

        List<NetworkCrushResultLine>
            networkResultLines = [];

        try
        {
            foreach (CrushLineObservation line in crush.Lines)
            {
                if (!currentServerState.IsCaptureLeaseActive(lease))
                    return;

                if (!itemDetails.TryGetValue(
                        line.ItemUid,
                        out ItemDetailObservation? item) ||
                    item.ItemId == 0)
                {
                    logger.LogDebug(
                        "Concassage UID {Uid} reçu sans ItemId connu.",
                        line.ItemUid);
                    continue;
                }

                await RememberLastEquipmentAsync(
                    item.ItemId,
                    observedAtUtc,
                    lease,
                    cancellationToken);

                NetworkCrushRuneResult[] runes =
                    line.Runes
                        .Where(rune =>
                            rune.RuneItemId > 0 &&
                            rune.Quantity > 0)
                        .Select(rune =>
                            new NetworkCrushRuneResult(
                                checked((long)rune.RuneItemId),
                                checked((int)rune.Quantity)))
                        .ToArray();

                networkResultLines.Add(
                    new NetworkCrushResultLine(
                        checked((long)item.ItemId),
                        line.CoefficientPercent,
                        runes));

                if (coefficientService is null ||
                    line.CoefficientPercent <= 0)
                {
                    continue;
                }

                if (!currentServerState.IsCaptureLeaseActive(lease))
                    return;

                await coefficientService
                    .AddObservationAsync(
                        checked((long)item.ItemId),
                        serverName,
                        line.CoefficientPercent,
                        CoefficientSource.InGameAutomatic,
                        cancellationToken);

                if (!currentServerState.IsCaptureLeaseActive(lease))
                    return;

                marketDataChangeNotifier.Notify(
                    MarketObjectType.Equipment,
                    checked((long)item.ItemId),
                    serverName);
            }
        }
        finally
        {
            scope?.Dispose();
        }

        if (networkResultLines.Count > 0 &&
            currentServerState.IsCaptureLeaseActive(lease))
        {
            await applyNetworkCrushAsync(
                networkResultLines,
                observedAtUtc,
                serverName,
                cancellationToken);
        }
    }

    internal async Task RememberLastEquipmentAsync(
        ulong itemId,
        DateTime observedAtUtc,
        NetworkCaptureLease lease,
        CancellationToken cancellationToken)
    {
        if (itemId == 0 ||
            !currentServerState.IsCaptureLeaseActive(lease))
            return;

        string serverName = lease.ServerName;

        long dofusDbId =
            checked((long)itemId);

        MarketObjectType? objectType;

        if (_marketObjectTypes.TryGetValue(
            dofusDbId,
            out MarketObjectType? cachedType))
        {
            objectType = cachedType;
        }
        else
        {
            using IServiceScope scope =
                serviceScopeFactory.CreateScope();

            BestCrushDbContext context =
                scope.ServiceProvider
                    .GetRequiredService<BestCrushDbContext>();

            objectType =
                await ResolveMarketObjectTypeAsync(
                    dofusDbId,
                    context,
                    cancellationToken);
        }

        if (objectType !=
                MarketObjectType.Equipment ||
            !currentServerState.IsCaptureLeaseActive(lease))
        {
            return;
        }

        lastNetworkEquipmentState.Set(
            dofusDbId,
            serverName,
            observedAtUtc);

        await debugWriter.WriteEventDebugAsync(
            observedAtUtc,
            $"[LAST-EQUIPMENT] ItemId={dofusDbId}",
            cancellationToken);
    }


    private bool IsMarketCaptureEnabled(
        MarketObjectType objectType)
    {
        return objectType switch
        {
            MarketObjectType.Equipment =>
                settings.EquipmentCaptureEnabled,

            MarketObjectType.Rune =>
                settings.RuneCaptureEnabled,

            MarketObjectType.Resource =>
                settings.ResourceCaptureEnabled,

            _ => false
        };
    }

    private async Task<MarketObjectType?>
        ResolveMarketObjectTypeAsync(
            long dofusDbId,
            BestCrushDbContext context,
            CancellationToken cancellationToken)
    {
        if (_marketObjectTypes.TryGetValue(
            dofusDbId,
            out MarketObjectType? cached))
        {
            return cached;
        }

        MarketObjectType? result;

        if (await context.Equipments
                .AsNoTracking()
                .AnyAsync(
                    item =>
                        item.DofusDbId ==
                        dofusDbId,
                    cancellationToken))
        {
            result =
                MarketObjectType.Equipment;
        }
        else if (
            await context.Runes
                .AsNoTracking()
                .AnyAsync(
                    item =>
                        item.DofusDbId ==
                        dofusDbId,
                    cancellationToken))
        {
            result =
                MarketObjectType.Rune;
        }
        else if (
            await context.Resources
                .AsNoTracking()
                .AnyAsync(
                    item =>
                        item.DofusDbId ==
                        dofusDbId,
                    cancellationToken))
        {
            result =
                MarketObjectType.Resource;
        }
        else
        {
            result = null;
        }

        _marketObjectTypes[dofusDbId] = result;
        return result;
    }


}
