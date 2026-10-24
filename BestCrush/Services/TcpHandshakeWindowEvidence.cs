using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// Bounded, short-lived evidence that a *specific logical TCP handshake*
/// belonged to a unique visible Dofus window. A Connection socket can close
/// before its outgoing server-selection protobuf is fully decoded. Preserve
/// the previously verified exact Windows TCP tuple+PID+HWND evidence without
/// replacing it by a guess based on packet order, focus, title or IP.
/// </summary>
internal sealed class TcpHandshakeWindowEvidence
{
    private const int MaximumConnections = 96;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(10);

    private sealed record Evidence(
        TcpSocketTuple Socket,
        DofusClientWindowKey Window,
        DateTime ObservedAtUtc,
        bool Conflicted = false);

    private readonly object _gate = new();
    private readonly Dictionary<long, Evidence> _byConnection = [];
    private long _epoch = -1;

    internal void Reset(long captureEpoch)
    {
        lock (_gate)
        {
            if (captureEpoch < _epoch)
                return;
            _epoch = captureEpoch;
            _byConnection.Clear();
        }
    }

    internal void Remember(
        long captureEpoch, long connectionId, TcpSocketTuple socket,
        DofusClientWindowKey window, DateTime whenUtc)
    {
        if (connectionId <= 0 || window.Handle == 0 ||
            window.ProcessId <= 0 || whenUtc.Kind != DateTimeKind.Utc)
            return;

        lock (_gate)
        {
            if (captureEpoch != _epoch)
                return;

            Prune(whenUtc);
            if (_byConnection.TryGetValue(connectionId, out Evidence? old))
            {
                // A recycled logical token, changed endpoint or changed
                // window must never be allowed to adopt a fresh owner.
                if (old.Socket != socket || old.Window != window)
                    _byConnection[connectionId] = old with { Conflicted = true };
                return;
            }

            if (_byConnection.Count < MaximumConnections)
                _byConnection.Add(connectionId, new Evidence(socket, window, whenUtc));
        }
    }

    internal DofusClientWindowKey? Resolve(
        long captureEpoch, long connectionId, TcpSocketTuple socket,
        DateTime whenUtc, Func<IReadOnlyList<DofusClientWindowKey>> liveWindows)
    {
        ArgumentNullException.ThrowIfNull(liveWindows);
        if (connectionId <= 0 || whenUtc.Kind != DateTimeKind.Utc)
            return null;

        Evidence? prior;
        lock (_gate)
        {
            if (captureEpoch != _epoch ||
                !_byConnection.TryGetValue(connectionId, out prior) ||
                prior.Conflicted || prior.Socket != socket ||
                whenUtc < prior.ObservedAtUtc ||
                whenUtc - prior.ObservedAtUtc > MaximumAge)
                return null;
        }

        // The handshake was once linked to exactly one TCP owner. Verify
        // that the same HWND+PID still uniquely identifies a live client;
        // the title is NOT used to identify its server or account.
        IReadOnlyList<DofusClientWindowKey> current = liveWindows();
        return current.Count(w => w.ProcessId == prior.Window.ProcessId) == 1 &&
               current.Count(w => w == prior.Window) == 1
            ? prior.Window
            : null;
    }

    private void Prune(DateTime nowUtc)
    {
        foreach (long key in _byConnection
            .Where(pair => nowUtc - pair.Value.ObservedAtUtc > MaximumAge)
            .Select(pair => pair.Key).ToArray())
            _byConnection.Remove(key);
    }
}
