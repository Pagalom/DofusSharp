using System.Text.Json;
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
/// for the explicit F8 tooltip-focus path in OverlayService.
/// </summary>
public sealed class DofusNetworkCaptureService(
    IServiceScopeFactory serviceScopeFactory,
    CurrentServerState currentServerState,
    LastNetworkEquipmentState lastNetworkEquipmentState,
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

    private string? _debugSessionDirectory;
    private string? _wireDebugPath;
    private string? _eventsDebugPath;
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

                        string direction =
                            tcp.SourcePort == DofusPort
                                ? "S→C"
                                : "C→S";

                        _messages.Writer.TryWrite(
                            new DofusWireMessage(
                                direction,
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
        await WriteWireDebugAsync(
            message,
            cancellationToken);

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
            {
                _itemDetails[item.ItemUid] = item;
                await RememberLastEquipmentAsync(
                    item.ItemId,
                    message.ObservedAtUtc,
                    cancellationToken);

                await WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[ITEM] UID={item.ItemUid} ItemId={item.ItemId} x{item.Quantity} | {FormatStats(item.Stats)}",
                    cancellationToken);
            }

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
            {
                _itemDetails[item.ItemUid] = item;
                await RememberLastEquipmentAsync(
                    item.ItemId,
                    message.ObservedAtUtc,
                    cancellationToken);

                await WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[INVENTORY-ADD] UID={item.ItemUid} ItemId={item.ItemId} x{item.Quantity} | {FormatStats(item.Stats)}",
                    cancellationToken);
            }

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
            {
                _itemDetails[item.ItemUid] = item;
                await RememberLastEquipmentAsync(
                    item.ItemId,
                    message.ObservedAtUtc,
                    cancellationToken);

                await WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[CRAFT-OUTPUT] UID={item.ItemUid} ItemId={item.ItemId} x{item.Quantity} | {FormatStats(item.Stats)}",
                    cancellationToken);
            }

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
            {
                _itemDetails[result.Item.ItemUid] =
                    result.Item;

                await RememberLastEquipmentAsync(
                    result.Item.ItemId,
                    message.ObservedAtUtc,
                    cancellationToken);

                await WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[WORKSHOP-RESULT] code={result.ResultCode} UID={result.Item.ItemUid} ItemId={result.Item.ItemId} | {FormatStats(result.Item.Stats)}",
                    cancellationToken);
            }

            return;
        }

        if (map.MarketListingCreated is not null &&
            string.Equals(
                message.Key,
                map.MarketListingCreated,
                StringComparison.Ordinal))
        {
            MarketListingCreatedObservation? listing =
                SemanticDecoders.TryDecodeMarketListingCreated(
                    message.Body);

            if (listing is not null)
            {
                await RememberLastEquipmentAsync(
                    listing.ItemId,
                    message.ObservedAtUtc,
                    cancellationToken);

                await WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    $"[LISTING] MarketUid={listing.MarketListingUid} ItemId={listing.ItemId} x{listing.Quantity} price={listing.Price} K",
                    cancellationToken);
            }

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
                await WriteEventDebugAsync(
                    message.ObservedAtUtc,
                    FormatMarketDebug(market),
                    cancellationToken);

                await PersistMarketAsync(
                    market,
                    message.ObservedAtUtc,
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
                foreach (CrushLineObservation line in crush.Lines)
                {
                    _itemDetails.TryGetValue(
                        line.ItemUid,
                        out ItemDetailObservation? knownItem);

                    await WriteEventDebugAsync(
                        message.ObservedAtUtc,
                        FormatCrushDebug(
                            line,
                            knownItem),
                        cancellationToken);
                }

                await PersistCrushAsync(
                    crush,
                    message.ObservedAtUtc,
                    cancellationToken);
            }
        }
    }

    private async Task PersistMarketAsync(
        MarketObservation market,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        string? serverName =
            currentServerState.ServerName;

        if (string.IsNullOrWhiteSpace(serverName) ||
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

        if (objectType ==
            MarketObjectType.Equipment)
        {
            lastNetworkEquipmentState.Set(
                checked((long)market.ItemId),
                serverName,
                observedAtUtc);
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
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        string? serverName =
            currentServerState.ServerName;

        if (string.IsNullOrWhiteSpace(serverName))
        {
            return;
        }

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

        try
        {
            foreach (CrushLineObservation line in crush.Lines)
            {
                if (!_itemDetails.TryGetValue(
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
                    cancellationToken);

                if (coefficientService is null ||
                    line.CoefficientPercent <= 0)
                {
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
        finally
        {
            scope?.Dispose();
        }
    }

    private async Task RememberLastEquipmentAsync(
        ulong itemId,
        DateTime observedAtUtc,
        CancellationToken cancellationToken)
    {
        if (itemId == 0)
        {
            return;
        }

        string? serverName =
            currentServerState.ServerName;

        if (string.IsNullOrWhiteSpace(
            serverName))
        {
            return;
        }

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
            MarketObjectType.Equipment)
        {
            return;
        }

        lastNetworkEquipmentState.Set(
            dofusDbId,
            serverName,
            observedAtUtc);

        await WriteEventDebugAsync(
            observedAtUtc,
            $"[LAST-EQUIPMENT] ItemId={dofusDbId}",
            cancellationToken);
    }

    private async Task WriteWireDebugAsync(
        DofusWireMessage message,
        CancellationToken cancellationToken)
    {
        if (!settings.DevTool_KeepDebugArtifacts)
        {
            return;
        }

        EnsureDebugSessionDirectory();

        if (_wireDebugPath is null)
        {
            return;
        }

        string json =
            JsonSerializer.Serialize(
                new
                {
                    utc =
                        message.ObservedAtUtc,
                    direction =
                        message.Direction,
                    key =
                        message.Key,
                    bodyLength =
                        message.Body.Length,
                    bodyBase64 =
                        Convert.ToBase64String(
                            message.Body)
                }
            );

        await File.AppendAllTextAsync(
            _wireDebugPath,
            json + Environment.NewLine,
            cancellationToken);
    }

    private async Task WriteEventDebugAsync(
        DateTime observedAtUtc,
        string text,
        CancellationToken cancellationToken)
    {
        if (!settings.DevTool_KeepDebugArtifacts)
        {
            return;
        }

        EnsureDebugSessionDirectory();

        if (_eventsDebugPath is null)
        {
            return;
        }

        await File.AppendAllTextAsync(
            _eventsDebugPath,
            $"[{observedAtUtc:O}] {text}" +
            Environment.NewLine,
            cancellationToken);
    }

    private void EnsureDebugSessionDirectory()
    {
        if (_debugSessionDirectory is not null)
        {
            return;
        }

        string root =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "BestCrush",
                "DebugCaptures",
                "Network");

        Directory.CreateDirectory(root);

        string sessionName =
            $"session-{DateTime.Now:yyyyMMdd-HHmmss}-" +
            $"{Guid.NewGuid():N}";

        _debugSessionDirectory =
            Path.Combine(
                root,
                sessionName);

        Directory.CreateDirectory(
            _debugSessionDirectory);

        _wireDebugPath =
            Path.Combine(
                _debugSessionDirectory,
                "wire.jsonl");

        _eventsDebugPath =
            Path.Combine(
                _debugSessionDirectory,
                "events.log");

        string metadataPath =
            Path.Combine(
                _debugSessionDirectory,
                "session.txt");

        File.WriteAllText(
            metadataPath,
            string.Join(
                Environment.NewLine,
                [
                    "BESTCRUSH NETWORK DEBUG",
                    $"CreatedLocal: {DateTime.Now:O}",
                    $"ClientBuild: {_map?.ClientBuild ?? "unknown"}",
                    $"TCP port: {DofusPort}",
                    $"Server: {currentServerState.ServerName ?? "(not selected)"}",
                    "",
                    "wire.jsonl = exact decoded Ankama Any bodies (Base64), one message per line.",
                    "events.log = human-readable semantic events decoded by BestCrush."
                ]
            )
        );
    }

    private static string FormatStats(
        IReadOnlyList<ItemStatObservation> stats)
    {
        return stats.Count == 0
            ? "stats=(none)"
            : "stats=" +
              string.Join(
                  ", ",
                  stats.Select(
                      stat =>
                          $"{stat.EffectId}={stat.Value}"));
    }

    private static string FormatMarketDebug(
        MarketObservation market)
    {
        int[] quantities =
            [1, 10, 100, 1000];

        List<string> prices = [];

        for (
            int index = 0;
            index < quantities.Length;
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
            {
                continue;
            }

            prices.Add(
                $"x{quantities[index]}={candidates.Min()} K");
        }

        return
            $"[MARKET] ItemId={market.ItemId} offers={market.Offers.Count}" +
            (
                prices.Count == 0
                    ? " | no-price"
                    : " | " +
                      string.Join(
                          " | ",
                          prices)
            );
    }

    private static string FormatCrushDebug(
        CrushLineObservation line,
        ItemDetailObservation? item)
    {
        string runes =
            line.Runes.Count == 0
                ? "(none)"
                : string.Join(
                    ", ",
                    line.Runes.Select(
                        rune =>
                            $"{rune.RuneItemId}x{rune.Quantity}"));

        return
            $"[CRUSH] UID={line.ItemUid} " +
            $"ItemId={(item?.ItemId.ToString() ?? "?")} " +
            $"coefficient={line.CoefficientPercent:0.#####}% " +
            $"runes={runes}";
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
        string Direction,
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
