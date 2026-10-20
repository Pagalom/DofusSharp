using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// Ephemeral association of exact IPv4 TCP streams with distinct, live Dofus
/// windows. A link is NOT an account, character ID, or verified game server.
/// No network endpoint, PID, HWND or title is persisted or logged.
/// </summary>
public sealed class LiveDofusClientNetworkBindings
{
    private const int MaxFlows = 128;
    private readonly object _sync = new();
    private readonly Dictionary<TcpSocketTuple, DofusClientWindowKey> _flows = [];
    private readonly HashSet<TcpSocketTuple> _conflicts = [];
    private long _captureEpoch = -1;
    private DofusClientWindowKey[] _linkedClients = [];

    public event Action? Changed;

    public IReadOnlyList<DofusClientWindowKey> LinkedClients
    {
        get { lock (_sync) return _linkedClients.ToArray(); }
    }

    public bool IsLinked(DofusClientWindowKey client)
    {
        lock (_sync) return _linkedClients.Contains(client);
    }

    public void Reset(long captureEpoch)
    {
        bool changed;
        lock (_sync)
        {
            changed = _linkedClients.Length > 0;
            _flows.Clear();
            _conflicts.Clear();
            _linkedClients = [];
            _captureEpoch = captureEpoch;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Accept only a unique exact socket owner and exactly one visible
    /// Dofus window for that PID. If an active tuple changes owner/window
    /// inside the same capture epoch, quarantine it until the next epoch.
    /// </summary>
    public bool Observe(
        TcpSocketTuple tuple,
        long captureEpoch,
        IReadOnlyList<TcpSocketOwner> sockets,
        IReadOnlyList<DofusClientWindowKey> windows)
    {
        ArgumentNullException.ThrowIfNull(sockets);
        ArgumentNullException.ThrowIfNull(windows);

        bool matched;
        bool changed = false;
        lock (_sync)
        {
            if (_captureEpoch != captureEpoch)
            {
                changed = _linkedClients.Length > 0;
                _flows.Clear();
                _conflicts.Clear();
                _linkedClients = [];
                _captureEpoch = captureEpoch;
            }

            int? pid = TcpSocketOwnerMatcher.FindUniqueOwner(tuple, sockets);
            DofusClientWindowKey? resolved = null;
            if (pid is > 0)
            {
                DofusClientWindowKey[] matching = windows
                    .Where(window => window.ProcessId == pid)
                    .Distinct().ToArray();
                if (matching.Length == 1 && matching[0].Handle != 0)
                    resolved = matching[0];
            }

            if (_conflicts.Contains(tuple))
                resolved = null;

            if (_flows.TryGetValue(tuple, out DofusClientWindowKey existing))
            {
                if (resolved is not { } current || existing != current)
                {
                    _flows.Remove(tuple);
                    // Reusing the same tuple after an identity conflict
                    // must not silently redirect observations.
                    if (resolved is not null && existing != resolved.Value)
                        _conflicts.Add(tuple);
                }
            }
            else if (resolved is { } newClient &&
                     _flows.Count < MaxFlows && !_conflicts.Contains(tuple))
            {
                _flows.Add(tuple, newClient);
            }

            DofusClientWindowKey[] next = _flows.Values
                .Distinct()
                .OrderBy(key => key.ProcessId)
                .ThenBy(key => key.Handle)
                .ToArray();
            if (!_linkedClients.SequenceEqual(next))
            {
                changed = true;
                _linkedClients = next;
            }
            matched = _flows.ContainsKey(tuple);
        }
        if (changed) Changed?.Invoke();
        return matched;
    }

    /// <summary>
    /// Revoke closed windows even when no new TCP packets arrive.
    /// Does not attempt to infer the owner of any remaining socket.
    /// </summary>
    public void RemoveMissingWindows(IReadOnlyList<DofusClientWindowKey> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        bool changed = false;
        lock (_sync)
        {
            HashSet<DofusClientWindowKey> live = windows.ToHashSet();
            foreach ((TcpSocketTuple tuple, DofusClientWindowKey owner) in _flows.ToArray())
            {
                if (!live.Contains(owner))
                {
                    _flows.Remove(tuple);
                    _conflicts.Add(tuple);
                }
            }
            DofusClientWindowKey[] next = _flows.Values
                .Distinct()
                .OrderBy(key => key.ProcessId)
                .ThenBy(key => key.Handle)
                .ToArray();
            if (!_linkedClients.SequenceEqual(next))
            {
                changed = true;
                _linkedClients = next;
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Called on each OS socket inventory refresh, and on window refreshes.
    /// A binding is revoked if the socket no longer exists or the window
    /// identity (HWND + PID) is no longer visible.
    /// </summary>
    public void Reconcile(
        long captureEpoch,
        IReadOnlyList<TcpSocketOwner> sockets,
        IReadOnlyList<DofusClientWindowKey> windows)
    {
        ArgumentNullException.ThrowIfNull(sockets);
        ArgumentNullException.ThrowIfNull(windows);

        if (captureEpoch != Volatile.Read(ref _captureEpoch))
        {
            Reset(captureEpoch);
            return;
        }

        bool changed = false;
        lock (_sync)
        {
            HashSet<DofusClientWindowKey> alive = windows.ToHashSet();
            foreach ((TcpSocketTuple tuple, DofusClientWindowKey key) in _flows.ToArray())
            {
                int? owner = TcpSocketOwnerMatcher.FindUniqueOwner(tuple, sockets);
                bool sameWindowVisible = alive.Contains(key) &&
                    windows.Count(w => w.ProcessId == key.ProcessId) == 1;
                if (owner != key.ProcessId || !sameWindowVisible)
                {
                    _flows.Remove(tuple);
                    // If Windows still reports the socket but its owner or
                    // window changed, quarantine this tuple until restart.
                    // A reconnect cannot inherit a previous client's state.
                    if (owner.HasValue && (owner != key.ProcessId || !sameWindowVisible))
                        _conflicts.Add(tuple);
                }
            }

            DofusClientWindowKey[] next = _flows.Values
                .Distinct()
                .OrderBy(key => key.ProcessId)
                .ThenBy(key => key.Handle)
                .ToArray();
            if (!_linkedClients.SequenceEqual(next))
            {
                changed = true;
                _linkedClients = next;
            }
        }
        if (changed) Changed?.Invoke();
    }
}
