using System.Net.Sockets;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using PacketDotNet;
using SharpPcap;

namespace BestCrush.NetworkProbe.Capture;

/// <summary>
/// P3m.13 research-only per-window Connection→Game mapping. Captures at
/// most a single IPv4 TCP port, never prints packet, endpoints, PIDs or HWND.
/// Matching is based on exact socket tuple, OS owner and unique window.
/// </summary>
internal sealed class WindowServerConnectionLinkProbe : IDisposable
{
    private const int MaximumWindows = 16;
    private const int MaximumOwnerAttemptsPerFlow = 24;
    private const int MaximumFlows = 96;
    // P3m.13e opt-in diagnostic: samples the Windows TCP table
    // continuously while outstanding short-lived sockets are active.
    private static readonly TimeSpan OwnerSamplingInterval =
        TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan WindowsRefreshInterval =
        TimeSpan.FromMilliseconds(200);

    private readonly ICaptureDevice _device;
    private readonly int _port;
    private readonly bool _useEtw;
    private readonly EtwTcpConnectionOwnerIndex _etwIndex = new();
    private readonly Dictionary<TcpSocketTuple, DateTime> _firstSeen = [];
    private readonly HashSet<TcpSocketTuple> _etwMatched = [];
    private WindowTcpEtwConnectProbe? _etw;
    private readonly WindowServerConnectionLinkStudy _links = new();
    private readonly ConnectionServerSelectionStudy _selection;
    private readonly CharacterCaptureFlowTracker _tracker;
    private readonly object _sync = new();
    private readonly Dictionary<(long Handle, int ProcessId), int> _slots = [];
    private readonly Dictionary<TcpSocketTuple, int> _attempts = [];
    private readonly HashSet<TcpSocketTuple> _activeTuples = [];
    private IReadOnlyList<(int ProcessId, int WindowSlot)> _windows = [];
    private DateTime _lastRefresh = DateTime.MinValue;
    private CancellationTokenSource? _ownerPollingCancel;
    private Task? _ownerPollingTask;
    private int _ownerPolls;
    private int _ownerPollingMatches;
    private bool _running;

    internal WindowServerConnectionLinkProbe(
        ICaptureDevice device, int port, bool useEtw = false)
    {
        _device = device;
        _port = port;
        _useEtw = useEtw;
        _selection = new ConnectionServerSelectionStudy(_links.ObserveSelectedId);
        _tracker = new CharacterCaptureFlowTracker(connectionSelectionStudy: _selection);
        // The study's explicit staging is a legacy single-client concern:
        // P3m.13 does not use P3m.11 stage output, but it must initialize
        // its bounded collector to accept session frames.
        if (!_selection.StartStage(0))
            throw new InvalidOperationException("Connection study could not start.");
    }

    internal void Start()
    {
        _device.OnPacketArrival += OnPacketArrival;
        try
        {
            // ETW must be active before packet capture and client startup.
            // If privilege/provider setup fails, do not silently run Npcap.
            if (_useEtw)
            {
                _etw = new WindowTcpEtwConnectProbe(_port, OnEtwConnect);
                _etw.Start();
            }
            _device.Open(DeviceModes.Promiscuous, 1000);
            _device.Filter = $"tcp port {_port}";
            lock (_sync) { RefreshWindows(force: true); }
            _device.StartCapture();
            _running = true;
            _ownerPollingCancel = new CancellationTokenSource();
            CancellationToken samplingToken = _ownerPollingCancel.Token;
            _ownerPollingTask = Task.Run(() =>
                PollOwnersAsync(samplingToken));
        }
        catch
        {
            _device.OnPacketArrival -= OnPacketArrival;
            _etw?.Dispose();
            _etw = null;
            throw;
        }
    }

    internal void Stop()
    {
        if (!_running)
            return;
        _running = false;
        _ownerPollingCancel?.Cancel();
        try { _ownerPollingTask?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { /* normal shutdown */ }
        finally
        {
            _ownerPollingTask = null;
            _ownerPollingCancel?.Dispose();
            _ownerPollingCancel = null;
        }
        try { _device.StopCapture(); }
        catch { /* best effort; never print packet details */ }
        _device.OnPacketArrival -= OnPacketArrival;
        _etw?.Stop();
    }

    internal void WriteSummary()
    {
        lock (_sync)
        {
            RefreshWindows(force: true);
            foreach (string line in _links.Summarize())
                Console.WriteLine(line);
            Console.WriteLine("p3m13=owner-polling samples=" + _ownerPolls +
                " matches=" + _ownerPollingMatches +
                " active-at-stop=" + _activeTuples.Count +
                " interval-ms=25 meaning=exact-tuple-only" +
                " account=unverified server=unverified");
            if (_useEtw)
            {
                Console.WriteLine("p3m14=matches exact-tuples=" + _etwMatched.Count +
                    " events-indexed=" + _etwIndex.Count +
                    " index-overflow=" + (_etwIndex.Overflow ? "yes" : "no") +
                    " meaning=exact-tuple-and-timestamp-only" +
                    " account=unverified server=unverified");
                _etw?.WriteStatus();
            }
        }
    }

    private void OnPacketArrival(object sender, PacketCapture packetCapture)
    {
        try
        {
            RawCapture raw = packetCapture.GetPacket();
            Packet packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            IPPacket? ip = packet.Extract<IPPacket>();
            TcpPacket? tcp = packet.Extract<TcpPacket>();
            if (ip is null || tcp is null ||
                ip.SourceAddress.AddressFamily != AddressFamily.InterNetwork ||
                ip.DestinationAddress.AddressFamily != AddressFamily.InterNetwork)
                return;

            TcpSocketTuple? oriented = TcpSocketTuple.FromPacket(
                ip.SourceAddress, tcp.SourcePort,
                ip.DestinationAddress, tcp.DestinationPort, _port);
            if (oriented is not { } tuple)
                return;

            bool incoming = tcp.SourcePort == _port;
            CharacterCaptureFlowKey key = new(
                tuple.LocalAddress.ToString(), tuple.LocalPort,
                tuple.RemoteAddress.ToString(), tuple.RemotePort);
            lock (_sync)
            {
                RefreshWindows(force: false);
                if (_useEtw && !incoming && !_firstSeen.ContainsKey(tuple) &&
                    _firstSeen.Count < MaximumFlows)
                    _firstSeen.Add(tuple, DateTime.UtcNow);

                if (_useEtw)
                    TryApplyEtwOwner(tuple);
                // Only sockets seen on an outgoing packet are eligible for
                // ongoing sampling; revoke on FIN/RST from either direction.
                if (!incoming && !tcp.Finished && !tcp.Reset &&
                    (_activeTuples.Contains(tuple) || _activeTuples.Count < MaximumFlows))
                    _activeTuples.Add(tuple);

                // Consult Windows on the outgoing SYN too (SYN_SENT);
                // some Connection sockets close before the first payload
                // callback. A later ACK or payload can retry if not found.
                // Never query on FIN/RST and never infer owner from order.
                if (!incoming && !tcp.Finished && !tcp.Reset &&
                    _links.NeedsOwner(tuple))
                {
                    _attempts.TryGetValue(tuple, out int attempts);
                    if (attempts < MaximumOwnerAttemptsPerFlow &&
                        (attempts > 0 || _attempts.Count < MaximumFlows))
                    {
                        _attempts[tuple] = attempts + 1;
                        IReadOnlyList<TcpSocketOwner> owners =
                            WindowsDofusSocketTable.ConnectingOrEstablishedIPv4(_port);
                        _links.ObserveOwner(tuple, owners, _windows);
                    }
                }

                IReadOnlyList<CharacterCaptureEvent> events =
                    _tracker.Observe(key,
                        incoming
                            ? CharacterCaptureDirection.ServerToClient
                            : CharacterCaptureDirection.ClientToServer,
                        tcp.SequenceNumber, tcp.PayloadData, DateTime.UtcNow,
                        syn: tcp.Synchronize, fin: tcp.Finished, rst: tcp.Reset);

                _links.ObserveEvents(tuple, events);
                _selection.ObserveEvents(key, events);
                if (tcp.Finished || tcp.Reset)
                    _activeTuples.Remove(tuple);
            }
        }
        catch (Exception ex)
        {
            // No source addresses, payloads, account details or token data.
            Console.Error.WriteLine("p3m13=packet-ignored reason=" +
                ex.GetType().Name);
        }
    }

    private void OnEtwConnect(TcpSocketTuple tuple, int pid, DateTime whenUtc)
    {
        lock (_sync)
        {
            _etwIndex.Observe(tuple, pid, whenUtc);
            if (_etwIndex.IsConflicted(tuple))
                _links.InvalidateOwner(tuple);
            else
                TryApplyEtwOwner(tuple);
        }
    }

    private void TryApplyEtwOwner(TcpSocketTuple tuple)
    {
        if (_etwIndex.IsConflicted(tuple))
        {
            _links.InvalidateOwner(tuple);
            return;
        }
        if (!_firstSeen.TryGetValue(tuple, out DateTime firstSeen) ||
            _etwMatched.Contains(tuple) || _etwIndex.Overflow)
            return;

        int? pid = _etwIndex.Resolve(tuple, firstSeen);
        if (pid is not > 0)
            return;

        // A real ETW TCP Connect event supplies this PID. The exact tuple
        // and close event timestamp were checked before accepting it.
        // Existing owner conflicts are rejected by the link study.
        _links.ObserveOwner(tuple,
            [new TcpSocketOwner(tuple, pid.Value)], _windows);
        _etwMatched.Add(tuple);
    }

    private async Task PollOwnersAsync(CancellationToken cancellation)
    {
        // This runs only in the opt-in P3m.13 probe, never in BestCrush.
        // Do not infer an owner from time, remote server IP, or PID alone.
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                TcpSocketTuple[] pending;
                lock (_sync)
                    pending = _activeTuples.Where(_links.NeedsOwner)
                        .Take(MaximumFlows).ToArray();

                if (pending.Length > 0)
                {
                    // The native query is outside the capture callback lock,
                    // so it cannot stall Npcap packet reassembly.
                    IReadOnlyList<TcpSocketOwner> snapshot =
                        WindowsDofusSocketTable.ConnectingOrEstablishedIPv4(_port);
                    lock (_sync)
                    {
                        _ownerPolls++;
                        RefreshWindows(force: false);
                        foreach (TcpSocketTuple tuple in pending)
                        {
                            if (!_activeTuples.Contains(tuple) ||
                                !_links.NeedsOwner(tuple))
                                continue;
                            _links.ObserveOwner(tuple, snapshot, _windows);
                            if (!_links.NeedsOwner(tuple))
                                _ownerPollingMatches++;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // A failed OS poll must not abort an otherwise healthy
                // packet capture. No exception text or tuple is logged.
            }

            try { await Task.Delay(OwnerSamplingInterval, cancellation); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void RefreshWindows(bool force)
    {
        DateTime now = DateTime.UtcNow;
        if (!force && now - _lastRefresh < WindowsRefreshInterval)
            return;
        _lastRefresh = now;
        IReadOnlyList<WindowsDofusSocketTable.GameWindow> visible =
            WindowsDofusSocketTable.VisibleWindows();
        List<(int ProcessId, int WindowSlot)> next = [];
        foreach (var window in visible)
        {
            var id = (window.Handle, window.ProcessId);
            if (!_slots.TryGetValue(id, out int slot))
            {
                if (_slots.Count >= MaximumWindows)
                    continue;
                slot = _slots.Count + 1;
                _slots.Add(id, slot);
            }
            next.Add((window.ProcessId, slot));
        }
        _windows = next;
        _links.ObserveWindows(_windows);
    }

    public void Dispose()
    {
        Stop();
        _etw?.Dispose();
        _device.Dispose();
    }
}
