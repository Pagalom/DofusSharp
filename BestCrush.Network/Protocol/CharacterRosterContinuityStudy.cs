using BestCrush.Network.Capture;

namespace BestCrush.Network.Protocol;

/// <summary>
/// P3c.5 RESEARCH ONLY: compare selectable-character rosters from S->C ksc
/// and the loaded character candidate from S->C ksb on the same TCP game
/// flow in four operator-controlled phases (A-X, A-Y, B-Z, A-X).
/// This is not an Ankama account ID, authentication or verified account link.
/// Candidate identifiers and names remain transient RAM-only data.
/// </summary>
public sealed class CharacterRosterContinuityStudy
{
    private const int PhaseCount = 4;
    private const int MaxOpenSessionsPerPhase = 32;

    private sealed class Phase
    {
        internal readonly HashSet<int> Opened = [];
        internal int? GameSession;
        internal HashSet<ulong>? RosterIds;
        internal CharacterWireCandidateSnapshot? Selection;
        internal ulong? SelectedId;
        internal bool Conflict;
        internal string Reason = "none";
        internal int KscCount;
        internal int KsbCount;
    }

    private readonly object _gate = new();
    private readonly Phase[] _phases =
        Enumerable.Range(0, PhaseCount).Select(_ => new Phase()).ToArray();
    private int _current = -1;

    public override string ToString() => "[roster study: values redacted]";

    public bool StartPhase(int phaseIndex)
    {
        lock (_gate)
        {
            if (phaseIndex != _current + 1 || phaseIndex < 0 ||
                phaseIndex >= PhaseCount)
                return false;
            _current = phaseIndex;
            return true;
        }
    }

    public void ObserveOpenedSession(int sessionNumber)
    {
        lock (_gate)
        {
            if (_current < 0 || sessionNumber <= 0)
                return;
            Phase current = _phases[_current];
            if (current.Opened.Count >= MaxOpenSessionsPerPhase)
            {
                Reject(current, "session-limit");
                return;
            }
            current.Opened.Add(sessionNumber);
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
            if (_current < 0)
                return null;
            Phase phase = _phases[_current];
            if (!phase.Opened.Contains(sessionNumber))
                return null;
            if (phase.Conflict)
                return Describe("conflicted");

            // One distinct game TCP flow per operator phase. The first
            // ksc/ksb reserves the session, including when malformed.
            if (phase.GameSession is { } existing && existing != sessionNumber)
            {
                Reject(phase, "multiple-game-sessions");
                return Describe("conflicted");
            }
            phase.GameSession = sessionNumber;

            if (!CharacterWireCandidateDecoder.TryDecode(
                    opcode, body, out CharacterWireCandidateSnapshot? decoded) ||
                decoded is null)
            {
                Reject(phase, "layout-rejected");
                return Describe("conflicted");
            }

            if (opcode == "ksc")
            {
                if (++phase.KscCount > 8)
                {
                    Reject(phase, "repeated-ksc");
                    return Describe("conflicted");
                }

                HashSet<ulong> ids = decoded.Candidates
                    .Select(p => p.CharacterIdCandidate)
                    .ToHashSet();
                if (ids.Count == 0 ||
                    phase.RosterIds is not null &&
                    !phase.RosterIds.SetEquals(ids))
                {
                    Reject(phase, "roster-changed");
                    return Describe("conflicted");
                }

                phase.RosterIds ??= ids;
                phase.Selection = decoded;
            }
            else
            {
                if (++phase.KsbCount > 8 || decoded.Candidates.Count != 1)
                {
                    Reject(phase, "repeated-ksb");
                    return Describe("conflicted");
                }

                ulong selected = decoded.Candidates[0].CharacterIdCandidate;
                if (phase.SelectedId is { } prior && prior != selected)
                {
                    Reject(phase, "selection-changed");
                    return Describe("conflicted");
                }

                phase.SelectedId = selected;
                if (phase.Selection is null ||
                    !CharacterWireCandidateDecoder.HasMatchingCandidate(
                        decoded, phase.Selection))
                {
                    Reject(phase, "selection-not-in-roster");
                    return Describe("conflicted");
                }
                // Drop transient names and level after same-flow check.
                phase.Selection = null;
            }

            return Describe(phase.RosterIds is not null &&
                phase.SelectedId is not null ? "linked" : "pending");
        }
    }

    public IReadOnlyList<string> Summarize()
    {
        lock (_gate)
        {
            List<string> output = [];
            foreach (var (phase, index) in _phases.Select((p, i) => (p, i)))
            {
                string status = phase.Conflict ? "conflicted" :
                    phase.RosterIds is not null && phase.SelectedId is not null
                        ? "linked" : "incomplete";
                output.Add($"p3c5=phase={index + 1} status={status}" +
                    $" roster-rows={phase.RosterIds?.Count ?? 0}" +
                    $" reason={phase.Reason}" +
                    " account=unverified server=unverified");
            }

            if (_current != PhaseCount - 1 ||
                _phases.Any(phase => phase.Conflict ||
                    phase.RosterIds is null || phase.SelectedId is null))
            {
                output.Add("p3c5=summary status=incomplete " +
                    "account=unverified server=unverified semantic=unverified");
                return output;
            }

            Phase ax = _phases[0], ay = _phases[1],
                bz = _phases[2], ax2 = _phases[3];
            bool sameRosterWithinAccount =
                ax.RosterIds!.SetEquals(ay.RosterIds!) &&
                ax.RosterIds.SetEquals(ax2.RosterIds!);
            bool differentRosterAcrossAccounts =
                !ax.RosterIds.SetEquals(bz.RosterIds!);
            bool disjointRosters = !ax.RosterIds.Overlaps(bz.RosterIds);
            bool distinctXandY = ax.SelectedId != ay.SelectedId;
            bool sameXandX2 = ax.SelectedId == ax2.SelectedId;

            bool candidate = sameRosterWithinAccount &&
                differentRosterAcrossAccounts && disjointRosters &&
                distinctXandY && sameXandX2;

            output.Add("p3c5=summary status=" +
                (candidate ? "roster-group-candidate" : "pattern-not-confirmed") +
                " same-account-rosters=" + YesNo(sameRosterWithinAccount) +
                " cross-account-distinct=" + YesNo(differentRosterAcrossAccounts) +
                " cross-account-disjoint=" + YesNo(disjointRosters) +
                " selected-x-y-distinct=" + YesNo(distinctXandY) +
                " selected-x-return-equal=" + YesNo(sameXandX2) +
                " account=unverified server=unverified semantic=unverified");
            return output;
        }
    }

    private string Describe(string status) =>
        $"p3c5=phase={_current + 1} status={status} " +
        "account=unverified server=unverified";

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static void Reject(Phase phase, string reason)
    {
        phase.Conflict = true;
        phase.Reason = reason;
        phase.RosterIds = null;
        phase.SelectedId = null;
        phase.Selection = null;
    }
}
