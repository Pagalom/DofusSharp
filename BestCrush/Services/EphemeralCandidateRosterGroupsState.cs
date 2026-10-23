namespace BestCrush.Services;

/// <summary>
/// P3c.8 - research-only in-memory view of candidate character rosters tied
/// to exact OS windows. Never an authenticated Ankama account, verified
/// character or game server. Deliberately NOT connected to production capture,
/// character profiles, professions, SQLite or overlay selection.
/// </summary>
internal sealed class EphemeralCandidateRosterGroupsState
{
    private const int MaxWindows = 16;
    private const int MaxRosterMembers = 16;

    internal enum Comparison
    {
        Unavailable,
        SameRosterCandidate,
        DisjointRosterCandidates,
        Ambiguous
    }

    private sealed class Entry(
        long connectionId, ulong selected, HashSet<ulong> roster)
    {
        internal readonly long ConnectionId = connectionId;
        internal readonly ulong Selected = selected;
        internal readonly HashSet<ulong> Roster = roster;
    }

    private readonly object _gate = new();
    private readonly Dictionary<DofusClientWindowKey, Entry> _windows = [];
    private readonly Dictionary<DofusClientWindowKey, long> _connectionFloor = [];
    // A closed (or contradictory) HWND/PID is not eligible to come back
    // within the same capture epoch, even if the OS reuses its key.
    private readonly HashSet<DofusClientWindowKey> _quarantined = [];
    private NetworkCaptureLease? _lease;
    private long _captureEpoch = -1;

    internal int LiveWindowCount
    {
        get { lock (_gate) return _windows.Count; }
    }

    public override string ToString() =>
        "[candidate roster workspace: redacted account=unverified]";

    /// <summary>
    /// Caller must first establish same-Game ksc/ksb consistency and unique
    /// TCP socket PID/HWND OS ownership. This staging API does not itself
    /// authenticate those facts and is not wired to any live capture.
    /// The server lease is the user's selection, NOT a protocol check.
    /// </summary>
    internal bool Observe(
        DofusClientWindowKey window,
        NetworkCaptureLease lease,
        long captureEpoch, long connectionId,
        ulong selectedCandidate,
        IReadOnlyCollection<ulong> rosterCandidates)
    {
        ArgumentNullException.ThrowIfNull(rosterCandidates);
        if (!ValidWindow(window) ||
            !ValidScope(lease, captureEpoch) ||
            connectionId <= 0 || selectedCandidate == 0 ||
            rosterCandidates.Count is < 1 or > MaxRosterMembers ||
            rosterCandidates.Any(value => value == 0))
            return false;

        HashSet<ulong> roster = rosterCandidates.ToHashSet();
        if (roster.Count != rosterCandidates.Count ||
            !roster.Contains(selectedCandidate))
            return false;

        lock (_gate)
        {
            if (!Transition(lease, captureEpoch) ||
                _quarantined.Contains(window) ||
                (_connectionFloor.TryGetValue(window, out long floor) &&
                 connectionId <= floor && !_windows.ContainsKey(window)))
                return false;

            if (_windows.TryGetValue(window, out Entry? current))
            {
                if (connectionId < current.ConnectionId)
                    return false;

                if (connectionId == current.ConnectionId)
                {
                    if (current.Selected == selectedCandidate &&
                        current.Roster.SetEquals(roster))
                        return true;

                    // Contradictory identity on the same TCP epoch must not
                    // retain the previously accepted candidate snapshot.
                    Revoke(window, current.ConnectionId, quarantine: true);
                    return false;
                }

                // A newer TCP session requires fresh evidence. Old evidence
                // must be invalidated immediately, even if new IDs overlap.
                _windows.Remove(window);
                RaiseFloor(window, current.ConnectionId);
            }

            if (!_windows.ContainsKey(window) && _windows.Count >= MaxWindows)
                return false;

            // Partial overlap is ambiguous: it can never serve as a
            // same-account test or justify merging data from two windows.
            foreach ((DofusClientWindowKey otherWindow, Entry other) in
                _windows.ToArray())
            {
                if (otherWindow == window ||
                    !other.Roster.Overlaps(roster) ||
                    other.Roster.SetEquals(roster))
                    continue;

                Revoke(otherWindow, other.ConnectionId, quarantine: true);
                RaiseFloor(window, connectionId);
                _quarantined.Add(window);
                return false;
            }

            _windows[window] = new Entry(
                connectionId, selectedCandidate, roster);
            return true;
        }
    }

    /// <summary>
    /// Same = exactly equal candidate sets; distinct = no overlap.
    /// This NEVER verifies that two windows use the same Ankama account.
    /// Absence, different epochs or ambiguous overlap cannot be coerced
    /// into positive or negative account attribution.
    /// </summary>
    internal Comparison Compare(
        DofusClientWindowKey a, DofusClientWindowKey b,
        NetworkCaptureLease lease, long captureEpoch)
    {
        lock (_gate)
        {
            if (_lease != lease || _captureEpoch != captureEpoch ||
                !_windows.TryGetValue(a, out Entry? left) ||
                !_windows.TryGetValue(b, out Entry? right))
                return Comparison.Unavailable;

            if (left.Roster.SetEquals(right.Roster))
                return Comparison.SameRosterCandidate;
            if (!left.Roster.Overlaps(right.Roster))
                return Comparison.DisjointRosterCandidates;
            return Comparison.Ambiguous;
        }
    }

    internal bool IsCurrent(
        DofusClientWindowKey window,
        NetworkCaptureLease lease,
        long captureEpoch, long connectionId)
    {
        lock (_gate)
            return _lease == lease && _captureEpoch == captureEpoch &&
                _windows.TryGetValue(window, out Entry? current) &&
                current.ConnectionId == connectionId;
    }

    /// <summary>Retire uniquement le token encore en vigueur.</summary>
    internal void Disconnect(DofusClientWindowKey window,
        NetworkCaptureLease lease, long captureEpoch, long connectionId)
    {
        lock (_gate)
        {
            if (_lease != lease || _captureEpoch != captureEpoch ||
                !_windows.TryGetValue(window, out Entry? current) ||
                current.ConnectionId != connectionId)
                return;

            Revoke(window, connectionId, quarantine: false);
        }
    }

    /// <summary>
    /// A window missing from the independently polled Win32 inventory
    /// loses its entry. Reuse of HWND/PID within this capture is quarantined.
    /// </summary>
    internal void RemoveMissingWindows(
        IReadOnlyCollection<DofusClientWindowKey> liveWindows)
    {
        ArgumentNullException.ThrowIfNull(liveWindows);
        lock (_gate)
        {
            HashSet<DofusClientWindowKey> live = liveWindows.ToHashSet();
            foreach (DofusClientWindowKey window in _windows.Keys.ToArray())
            {
                if (!live.Contains(window))
                    Revoke(window, _windows[window].ConnectionId,
                        quarantine: true);
            }
        }
    }

    /// <summary>
    /// Immediately invalidate all candidate associations on a user-driven
    /// server-selection change. The lease is NOT a verified game-server ID;
    /// this is only a lifetime boundary. Old messages must be blocked by
    /// CurrentServerState.IsCaptureLeaseActive before entering the workspace.
    /// </summary>
    internal void InvalidateManualSelection(NetworkCaptureLease? newLease)
    {
        lock (_gate)
        {
            _windows.Clear();
            _connectionFloor.Clear();
            _quarantined.Clear();
            _lease = newLease;
        }
    }

    /// <summary>
    /// Explicit restart: caller provides a genuinely newer capture epoch.
    /// Old in-flight messages cannot revive a previous cohort.
    /// </summary>
    internal bool Reset(long newCaptureEpoch)
    {
        lock (_gate)
        {
            if (newCaptureEpoch <= _captureEpoch)
                return false;
            _windows.Clear();
            _connectionFloor.Clear();
            _quarantined.Clear();
            _lease = null;
            _captureEpoch = newCaptureEpoch;
            return true;
        }
    }

    private void Revoke(DofusClientWindowKey window,
        long connectionId, bool quarantine)
    {
        _windows.Remove(window);
        RaiseFloor(window, connectionId);
        if (quarantine)
            _quarantined.Add(window);
    }

    private void RaiseFloor(DofusClientWindowKey window, long token)
    {
        if (!_connectionFloor.TryGetValue(window, out long prior) ||
            token > prior)
            _connectionFloor[window] = token;
    }

    private bool Transition(NetworkCaptureLease lease, long captureEpoch)
    {
        if (_lease is not { } current)
        {
            if (captureEpoch < _captureEpoch)
                return false;

            _lease = lease;
            _captureEpoch = captureEpoch;
            return true;
        }

        // No rollback in either the manual selection generation or the
        // capture epoch. A server rename at the same generation is invalid.
        if (captureEpoch < _captureEpoch ||
            lease.Generation < current.Generation ||
            (lease.Generation == current.Generation &&
             !string.Equals(lease.ServerName, current.ServerName,
                 StringComparison.Ordinal)))
            return false;

        if (_lease == lease && _captureEpoch == captureEpoch)
            return true;

        _windows.Clear();
        _connectionFloor.Clear();
        _quarantined.Clear();
        _lease = lease;
        _captureEpoch = captureEpoch;
        return true;
    }

    private static bool ValidScope(
        NetworkCaptureLease lease, long captureEpoch) =>
        captureEpoch >= 0 && lease.Generation > 0 &&
        !string.IsNullOrWhiteSpace(lease.ServerName);

    private static bool ValidWindow(DofusClientWindowKey key) =>
        key.Handle != 0 && key.ProcessId > 0;
}
