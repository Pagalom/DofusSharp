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
    // One currently valid logical TCP connection token per exact socket.
    // Reconnects with a reused 4-tuple must never inherit earlier messages.
    private readonly Dictionary<TcpSocketTuple, long> _sessionTokens = [];
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

    /// <summary>
    /// Attaches a P3m.2 logical TCP connection to exactly one verified
    /// Windows socket. Returned window metadata is LOCAL CLIENT CONTEXT,
    /// never proof of game character, account or server.
    /// </summary>
    public DofusClientWindowKey? ResolveConnection(
        TcpSocketTuple tuple, long captureEpoch, long connectionId)
    {
        if (connectionId <= 0)
            return null;

        lock (_sync)
        {
            if (captureEpoch != _captureEpoch ||
                !_flows.TryGetValue(tuple, out DofusClientWindowKey window))
                return null;

            if (_sessionTokens.TryGetValue(tuple, out long currentToken) &&
                connectionId < currentToken)
                return null; // delayed data from a previous TCP connection

            _sessionTokens[tuple] = connectionId;
            return window;
        }
    }

    /// <summary>
    /// Worker-side check for a queued message. No endpoint metadata needs
    /// to be passed into the semantic message processor.
    /// </summary>
    public bool IsCurrentConnection(
        long captureEpoch, long connectionId, DofusClientWindowKey window)
    {
        if (connectionId <= 0)
            return false;

        lock (_sync)
            return captureEpoch == _captureEpoch &&
                _sessionTokens.Any(pair =>
                    pair.Value == connectionId &&
                    _flows.TryGetValue(pair.Key, out DofusClientWindowKey live) &&
                    live == window);
    }

    /// <summary>
    /// Only the latest logical connection of a TCP four-tuple can use the
    /// verified window. A stale queued message cannot reclaim that tuple.
    /// </summary>
    public bool IsCurrentConnection(
        TcpSocketTuple tuple, long captureEpoch,
        long connectionId, DofusClientWindowKey window)
    {
        if (connectionId <= 0)
            return false;

        lock (_sync)
            return captureEpoch == _captureEpoch &&
                _flows.TryGetValue(tuple, out DofusClientWindowKey actual) &&
                actual == window &&
                _sessionTokens.TryGetValue(tuple, out long token) &&
                token == connectionId;
    }

    public void Reset(long captureEpoch)
    {
        bool changed;
        lock (_sync)
        {
            changed = _linkedClients.Length > 0;
            _flows.Clear();
            _sessionTokens.Clear();
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
                _sessionTokens.Clear();
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
                    _sessionTokens.Remove(tuple);
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
                    _sessionTokens.Remove(tuple);
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
                    _sessionTokens.Remove(tuple);
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
