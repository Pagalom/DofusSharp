using BestCrush.Network.Protocol;

namespace BestCrush.Network.Capture;

/// <summary>
/// P3m.11: observation-only candidate for the connection-server selection
/// envelope, distinct from type.ankama.com/* game messages.
/// Two documented Unity schemas both encode:
///   outer.f1 request/auth -> f4 selectServer -> f1 varint serverId.
/// Requiring a short UTF-8 string at outer.f1.f1 rejects some unrelated
/// protobuf structures, but never proves schema identity.
/// No authentication token, UUID, account or character data is surfaced.
/// </summary>
public sealed class ConnectionServerSelectionStudy
{
    private const int MaxSessions = 24;
    private const int MaxFramesPerSession = 192;
    private const int MaxFrameBytes = 8192;
    private readonly object _gate = new();
    private readonly Dictionary<int, Session> _sessions = [];
    private bool _capacityExceeded;
    private int _stage = -1;

    private sealed class Session(int number, int stage, string address, int port)
    {
        internal readonly int Number = number;
        internal readonly int Stage = stage;
        internal readonly string Address = address;
        internal readonly int Port = port;
        internal readonly HashSet<int> SelectedIds = [];
        internal int Frames;
        internal int NonGameFrames;
        internal int SelectMessages;
        internal bool Truncated;
        internal bool Ksc;
        internal bool Ksb;
        internal bool Closed;
        internal bool Evicted;
    }

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

    public void ObserveFrame(int sessionNumber, CharacterCaptureFlowKey key,
        CharacterCaptureDirection direction, ReadOnlySpan<byte> frame, bool hasGameAny)
    {
        lock (_gate)
        {
            Session? session = GetOrCreate(sessionNumber, key);
            if (session is null || session.Closed || session.Evicted)
                return;

            if (++session.Frames > MaxFramesPerSession ||
                frame.Length > MaxFrameBytes)
            {
                session.Truncated = true;
                return;
            }
            if (hasGameAny)
                return;
            session.NonGameFrames++;
            if (session.Ksb || direction != CharacterCaptureDirection.ClientToServer)
                return;

            if (TryGetSelectedServerCandidate(frame, out int value))
            {
                session.SelectMessages++;
                session.SelectedIds.Add(value);
            }
        }
    }

    public void ObserveEvents(CharacterCaptureFlowKey key,
        IReadOnlyList<CharacterCaptureEvent> events)
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
                    GetOrCreate(evt.SessionNumber, key);
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

    /// <summary>
    /// Strict, bounded structural match only. It may also fit unknown other
    /// protobuf messages, and does NOT assign the meaning "server ID".
    /// </summary>
    public static bool TryGetSelectedServerCandidate(
        ReadOnlySpan<byte> frame, out int serverIdCandidate)
    {
        serverIdCandidate = 0;
        if (frame.IsEmpty || frame.Length > MaxFrameBytes)
            return false;
        try
        {
            List<ProtoField>? outer = ProtoWire.ReadFields(frame);
            if (outer is not { Count: 1 } ||
                outer[0].Number != 1 ||
                outer[0].WireType != ProtoWireType.LengthDelimited ||
                outer[0].Bytes is not { Length: > 0 and <= MaxFrameBytes } requestBytes)
                return false;

            List<ProtoField>? request = ProtoWire.ReadFields(requestBytes);
            if (request is null || request.Count is < 2 or > 8)
                return false;

            ProtoField[] contexts = request.Where(f => f.Number == 1 &&
                f.WireType == ProtoWireType.LengthDelimited).ToArray();
            ProtoField[] selects = request.Where(f => f.Number == 4 &&
                f.WireType == ProtoWireType.LengthDelimited).ToArray();
            if (contexts.Length != 1 || selects.Length != 1 ||
                contexts[0].Bytes is not { Length: >= 2 and <= 80 } contextBytes ||
                selects[0].Bytes is not { Length: > 0 } selectBytes)
                return false;

            // Legacy envelope uses language (fr); modern connection request
            // uses request UUID. Read only to validate type/length, never log.
            string? context = ProtoWire.TryUtf8(contextBytes);
            if (string.IsNullOrWhiteSpace(context) ||
                context.Any(char.IsControl))
                return false;

            List<ProtoField>? selection = ProtoWire.ReadFields(selectBytes);
            if (selection is not { Count: 1 } ||
                selection[0].Number != 1 ||
                selection[0].WireType != ProtoWireType.Varint ||
                selection[0].Varint is 0 or > int.MaxValue)
                return false;
            serverIdCandidate = checked((int)selection[0].Varint);
            return true;
        }
        catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public IReadOnlyList<string> Summarize()
    {
        lock (_gate)
        {
            List<string> lines = [];
            int?[] ids = new int?[3];
            bool comparable = _stage == 2 && !_capacityExceeded;
            bool partial = _capacityExceeded;
            string[] labels = ["A1", "B", "A2"];

            for (int stage = 0; stage < 3; stage++)
            {
                Session[] sessions = _sessions.Values.Where(s => s.Stage == stage).ToArray();
                Session[] gameCandidates = sessions.Where(s => s.Ksc && s.Ksb).ToArray();
                Session[] game = gameCandidates.Where(s => s.Closed && !s.Evicted).ToArray();
                HashSet<int> values = [];
                foreach (Session s in sessions)
                    values.UnionWith(s.SelectedIds);
                Session[] selectors = sessions.Where(s => s.SelectMessages > 0).ToArray();

                bool complete = gameCandidates.Length == 1 && game.Length == 1;
                bool oneCandidate = values.Count == 1;
                if (!complete || !oneCandidate || sessions.Any(s => s.Evicted))
                    comparable = false;
                if (oneCandidate)
                    ids[stage] = values.Single();
                bool stagePartial = sessions.Any(s => s.Truncated);
                partial |= stagePartial;

                Session? selectedSession = selectors.Length == 1
                    ? selectors[0] : null;
                string destination = selectedSession is null ? "unavailable" :
                    selectedSession.Address.Contains(':')
                        ? "[" + selectedSession.Address + "]:" + selectedSession.Port
                        : selectedSession.Address + ":" + selectedSession.Port;

                lines.Add("p3m11=stage-" + labels[stage] +
                    " tcp-sessions=" + sessions.Length +
                    " completed-game=" + game.Length +
                    " frames=" + sessions.Sum(s => s.Frames) +
                    " non-game-frames=" + sessions.Sum(s => s.NonGameFrames) +
                    " select-candidates=" + sessions.Sum(s => s.SelectMessages) +
                    " distinct-values=" + values.Count +
                    " selector-session=" + (selectedSession is null
                        ? "unavailable" : selectedSession.Number.ToString()) +
                    " selector-destination=" + destination +
                    " truncated=" + (stagePartial ? "yes" : "no") +
                    " server=unverified");
            }

            if (!comparable)
            {
                lines.Add("p3m11=summary comparable=no reason=" +
                    (_capacityExceeded ? "session-capacity" :
                        "missing-ambiguous-or-incomplete-phase") +
                    " meaning=structural-candidate-only server=unverified");
                return lines;
            }

            bool repeatA = ids[0] == ids[2];
            bool differsB = ids[0] != ids[1];
            lines.Add("p3m11=summary comparable=yes" +
                " A1-vs-A2=" + (repeatA ? "equal" : "different") +
                " A1-vs-B=" + (differsB ? "different" : "equal") +
                " pattern=" + (repeatA && differsB ? "A-B-A" : "other") +
                " coverage=" + (partial ? "partial" : "bounded") +
                " meaning=structural-candidate-only server=unverified");
            return lines;
        }
    }

    private Session? GetOrCreate(int number, CharacterCaptureFlowKey key)
    {
        if (_sessions.TryGetValue(number, out Session? found))
            return found;
        if (number <= 0 || _stage < 0)
            return null;
        if (_sessions.Count >= MaxSessions)
        {
            _capacityExceeded = true;
            return null;
        }
        Session s = new(number, _stage, key.ServerAddress, key.ServerPort);
        _sessions.Add(number, s);
        return s;
    }
}
