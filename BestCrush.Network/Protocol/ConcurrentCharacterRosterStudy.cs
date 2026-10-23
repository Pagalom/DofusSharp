using BestCrush.Network.Capture;

namespace BestCrush.Network.Protocol;

/// <summary>
/// P3c.6 research-only: two simultaneous Game TCP sessions (A and B),
/// followed by B reconnecting while A remains live. Compares the decoded
/// S->C ksc roster candidates and same-flow ksb selections, not Ankama
/// accounts. No OS-window or verified game-server identity is asserted.
/// All candidate IDs/names are held only in bounded in-memory state.
/// </summary>
public sealed class ConcurrentCharacterRosterStudy
{
    private const int StageCount = 3;
    private const int MaxNewSessions = 32;
    private const int MaxKscPerStage = 8;
    private const int MaxKsbPerStage = 8;

    private sealed class StageState
    {
        internal readonly HashSet<int> Opened = [];
        internal int? GameSession;
        internal HashSet<ulong>? Roster;
        internal CharacterWireCandidateSnapshot? List;
        internal ulong? SelectedId;
        internal int KscCount;
        internal int KsbCount;
        internal bool Closed;
        internal bool Conflicted;
        internal bool OverlapWithA;
        internal string Reason = "none";
        internal bool Linked => !Conflicted &&
            GameSession.HasValue && Roster is not null && SelectedId.HasValue;
    }

    private readonly object _gate = new();
    private readonly StageState[] _stages =
        Enumerable.Range(0, StageCount).Select(_ => new StageState()).ToArray();
    // TCP lifecycle token -> stage where it was first seen. Never reassign.
    private readonly Dictionary<int, int> _sessionStage = [];
    private int _current = -1;
    private bool _primaryClosedEarly;

    public override string ToString() => "[concurrent roster study: redacted]";

    public bool StartStage(int stage)
    {
        lock (_gate)
        {
            if (stage < 0 || stage >= StageCount || stage != _current + 1)
                return false;

            if (stage == 1 &&
                (!_stages[0].Linked || _stages[0].Closed))
                return false;

            if (stage == 2 &&
                (!_stages[0].Linked || _stages[0].Closed ||
                 !_stages[1].Linked || !_stages[1].Closed))
                return false;

            _current = stage;
            return true;
        }
    }

    public void ObserveOpenedSession(int sessionNumber)
    {
        if (sessionNumber <= 0)
            return;

        lock (_gate)
        {
            if (_current < 0)
                return;
            StageState stage = _stages[_current];
            if (_sessionStage.ContainsKey(sessionNumber))
                return;
            if (stage.Opened.Count >= MaxNewSessions)
            {
                Reject(stage, "session-limit");
                return;
            }
            stage.Opened.Add(sessionNumber);
            _sessionStage.Add(sessionNumber, _current);
        }
    }

    public void ObserveClosedSession(int sessionNumber)
    {
        lock (_gate)
        {
            if (!_sessionStage.TryGetValue(sessionNumber, out int index))
                return;
            StageState stage = _stages[index];
            if (stage.GameSession != sessionNumber)
                return; // Ignore brief connection-server flows.

            stage.Closed = true;
            if (index == 0 &&
                (_current < 2 || !_stages[2].Linked))
                _primaryClosedEarly = true;
        }
    }

    public string? Observe(
        int sessionNumber, CharacterCaptureDirection direction,
        string opcode, ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 ||
            direction != CharacterCaptureDirection.ServerToClient ||
            opcode is not ("ksc" or "ksb"))
            return null;

        lock (_gate)
        {
            if (!_sessionStage.TryGetValue(sessionNumber, out int index))
                return null;

            StageState stage = _stages[index];
            if (stage.Conflicted)
                return Report(index, "conflicted");
            if (stage.Closed)
                return Report(index, "closed");

            if (stage.GameSession is { } previous && previous != sessionNumber)
            {
                Reject(stage, "multiple-game-sessions");
                return Report(index, "conflicted");
            }
            stage.GameSession = sessionNumber; // Reserve even on invalid body.

            if (!CharacterWireCandidateDecoder.TryDecode(
                    opcode, body, out CharacterWireCandidateSnapshot? decoded) ||
                decoded is null)
            {
                Reject(stage, "layout-rejected");
                return Report(index, "conflicted");
            }

            if (opcode == "ksc")
            {
                if (++stage.KscCount > MaxKscPerStage)
                {
                    Reject(stage, "ksc-limit");
                    return Report(index, "conflicted");
                }

                HashSet<ulong> roster = decoded.Candidates
                    .Select(candidate => candidate.CharacterIdCandidate)
                    .ToHashSet();
                if (roster.Count == 0 ||
                    stage.Roster is not null &&
                    !stage.Roster.SetEquals(roster))
                {
                    Reject(stage, "roster-changed");
                    return Report(index, "conflicted");
                }

                stage.Roster ??= roster;
                stage.List = decoded;
            }
            else
            {
                if (++stage.KsbCount > MaxKsbPerStage ||
                    decoded.Candidates.Count != 1)
                {
                    Reject(stage, "ksb-limit");
                    return Report(index, "conflicted");
                }

                ulong id = decoded.Candidates[0].CharacterIdCandidate;
                if (stage.SelectedId is { } selected && selected != id)
                {
                    Reject(stage, "selection-changed");
                    return Report(index, "conflicted");
                }

                if (stage.List is null ||
                    !CharacterWireCandidateDecoder.HasMatchingCandidate(
                        decoded, stage.List))
                {
                    Reject(stage, "selection-not-in-roster");
                    return Report(index, "conflicted");
                }

                stage.SelectedId = id;
                stage.List = null; // Drop names/levels as soon as linked.
            }

            if (index > 0 && stage.Linked)
                stage.OverlapWithA |= _stages[0].Linked &&
                                      !_stages[0].Closed && !_primaryClosedEarly;

            return Report(index, stage.Linked ? "linked" : "pending");
        }
    }

    public IReadOnlyList<string> Summarize()
    {
        lock (_gate)
        {
            List<string> output = [];
            for (int i = 0; i < StageCount; i++)
            {
                StageState stage = _stages[i];
                output.Add("p3c6=stage=" + (i + 1) +
                    " status=" + (stage.Conflicted ? "conflicted" :
                        stage.Linked ? "linked" : "incomplete") +
                    " roster-rows=" + (stage.Roster?.Count ?? 0) +
                    " game-closed=" + (stage.Closed ? "yes" : "no") +
                    " overlap-with-A=" + (stage.OverlapWithA ? "yes" : "no") +
                    " reason=" + stage.Reason +
                    " account=unverified server=unverified");
            }

            if (_current != StageCount - 1 ||
                _stages.Any(s => !s.Linked) || _primaryClosedEarly ||
                !_stages[1].Closed || !_stages[1].OverlapWithA ||
                !_stages[2].OverlapWithA)
            {
                output.Add("p3c6=summary status=incomplete" +
                    " primary-closed-early=" + YesNo(_primaryClosedEarly) +
                    " account=unverified server=unverified semantic=unverified");
                return output;
            }

            StageState a = _stages[0], b1 = _stages[1], b2 = _stages[2];
            bool bStable = b1.Roster!.SetEquals(b2.Roster!) &&
                           b1.SelectedId == b2.SelectedId;
            bool disjoint = !a.Roster!.Overlaps(b1.Roster!) &&
                            !a.Roster.Overlaps(b2.Roster);
            bool distinct = !a.Roster.SetEquals(b1.Roster);

            output.Add("p3c6=summary status=" +
                (bStable && disjoint && distinct
                    ? "concurrent-roster-group-candidate"
                    : "pattern-not-confirmed") +
                " B-reconnect-roster-stable=" + YesNo(bStable) +
                " A-B-disjoint=" + YesNo(disjoint) +
                " A-B-distinct=" + YesNo(distinct) +
                " both-overlap-A=yes" +
                " account=unverified server=unverified semantic=unverified");
            return output;
        }
    }

    private string Report(int stage, string status) =>
        "p3c6=stage=" + (stage + 1) + " status=" + status +
        " account=unverified server=unverified";

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static void Reject(StageState stage, string reason)
    {
        stage.Conflicted = true;
        stage.Reason = reason;
        stage.Roster = null;
        stage.SelectedId = null;
        stage.List = null;
    }
}
