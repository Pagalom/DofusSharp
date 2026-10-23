namespace BestCrush.Services;

/// <summary>
/// Immutable network-equipment choice for a single verified OS window at
/// the moment of the click. Never an account or character identity.
/// </summary>
internal sealed record WindowEquipmentFocusToken(
    DofusClientWindowKey Window,
    NetworkCaptureLease Lease,
    long CaptureEpoch,
    LastNetworkEquipmentSnapshot Equipment);

/// <summary>
/// P3m.16 staging state only: last network equipment per *verified local
/// Windows client*, scoped to the user-selected server lease and capture run.
/// Not wired to ingestion or wheel targeting until real client ownership is
/// sufficiently reliable. All information is in RAM and is never persisted.
/// A window key does not establish account/character identity.
/// </summary>
internal sealed class WindowScopedNetworkEquipmentState
{
    private const int MaximumClients = 16;
    private readonly object _sync = new();
    private readonly Dictionary<DofusClientWindowKey, LastNetworkEquipmentSnapshot>
        _byWindow = [];
    private NetworkCaptureLease? _lease;
    private long _captureEpoch = -1;

    /// <summary>
    /// Records a per-window equipment observation. Caller must independently
    /// validate the capture lease and the window-to-connection association.
    /// Rejects older capture epochs and older server generations.
    /// </summary>
    internal bool Remember(
        DofusClientWindowKey window,
        NetworkCaptureLease lease,
        long captureEpoch,
        long equipmentId,
        DateTime observedAtUtc)
    {
        if (window.Handle == 0 || window.ProcessId <= 0 ||
            captureEpoch < 0 || lease.Generation < 0 ||
            string.IsNullOrWhiteSpace(lease.ServerName) || equipmentId <= 0)
            return false;

        lock (_sync)
        {
            if (!TransitionTo(lease, captureEpoch))
                return false;

            if (!_byWindow.ContainsKey(window) &&
                _byWindow.Count >= MaximumClients)
                return false;

            _byWindow[window] = new LastNetworkEquipmentSnapshot(
                equipmentId, lease.ServerName, observedAtUtc);
            return true;
        }
    }

    /// <summary>
    /// Lookup does not fall back to another window or an older server lease.
    /// It never chooses a target based on focus or a character display name.
    /// </summary>
    internal LastNetworkEquipmentSnapshot? Get(
        DofusClientWindowKey window,
        NetworkCaptureLease lease,
        long captureEpoch)
    {
        lock (_sync)
            return _captureEpoch == captureEpoch &&
                _lease == lease &&
                _byWindow.TryGetValue(window, out var snapshot)
                    ? snapshot : null;
    }

    /// <summary>
    /// Read only the exact verified window, in the currently selected manual
    /// server lease. Never consult the legacy global last-equipment slot.
    /// </summary>
    internal WindowEquipmentFocusToken? GetFocusToken(
        DofusClientWindowKey window, NetworkCaptureLease lease)
    {
        if (window.Handle == 0 || window.ProcessId <= 0)
            return null;

        lock (_sync)
        {
            return _lease == lease && _captureEpoch >= 0 &&
                _byWindow.TryGetValue(window, out var equipment)
                ? new WindowEquipmentFocusToken(
                    window, lease, _captureEpoch, equipment)
                : null;
        }
    }

    /// <summary>
    /// Recheck after asynchronous database access: a closed window, server
    /// change, capture restart or newer equipment invalidates the click.
    /// </summary>
    internal bool IsCurrent(WindowEquipmentFocusToken? token)
    {
        if (token is null)
            return false;

        lock (_sync)
        {
            return _lease == token.Lease &&
                _captureEpoch == token.CaptureEpoch &&
                _byWindow.TryGetValue(token.Window, out var current) &&
                current == token.Equipment;
        }
    }

    /// <summary>Closed windows lose their equipment state immediately.</summary>
    internal void Reconcile(
        NetworkCaptureLease lease,
        long captureEpoch,
        IReadOnlyCollection<DofusClientWindowKey> liveWindows)
    {
        ArgumentNullException.ThrowIfNull(liveWindows);
        lock (_sync)
        {
            if (!TransitionTo(lease, captureEpoch))
                return;

            var live = liveWindows.ToHashSet();
            foreach (var window in _byWindow.Keys.ToArray())
                if (!live.Contains(window))
                    _byWindow.Remove(window);
        }
    }

    internal void RemoveMissingWindows(IReadOnlyCollection<DofusClientWindowKey> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        lock (_sync)
        {
            HashSet<DofusClientWindowKey> live = current.ToHashSet();
            foreach (DofusClientWindowKey key in _byWindow.Keys.ToArray())
                if (!live.Contains(key))
                    _byWindow.Remove(key);
        }
    }

    internal void Clear()
    {
        lock (_sync)
        {
            _byWindow.Clear();
            _lease = null;
            _captureEpoch = -1;
        }
    }

    private bool TransitionTo(NetworkCaptureLease lease, long epoch)
    {
        if (epoch < 0 || lease.Generation < 0 ||
            string.IsNullOrWhiteSpace(lease.ServerName))
            return false;

        if (_lease is { } old)
        {
            if (epoch < _captureEpoch ||
                (epoch == _captureEpoch && lease.Generation < old.Generation))
                return false;

            if (epoch == _captureEpoch && lease.Generation == old.Generation &&
                !string.Equals(lease.ServerName, old.ServerName,
                    StringComparison.Ordinal))
                return false;

            if (epoch == _captureEpoch && lease == old)
                return true;
        }

        _byWindow.Clear();
        _lease = lease;
        _captureEpoch = epoch;
        return true;
    }
}
