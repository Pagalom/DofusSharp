namespace BestCrush.Network.Capture;

/// <summary>
/// P3m.5: opt-in, observation-only A/B/A comparison of TCP destinations.
/// Server stages are user-declared, never protocol-verified.
/// No IP, port, account, character or server name is printed or persisted.
/// </summary>
public sealed class ServerRouteTransitionStudy
{
    private const int MaxSessions = 24;
    private readonly object _gate = new();
    private readonly Dictionary<int, Session> _sessions = [];
    private int _stage = -1;

    private sealed class Session(int stage, string remoteAddress, int remotePort)
    {
        internal readonly int Stage = stage;
        internal readonly string RemoteAddress = remoteAddress;
        internal readonly int RemotePort = remotePort;
        internal bool Ksc;
        internal bool Ksb;
        internal bool Closed;
        internal bool Invalid;
    }

    /// <summary>Starts stage 0=A, 1=B, 2=A; only user actions move stages.</summary>
    public bool StartStage(int stage)
    {
        lock (_gate)
        {
            if (stage is < 0 or > 2 || stage != _stage + 1)
                return false;
            _stage = stage;
            return true;
        }
    }

    /// <summary>
    /// Observe events from the existing independent TCP flow tracker.
    /// This method never tries to decode character identity or discover
    /// which user account/server was selected.
    /// </summary>
    public void Observe(CharacterCaptureFlowKey key,
        IReadOnlyList<CharacterCaptureEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        lock (_gate)
        {
            foreach (CharacterCaptureEvent entry in events)
            {
                if (entry.SessionNumber <= 0)
                    continue;

                if (entry.Kind == CharacterCaptureEventKind.Opened)
                {
                    if (_stage < 0 || _sessions.Count >= MaxSessions)
                        continue;

                    _sessions.TryAdd(entry.SessionNumber, new Session(
                        _stage, key.ServerAddress, key.ServerPort));
                    continue;
                }

                if (!_sessions.TryGetValue(entry.SessionNumber, out Session? s))
                    continue;

                if (entry.Kind == CharacterCaptureEventKind.Evicted)
                {
                    // Idle/capacity eviction does not prove a normal socket
                    // termination; fail closed rather than trust its route.
                    s.Invalid = true;
                    continue;
                }
                if (entry.Kind == CharacterCaptureEventKind.Closed)
                {
                    s.Closed = true;
                    continue;
                }

                if (entry.Kind != CharacterCaptureEventKind.Message ||
                    entry.Direction != CharacterCaptureDirection.ServerToClient)
                    continue;

                if (entry.Opcode == "ksc")
                    s.Ksc = true;
                else if (entry.Opcode == "ksb")
                    s.Ksb = true;
            }
        }
    }

    public IReadOnlyList<string> Summarize()
    {
        lock (_gate)
        {
            var stages = new Session?[3];
            List<string> output = [];
            for (int index = 0; index < 3; index++)
            {
                Session[] all = _sessions.Values.Where(s => s.Stage == index).ToArray();
                Session[] complete = all.Where(s => s.Ksc && s.Ksb).ToArray();
                Session[] usable = complete.Where(s => s.Closed && !s.Invalid).ToArray();
                if (complete.Length == 1 && usable.Length == 1)
                    stages[index] = usable[0];

                output.Add("p3m5=stage-" + (index == 1 ? "B" : "A" + (index == 0 ? "1" : "2")) +
                    " tcp-sessions=" + all.Length +
                    " ksc-ksb-sessions=" + complete.Length +
                    " closed-complete=" + usable.Length +
                    " comparable=" + (stages[index] is null ? "no" : "yes") +
                    " server=unverified");
            }

            if (stages.Any(s => s is null))
            {
                output.Add("p3m5=summary destination-pattern=unavailable " +
                           "reason=incomplete-or-ambiguous server=unverified");
                return output;
            }

            static bool EqualsRoute(Session a, Session b) =>
                a.RemotePort == b.RemotePort &&
                string.Equals(a.RemoteAddress, b.RemoteAddress, StringComparison.Ordinal);

            bool firstThird = EqualsRoute(stages[0]!, stages[2]!);
            bool firstSecond = EqualsRoute(stages[0]!, stages[1]!);
            bool secondThird = EqualsRoute(stages[1]!, stages[2]!);

            string pattern = firstThird && !firstSecond
                ? "A-B-A"
                : firstSecond && secondThird
                    ? "same-all"
                    : "other";

            output.Add("p3m5=summary destination-pattern=" + pattern +
                " A1-vs-A2=" + (firstThird ? "equal" : "different") +
                " A1-vs-B=" + (firstSecond ? "equal" : "different") +
                " B-vs-A2=" + (secondThird ? "equal" : "different") +
                " server=unverified meaning=route-only");
            return output;
        }
    }
}
