using System.Net;
using System.Net.Sockets;
using BestCrush.Network.Protocol;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace BestCrush.NetworkProbe.Capture;

/// <summary>
/// P3m.14 Windows-only, opt-in real-time ETW TCP Connect research.
/// Listens to a private, uniquely named kernel session; never prints or
/// saves PIDs, network endpoints, ETW payloads or account identifiers.
/// </summary>
internal sealed class WindowTcpEtwConnectProbe : IDisposable
{
    private readonly int _port;
    private readonly Action<TcpSocketTuple, int, DateTime> _onConnect;
    private TraceEventSession? _session;
    private ETWTraceEventSource? _source;
    private Task? _pump;
    private int _tcpEvents;
    private int _portEvents;
    private int _rejected;
    private bool _ready;
    private bool _drained;

    internal WindowTcpEtwConnectProbe(int port,
        Action<TcpSocketTuple, int, DateTime> onConnect)
    {
        _port = port;
        _onConnect = onConnect ?? throw new ArgumentNullException(nameof(onConnect));
    }

    internal bool Ready => _ready;
    internal bool Drained => _drained;

    internal void Start()
    {
        if (_session is not null)
            throw new InvalidOperationException("ETW capture already started.");

        // Never take over the global NT Kernel Logger or replace an active
        // third-party trace. A unique session has no existing owner to evict.
        string name = "BestCrush-P3m14-" + Guid.NewGuid().ToString("N");
        try
        {
            _session = new TraceEventSession(
                name, TraceEventSessionOptions.NoRestartOnCreate);
            _session.StopOnDispose = true;
            // For a uniquely named non-NT-Kernel-Logger session, reading
            // Source first may start the session; EnableKernelProvider then
            // rejects it ("must be enabled first"). Enable before Source.
            _session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.NetworkTCPIP);
            _source = _session.Source;

            _source.Kernel.TcpIpConnect += data =>
            {
                Interlocked.Increment(ref _tcpEvents);
                try
                {
                    if (data.dport != _port || data.sport is < 1 or > ushort.MaxValue ||
                        data.daddr.AddressFamily != AddressFamily.InterNetwork ||
                        data.saddr.AddressFamily != AddressFamily.InterNetwork)
                        return;

                    Interlocked.Increment(ref _portEvents);
                    // TcpIpConnectTraceData fixes up ProcessID from the
                    // event's TCP payload. Do not use a generic kernel
                    // event-header process identifier as a substitute.
                    int pid = data.ProcessID;
                    DateTime when = data.TimeStamp.ToUniversalTime();
                    if (pid <= 0)
                    {
                        Interlocked.Increment(ref _rejected);
                        return;
                    }
                    var tuple = new TcpSocketTuple(
                        data.saddr, (ushort)data.sport,
                        data.daddr, (ushort)data.dport);
                    _onConnect(tuple, pid, when);
                }
                catch
                {
                    // Prevent exceptions from stopping ETW processing.
                    Interlocked.Increment(ref _rejected);
                }
            };

            // Enable before launching either Dofus client or packet capture.
            // This may require administrative rights. No fallbacks that
            // falsely claim ETW has started.
            _ready = true;
            ETWTraceEventSource source = _source;
            _pump = Task.Run(() => source.Process());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Stop()
    {
        if (_session is null)
            return;
        // Stop the provider first and let the source consume any queued
        // events. StopProcessing is a last resort, not the first operation:
        // stopping the reader first can discard a delayed TCP Connect.
        try { _session.Stop(); }
        catch { /* best effort */ }
        try { _drained = _pump?.Wait(TimeSpan.FromSeconds(5)) ?? true; }
        catch (AggregateException) { _drained = false; }
        if (!_drained)
        {
            try { _source?.StopProcessing(); }
            catch { /* best effort */ }
            try { _pump?.Wait(TimeSpan.FromSeconds(2)); }
            catch (AggregateException) { /* status remains not drained */ }
        }

        // Ownership and session lifetime are not left to GC.
        _session.Dispose();
        _session = null;
        _source = null;
        _pump = null;
    }

    internal void WriteStatus()
    {
        Console.WriteLine("p3m14=etw ready=" + (_ready ? "yes" : "no") +
            " processing-stopped=" + (_drained ? "yes" : "no") +
            " tcp-connect-events=" + Volatile.Read(ref _tcpEvents) +
            " port-connect-events=" + Volatile.Read(ref _portEvents) +
            " rejected=" + Volatile.Read(ref _rejected) +
            " provider=kernel-tcpip-connect" +
            " account=unverified server=unverified");
    }

    public void Dispose() => Stop();
}
