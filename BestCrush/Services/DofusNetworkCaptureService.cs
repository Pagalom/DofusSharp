using System.Threading.Channels;
using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#if WINDOWS
using BestCrush.NetworkProbe.Capture;
using BestCrush.NetworkProbe.Protocol;
using PacketDotNet;
using SharpPcap;
#endif

namespace BestCrush.Services;

/// <summary>
/// Passive Dofus network ingestion.
///
/// Network packets are the source of truth for market prices and crush
/// coefficients. Visual OCR is intentionally not used here; it is reserved
/// for the middle-click tooltip focus path in OverlayService.
/// </summary>
public sealed class DofusNetworkCaptureService(
    IServiceScopeFactory serviceScopeFactory,
    CurrentServerState currentServerState,
    BestCrushSettingsService settings,
    MarketDataChangeNotifier marketDataChangeNotifier,
    ILogger<DofusNetworkCaptureService> logger)
    : IDisposable
{
#if WINDOWS
    private const int DofusPort = 5555;

    private readonly object _captureLock = new();
    private readonly object _streamLock = new();

    private readonly List<ICaptureDevice> _devices = [];
    private readonly Dictionary<TcpFlowKey, TcpReassembler> _streams = [];
    private readonly Dictionary<ulong, ItemDetailObservation> _itemDetails = [];
    private readonly Dictionary<long, MarketObjectType?> _marketObjectTypes = [];

    private readonly Channel<DofusWireMessage> _messages =
        Channel.CreateUnbounded<DofusWireMessage>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

    private readonly CancellationTokenSource _cancellation = new();

    private ProtocolMap? _map;
    private Task? _worker;
    private bool _started;
#endif

    public void Start()
    {
#if WINDOWS
        lock (_captureLock)
        {
            if (_started)
                return;

            _started = true;

            string mapPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "protocol-map.json");

            _map = ProtocolMap.Load(mapPath);
            _worker = Task.Run(
                () => ProcessMessagesAsync(_cancellation.Token));

            CaptureDeviceList captureDevices;

            try
            {
                captureDevices = CaptureDeviceList.Instance;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Impossible d'initialiser Npcap/SharpPcap.");
                return;
            }

            foreach (ICaptureDevice device in captureDevices)
            {
                try
                {
                    device.OnPacketArrival += OnPacketArrival;
                    device.Open(DeviceModes.Promiscuous, 1000);
                    device.Filter = $"tcp port {DofusPort}";
                    device.StartCapture();
                    _devices.Add(device);

                    logger.LogInformation(
                        "Capture réseau Dofus active sur {Device}.",
                        device.Description ?? device.Name);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(
                        ex,
                        "Interface ignorée pour la capture Dofus : {Device}.",
                        device.Description ?? device.Name);

                    try
                    {
                        device.OnPacketArrival -= OnPacketArrival;
                        device.Dispose();
                    }
                    catch
                    {
                        // Best effort.
                    }
                }
            }

            if (_devices.Count == 0)
            {
                logger.LogWarning(
                    "Aucune interface réseau n'a pu être ouverte pour Dofus.");
            }
        }
#endif
    }

    public void Stop()
    {
#if WINDOWS
        lock (_captureLock)
        {
            if (!_started)
                return;

            _started = false;

            try
            {
                _cancellation.Cancel();
                _messages.Writer.TryComplete();
            }
            catch
            {
                // Best effort.
            }

            foreach (ICaptureDevice device in _devices)
            {
                try
                {
                    device.StopCapture();
                }
                catch
                {
                    // Best effort.
                }

                try
                {
                    device.OnPacketArrival -= OnPacketArrival;
                    device.Dispose();
                }
                catch
                {
                    // Best effort.
                }
            }

            _devices.Clear();

            lock (_streamLock)
                _streams.Clear();
        }
#endif
    }

#if WINDOWS
    private void OnPacketArrival(
        object sender,
        PacketCapture capture)
    {
        try
        {
            if (_map is null)
                return;

            RawCapture raw = capture.GetPacket();
            Packet packet =
                Packet.ParsePacket(
                    raw.LinkLayerType,
                    raw.Data);

            IPPacket? ip = packet.Extract<IPPacket>();
            TcpPacket? tcp = packet.Extract<TcpPacket>();

            if (ip is null ||
                tcp is null ||
                tcp.PayloadData.Length == 0)
            {
                return;
            }

            if (tcp.SourcePort != DofusPort &&
                tcp.DestinationPort != DofusPort)
            {
                return;
            }

            string deviceName =
                sender is ICaptureDevice captureDevice
                    ? captureDevice.Name
                    : "unknown";

            TcpFlowKey flow =
                new(
                    deviceName,
                    ip.SourceAddress.ToString(),
                    tcp.SourcePort,
                    ip.DestinationAddress.ToString(),
                    tcp.DestinationPort);

            TcpReassembler stream;

            lock (_streamLock)
            {
                if (!_streams.TryGetValue(
                    flow,
                    out stream!))
                {
                    stream = new TcpReassembler();
                    _streams[flow] = stream;
                }
            }

            lock (stream)
            {
                foreach (
                    ReadOnlyMemory<byte> contiguous
                    in stream.Push(
                        tcp.SequenceNumber,
                        tcp.PayloadData))
                {
                    stream.FrameBuffer.Append(
                        contiguous.Span);

                    while (
                        stream.FrameBuffer.TryReadFrame(
                            out byte[] frame))
                    {
                        AnkamaAny? any =
                            AnkamaFrameDecoder.FindAny(
                                frame);

                        if (any is null)
                            continue;

                        _messages.Writer.TryWrite(
                            new DofusWireMessage(
                                any.Key,
                                any.Body,
                                DateTime.UtcNow));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Paquet Dofus ignoré après erreur de décodage.");
        }
    }

    private async Task ProcessMessagesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                DofusWireMessage message
                in _messages.Reader.ReadAllAsync(
                    cancellationToken))
            {
                try
                {
                    await ProcessMessageAsync(
                        message,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Message réseau Dofus {Key} ignoré.",
                        message.Key);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutdown.
        }
    }

    private async Task ProcessMessageAsync(
        DofusWireMessage message,
        CancellationToken cancellationToken)
    {
        ProtocolMap? map = _map;
        if (map is null)
            return;

        if (map.ItemDetail is not null &&
            string.Equals(
                message.Key,
                map.ItemDetail,
                StringComparison.Ordinal))
        {
            ItemDetailObservation? item =
                SemanticDecoders.TryDecodeItemDetail(
                    message.Body);

            if (item is not null)
                _itemDetails[item.ItemUid] = item;

            return;
        }

        if (map.InventoryAdd is not null &&
            string.Equals(
                message.Key,
                map.InventoryAdd,
                StringComparison.Ordinal))
        {
            ItemDetailObservation? item =
                SemanticDecoders.TryDecodeInventoryAdd(
                    message.Body);

            if (item is not null)
                _itemDetails[item.ItemUid] = item;

            return;
        }

        if (map.CraftOutput is not null &&
            string.Equals(
                message.Key,
                map.CraftOutput,
                StringComparison.Ordinal))
        {
            ItemDetailObservation? item =
                SemanticDecoders.TryDecodeCraftOutput(
                    message.Body);

            if (item is not null)
                _itemDetails[item.ItemUid] = item;

            return;
        }

        if (map.SmithmagicResult is not null &&
            string.Equals(
                message.Key,
                map.SmithmagicResult,
                StringComparison.Ordinal))
        {
            SmithmagicResultObservation? result =
                SemanticDecoders.TryDecodeSmithmagicResult(
                    message.Body);

            if (result is not null)
                _itemDetails[result.Item.ItemUid] =
                    result.Item;

            return;
        }

        if (map.PriceList is not null &&
            string.Equals(
                message.Key,
                map.PriceList,
                StringComparison.Ordinal))
        {
            MarketObservation? market =
                SemanticDecoders.TryDecodeMarket(
                    message.Body);

            if (market is not null)
            {
                await PersistMarketAsync(
                    market,
                    cancellationToken);
            }

            return;
        }

        if (map.CrushResult is not null &&
            string.Equals(
                message.Key,
                map.CrushResult,
                StringComparison.Ordinal))
        {
            CrushObservation? crush =
                SemanticDecoders.TryDecodeCrush(
                    message.Body);

            if (crush is not null)
            {
                await PersistCrushAsync(
                    crush,
                    cancellationToken);
            }
        }
    }

    private async Task PersistMarketAsync(
        MarketObservation market,
        CancellationToken cancellationToken)
    {
        string? serverName =
            currentServerState.ServerName;

        if (string.IsNullOrWhiteSpace(serverName) ||
            market.ItemId == 0 ||
            market.Offers.Count == 0)
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

        if (objectType is null ||
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
            market.Offers.Count == 0
                ? 0
                : market.Offers.Max(
                    offer => offer.Ladder.Count);

        for (
            int index = 0;
            index < Math.Min(
                quantities.Length,
                maximumLadderLength);
            index++)
        {
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

            marketDataChangeNotifier.Notify(
                objectType.Value,
                checked((long)market.ItemId),
                serverName,
                quantity);
        }
    }

    private async Task PersistCrushAsync(
        CrushObservation crush,
        CancellationToken cancellationToken)
    {
        string? serverName =
            currentServerState.ServerName;

        if (string.IsNullOrWhiteSpace(serverName) ||
            !settings.CoefficientCaptureEnabled)
        {
            return;
        }

        using IServiceScope scope =
            serviceScopeFactory.CreateScope();

        CoefficientService coefficientService =
            scope.ServiceProvider
                .GetRequiredService<CoefficientService>();

        foreach (CrushLineObservation line in crush.Lines)
        {
            if (!_itemDetails.TryGetValue(
                    line.ItemUid,
                    out ItemDetailObservation? item) ||
                item.ItemId == 0 ||
                line.CoefficientPercent <= 0)
            {
                logger.LogDebug(
                    "Concassage UID {Uid} reçu sans ItemId connu.",
                    line.ItemUid);
                continue;
            }

            await coefficientService
                .AddObservationAsync(
                    checked((long)item.ItemId),
                    serverName,
                    line.CoefficientPercent,
                    CoefficientSource.InGameAutomatic,
                    cancellationToken);

            marketDataChangeNotifier.Notify(
                MarketObjectType.Equipment,
                checked((long)item.ItemId),
                serverName);
        }
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

    private readonly record struct TcpFlowKey(
        string DeviceName,
        string SourceAddress,
        int SourcePort,
        string DestinationAddress,
        int DestinationPort);

    private sealed record DofusWireMessage(
        string Key,
        byte[] Body,
        DateTime ObservedAtUtc);
#endif

    public void Dispose()
    {
        Stop();
#if WINDOWS
        _cancellation.Dispose();
#endif
    }
}
