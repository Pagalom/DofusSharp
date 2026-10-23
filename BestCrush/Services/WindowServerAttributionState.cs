using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// Community-corroborated public Dofus server IDs. The list is a bounded
/// reference catalogue, not evidence that a particular window uses an ID.
/// Unknown IDs are deliberately not written to the economic database.
/// </summary>
public static class DofusServerIdCatalog
{
    private static readonly IReadOnlyDictionary<int, string> Known = new Dictionary<int, string>
    {
        [50] = "Ombre",
        [290] = "Tal Kasha",
        [291] = "Imagiro",
        [292] = "Orukam",
        [293] = "Tylezia",
        [294] = "Hell Mina",
        [295] = "Draconiros",
        [350] = "Rafal",
        [351] = "Brial",
        [352] = "Salar",
        [353] = "Dakal",
        [354] = "Mikhal",
        [355] = "Kourial"
    };

    public static string? NameFor(int id) =>
        Known.TryGetValue(id, out string? name) ? name : null;
}

/// <summary>
/// P3m.16: a temporary server candidate, tied to one exact Windows Dofus
/// window only after a distinct Connection selection and a same-flow
/// ksc/ksb Game load. No account or CharacterId is authenticated.
/// No server ID is attributed by IP, title, focus, or global selection.
/// </summary>
public sealed record WindowServerAttribution(int ServerId, string ServerName)
{
    public override string ToString() => "[window server attribution: redacted window]";
}

public sealed class WindowServerAttributionState
{
    private const int MaximumWindows = 16;
    private const int MaximumPendingGameFlows = 64;
    private static readonly TimeSpan SelectionLifetime = TimeSpan.FromMinutes(2);

    private sealed record Selection(long ConnectionId, int Id, DateTime AtUtc);
    private sealed record Game(
        DofusClientWindowKey Window,
        CharacterWireCandidateSnapshot Roster,
        Selection? Selection);

    private readonly object _sync = new();
    private readonly Dictionary<DofusClientWindowKey, List<Selection>> _selections = [];
    private readonly Dictionary<long, Game> _games = [];
    // Each loaded server belongs to the exact Game TCP token and capture epoch.
    // Its generation is unique even if two windows share the same server ID.
    private sealed record Loaded(
        WindowServerAttribution Server, long CaptureEpoch,
        long ConnectionId, long Generation, int? LevelCandidate);
    private readonly Dictionary<DofusClientWindowKey, Loaded> _loaded = [];
    private long _nextGeneration;
    private readonly HashSet<DofusClientWindowKey> _closedWindows = [];
    private long _epoch = -1;

    public event Action? Changed;

    public WindowServerAttribution? ForWindow(DofusClientWindowKey window)
    {
        lock (_sync)
            return _loaded.TryGetValue(window, out Loaded? loaded)
                ? loaded.Server : null;
    }

    public int? SelectedLevelCandidate(DofusClientWindowKey window)
    {
        lock (_sync)
            return _loaded.TryGetValue(window, out Loaded? loaded)
                ? loaded.LevelCandidate : null;
    }

    /// <summary>
    /// Per-window game lease. Distinct windows on the same Dofus server
    /// return the SAME ServerName but independent revocable credentials.
    /// Never return a lease for a Connection-only or different Game socket.
    /// </summary>
    internal NetworkCaptureLease? GetGameLease(
        DofusClientWindowKey window, long captureEpoch, long connectionId)
    {
        lock (_sync)
            return _loaded.TryGetValue(window, out Loaded? loaded) &&
                !_closedWindows.Contains(window) &&
                loaded.CaptureEpoch == captureEpoch &&
                loaded.ConnectionId == connectionId
                    ? new NetworkCaptureLease(
                        loaded.Server.ServerName, loaded.Generation,
                        window, captureEpoch, connectionId)
                    : null;
    }

    internal NetworkCaptureLease? GetWindowLease(DofusClientWindowKey window)
    {
        lock (_sync)
            return _loaded.TryGetValue(window, out Loaded? loaded) &&
                !_closedWindows.Contains(window)
                ? new NetworkCaptureLease(
                    loaded.Server.ServerName, loaded.Generation, window,
                    loaded.CaptureEpoch, loaded.ConnectionId)
                : null;
    }

    internal bool IsGameLeaseActive(NetworkCaptureLease lease)
    {
        if (lease.Window is not { } window || lease.CaptureEpoch < 0 ||
            lease.ConnectionId <= 0)
            return false;

        lock (_sync)
            return _loaded.TryGetValue(window, out Loaded? loaded) &&
                !_closedWindows.Contains(window) &&
                loaded.CaptureEpoch == lease.CaptureEpoch &&
                loaded.ConnectionId == lease.ConnectionId &&
                loaded.Generation == lease.Generation &&
                string.Equals(loaded.Server.ServerName, lease.ServerName,
                    StringComparison.Ordinal);
    }

    /// <summary>Only a newer capture epoch resets quarantined window keys.</summary>
    public void Reset(long epoch)
    {
        bool changed = false;
        lock (_sync)
        {
            if (epoch <= _epoch)
                return;
            changed = _loaded.Count != 0;
            _epoch = epoch;
            _loaded.Clear();
            _selections.Clear();
            _games.Clear();
            _closedWindows.Clear();
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Call only after the exact socket has a unique Windows PID/HWND owner
    /// and the non-Game outbound frame matches root.f1.f4.f1.
    /// </summary>
    internal void ObserveSelection(
        DofusClientWindowKey window, long epoch, long connectionId,
        int serverId, DateTime atUtc)
    {
        if (window.Handle == 0 || window.ProcessId <= 0 ||
            connectionId <= 0 || DofusServerIdCatalog.NameFor(serverId) is null)
            return;
        bool changed;
        lock (_sync)
        {
            if (epoch != _epoch || _closedWindows.Contains(window))
                return;
            if (!_selections.TryGetValue(window, out List<Selection>? list))
            {
                if (_selections.Count >= MaximumWindows)
                    return;
                list = [];
                _selections.Add(window, list);
            }
            // Repeated selections, including the same ID on the same TCP,
            // are ambiguous. Do not silently choose the first value.
            if (list.Any(s => s.ConnectionId == connectionId))
            {
                if (list.Count < 4)
                    list.Add(new Selection(connectionId, 0, atUtc));
            }
            else if (list.Count < 4)
                list.Add(new Selection(connectionId, serverId, atUtc));
            changed = _loaded.Remove(window);
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// Only revalidated packets with an exact TCP-window link may advance the
    /// loaded state. ksc and ksb MUST match on the same logical TCP token.
    /// A new game attempt revokes the old displayed server immediately.
    /// </summary>
    internal void ObserveGame(DofusWireMessage message, bool ownerRevalidated)
    {
        if (message.Direction != "S→C" ||
            message.Key is not ("ksc" or "ksb") ||
            message.CandidateWindow is not { } window)
            return;

        bool changed = false;
        lock (_sync)
        {
            if (message.CaptureEpoch != _epoch ||
                _closedWindows.Contains(window) ||
                message.ConnectionId <= 0)
                return;

            if (!ownerRevalidated)
            {
                changed = _loaded.Remove(window);
                _games.Remove(message.ConnectionId);
                _selections.Remove(window);
            }
            else if (message.Key == "ksc")
            {
                changed = _loaded.Remove(window);
                // Even an unknown/ambiguous next Game invalidates the
                // prior server until a fresh completed load is observed.
                if (_games.Count >= MaximumPendingGameFlows ||
                    _games.ContainsKey(message.ConnectionId) ||
                    !CharacterWireCandidateDecoder.TryDecode(
                        "ksc", message.Body, out var roster) ||
                    roster is null)
                {
                    _games.Remove(message.ConnectionId);
                    _selections.Remove(window);
                }
                else
                {
                    Selection? eligible = null;
                    if (_selections.Remove(window, out List<Selection>? options))
                    {
                        Selection[] recent = options.Where(s =>
                            s.AtUtc <= message.ObservedAtUtc &&
                            message.ObservedAtUtc - s.AtUtc <= SelectionLifetime)
                            .ToArray();
                        // Multiple selectors (even identical IDs) are
                        // ambiguous. Never choose the last or first.
                        if (recent.Length == 1 && recent[0].Id > 0 &&
                            recent[0].ConnectionId != message.ConnectionId)
                            eligible = recent[0];
                    }
                    _games.Add(message.ConnectionId, new Game(window, roster, eligible));
                }
            }
            else if (_games.Remove(message.ConnectionId, out Game? game) &&
                     game.Window == window && game.Selection is { } selection &&
                     CharacterWireCandidateDecoder.TryDecode(
                         "ksb", message.Body, out var selected) &&
                     selected is not null &&
                     CharacterWireCandidateDecoder.HasMatchingCandidate(
                         selected, game.Roster) &&
                     DofusServerIdCatalog.NameFor(selection.Id) is { } serverName)
            {
                _loaded[window] = new Loaded(
                    new WindowServerAttribution(selection.Id, serverName),
                    message.CaptureEpoch, message.ConnectionId,
                    ++_nextGeneration, selected.Candidates[0].Level);
                changed = true;
            }
            else
            {
                changed = _loaded.Remove(window);
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// A closed window is quarantined against HWND/PID reuse until the next
    /// capture epoch, even if the game stops sending network traffic.
    /// </summary>
    public void RemoveMissingWindows(
        IReadOnlyCollection<DofusClientWindowKey> liveWindows)
    {
        ArgumentNullException.ThrowIfNull(liveWindows);
        bool changed = false;
        lock (_sync)
        {
            HashSet<DofusClientWindowKey> live = liveWindows.ToHashSet();
            foreach (DofusClientWindowKey window in _loaded.Keys
                .Concat(_selections.Keys)
                .Concat(_games.Values.Select(g => g.Window))
                .Distinct().Where(w => !live.Contains(w)).ToArray())
            {
                changed |= _loaded.Remove(window);
                _selections.Remove(window);
                foreach (long flow in _games.Where(g => g.Value.Window == window)
                    .Select(g => g.Key).ToArray())
                    _games.Remove(flow);
                _closedWindows.Add(window);
            }
        }
        if (changed) Changed?.Invoke();
    }
}
