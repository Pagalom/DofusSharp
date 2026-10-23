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
    private readonly WindowScopedNetworkEquipmentState? _windowEquipmentState;
    private readonly LiveDofusClientNetworkBindings? _clientBindings;
    private readonly DofusWindowService? _dofusWindowService;
    // The observer remains null in standard builds and normal Debug runs.
    // P3c.10 activates anonymous diagnostics only on an explicit Debug opt-in.
    private readonly EphemeralCandidateRosterWireObserver? _rosterWireObserver;
    private readonly CurrentCandidateWindowCardsState? _candidateCardsState;
    private readonly WindowServerAttributionState? _windowServerAttributionState;
    private readonly bool _liveRosterDiagnostics;
    private int _liveRosterReportCount;
    private const int MaximumLiveRosterReports = 128;
#endif

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
        LiveDofusClientNetworkBindings clientBindings,
        DofusWindowService dofusWindowService,
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
            logger,
            clientBindings,
            dofusWindowService)
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
        Func<bool>? keepDebugArtifacts = null,
        EphemeralCandidateRosterWireObserver? rosterWireObserver = null)
        : this(
            serviceScopeFactory,
            currentServerState,
            lastNetworkEquipmentState,
            null,
            settings,
            keepDebugArtifacts ?? (() => false),
            applyNetworkCrushAsync,
            marketDataChangeNotifier,
            logger,
            null,
            null,
            rosterWireObserver)
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
        ILogger<DofusNetworkCaptureService> logger,
        LiveDofusClientNetworkBindings? clientBindings,
        DofusWindowService? dofusWindowService,
        EphemeralCandidateRosterWireObserver? rosterWireObserver = null)
    {
        this.npcapPrerequisiteService =
            npcapPrerequisiteService;
        this.logger = logger;
        _currentServerState = currentServerState;
        _lastNetworkEquipmentState = lastNetworkEquipmentState;

#if WINDOWS
        _clientBindings = clientBindings;
        _dofusWindowService = dofusWindowService;
        // Reuse the existing process-lifetime singleton: only anonymous
        // window/card counts cross into Blazor, never candidate identity.
        using (IServiceScope scope = serviceScopeFactory.CreateScope())
        {
            _windowEquipmentState = scope.ServiceProvider
                .GetService<WindowScopedNetworkEquipmentState>();
            _candidateCardsState = scope.ServiceProvider
                .GetService<CurrentCandidateWindowCardsState>();
            _windowServerAttributionState = scope.ServiceProvider
                .GetService<WindowServerAttributionState>();
        }
#if DEBUG
        // No activation by production DI, settings, command-line argument,
        // or release build. Only an explicit per-process Debug environment
        // variable starts the anonymous live study.
        if (rosterWireObserver is null &&
            _candidateCardsState is not null &&
            IsLiveRosterStudyEnabled(
                Environment.GetEnvironmentVariable(
                    "BESTCRUSH_P3C10_ROSTER_STUDY"),
                debugBuild: true,
                hasOwnerServices: clientBindings is not null &&
                                  dofusWindowService is not null))
        {
            _rosterWireObserver =
                _candidateCardsState.EnableForResearch(currentServerState);
            _liveRosterDiagnostics = true;
            logger.LogInformation(
                "p3c10=status=enabled mode=debug-opt-in " +
                "account=unverified server=unverified");
        }
        else
#endif
        {
            _rosterWireObserver = rosterWireObserver;
        }
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
                    logger,
                    _windowEquipmentState);

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

    /// <summary>
    /// Pure diagnostic gate for tests. Production builds never call this
    /// with debugBuild=true; the activation branch is compiled out.
    /// </summary>
    internal static bool IsLiveRosterStudyEnabled(
        string? flag, bool debugBuild, bool hasOwnerServices) =>
        debugBuild && hasOwnerServices &&
        string.Equals(flag, "1", StringComparison.Ordinal);

    private readonly object _captureLock = new();
    private readonly object _streamLock = new();
    private readonly object _clientBindingsRefreshLock = new();
    private DateTime _clientBindingsLastRefreshUtc = DateTime.MinValue;
    private IReadOnlyList<TcpSocketOwner> _clientSockets = [];
    private IReadOnlyList<DofusClientWindowKey> _clientWindows = [];


    private readonly List<ICaptureDevice> _devices = [];
    private readonly NetworkFlowCache<TcpFlowKey, TcpReassembler>
        _streams = new();
    // One opaque session ID per bidirectional TCP socket and capture lease.
    // The addresses and ports live in memory only and are never logged.
    private readonly NetworkFlowCache<TcpSocketTuple, long>
        _connectionScopes = new();
    private long _nextConnectionId;
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
        // A UI server context change must NEVER revoke other live windows'
        // game leases in automatic multi-server capture.
        if (_currentServerState.AutomaticRoutingEnabled)
            return;

        _lastNetworkEquipmentState.Clear();
        _windowEquipmentState?.Clear();
        _rosterWireObserver?.OnManualServerChanged();
        _candidateCardsState?.NotifyChanged();
        if (_liveRosterDiagnostics)
            logger.LogInformation(
                "p3c10=status=reset reason=manual-server-change " +
                "account=unverified server=unverified");

        // Drop partial frames when the user selects another server.
        lock (_streamLock)
        {
            _streams.Clear();
            _connectionScopes.Clear();
        }

        _clientBindings?.Reset(Interlocked.Read(ref _captureEpoch));
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

            long epoch = Interlocked.Increment(ref _captureEpoch);
            _clientBindings?.Reset(epoch);
            _windowEquipmentState?.Clear();
            _rosterWireObserver?.OnCaptureEpochChanged(epoch);
            _windowServerAttributionState?.Reset(epoch);
            _candidateCardsState?.NotifyChanged();
            if (_liveRosterDiagnostics)
                Interlocked.Exchange(ref _liveRosterReportCount, 0);
            lock (_clientBindingsRefreshLock)
            {
                _clientBindingsLastRefreshUtc = DateTime.MinValue;
                _clientSockets = [];
                _clientWindows = [];
            }
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
                long stoppedEpoch = Interlocked.Increment(ref _captureEpoch);
                _clientBindings?.Reset(stoppedEpoch);
                _windowEquipmentState?.Clear();
                _rosterWireObserver?.OnCaptureEpochChanged(stoppedEpoch);
                _windowServerAttributionState?.Reset(stoppedEpoch);
                _candidateCardsState?.NotifyChanged();

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
            long stoppedEpoch = Interlocked.Increment(ref _captureEpoch);
            _clientBindings?.Reset(stoppedEpoch);
            _windowEquipmentState?.Clear();
            _rosterWireObserver?.OnCaptureEpochChanged(stoppedEpoch);
            _windowServerAttributionState?.Reset(stoppedEpoch);
            _candidateCardsState?.NotifyChanged();

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
            {
                _streams.Clear();
                _connectionScopes.Clear();
            }
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
        long replayEpoch = Interlocked.Increment(ref _captureEpoch);
        _windowEquipmentState?.Clear();
        _rosterWireObserver?.OnCaptureEpochChanged(replayEpoch);
        _windowServerAttributionState?.Reset(replayEpoch);
        _candidateCardsState?.NotifyChanged();
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

            long replayEpoch = Interlocked.Increment(ref _captureEpoch);
            _windowEquipmentState?.Clear();
            _rosterWireObserver?.OnCaptureEpochChanged(replayEpoch);
            _windowServerAttributionState?.Reset(replayEpoch);
            _candidateCardsState?.NotifyChanged();
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
        long? recordedCaptureEpoch = null,
        long connectionId = 1,
        DofusClientWindowKey? candidateWindow = null)
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
                    completion,
                    connectionId,
                    candidateWindow,
                    SyntheticWindowForReplay: candidateWindow is not null)))
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

            if (ip is null || tcp is null)
                return;

            if (tcp.SourcePort != DofusPort &&
                tcp.DestinationPort != DofusPort)
            {
                return;
            }

            TcpSocketTuple? oriented = TcpSocketTuple.FromPacket(
                ip.SourceAddress, tcp.SourcePort,
                ip.DestinationAddress, tcp.DestinationPort, DofusPort);
            if (oriented is not { } connection)
                return;

            string deviceName =
                sender is ICaptureDevice captureDevice
                    ? captureDevice.Name
                    : "unknown";

            if (tcp.Synchronize || tcp.Reset)
            {
                lock (_streamLock)
                {
                    // Handshake or hard reset invalidates previous correlation,
                    // even if the TCP control packet has zero payload.
                    _connectionScopes.Remove(connection);
                    _streams.Remove(new TcpFlowKey(
                        deviceName,
                        ip.SourceAddress.ToString(), tcp.SourcePort,
                        ip.DestinationAddress.ToString(), tcp.DestinationPort));
                    _streams.Remove(new TcpFlowKey(
                        deviceName,
                        ip.DestinationAddress.ToString(), tcp.DestinationPort,
                        ip.SourceAddress.ToString(), tcp.SourcePort));
                }
            }

            if (tcp.PayloadData.Length == 0)
                return;

            // Discovery can parse Connection selections before any legacy
            // global server is chosen. The discovery lease is NEVER an
            // active persistence authorization in CurrentServerState.
            NetworkCaptureLease lease =
                _currentServerState.GetCaptureLease() ??
                new NetworkCaptureLease("__server-discovery-only__", 1);

            TcpFlowKey flow =
                new(
                    deviceName,
                    ip.SourceAddress.ToString(),
                    tcp.SourcePort,
                    ip.DestinationAddress.ToString(),
                    tcp.DestinationPort);

            TcpReassembler stream;
            long connectionId;

            lock (_streamLock)
            {
                // Control-packet invalidation was handled above.
                DateTime now = DateTime.UtcNow;
                connectionId = _connectionScopes.GetOrCreate(
                    connection, lease, now, () => ++_nextConnectionId);

                stream = _streams.GetOrCreate(
                    flow, lease, now,
                    static () => new TcpReassembler());
            }

            // The 4-tuple and opaque logical TCP connection ID must BOTH
            // match before we attach a window to the in-memory message.
            DofusClientWindowKey? candidateWindow = ObserveLiveDofusClient(
                ip.SourceAddress, tcp.SourcePort,
                ip.DestinationAddress, tcp.DestinationPort,
                captureEpoch, connectionId);

            try
            {
                lock (stream)
                {
                    foreach (
                        ReadOnlyMemory<byte> contiguous
                        in stream.Push(
                            tcp.SequenceNumber,
                            tcp.PayloadData))
                    {
                        // A gap can release a large contiguous suffix at
                        // once. Drain complete frames between small chunks
                        // so even a valid 16-MiB frame cannot trip the
                        // per-flow framing limit by coalescing with its
                        // next frame.
                        ReadOnlySpan<byte> remaining = contiguous.Span;
                        while (!remaining.IsEmpty)
                        {
                            int chunkLength =
                                Math.Min(remaining.Length, 64 * 1024);

                            stream.FrameBuffer.Append(
                                remaining[..chunkLength]);
                            remaining = remaining[chunkLength..];

                            while (
                                stream.FrameBuffer.TryReadFrame(
                                    out byte[] frame))
                            {
                                AnkamaAny? any =
                                    AnkamaFrameDecoder.FindAny(
                                        frame);

                                if (any is null)
                                {
                                    // Connection traffic has no Ankama Any.
                                    // A selection alone never proves the
                                    // loaded server; the same-window Game
                                    // ksc/ksb must also complete.
                                    if (tcp.DestinationPort == DofusPort &&
                                        _windowServerAttributionState is not null &&
                                        ConnectionServerSelectionStudy
                                            .TryGetSelectedServerCandidate(
                                                frame, out int selectedServerId))
                                    {
                                        // Short Connection sockets may appear
                                        // between periodic Windows table
                                        // refreshes; force one exact-tuple
                                        // owner lookup while payload is live.
                                        DofusClientWindowKey? selectedWindow =
                                            ObserveLiveDofusClient(
                                                ip.SourceAddress, tcp.SourcePort,
                                                ip.DestinationAddress, tcp.DestinationPort,
                                                captureEpoch, connectionId,
                                                forceRefresh: true);
                                        if (selectedWindow is { } owner)
                                            _windowServerAttributionState.ObserveSelection(
                                                owner, captureEpoch,
                                                connectionId, selectedServerId,
                                                DateTime.UtcNow);
                                    }
                                    continue;
                                }

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
                                        captureEpoch,
                                        ConnectionId: connectionId,
                                        CandidateWindow: candidateWindow));
                            }
                        }
                    }
                }
            }
            catch (TcpReassemblyLimitExceededException ex)
            {
                // Never keep a saturated/partial decoder as the source
                // of subsequent frames. Only this TCP flow is evicted;
                // other flows, already decoded messages and SQLite
                // observations remain untouched.
                lock (_streamLock)
                {
                    _streams.RemoveIfSame(flow, stream);
                    // A discarded frame parser must not reuse the old
                    // connection's UID or pending-purchase correlations.
                    _connectionScopes.Remove(connection);
                }

                logger.LogWarning(
                    ex,
                    "Flux TCP Dofus abandonné après dépassement de limite de réassemblage.");
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Paquet Dofus ignoré après erreur de décodage.");
        }
    }

    private DofusClientWindowKey? ObserveLiveDofusClient(
        System.Net.IPAddress source, int sourcePort,
        System.Net.IPAddress destination, int destinationPort,
        long captureEpoch, long connectionId,
        bool forceRefresh = false)
    {
        if (_clientBindings is null || _dofusWindowService is null ||
            !_started || captureEpoch != Interlocked.Read(ref _captureEpoch) ||
            connectionId <= 0)
            return null;

        TcpSocketTuple? candidate = TcpSocketTuple.FromPacket(
            source, sourcePort, destination, destinationPort, DofusPort);
        if (candidate is not { } tuple ||
            source.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return null;

        try
        {
            lock (_clientBindingsRefreshLock)
            {
                DateTime now = DateTime.UtcNow;
                if (forceRefresh ||
                    now - _clientBindingsLastRefreshUtc >= TimeSpan.FromSeconds(1))
                {
                    // OS snapshots are inspected locally, never written to
                    // logs or persisted. A missing snapshot fails closed.
                    _clientBindingsLastRefreshUtc = now;
                    _clientSockets = WindowsTcpSocketTableReader.ReadEstablishedIPv4(DofusPort);
                    _clientWindows = _dofusWindowService.ListVisibleDofusWindows()
                        .Where(window => window.ProcessId > 0 && window.Handle != IntPtr.Zero)
                        .Select(window => new DofusClientWindowKey(
                            window.Handle.ToInt64(), window.ProcessId))
                        .Distinct().ToArray();

                    _clientBindings.Reconcile(
                        captureEpoch, _clientSockets, _clientWindows);
                    // Closing a client prunes its shadow equipment when
                    // the next OS window snapshot is observed.
                    if (_currentServerState.GetCaptureLease() is { } liveLease &&
                        _currentServerState.IsCaptureLeaseActive(liveLease))
                        _windowEquipmentState?.Reconcile(
                            liveLease, captureEpoch, _clientWindows);
                    // Notify the UI only if the set of candidate cards
                    // actually changed; do not redraw on every OS scan.
                    if (_liveRosterDiagnostics && _candidateCardsState is not null)
                        _candidateCardsState.RemoveMissingWindows(_clientWindows);
                    else
                        _rosterWireObserver?.ReconcileWindows(_clientWindows);
                }

                if (_started && captureEpoch == Interlocked.Read(ref _captureEpoch) &&
                    _clientBindings.Observe(
                        tuple, captureEpoch, _clientSockets, _clientWindows))
                {
                    return _clientBindings.ResolveConnection(
                        tuple, captureEpoch, connectionId);
                }
            }
        }
        catch (Exception ex)
        {
            // Never include private socket endpoints or a stack trace.
            logger.LogDebug(
                "Association fenetre/flux indisponible : {Reason}.",
                ex.GetType().Name);
        }
        return null;
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
                        // Queued packet metadata must be revalidated:
                        // a closed window or recycled TCP token cannot
                        // silently become another client's identity.
                        DofusWireMessage routed = message;
                        if (!message.SyntheticWindowForReplay &&
                            message.CandidateWindow is { } client &&
                            (_clientBindings is null ||
                             !_clientBindings.IsCurrentConnection(
                                 message.CaptureEpoch, message.ConnectionId, client)))
                        {
                            routed = message with { CandidateWindow = null };
                        }

                        // Independent from the global server lease. Only a
                        // uniquely revalidated TCP/HWND owner and same-flow
                        // ksc+ksb can publish a window-scoped server.
                        if (_windowServerAttributionState is not null)
                        {
                            bool windowOwnerValid =
                                routed.CandidateWindow is not null &&
                                (routed.SyntheticWindowForReplay ||
                                 (_clientBindings?.IsCurrentConnection(
                                     routed.CaptureEpoch,
                                     routed.ConnectionId,
                                     routed.CandidateWindow.Value) == true));
                            _windowServerAttributionState.ObserveGame(
                                routed, windowOwnerValid);
                        }

                        if (_currentServerState.AutomaticRoutingEnabled)
                        {
                            NetworkCaptureLease? verifiedServerLease =
                                routed.CandidateWindow is { } window
                                ? _currentServerState.GetGameCaptureLease(
                                    window, routed.CaptureEpoch,
                                    routed.ConnectionId)
                                : null;
                            // The UI selection and the discovery lease
                            // can NEVER authorize economic writes.
                            routed = routed with
                            {
                                CaptureLease = verifiedServerLease
                            };
                        }

                        await _messageProcessor
                            .ProcessMessageAsync(
                                _map,
                                routed,
                                cancellationToken);

                        // P3c.9 research observer is never constructed by
                        // production DI. Reuse the worker's exact
                        // connection/window revalidation; replay fixtures
                        // are explicitly flagged synthetic. Failures never
                        // suppress economic routing.
                        if (_rosterWireObserver is not null)
                        {
                            try
                            {
                                var outcome = _rosterWireObserver.Observe(
                                    routed,
                                    routed.CandidateWindow is not null &&
                                    (routed.SyntheticWindowForReplay ||
                                     (_clientBindings?.IsCurrentConnection(
                                         routed.CaptureEpoch,
                                         routed.ConnectionId,
                                         routed.CandidateWindow.Value) == true)));

                                if (outcome != EphemeralCandidateRosterWireObserver
                                    .Result.Ignored)
                                    _candidateCardsState?.NotifyChanged();

                                if (_liveRosterDiagnostics &&
                                    outcome != EphemeralCandidateRosterWireObserver
                                        .Result.Ignored)
                                {
                                    int emitted = Interlocked.Increment(
                                        ref _liveRosterReportCount);
                                    if (emitted <= MaximumLiveRosterReports)
                                        logger.LogInformation("{P3c10Status}",
                                            _rosterWireObserver
                                                .FormatAnonymousStatus(outcome));
                                    else if (emitted ==
                                             MaximumLiveRosterReports + 1)
                                        logger.LogInformation(
                                            "p3c10=status=report-limit " +
                                            "account=unverified server=unverified");
                                }
                            }
                            catch (Exception ex)
                            {
                                // Never disclose candidate payloads, IDs,
                                // HWNDs, endpoints or exception content.
                                logger.LogDebug(
                                    "Étude éphémère ksc/ksb indisponible : {ExceptionType}.",
                                    ex.GetType().Name);
                            }
                        }
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
