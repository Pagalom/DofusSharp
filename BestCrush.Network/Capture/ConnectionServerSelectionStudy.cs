using BestCrush.Network.Protocol;

namespace BestCrush.Network.Capture;

/// <summary>
/// P3m.11: observation-only candidate for the connection-server selection
/// envelope, distinct from type.ankama.com/* game messages.
/// Two documented Unity schemas both encode:
///   outer.f1 request/auth -> f4 selectServer -> f1 varint serverId.
/// A request context at outer.f1.f1 may be opaque; only its presence and
/// length are checked. The numeric field's meaning is NOT confirmed by shape.
/// No authentication token, UUID, account or character data is surfaced.
/// </summary>
public sealed class ConnectionServerSelectionStudy
{
    private const int MaxSessions = 24;
    private const int MaxNonGameFramesPerSession = 256;
    private const int MaxFrameBytes = 8192;
    private const int MaxShapesPerSession = 12;
    private const int MaxShapesPerStageOutput = 8;
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
        internal int ClientNonGameFrames;
        internal int ServerNonGameFrames;
        internal int SelectMessages;
        internal int SelectionShapeMatches;
        // Only bounded, non-sensitive reason codes are stored.
        internal readonly Dictionary<string, int> SelectionRejects = new(StringComparer.Ordinal);
        // Structural metadata only: field numbers and wire types.
        // No field values, sizes, names, UUIDs or tokens.
        internal readonly Dictionary<string, int> ClientShapes = new(StringComparer.Ordinal);
        internal bool ShapesOverflow;
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

            // Previous P3m.11 limited ALL Game frames to 192, causing
            // truncation as soon as the initial world-load burst began.
            // Game frames do not consume the Connection research budget.
            if (session.Ksb)
                return;
            session.Frames++;
            if (hasGameAny)
                return;
            if (++session.NonGameFrames > MaxNonGameFramesPerSession ||
                frame.Length > MaxFrameBytes)
            {
                session.Truncated = true;
                return;
            }
            if (direction == CharacterCaptureDirection.ServerToClient)
            {
                session.ServerNonGameFrames++;
                return;
            }
            session.ClientNonGameFrames++;
            string shape = DescribeNonGameFrameShape(frame);
            if (session.ClientShapes.TryGetValue(shape, out int count))
                session.ClientShapes[shape] = count + 1;
            else if (session.ClientShapes.Count < MaxShapesPerSession)
                session.ClientShapes.Add(shape, 1);
            else
                session.ShapesOverflow = true;

            SelectionCandidateStatus status = InspectSelectedServerCandidate(
                frame, out int value);
            if (status != SelectionCandidateStatus.Unrelated)
            {
                session.SelectionShapeMatches++;
                if (status != SelectionCandidateStatus.Accepted)
                {
                    string reason = status.ToString().ToLowerInvariant();
                    session.SelectionRejects.TryGetValue(reason, out int existing);
                    session.SelectionRejects[reason] = existing + 1;
                }
            }
            if (status == SelectionCandidateStatus.Accepted)
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
    /// Only protobuf field numbers and wire types are exposed.
    /// Nested text, numeric values and lengths never enter the signature.
    /// </summary>
    public static string DescribeNonGameFrameShape(ReadOnlySpan<byte> frame)
    {
        if (frame.IsEmpty || frame.Length > MaxFrameBytes)
            return "invalid";
        try
        {
            List<ProtoField>? root = ProtoWire.ReadFields(frame);
            if (root is null)
                return "invalid";

            static string Describe(List<ProtoField> fields)
            {
                if (fields.Count is < 1 or > 32)
                    return "unclassified";
                return string.Join(".", fields.Take(8).Select(f =>
                    "f" + f.Number + ":" + (f.WireType switch
                    {
                        ProtoWireType.Varint => "V",
                        ProtoWireType.LengthDelimited => "L",
                        ProtoWireType.Fixed32 => "F32",
                        ProtoWireType.Fixed64 => "F64",
                        _ => "?"
                    }))) + (fields.Count > 8 ? ".more" : "");
            }

            string signature = "root(" + Describe(root) + ")";
            if (root.Count != 1 || root[0].Number != 1 ||
                root[0].WireType != ProtoWireType.LengthDelimited ||
                root[0].Bytes is not { Length: > 0 and <= 2048 } nested)
                return signature;

            List<ProtoField>? inner = ProtoWire.ReadFields(nested);
            if (inner is null)
                return signature + "/f1(invalid)";
            signature += "/f1(" + Describe(inner) + ")";
            ProtoField? selection = inner.FirstOrDefault(f =>
                f.Number == 4 && f.WireType == ProtoWireType.LengthDelimited);
            if (selection?.Bytes is not { Length: > 0 and <= 128 } selectedBytes)
                return signature;

            List<ProtoField>? last = ProtoWire.ReadFields(selectedBytes);
            return signature + "/f4(" + (last is null
                ? "invalid" : Describe(last)) + ")";
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            return "invalid";
        }
    }

    /// <summary>
    /// A bounded, privacy-preserving validation of the numeric selection path.
    /// Only the structural match, not its semantics, is reported as evidence.
    /// </summary>
    public enum SelectionCandidateStatus
    {
        Unrelated,
        Accepted,
        ContextAbsentOrTooLong,
        SelectionFieldNotNumeric,
        ZeroId,
        OutOfInt32Range
    }

    public static bool TryGetSelectedServerCandidate(
        ReadOnlySpan<byte> frame, out int serverIdCandidate) =>
        InspectSelectedServerCandidate(frame, out serverIdCandidate) ==
        SelectionCandidateStatus.Accepted;

    /// <summary>
    /// Returns only a categorical result and, when accepted, an ephemeral
    /// numeric candidate. Never returns context or token bytes to callers.
    /// </summary>
    public static SelectionCandidateStatus InspectSelectedServerCandidate(
        ReadOnlySpan<byte> frame, out int serverIdCandidate)
    {
        serverIdCandidate = 0;
        if (frame.IsEmpty || frame.Length > MaxFrameBytes)
            return SelectionCandidateStatus.Unrelated;
        try
        {
            List<ProtoField>? outer = ProtoWire.ReadFields(frame);
            if (outer is not { Count: 1 } ||
                outer[0].Number != 1 ||
                outer[0].WireType != ProtoWireType.LengthDelimited ||
                outer[0].Bytes is not { Length: > 0 and <= MaxFrameBytes } requestBytes)
                return SelectionCandidateStatus.Unrelated;

            List<ProtoField>? request = ProtoWire.ReadFields(requestBytes);
            if (request is not { Count: 2 } ||
                request.Count(f => f.Number == 1 &&
                    f.WireType == ProtoWireType.LengthDelimited) != 1)
                return SelectionCandidateStatus.Unrelated;

            ProtoField[] selects = request.Where(f => f.Number == 4 &&
                f.WireType == ProtoWireType.LengthDelimited).ToArray();
            if (selects.Length != 1 ||
                selects[0].Bytes is not { Length: > 0 } selectBytes)
                return SelectionCandidateStatus.Unrelated;

            // Both source schemas require a context (lang or uuid). Do not
            // attempt to decode it as text: newer wire versions may encode
            // it opaquely, and printing it would expose identifying data.
            byte[]? context = request.Single(f => f.Number == 1).Bytes;
            if (context is not { Length: >= 1 and <= 256 })
                return SelectionCandidateStatus.ContextAbsentOrTooLong;

            List<ProtoField>? selection = ProtoWire.ReadFields(selectBytes);
            if (selection is not { Count: 1 } ||
                selection[0].Number != 1 ||
                selection[0].WireType != ProtoWireType.Varint)
                return SelectionCandidateStatus.SelectionFieldNotNumeric;
            if (selection[0].Varint == 0)
                return SelectionCandidateStatus.ZeroId;
            if (selection[0].Varint > int.MaxValue)
                return SelectionCandidateStatus.OutOfInt32Range;

            serverIdCandidate = checked((int)selection[0].Varint);
            return SelectionCandidateStatus.Accepted;
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            return SelectionCandidateStatus.Unrelated;
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
                    " non-game-c2s=" + sessions.Sum(s => s.ClientNonGameFrames) +
                    " non-game-s2c=" + sessions.Sum(s => s.ServerNonGameFrames) +
                    " select-candidates=" + sessions.Sum(s => s.SelectMessages) +
                    " shape-matches=" + sessions.Sum(s => s.SelectionShapeMatches) +
                    " distinct-values=" + values.Count +
                    " selector-session=" + (selectedSession is null
                        ? "unavailable" : selectedSession.Number.ToString()) +
                    " selector-destination=" + destination +
                    " truncated=" + (stagePartial ? "yes" : "no") +
                    " server=unverified");

                // A bounded per-socket view preserves the origin of Connection
                // candidates and reveals whether the root envelope differs.
                foreach (Session session in sessions.Where(s => s.ClientNonGameFrames > 0)
                    .OrderBy(s => s.Number))
                {
                    string socketDestination = session.Address.Contains(':')
                        ? "[" + session.Address + "]:" + session.Port
                        : session.Address + ":" + session.Port;
                    string rejectCodes = string.Join(",",
                        session.SelectionRejects.OrderBy(p => p.Key, StringComparer.Ordinal)
                            .Select(p => p.Key + "*" + p.Value));
                    string shapes = string.Join(",",
                        session.ClientShapes.OrderByDescending(p => p.Value)
                            .ThenBy(p => p.Key, StringComparer.Ordinal)
                            .Take(MaxShapesPerStageOutput)
                            .Select(p => p.Key + "*" + p.Value));

                    lines.Add("p3m11=socket stage=" + labels[stage] +
                        " session=" + session.Number +
                        " destination=" + socketDestination +
                        " non-game-c2s=" + session.ClientNonGameFrames +
                        " select-candidates=" + session.SelectMessages +
                        " shape-matches=" + session.SelectionShapeMatches +
                        " rejection-codes=[" +
                        (rejectCodes.Length == 0 ? "none" : rejectCodes) + "]" +
                        " shapes=[" + (shapes.Length == 0 ? "none" : shapes) + "]" +
                        " shapes-truncated=" +
                        (session.ShapesOverflow ||
                         session.ClientShapes.Count > MaxShapesPerStageOutput
                            ? "yes" : "no") +
                        " server=unverified");
                }
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
