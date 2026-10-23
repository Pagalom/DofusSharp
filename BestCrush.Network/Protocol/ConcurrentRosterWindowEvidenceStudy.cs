namespace BestCrush.Network.Protocol;

/// <summary>
/// P3c.7 passive Windows ownership evidence for three operator-controlled
/// Game sessions: A, B, B reconnection. Independently of the network roster
/// candidate, prove a Game TCP tuple has one OS owner PID and exactly one
/// visible Dofus HWND, including the still-live A socket while B loads.
/// This is NOT an authenticated game character, account or server.
/// No endpoints, PIDs or window handles leave RAM.
/// </summary>
public sealed class ConcurrentRosterWindowEvidenceStudy
{
    public readonly record struct WindowCandidate(long Handle, int ProcessId)
    {
        public override string ToString() => "[window: redacted]";
    }

    private sealed class Stage
    {
        internal int? Session;
        internal TcpSocketTuple? Socket;
        internal WindowCandidate? OwnerWindow;
        internal bool Conflict;
        internal bool Closed;
        internal bool PrimaryStillMatched;
        internal string Reason = "none";
        internal bool Matched => !Conflict && Session.HasValue &&
            OwnerWindow.HasValue && Socket.HasValue;
    }

    private readonly object _sync = new();
    private readonly Stage[] _stages = [new(), new(), new()];
    private bool _primaryClosedBeforeThirdLink;

    public override string ToString() => "[game-window evidence: redacted]";

    /// <summary>
    /// Invoked only for a uniquely linked S->C ksc+ksb Game session.
    /// The OS inventory must represent the same instant as that message.
    /// No phase-index inference from an OS window or packet order is allowed.
    /// </summary>
    public string ObserveLinked(
        int stage, int session, TcpSocketTuple socket,
        IReadOnlyList<TcpSocketOwner> owners,
        IReadOnlyList<WindowCandidate> windows)
    {
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(windows);
        if (stage is < 0 or > 2 || session <= 0)
            return "p3c7=ignored account=unverified server=unverified";

        lock (_sync)
        {
            Stage current = _stages[stage];
            if (current.Conflict || current.Closed)
                return Status(stage, "conflicted");

            if (current.Session is { } oldSession && oldSession != session ||
                current.Socket is { } oldSocket && oldSocket != socket)
                return Quarantine(current, stage, "session-or-tuple-change");

            // Reserve a particular flow BEFORE asking the OS for evidence.
            // A missing owner cannot be substituted by another socket.
            current.Session ??= session;
            current.Socket ??= socket;

            WindowCandidate? window = Resolve(socket, owners, windows);
            if (window is null)
            {
                if (current.Matched)
                    return Quarantine(current, stage, "owner-lost");
                return Status(stage, "os-unavailable");
            }

            if (current.OwnerWindow is { } oldWindow &&
                oldWindow != window.Value)
                return Quarantine(current, stage, "owner-changed");

            if (stage > 0)
            {
                Stage first = _stages[0];
                if (!first.Matched || first.Closed ||
                    _primaryClosedBeforeThirdLink)
                    return Status(stage, "A-unavailable");

                WindowCandidate? currentA = Resolve(
                    first.Socket!.Value, owners, windows);
                if (currentA is null || currentA.Value != first.OwnerWindow)
                    return Quarantine(current, stage, "A-owner-not-current");

                if (currentA == window)
                    return Quarantine(current, stage, "A-B-same-window");

                if (stage == 2)
                {
                    Stage b1 = _stages[1];
                    if (!b1.Matched || !b1.Closed ||
                        b1.Session == session || b1.Socket == socket)
                        return Status(stage, "B-reconnect-not-distinct");
                }

                current.PrimaryStillMatched = true;
            }

            current.OwnerWindow = window;
            return Status(stage, "os-matched");
        }
    }

    /// <summary>
    /// A closed Game cannot be revived by delayed messages.
    /// Closing B1 is expected. Closing A before B2 matches invalidates
    /// the concurrent test even if A later reappears with the same HWND.
    /// </summary>
    public void ObserveClosed(int session)
    {
        if (session <= 0)
            return;
        lock (_sync)
        {
            for (int i = 0; i < _stages.Length; i++)
            {
                Stage current = _stages[i];
                if (current.Session != session)
                    continue;
                current.Closed = true;
                if (i == 0 && !_stages[2].Matched)
                    _primaryClosedBeforeThirdLink = true;
            }
        }
    }

    public IReadOnlyList<string> Summarize()
    {
        lock (_sync)
        {
            List<string> result = [];
            for (int i = 0; i < _stages.Length; i++)
            {
                Stage s = _stages[i];
                result.Add("p3c7=stage=" + (i + 1) +
                    " status=" + (s.Conflict ? "conflicted" :
                        s.Matched ? "os-matched" : "unavailable") +
                    " game-closed=" + Bool(s.Closed) +
                    " A-still-matched=" + (i == 0 ? "not-applicable" :
                        Bool(s.PrimaryStillMatched)) +
                    " reason=" + s.Reason +
                    " account=unverified server=unverified");
            }

            bool complete = _stages.All(s => s.Matched) &&
                _stages[1].Closed &&
                !_primaryClosedBeforeThirdLink &&
                _stages[1].PrimaryStillMatched &&
                _stages[2].PrimaryStillMatched &&
                _stages[0].OwnerWindow != _stages[1].OwnerWindow &&
                _stages[0].OwnerWindow != _stages[2].OwnerWindow &&
                _stages[0].Session != _stages[1].Session &&
                _stages[1].Session != _stages[2].Session;

            result.Add("p3c7=summary status=" +
                (complete ? "window-roster-link-candidate" : "incomplete") +
                " A-window-stable-across-B=" +
                    Bool(_stages[1].PrimaryStillMatched &&
                        _stages[2].PrimaryStillMatched) +
                " A-B-distinct-windows=" +
                    Bool(_stages[0].Matched && _stages[1].Matched &&
                         _stages[2].Matched &&
                         _stages[0].OwnerWindow != _stages[1].OwnerWindow &&
                         _stages[0].OwnerWindow != _stages[2].OwnerWindow) +
                " account=unverified server=unverified semantic=unverified");
            return result;
        }
    }

    private static WindowCandidate? Resolve(
        TcpSocketTuple socket,
        IReadOnlyList<TcpSocketOwner> owners,
        IReadOnlyList<WindowCandidate> windows)
    {
        int? pid = TcpSocketOwnerMatcher.FindUniqueOwner(socket, owners);
        if (pid is null)
            return null;

        WindowCandidate? unique = null;
        foreach (WindowCandidate window in windows)
        {
            if (window.ProcessId != pid)
                continue;

            if (window.Handle == 0 || unique.HasValue)
                return null;
            unique = window;
        }
        return unique;
    }

    private string Quarantine(Stage state, int stage, string reason)
    {
        state.Conflict = true;
        state.Reason = reason;
        state.OwnerWindow = null;
        return Status(stage, "conflicted");
    }

    private static string Status(int stage, string status) =>
        "p3c7=stage=" + (stage + 1) + " status=" + status +
        " account=unverified server=unverified";

    private static string Bool(bool value) => value ? "yes" : "no";
}
