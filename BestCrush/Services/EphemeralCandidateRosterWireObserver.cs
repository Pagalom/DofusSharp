using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// P3c.9: opt-in ONLY adapter between already revalidated Dofus wire
/// messages and P3c.8's unverified RAM-only roster candidates.
/// There is no account identity, production ingestion, profession link,
/// UI binding or disk persistence here. Constructed only by replay/test
/// harnesses until the separate live-capture gate is validated.
/// </summary>
internal sealed class EphemeralCandidateRosterWireObserver(
    CurrentServerState serverState,
    EphemeralCandidateRosterGroupsState state)
{
    internal enum Result
    {
        Ignored,
        Pending,
        Linked,
        Rejected
    }

    private const int MaxTrackedFlows = 128;
    private readonly object _gate = new();
    private readonly Dictionary<(long Epoch, long ConnectionId), Pending>
        _pending = [];
    private readonly HashSet<(long Epoch, long ConnectionId)> _rejected = [];
    private readonly Dictionary<(long Epoch, long ConnectionId),
        (DofusClientWindowKey Window, NetworkCaptureLease Lease)> _linked = [];
    private long _epoch = -1;

    private sealed class Pending(
        DofusClientWindowKey window,
        NetworkCaptureLease lease,
        CharacterWireCandidateSnapshot roster)
    {
        internal readonly DofusClientWindowKey Window = window;
        internal readonly NetworkCaptureLease Lease = lease;
        internal CharacterWireCandidateSnapshot? Roster = roster;
    }

    internal int CandidateWindowCount => state.LiveWindowCount;

    public override string ToString() =>
        "[candidate wire observer: redacted account=unverified]";

    /// <summary>
    /// Only the production worker can provide ownerRevalidated=true after
    /// rechecking the exact TCP connection / OS window binding. Replay
    /// harnesses can supply a synthetic prevalidated window explicitly.
    /// </summary>
    internal Result Observe(DofusWireMessage message, bool ownerRevalidated)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Key is not ("ksc" or "ksb") ||
            message.Direction != "S→C")
            return Result.Ignored;

        if (!ownerRevalidated ||
            message.CandidateWindow is not { Handle: not 0, ProcessId: > 0 } ||
            message.CaptureLease is not { } lease ||
            message.CaptureEpoch < 0 || message.ConnectionId <= 0 ||
            !serverState.IsCaptureLeaseActive(lease))
        {
            // If an already linked flow loses owner/lease evidence, drop
            // its in-memory candidate immediately. Never reassign a delayed
            // packet to another window by falling back to focus/title.
            lock (_gate)
                if (message.CaptureEpoch == _epoch &&
                    message.ConnectionId > 0)
                    Reject((message.CaptureEpoch, message.ConnectionId));
            return Result.Rejected;
        }

        DofusClientWindowKey window = message.CandidateWindow.Value;

        lock (_gate)
        {
            if (_epoch != message.CaptureEpoch)
            {
                // Only a strictly newer capture epoch can start a new
                // identity generation. A delayed message cannot roll back.
                if (message.CaptureEpoch < _epoch)
                    return Result.Rejected;
                _pending.Clear();
                _rejected.Clear();
                _linked.Clear();
                if (!state.Reset(message.CaptureEpoch))
                    return Result.Rejected;
                _epoch = message.CaptureEpoch;
            }

            var key = (message.CaptureEpoch, message.ConnectionId);
            if (_rejected.Contains(key))
                return Result.Rejected;

            if (message.Key == "ksc")
            {
                if (_pending.ContainsKey(key) || _linked.ContainsKey(key) ||
                    _pending.Count + _linked.Count + _rejected.Count >= MaxTrackedFlows ||
                    !CharacterWireCandidateDecoder.TryDecode(
                        "ksc", message.Body, out var roster) ||
                    roster is null)
                {
                    Reject(key);
                    return Result.Rejected;
                }

                _pending.Add(key, new Pending(window, lease, roster));
                return Result.Pending;
            }

            if (!_pending.TryGetValue(key, out Pending? current) ||
                current.Window != window || current.Lease != lease ||
                current.Roster is null ||
                !CharacterWireCandidateDecoder.TryDecode(
                    "ksb", message.Body, out var selection) ||
                selection is null ||
                !CharacterWireCandidateDecoder.HasMatchingCandidate(
                    selection, current.Roster))
            {
                Reject(key);
                return Result.Rejected;
            }

            ulong selected = selection.Candidates[0].CharacterIdCandidate;
            ulong[] members = current.Roster.Candidates
                .Select(candidate => candidate.CharacterIdCandidate).ToArray();

            // No name, class, level or parsed message is retained after the
            // same-flow selection equality has been checked.
            _pending.Remove(key);
            if (!state.Observe(window, lease, message.CaptureEpoch,
                    message.ConnectionId, selected, members))
            {
                Reject(key);
                return Result.Rejected;
            }

            _linked[key] = (window, lease);
            return Result.Linked;
        }
    }

    /// <summary>
    /// An OS window disappeared (as established by a separate Win32 scan).
    /// Old pending selections are discarded and never reassigned.
    /// </summary>
    internal void ReconcileWindows(
        IReadOnlyCollection<DofusClientWindowKey> liveWindows)
    {
        ArgumentNullException.ThrowIfNull(liveWindows);
        lock (_gate)
        {
            var live = liveWindows.ToHashSet();
            // Revoke linked entries first so the workspace can quarantine
            // closed HWND/PID keys, then quarantine pending-only windows too.
            state.RemoveMissingWindows(liveWindows);
            foreach (var pair in _pending.ToArray())
                if (!live.Contains(pair.Value.Window))
                {
                    state.QuarantineWindow(pair.Value.Window);
                    Reject(pair.Key);
                }
            foreach (var pair in _linked.ToArray())
                if (!live.Contains(pair.Value.Window))
                {
                    state.QuarantineWindow(pair.Value.Window);
                    Reject(pair.Key);
                }
        }
    }

    internal void Disconnect(DofusClientWindowKey window,
        NetworkCaptureLease lease, long captureEpoch, long connectionId)
    {
        lock (_gate)
        {
            var key = (captureEpoch, connectionId);
            Reject(key);
            state.Disconnect(window, lease, captureEpoch, connectionId);
        }
    }

    internal void OnManualServerChanged()
    {
        lock (_gate)
        {
            _pending.Clear();
            _rejected.Clear();
            _linked.Clear();
            state.InvalidateManualSelection(serverState.GetCaptureLease());
        }
    }

    internal void OnCaptureEpochChanged(long nextEpoch)
    {
        lock (_gate)
        {
            if (nextEpoch <= _epoch)
                return;
            _pending.Clear();
            _rejected.Clear();
            _linked.Clear();
            if (state.Reset(nextEpoch))
                _epoch = nextEpoch;
        }
    }

    private void Reject((long Epoch, long ConnectionId) key)
    {
        _pending.Remove(key);
        if (_linked.Remove(key, out var prior))
            state.Disconnect(prior.Window, prior.Lease,
                key.Epoch, key.ConnectionId);
        // A flow excluded by malformed or inconsistent metadata never
        // becomes a valid candidate until a new logical TCP connection.
        if (_rejected.Count < MaxTrackedFlows)
            _rejected.Add(key);
    }
}
