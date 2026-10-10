using System.Threading.Channels;
using BestCrush.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#if WINDOWS
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
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
public sealed class DofusNetworkCaptureService : IDisposable
{
    private readonly NpcapPrerequisiteService?
        npcapPrerequisiteService;

    private readonly ILogger<
        DofusNetworkCaptureService> logger;

    private readonly CurrentServerState _currentServerState;
    private readonly LastNetworkEquipmentState _lastNetworkEquipmentState;

#if WINDOWS
    private readonly NetworkDebugWriter
        _networkDebugWriter;

    private readonly DofusNetworkMessageProcessor
        _messageProcessor;
#endif

    public DofusNetworkCaptureService(
        IServiceScopeFactory serviceScopeFactory,
        CurrentServerState currentServerState,
        LastNetworkEquipmentState lastNetworkEquipmentState,
        CrushSessionService crushSessionService,
        NpcapPrerequisiteService npcapPrerequisiteService,
        BestCrushSettingsService settings,
        MarketDataChangeNotifier marketDataChangeNotifier,
        ILogger<DofusNetworkCaptureService> logger)
        : this(
            serviceScopeFactory,
            currentServerState,
            lastNetworkEquipmentState,
            npcapPrerequisiteService,
            settings,
            () => settings.DevTool_KeepDebugArtifacts,
            (lines, observedAtUtc, serverName, cancellationToken) =>
                crushSessionService.ApplyNetworkCrushAsync(
                    lines,
                    observedAtUtc,
                    serverName,
                    cancellationToken),
            marketDataChangeNotifier,
            logger)
    {
    }

    internal DofusNetworkCaptureService(
        IServiceScopeFactory serviceScopeFactory,
        CurrentServerState currentServerState,
        LastNetworkEquipmentState lastNetworkEquipmentState,
        IBestCrushSettingsProvider settings,
        MarketDataChangeNotifier marketDataChangeNotifier,
        Func<
            IReadOnlyList<NetworkCrushResultLine>,
            DateTime,
            string,
            CancellationToken,
            Task> applyNetworkCrushAsync,
        ILogger<DofusNetworkCaptureService> logger,
        Func<bool>? keepDebugArtifacts = null)
        : this(
            serviceScopeFactory,
            currentServerState,
            lastNetworkEquipmentState,
            null,
            settings,
            keepDebugArtifacts ?? (() => false),
            applyNetworkCrushAsync,
            marketDataChangeNotifier,
            logger)
    {
    }

    private DofusNetworkCaptureService(
        IServiceScopeFactory serviceScopeFactory,
        CurrentServerState currentServerState,
        LastNetworkEquipmentState lastNetworkEquipmentState,
        NpcapPrerequisiteService? npcapPrerequisiteService,
        IBestCrushSettingsProvider settings,
        Func<bool> keepDebugArtifacts,
        Func<
            IReadOnlyList<NetworkCrushResultLine>,
            DateTime,
            string,
            CancellationToken,
            Task> applyNetworkCrushAsync,
        MarketDataChangeNotifier marketDataChangeNotifier,
        ILogger<DofusNetworkCaptureService> logger)
    {
        this.npcapPrerequisiteService =
            npcapPrerequisiteService;
        this.logger = logger;
        _currentServerState = currentServerState;
        _lastNetworkEquipmentState = lastNetworkEquipmentState;

#if WINDOWS
        _networkDebugWriter =
            new NetworkDebugWriter(
                currentServerState,
                keepDebugArtifacts,
                DofusPort);

        NetworkObservationWriter
            observationWriter =
                new(
                    serviceScopeFactory,
                    currentServerState,
                    lastNetworkEquipmentState,
                    settings,
                    marketDataChangeNotifier,
                    _networkDebugWriter,
                    applyNetworkCrushAsync,
                    logger);

        _messageProcessor =
            new DofusNetworkMessageProcessor(
                observationWriter,
                _networkDebugWriter,
                currentServerState);

        currentServerState.ServerSelectionChanged +=
            OnServerSelectionChanged;
#endif
    }
#if WINDOWS
    private const int DofusPort = 5555;

    private readonly object _captureLock = new();
    private readonly object _streamLock = new();

    private readonly List<ICaptureDevice> _devices = [];
    private readonly Dictionary<
        TcpFlowKey,
        (NetworkCaptureLease Lease, TcpReassembler Reader)> _streams = [];
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
    private volatile bool _started;
    private long _captureEpoch;

    // The channel and its worker live until Dispose, not until Stop.
    // A Stop/Start cycle invalidates the previous capture epoch.
    internal long CaptureEpochForReplay =>
        Interlocked.Read(ref _captureEpoch);

    private void OnServerSelectionChanged()
    {
        _lastNetworkEquipmentState.Clear();

        // Drop partial frames when the user selects another server.
        lock (_streamLock)
            _streams.Clear();
    }

#endif

    public void Start()
    {
#if WINDOWS
        if (npcapPrerequisiteService is null ||
            !npcapPrerequisiteService.Refresh())
        {
            logger.LogWarning(
                "Capture réseau inactive : Npcap n'est pas disponible.");

            return;
        }

        lock (_captureLock)
        {
            if (_started)
                return;

            string mapPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "protocol-map.json");

            _map =
                ProtocolMap.Load(
                    mapPath);

            _networkDebugWriter
                .SetProtocolMap(
                    _map);

            CaptureDeviceList captureDevices;

            try
            {
                captureDevices =
                    CaptureDeviceList.Instance;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Impossible d'initialiser Npcap/SharpPcap.");

                return;
            }

            _worker ??=
                Task.Run(
                    () =>
                        ProcessMessagesAsync(
                            _cancellation.Token));

            Interlocked.Increment(ref _captureEpoch);
            _started = true;

            foreach (
                ICaptureDevice device
                in captureDevices)
            {
                try
                {
                    device.OnPacketArrival +=
                        OnPacketArrival;

                    device.Open(
                        DeviceModes.Promiscuous,
                        1000);

                    device.Filter =
                        $"tcp port {DofusPort}";

                    device.StartCapture();

                    _devices.Add(
                        device);

                    logger.LogInformation(
                        "Capture réseau Dofus active sur {Device}.",
                        device.Description ??
                        device.Name);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(
                        ex,
                        "Interface ignorée pour la capture Dofus : {Device}.",
                        device.Description ??
                        device.Name);

                    try
                    {
                        device.OnPacketArrival -=
                            OnPacketArrival;

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
                _started = false;
                Interlocked.Increment(ref _captureEpoch);

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
            Interlocked.Increment(ref _captureEpoch);

            // Do not complete the shared channel or cancel the worker here:
            // Start() must be able to reactivate this service.
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
    internal void LoadProtocolMapForReplay(
        string path)
    {
        if (_worker is not null)
        {
            throw new InvalidOperationException(
                "Le worker réseau est déjà démarré.");
        }

        _map =
            ProtocolMap.Load(
                path);

        _networkDebugWriter
            .SetProtocolMap(
                _map);

        _worker =
            Task.Run(
                () =>
                    ProcessMessagesAsync(
                        _cancellation.Token));

        // Replay uses the same Stop/Resume epoch semantics as Npcap.
        Interlocked.Increment(ref _captureEpoch);
        _started = true;
    }

    internal void ResumeReplayForTests()
    {
        lock (_captureLock)
        {
            if (_worker is null || _map is null)
                throw new InvalidOperationException(
                    "Le replay doit être initialisé avant sa reprise.");

            if (_started)
                return;

            Interlocked.Increment(ref _captureEpoch);
            _started = true;
        }
    }

    internal async Task ReplayMessageAsync(
        string direction,
        string key,
        byte[] body,
        DateTime observedAtUtc,
        CancellationToken cancellationToken = default,
        NetworkCaptureLease? recordedLease = null,
        long? recordedCaptureEpoch = null)
    {
        if (_map is null ||
            _worker is null)
        {
            throw new InvalidOperationException(
                "Le ProtocolMap de replay doit être chargé avant l'injection.");
        }

        if (!_started)
            throw new InvalidOperationException(
                "La capture réseau est arrêtée.");

        NetworkCaptureLease? lease =
            recordedLease ?? _currentServerState.GetCaptureLease();

        long captureEpoch =
            recordedCaptureEpoch ?? Interlocked.Read(ref _captureEpoch);

        TaskCompletionSource<bool> completion =
            new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_messages.Writer.TryWrite(
                new DofusWireMessage(
                    direction,
                    key,
                    body,
                    observedAtUtc,
                    lease,
                    captureEpoch,
                    completion)))
        {
            throw new InvalidOperationException(
                "Le canal réseau n'accepte plus de messages.");
        }

        using CancellationTokenRegistration registration =
            cancellationToken.Register(
                () =>
                    completion.TrySetCanceled(
                        cancellationToken));

        await completion.Task
            .ConfigureAwait(false);
    }

    private void OnPacketArrival(
        object sender,
        PacketCapture capture)
    {
        try
        {
            if (_map is null || !_started)
                return;

            long captureEpoch = Interlocked.Read(ref _captureEpoch);

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

            NetworkCaptureLease? lease =
                _currentServerState.GetCaptureLease();

            if (lease is null)
                return;

            TcpFlowKey flow =
                new(
                    deviceName,
                    ip.SourceAddress.ToString(),
                    tcp.SourcePort,
                    ip.DestinationAddress.ToString(),
                    tcp.DestinationPort);

            (NetworkCaptureLease Lease, TcpReassembler Reader) flowState;

            lock (_streamLock)
            {
                if (!_currentServerState.IsCaptureLeaseActive(lease.Value))
                    return;

                if (!_streams.TryGetValue(flow, out flowState) ||
                    flowState.Lease != lease.Value)
                {
                    flowState = (lease.Value, new TcpReassembler());
                    _streams[flow] = flowState;
                }
            }

            TcpReassembler stream = flowState.Reader;

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
                                DateTime.UtcNow,
                                lease,
                                captureEpoch));
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
                    // Stop invalidates queued messages from the old run.
                    if (_started &&
                        message.CaptureEpoch ==
                            Interlocked.Read(ref _captureEpoch))
                    {
                        await _messageProcessor
                            .ProcessMessageAsync(
                                _map,
                                message,
                                cancellationToken);
                    }

                    message.Completion?
                        .TrySetResult(true);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    message.Completion?
                        .TrySetCanceled(
                            cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    message.Completion?
                        .TrySetException(ex);

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

    private readonly record struct TcpFlowKey(
        string DeviceName,
        string SourceAddress,
        int SourcePort,
        string DestinationAddress,
        int DestinationPort);

#endif

    public void Dispose()
    {
#if WINDOWS
        _currentServerState.ServerSelectionChanged -=
            OnServerSelectionChanged;
#endif
        Stop();
#if WINDOWS
        _cancellation.Cancel();
        _messages.Writer.TryComplete();
        _cancellation.Dispose();
#endif
    }
}
