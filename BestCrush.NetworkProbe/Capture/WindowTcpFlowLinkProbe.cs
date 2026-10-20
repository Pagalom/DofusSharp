using System.Net.Sockets;
using BestCrush.Network.Protocol;
using PacketDotNet;
using SharpPcap;

namespace BestCrush.NetworkProbe.Capture;

/// <summary>
/// P3k opt-in link study: compare a packet's oriented IPv4 four-tuple to
/// an OS-owned TCP socket and then to exactly one visible Dofus HWND.
/// No protocol decode, no endpoints or personal identifiers in output.
/// </summary>
internal sealed class WindowTcpFlowLinkProbe(
    ICaptureDevice device, int port) : IDisposable
{
    private const int MaxFlows = 64;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(1500);
    private readonly object _sync = new();
    private readonly Dictionary<TcpSocketTuple, FlowState> _flows = [];
    private readonly Dictionary<(long Handle, int ProcessId), int> _windowSlots = [];
    private IReadOnlyList<TcpSocketOwner> _sockets = [];
    private IReadOnlyList<(int ProcessId, int WindowSlot)> _windows = [];
    private DateTime _lastScanUtc = DateTime.MinValue;
    private bool _started;

    private sealed class FlowState(int index)
    {
        internal int Index { get; } = index;
        internal int? WindowSlot;
        internal string? LastState;
    }

    internal void Start()
    {
        device.OnPacketArrival += OnPacketArrival;
        try
        {
            device.Open(DeviceModes.Promiscuous, 1000);
            device.Filter = $"tcp port {port}";
            Refresh(force: true);
            device.StartCapture();
            _started = true;
        }
        catch
        {
            device.OnPacketArrival -= OnPacketArrival;
            throw;
        }
    }

    internal void Stop()
    {
        if (!_started)
            return;

        _started = false;
        try { device.StopCapture(); }
        catch { /* Shutdown is best effort. */ }
        device.OnPacketArrival -= OnPacketArrival;
    }

    internal void WriteSummary()
    {
        lock (_sync)
        {
            Refresh(force: true);
            foreach ((TcpSocketTuple key, FlowState state) in _flows)
                UpdateFlow(key, state);

            int linkedFlows = _flows.Values.Count(state => state.WindowSlot.HasValue);
            int uniqueWindows = _flows.Values
                .Where(state => state.WindowSlot.HasValue)
                .Select(state => state.WindowSlot!.Value).Distinct().Count();
            bool distinctPids = _windows.Select(w => w.ProcessId).Distinct().Count() == _windows.Count;
            string status = _windows.Count >= 2 && distinctPids &&
                            linkedFlows >= 2 && uniqueWindows >= 2
                ? "two-client-links-candidate"
                : "not-confirmed";

            Console.WriteLine(
                $"p3k=summary windows={_windows.Count} sockets-5555={_sockets.Count}" +
                $" flows-seen={_flows.Count} flow-matched={linkedFlows}" +
                $" unique-windows-linked={uniqueWindows} status={status}" +
                " account=unverified server=unverified");
        }
    }

    private void OnPacketArrival(object sender, PacketCapture capture)
    {
        try
        {
            RawCapture raw = capture.GetPacket();
            Packet packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            IPPacket? ip = packet.Extract<IPPacket>();
            TcpPacket? tcp = packet.Extract<TcpPacket>();
            if (ip is null || tcp is null ||
                ip.SourceAddress.AddressFamily != AddressFamily.InterNetwork ||
                ip.DestinationAddress.AddressFamily != AddressFamily.InterNetwork)
                return;

            TcpSocketTuple? tuple = TcpSocketTuple.FromPacket(
                ip.SourceAddress, tcp.SourcePort,
                ip.DestinationAddress, tcp.DestinationPort, port);
            if (tuple is not { } key)
                return;

            lock (_sync)
            {
                Refresh(force: false);
                if (!_flows.TryGetValue(key, out FlowState? state))
                {
                    if (_flows.Count >= MaxFlows)
                        return;
                    state = new FlowState(_flows.Count + 1);
                    _flows.Add(key, state);
                }
                UpdateFlow(key, state);
            }
        }
        catch (Exception e)
        {
            // No captured tuples, raw bytes, socket addresses or exception
            // messages are ever included in probe output.
            Console.Error.WriteLine(
                $"p3k=packet-ignored reason={e.GetType().Name}");
        }
    }

    private void UpdateFlow(TcpSocketTuple tuple, FlowState state)
    {
        int? pid = TcpSocketOwnerMatcher.FindUniqueOwner(tuple, _sockets);
        int? windowSlot = TcpSocketOwnerMatcher.FindUniqueWindowSlot(pid, _windows);
        string nextStatus = windowSlot.HasValue ? "matched" :
            pid.HasValue ? "no-unique-window" : "no-unique-socket";
        if (nextStatus == state.LastState && windowSlot == state.WindowSlot)
            return;

        state.LastState = nextStatus;
        state.WindowSlot = windowSlot;
        Console.WriteLine(
            $"p3k=flow flow-index={state.Index} status={nextStatus}" +
            $" window-index={(windowSlot.HasValue ? windowSlot.Value.ToString() : "none")}" +
            " account=unverified server=unverified");
    }

    private void Refresh(bool force)
    {
        DateTime now = DateTime.UtcNow;
        if (!force && now - _lastScanUtc < RefreshInterval)
            return;

        // Fail closed if either Windows inventory is temporarily inaccessible.
        _lastScanUtc = now;
        try
        {
            IReadOnlyList<WindowsDofusSocketTable.GameWindow> windows =
                WindowsDofusSocketTable.VisibleWindows();
            List<(int ProcessId, int WindowSlot)> slots = [];
            foreach (var window in windows)
            {
                var id = (window.Handle, window.ProcessId);
                if (!_windowSlots.TryGetValue(id, out int slot))
                {
                    slot = _windowSlots.Count + 1;
                    _windowSlots.Add(id, slot);
                }
                slots.Add((window.ProcessId, slot));
            }

            _windows = slots;
            _sockets = WindowsDofusSocketTable.EstablishedIPv4(port);
        }
        catch (Exception e)
        {
            _windows = [];
            _sockets = [];
            Console.Error.WriteLine(
                $"p3k=windows-unavailable reason={e.GetType().Name}");
        }
    }

    public void Dispose()
    {
        Stop();
        device.Dispose();
    }
}
