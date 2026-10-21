using BestCrush.Network.Protocol;

namespace BestCrush.Network.Capture;

/// <summary>
/// P3m.7: targeted kta.f1 four-phase character-vs-server control.
/// Research only; never asserts a semantic character/account/server ID.
/// Values remain in RAM and are never formatted, logged or persisted.
/// </summary>
public sealed class KtaFieldScopeStudy
{
    private const int MaxSessions = 32;
    private const int MaxKtaPerSession = 24;
    private const int MaxBodyBytes = 1024;
    private const int MaxRootFields = 64;
    private readonly object _gate = new();
    private readonly Dictionary<int, Session> _sessions = [];
    private int _stage = -1;
    private bool _capacityExceeded;

    private sealed class Session(int number, int stage, string address, int port)
    {
        internal readonly int Number = number;
        internal readonly int Stage = stage;
        internal readonly string Address = address;
        internal readonly int Port = port;
        internal readonly HashSet<ulong> KtaValues = [];
        internal int KtaMessages;
        internal bool KtaInvalid;
        internal bool Ksc;
        internal bool Ksb;
        internal bool Closed;
        internal bool Evicted;
    }

    public bool StartStage(int stage)
    {
        lock (_gate)
        {
            if (stage is < 0 or > 3 || stage != _stage + 1)
                return false;
            _stage = stage;
            return true;
        }
    }

    public void ObserveEvents(
        CharacterCaptureFlowKey key, IReadOnlyList<CharacterCaptureEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        lock (_gate)
        {
            foreach (CharacterCaptureEvent evt in events)
            {
                if (evt.SessionNumber <= 0)
                    continue;
                if (evt.Kind == CharacterCaptureEventKind.Opened)
                {
                    GetOrAdd(evt.SessionNumber, key.ServerAddress, key.ServerPort);
                    continue;
                }
                if (!_sessions.TryGetValue(evt.SessionNumber, out Session? s))
                    continue;
                if (evt.Kind == CharacterCaptureEventKind.Closed)
                    s.Closed = true;
                else if (evt.Kind == CharacterCaptureEventKind.Evicted)
                    s.Evicted = true;
                else if (evt.Kind == CharacterCaptureEventKind.Message &&
                         evt.Direction == CharacterCaptureDirection.ServerToClient)
                {
                    if (evt.Opcode == "ksc") s.Ksc = true;
                    if (evt.Opcode == "ksb") s.Ksb = true;
                }
            }
        }
    }

    public void ObserveMessage(int sessionNumber, CharacterCaptureFlowKey key,
        CharacterCaptureDirection direction, string opcode,
        ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 || opcode != "kta" ||
            direction != CharacterCaptureDirection.ClientToServer)
            return;

        lock (_gate)
        {
            // A SYN may be missed when capture starts during TCP setup.
            // The four-tuple still determines this session's remote endpoint.
            Session? session = GetOrAdd(
                sessionNumber, key.ServerAddress, key.ServerPort);
            if (session is null || session.Closed || session.Evicted ||
                session.Ksb)
                return;

            if (session.KtaMessages >= MaxKtaPerSession)
            {
                session.KtaInvalid = true;
                return;
            }
            session.KtaMessages++;
            if (body.Length is < 1 or > MaxBodyBytes)
            {
                session.KtaInvalid = true;
                return;
            }

            List<ProtoField>? fields;
            try
            {
                fields = ProtoWire.ReadFields(body);
            }
            catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException)
            {
                session.KtaInvalid = true;
                return;
            }

            if (fields is null || fields.Count is < 1 or > MaxRootFields)
            {
                session.KtaInvalid = true;
                return;
            }

            ProtoField[] candidate = fields.Where(f => f.Number == 1).ToArray();
            if (candidate.Length != 1 ||
                candidate[0].WireType != ProtoWireType.Varint)
            {
                session.KtaInvalid = true;
                return;
            }
            session.KtaValues.Add(candidate[0].Varint);
        }
    }

    public IReadOnlyList<string> Summarize()
    {
        lock (_gate)
        {
            string[] labels = ["A-X1", "A-Y", "B-Z", "A-X2"];
            ulong[] candidates = new ulong[4];
            bool comparable = _stage == 3 && !_capacityExceeded;
            List<string> lines = [];

            for (int stage = 0; stage < 4; stage++)
            {
                Session[] sessions = _sessions.Values
                    .Where(s => s.Stage == stage).ToArray();
                Session[] gameCandidates = sessions.Where(s =>
                    s.Ksc && s.Ksb).ToArray();
                Session[] game = gameCandidates.Where(s =>
                    s.Closed && !s.Evicted).ToArray();
                HashSet<ulong> distinct = [];
                foreach (Session s in sessions)
                    distinct.UnionWith(s.KtaValues);

                bool valid = gameCandidates.Length == 1 &&
                    game.Length == 1 &&
                    !sessions.Any(s => s.KtaInvalid || s.Evicted) &&
                    sessions.Sum(s => s.KtaMessages) > 0 &&
                    distinct.Count == 1;
                if (!valid)
                    comparable = false;
                if (valid)
                    candidates[stage] = distinct.Single();

                Session? selected = game.Length == 1 ? game[0] : null;
                string endpoint = selected is null ? "unavailable" :
                    selected.Address.Contains(':')
                        ? "[" + selected.Address + "]:" + selected.Port
                        : selected.Address + ":" + selected.Port;

                lines.Add("p3m7=phase-" + labels[stage] +
                    " tcp-sessions=" + sessions.Length +
                    " completed-game=" + game.Length +
                    " kta-messages=" + sessions.Sum(s => s.KtaMessages) +
                    " kta-distinct-values=" + distinct.Count +
                    " valid=" + (valid ? "yes" : "no") +
                    " game-session=" + (selected is null
                        ? "unavailable" : selected.Number.ToString()) +
                    " destination=" + endpoint +
                    " server=unverified");
            }

            if (!comparable)
            {
                lines.Add("p3m7=summary comparable=no kta-f1-scope=unavailable " +
                    "reason=" + (_capacityExceeded ? "session-limit" :
                        "missing-conflicting-or-incomplete-phase") +
                    " semantic=unverified server=unverified");
                return lines;
            }

            bool repeat = candidates[0] == candidates[3];
            bool sameServer = candidates[0] == candidates[1];
            bool differentServer = candidates[0] == candidates[2];

            string scope = !repeat ? "unstable-candidate" :
                !sameServer ? "character-or-selection-candidate" :
                differentServer ? "account-wide-or-constant-candidate" :
                "server-or-account-server-candidate";

            lines.Add("p3m7=summary comparable=yes" +
                " reconnect-X=" + (repeat ? "equal" : "different") +
                " same-server-X-vs-Y=" + (sameServer ? "equal" : "different") +
                " cross-server-X-vs-Z=" + (differentServer ? "equal" : "different") +
                " kta-f1-scope=" + scope +
                " semantic=unverified server=unverified");
            return lines;
        }
    }

    private Session? GetOrAdd(int number, string address, int port)
    {
        if (_sessions.TryGetValue(number, out Session? found))
            return found;
        if (_stage < 0)
            return null;
        if (_sessions.Count >= MaxSessions)
        {
            _capacityExceeded = true;
            return null;
        }
        Session s = new(number, _stage, address, port);
        _sessions.Add(number, s);
        return s;
    }
}
