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
    private static readonly TimeSpan WindowsRefreshInterval =
        TimeSpan.FromMilliseconds(200);

    private readonly ICaptureDevice _device;
    private readonly int _port;
    private readonly WindowServerConnectionLinkStudy _links = new();
    private readonly ConnectionServerSelectionStudy _selection;
    private readonly CharacterCaptureFlowTracker _tracker;
    private readonly object _sync = new();
    private readonly Dictionary<(long Handle, int ProcessId), int> _slots = [];
    private readonly Dictionary<TcpSocketTuple, int> _attempts = [];
    private IReadOnlyList<(int ProcessId, int WindowSlot)> _windows = [];
    private DateTime _lastRefresh = DateTime.MinValue;
    private bool _running;

    internal WindowServerConnectionLinkProbe(ICaptureDevice device, int port)
    {
        _device = device;
        _port = port;
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
            _device.Open(DeviceModes.Promiscuous, 1000);
            _device.Filter = $"tcp port {_port}";
            lock (_sync) { RefreshWindows(force: true); }
            _device.StartCapture();
            _running = true;
        }
        catch
        {
            _device.OnPacketArrival -= OnPacketArrival;
            throw;
        }
    }

    internal void Stop()
    {
        if (!_running)
            return;
        _running = false;
        try { _device.StopCapture(); }
        catch { /* best effort; never print packet details */ }
        _device.OnPacketArrival -= OnPacketArrival;
    }

    internal void WriteSummary()
    {
        lock (_sync)
        {
            RefreshWindows(force: true);
            foreach (string line in _links.Summarize())
                Console.WriteLine(line);
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

                // Query the OS while the socket is active, on the first
                // C→S payload, not after the short Connection flow closes.
                // A failed lookup is retried on later payload packets.
                if (!incoming && tcp.PayloadData.Length > 0 &&
                    _links.NeedsOwner(tuple))
                {
                    _attempts.TryGetValue(tuple, out int attempts);
                    if (attempts < MaximumOwnerAttemptsPerFlow &&
                        (attempts > 0 || _attempts.Count < 96))
                    {
                        _attempts[tuple] = attempts + 1;
                        IReadOnlyList<TcpSocketOwner> owners =
                            WindowsDofusSocketTable.EstablishedIPv4(_port);
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
            }
        }
        catch (Exception ex)
        {
            // No source addresses, payloads, account details or token data.
            Console.Error.WriteLine("p3m13=packet-ignored reason=" +
                ex.GetType().Name);
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
        _device.Dispose();
    }
}
