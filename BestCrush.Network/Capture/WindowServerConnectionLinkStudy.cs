using BestCrush.Network.Protocol;

namespace BestCrush.Network.Capture;

/// <summary>
/// P3m.13: experimental in-memory link from a TCP Connection selection
/// to a loaded Game flow through the exact Windows socket owner PID and
/// one unique Dofus window. All tuples, PIDs, HWNDs and numeric values
/// stay private; only anonymous slots and categorical results are returned.
/// </summary>
public sealed class WindowServerConnectionLinkStudy
{
    private const int MaxFlows = 96;
    private const int MaxSelections = 96;
    private readonly object _sync = new();
    private readonly Dictionary<TcpSocketTuple, Flow> _flows = [];
    private readonly Dictionary<int, List<int>> _selections = [];
    private IReadOnlyList<(int ProcessId, int WindowSlot)> _windows = [];
    private bool _capacityExceeded;

    private sealed class Flow(TcpSocketTuple tuple)
    {
        internal readonly TcpSocketTuple Tuple = tuple;
        internal int? Pid;
        internal int? WindowSlot;
        internal bool OwnerConflict;
        internal int Number;
        internal bool Ksc;
        internal bool Ksb;
        internal bool Closed;
        internal bool Evicted;
    }

    /// <summary>
    /// Called while a TCP socket is still ESTABLISHED. A missing owner
    /// can be retried; a conflicting owner permanently invalidates the flow.
    /// </summary>
    public void ObserveOwner(
        TcpSocketTuple tuple,
        IReadOnlyList<TcpSocketOwner> sockets,
        IReadOnlyList<(int ProcessId, int WindowSlot)> windows)
    {
        lock (_sync)
        {
            UpdateWindows(windows);
            Flow? flow = GetOrAdd(tuple);
            if (flow is null || flow.OwnerConflict)
                return;
            int? pid = TcpSocketOwnerMatcher.FindUniqueOwner(tuple, sockets);
            if (pid is not > 0)
                return;
            if (flow.Pid.HasValue && flow.Pid.Value != pid)
            {
                flow.OwnerConflict = true;
                flow.WindowSlot = null;
                return;
            }

            flow.Pid = pid;
            ResolveWindow(flow);
        }
    }

    /// <summary>Refresh visible window slots independently of short socket life.</summary>
    public void ObserveWindows(IReadOnlyList<(int ProcessId, int WindowSlot)> windows)
    {
        lock (_sync)
        {
            UpdateWindows(windows);
            foreach (Flow flow in _flows.Values)
                ResolveWindow(flow);
        }
    }

    private void UpdateWindows(IReadOnlyList<(int ProcessId, int WindowSlot)> windows)
    {
        _windows = windows;
    }

    private void ResolveWindow(Flow flow)
    {
        if (flow.OwnerConflict || !flow.Pid.HasValue)
            return;
        int? slot = TcpSocketOwnerMatcher.FindUniqueWindowSlot(flow.Pid, _windows);
        if (!slot.HasValue)
            return;
        if (flow.WindowSlot.HasValue && flow.WindowSlot != slot)
        {
            flow.OwnerConflict = true;
            flow.WindowSlot = null;
            return;
        }
        flow.WindowSlot = slot;
    }

    public bool NeedsOwner(TcpSocketTuple tuple)
    {
        lock (_sync)
        {
            return !_flows.TryGetValue(tuple, out Flow? flow) ||
                   (!flow.Pid.HasValue && !flow.OwnerConflict);
        }
    }

    public void ObserveEvents(TcpSocketTuple tuple, IReadOnlyList<CharacterCaptureEvent> events)
    {
        lock (_sync)
        {
            Flow? flow = GetOrAdd(tuple);
            if (flow is null)
                return;
            foreach (CharacterCaptureEvent evt in events)
            {
                if (flow.Number != 0 && flow.Number != evt.SessionNumber)
                {
                    // A reused TCP four-tuple within the same capture is
                    // ambiguous without a generation-safe socket identity.
                    flow.OwnerConflict = true;
                    continue;
                }
                if (evt.SessionNumber <= 0)
                    continue;
                flow.Number = evt.SessionNumber;
                if (evt.Kind == CharacterCaptureEventKind.Closed)
                    flow.Closed = true;
                else if (evt.Kind == CharacterCaptureEventKind.Evicted)
                    flow.Evicted = true;
                else if (evt.Kind == CharacterCaptureEventKind.Message &&
                         evt.Direction == CharacterCaptureDirection.ServerToClient)
                {
                    if (evt.Opcode == "ksc") flow.Ksc = true;
                    if (evt.Opcode == "ksb") flow.Ksb = true;
                }
            }
        }
    }

    /// <summary>
    /// Called by the already validated experimental Connection decoder.
    /// No selection value is exposed in output; no account/server state set.
    /// </summary>
    public void ObserveSelectedId(int sessionNumber, int id)
    {
        if (sessionNumber <= 0 || id <= 0)
            return;
        lock (_sync)
        {
            if (!_selections.TryGetValue(sessionNumber, out List<int>? list))
            {
                if (_selections.Count >= MaxSelections)
                {
                    _capacityExceeded = true;
                    return;
                }
                list = [];
                _selections.Add(sessionNumber, list);
            }
            if (list.Count < 3)
                list.Add(id);
            else
                _capacityExceeded = true;
        }
    }

    /// <summary>
    /// Each Game flow is linked only to the sole unused, closed Connection
    /// selection in the SAME WINDOW since its previous game attempt.
    /// A ksc+ksb Game flow may still be alive: closure is NOT required.
    /// Global TCP adjacency and destination IP are never used as identity.
    /// Multiple selectors remain ambiguous even if they carry the same ID.
    /// </summary>
    public IReadOnlyList<string> Summarize(int draconirosId = 295, int kourialId = 355)
    {
        if (draconirosId <= 0 || kourialId <= 0 || draconirosId == kourialId)
            throw new ArgumentException("Two positive and distinct references required.");
        lock (_sync)
        {
            List<string> output = [];
            Flow[] all = _flows.Values.ToArray();
            int[] slots = all.Where(f => !f.OwnerConflict && f.WindowSlot.HasValue)
                .Select(f => f.WindowSlot!.Value).Distinct().OrderBy(x => x).ToArray();
            int distinctReadyWindows = 0;
            int loadedPairs = 0;
            int matchedReferences = 0;

            foreach (int slot in slots)
            {
                Flow[] relevant = all.Where(f =>
                        !f.OwnerConflict && f.WindowSlot == slot && f.Number > 0)
                    .OrderBy(f => f.Number).ToArray();
                int boundSelectors = relevant.Count(f => _selections.ContainsKey(f.Number));
                int gameAttempts = 0;
                int linked = 0;
                int loaded = 0;
                int knownRef = 0;
                int unclassified = 0;
                int lastGameSession = 0;
                bool ambiguous = relevant.Any(f => f.Evicted);
                string reason = ambiguous ? "evicted-flow" : "none";

                foreach (Flow game in relevant.Where(f => f.Ksc))
                {
                    gameAttempts++;
                    Flow[] candidates = relevant.Where(f =>
                        f.Number > lastGameSession && f.Number < game.Number &&
                        !f.Ksc && !f.Ksb &&
                        _selections.ContainsKey(f.Number)).ToArray();
                    lastGameSession = game.Number;

                    // A running game connection is NOT expected to close when
                    // the user presses Ctrl+C. ksc + ksb are the load markers.
                    string gameState = game.Ksb
                        ? (game.Closed ? "loaded-closed" : "loaded-live")
                        : (game.Closed ? "ksc-only-closed" : "ksc-only-live");
                    int distinctIds = candidates.SelectMany(candidate =>
                            _selections[candidate.Number])
                        .Distinct().Count();
                    string attemptReason = "none";

                    if (candidates.Length == 0)
                        attemptReason = "selection-missing";
                    else if (candidates.Length > 1)
                        attemptReason = distinctIds > 1
                            ? "multiple-distinct-selections"
                            : "multiple-selections";
                    else if (!candidates[0].Closed || candidates[0].Evicted)
                        attemptReason = "selection-not-closed";
                    else if (_selections[candidates[0].Number].Count != 1)
                        attemptReason = "selection-multiple-values";
                    else if (!game.Ksb && !game.Closed)
                        attemptReason = "game-incomplete";

                    if (attemptReason == "selection-missing" && game.Ksb)
                    {
                        // Explicitly show observed prior loaded context, but do
                        // NOT inherit its server: a separate selection with an
                        // unresolved PID could belong to this very window.
                        Flow[] earlierGames = relevant.Where(f =>
                                f.Ksc && f.Ksb && f.Number < game.Number)
                            .OrderByDescending(f => f.Number).ToArray();
                        output.Add("p3m13=selection-gap window-index=" + slot +
                            " game-session=" + game.Number +
                            " previous-loaded-game=" +
                            (earlierGames.Length > 0 ? "observed" : "unavailable") +
                            " same-window=yes" +
                            " new-selection=not-observed-in-window" +
                            " inherited-server=unverified" +
                            " account=unverified server=unverified");
                    }

                    output.Add("p3m13=attempt window-index=" + slot +
                        " game-session=" + game.Number +
                        " game-state=" + gameState +
                        " preceding-selections=" + candidates.Length +
                        " distinct-selection-values=" + distinctIds +
                        " status=" + (attemptReason == "none"
                            ? "candidate-linked" : "unverified") +
                        " reason=" + attemptReason +
                        " account=unverified server=unverified");

                    if (attemptReason != "none")
                    {
                        ambiguous = true;
                        if (reason == "none")
                            reason = attemptReason;
                        continue;
                    }

                    linked++;
                    if (!game.Ksb)
                        continue;
                    loaded++;
                    int selected = _selections[candidates[0].Number][0];
                    string reference = selected == draconirosId
                        ? "Draconiros" : selected == kourialId
                            ? "Kourial" : "unknown";
                    if (reference == "unknown") unclassified++;
                    else knownRef++;
                    output.Add("p3m13=loaded window-index=" + slot +
                        " connection-session=" + candidates[0].Number +
                        " game-session=" + game.Number +
                        " reference=" + reference +
                        " match=community-candidate" +
                        " account=unverified server=unverified");
                }

                // Reject selectors that were never consumed by a game
                // attempt (e.g. interleaved login or lost packets).
                bool hasStraySelectors = boundSelectors != linked;
                if (hasStraySelectors && reason == "none")
                    reason = "unpaired-selection";
                if (gameAttempts == 0 && reason == "none")
                    reason = "game-missing";
                if (unclassified != 0 && reason == "none")
                    reason = "unknown-reference";
                bool ready = !ambiguous && !hasStraySelectors &&
                    gameAttempts > 0 && linked == gameAttempts && loaded > 0 &&
                    unclassified == 0 && knownRef == loaded;
                if (ready)
                    distinctReadyWindows++;
                loadedPairs += loaded;
                matchedReferences += knownRef;
                output.Add("p3m13=window window-index=" + slot +
                    " connection-selections=" + boundSelectors +
                    " game-attempts=" + gameAttempts +
                    " linked=" + linked +
                    " loaded=" + loaded +
                    " references-matched=" + knownRef +
                    " status=" + (ready ? "linked-candidate" : "unverified") +
                    " reason=" + (ready ? "none" : reason) +
                    " account=unverified server=unverified");
            }

            // Ignore unrelated port-5555 connections; their ownership does
            // not affect a candidate selector or an observed game load.
            Flow[] relevantFlows = all.Where(f =>
                f.Ksc || (f.Number > 0 && _selections.ContainsKey(f.Number)))
                .ToArray();
            // Missing selector owners may explain a "selection-missing"
            // in a different window. Report their anonymized session numbers
            // and role, not addresses, PIDs or candidate values.
            foreach (Flow unmatched in relevantFlows
                .Where(f => f.Pid is null || !f.WindowSlot.HasValue ||
                    f.OwnerConflict).OrderBy(f => f.Number))
            {
                bool selection = unmatched.Number > 0 &&
                    _selections.ContainsKey(unmatched.Number);
                string kind = selection && unmatched.Ksc
                    ? "selection-and-game"
                    : selection ? "selection"
                    : unmatched.Ksc ? "game" : "other";
                string unresolved = unmatched.OwnerConflict
                    ? "owner-conflict"
                    : !unmatched.Pid.HasValue
                        ? "owner-missing"
                        : "window-missing";
                output.Add("p3m13=unbound session=" + unmatched.Number +
                    " role=" + kind +
                    " reason=" + unresolved +
                    " selection-candidates=" +
                    (selection ? _selections[unmatched.Number].Count : 0) +
                    " account=unverified server=unverified");
            }

            int missingOwner = relevantFlows.Count(f => f.Pid is null);
            int noWindow = relevantFlows.Count(f => f.Pid.HasValue && !f.WindowSlot.HasValue);
            int conflicts = relevantFlows.Count(f => f.OwnerConflict);
            bool two = distinctReadyWindows >= 2 && !_capacityExceeded &&
                conflicts == 0 && missingOwner == 0 && noWindow == 0;
            output.Add("p3m13=summary flows=" + all.Length +
                " windows-linked=" + slots.Length +
                " windows-ready=" + distinctReadyWindows +
                " loaded-pairs=" + loadedPairs +
                " references-matched=" + matchedReferences +
                " owner-missing=" + missingOwner +
                " window-missing=" + noWindow +
                " conflicts=" + conflicts +
                " overflow=" + (_capacityExceeded ? "yes" : "no") +
                " status=" + (two ? "two-window-links-candidate" : "not-confirmed") +
                " account=unverified server=unverified");
            return output;
        }
    }

    private Flow? GetOrAdd(TcpSocketTuple tuple)
    {
        if (_flows.TryGetValue(tuple, out Flow? flow))
            return flow;
        if (_flows.Count >= MaxFlows)
        {
            _capacityExceeded = true;
            return null;
        }
        flow = new Flow(tuple);
        _flows.Add(tuple, flow);
        return flow;
    }
}
