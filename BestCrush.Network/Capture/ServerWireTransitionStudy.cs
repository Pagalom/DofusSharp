using BestCrush.Network.Protocol;

namespace BestCrush.Network.Capture;

/// <summary>
/// P3m.6: experimental A/B/A protobuf numeric-field comparison during
/// server selection / initial character load on TCP/5555.
/// Results are structural candidates, NOT semantic server identifiers.
/// All observed numbers are ephemeral and never rendered or persisted.
/// </summary>
public sealed class ServerWireTransitionStudy
{
    private const int MaxSessions = 24;
    private const int MaxMessagesPerSession = 256;
    private const int MaxBodyBytes = 8192;
    private const int MaxNodeFields = 256;
    private const int MaxNestedBytes = 2048;
    private const int MaxDepth = 3;
    private const int MaxNumericPerMessage = 64;
    private const int MaxReportedPaths = 24;
    private readonly object _sync = new();
    private readonly Dictionary<int, Session> _sessions = [];
    private int _stage = -1;
    private bool _capacityExceeded;

    private sealed record NumericField(string Key, ulong Value);
    private sealed class Session(int stage)
    {
        internal readonly int Stage = stage;
        internal readonly List<NumericField> Fields = [];
        internal bool Ksc;
        internal bool Ksb;
        internal bool Closed;
        internal bool Evicted;
        internal int Messages;
        internal bool Truncated;
    }

    public bool StartStage(int stage)
    {
        lock (_sync)
        {
            if (stage != _stage + 1 || stage is < 0 or > 2)
                return false;
            _stage = stage;
            return true;
        }
    }

    public void ObserveEvents(IReadOnlyList<CharacterCaptureEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        lock (_sync)
        {
            foreach (CharacterCaptureEvent evt in events)
            {
                if (evt.SessionNumber <= 0)
                    continue;
                if (evt.Kind == CharacterCaptureEventKind.Opened)
                {
                    GetOrAdd(evt.SessionNumber);
                    continue;
                }
                if (!_sessions.TryGetValue(evt.SessionNumber, out Session? session))
                    continue;

                if (evt.Kind == CharacterCaptureEventKind.Closed)
                    session.Closed = true;
                else if (evt.Kind == CharacterCaptureEventKind.Evicted)
                    session.Evicted = true;
            }
        }
    }

    /// <summary>Called on each decoded message by the shared TCP tracker.</summary>
    public void ObserveMessage(int sessionNumber,
        CharacterCaptureDirection direction, string opcode,
        ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 || opcode.Length is < 1 or > 16 ||
            !opcode.All(char.IsAsciiLetterOrDigit))
            return;

        lock (_sync)
        {
            Session? session = GetOrAdd(sessionNumber);
            if (session is null || session.Closed || session.Evicted)
                return;

            if (direction == CharacterCaptureDirection.ServerToClient)
            {
                if (opcode == "ksc") session.Ksc = true;
                if (opcode == "ksb")
                {
                    session.Ksb = true;
                    return;
                }
            }

            // ksc/ksb carry known character candidates, and ipz carries
            // profession/account-related values. These cannot be interpreted
            // as server identifiers by a simple A/B/A equality.
            if (opcode is "ksc" or "ksb" or "ipz" || session.Ksb)
                return;

            if (session.Messages >= MaxMessagesPerSession)
            {
                session.Truncated = true;
                return;
            }
            session.Messages++;

            if (body.Length is < 1 or > MaxBodyBytes)
            {
                session.Truncated = true;
                return;
            }

            List<NumericField> fields = [];
            int budget = MaxNumericPerMessage;
            Scan(body, (direction == CharacterCaptureDirection.ServerToClient
                ? "S-C:" : "C-S:") + opcode + ":", "",
                0, ref budget, fields);
            session.Fields.AddRange(fields);
            if (budget == 0)
                session.Truncated = true;
        }
    }

    public IReadOnlyList<string> Summarize()
    {
        lock (_sync)
        {
            List<string> lines = [];
            var stageValues = new Dictionary<string, HashSet<ulong>>[3];
            bool comparable = _stage == 2 && !_capacityExceeded;
            bool partial = false;

            for (int stage = 0; stage < 3; stage++)
            {
                Session[] sessions = _sessions.Values
                    .Where(x => x.Stage == stage).ToArray();
                Session[] complete = sessions
                    .Where(x => x.Ksc && x.Ksb && x.Closed && !x.Evicted)
                    .ToArray();
                // A single completed ksc/ksb session is needed to prove
                // the stage contained one full game login, not its server.
                if (complete.Length != 1)
                    comparable = false;
                partial |= sessions.Any(x => x.Truncated);

                var values = new Dictionary<string, HashSet<ulong>>(
                    StringComparer.Ordinal);
                foreach (NumericField f in sessions.SelectMany(x => x.Fields))
                {
                    if (!values.TryGetValue(f.Key, out HashSet<ulong>? set))
                    {
                        set = [];
                        values[f.Key] = set;
                    }
                    set.Add(f.Value);
                }
                stageValues[stage] = values;
                lines.Add("p3m6=stage-" + (stage == 1 ? "B" : stage == 0 ? "A1" : "A2") +
                    " tcp-sessions=" + sessions.Length +
                    " completed-game=" + complete.Length +
                    " early-messages=" + sessions.Sum(x => x.Messages) +
                    " numeric-paths=" + values.Count +
                    " truncated=" + (sessions.Any(x => x.Truncated) ? "yes" : "no") +
                    " server=unverified");
            }

            if (!comparable)
            {
                lines.Add("p3m6=summary comparable=no candidate-paths=[none] " +
                    "reason=" + (_capacityExceeded ? "session-capacity-exceeded" :
                        "incomplete-or-ambiguous-stage") +
                    " server=unverified");
                return lines;
            }

            // The same opcode, direction and protobuf path must be present
            // with ONE numeric value per stage. Exact A1 == A2 != B is a
            // necessary pattern, but also fits character/account metadata,
            // clock coincidences and many non-server fields.
            string[] matches = stageValues[0]
                .Where(pair =>
                    pair.Value.Count == 1 &&
                    stageValues[1].TryGetValue(pair.Key, out HashSet<ulong>? b) &&
                    b.Count == 1 &&
                    stageValues[2].TryGetValue(pair.Key, out HashSet<ulong>? a2) &&
                    a2.Count == 1 &&
                    pair.Value.Single() == a2.Single() &&
                    pair.Value.Single() != b.Single())
                .Select(pair => pair.Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            lines.Add("p3m6=summary comparable=yes candidate-count=" +
                matches.Length + " candidate-paths=[" +
                (matches.Length == 0 ? "none" :
                    string.Join(",", matches.Take(MaxReportedPaths))) + "]" +
                " output-truncated=" +
                (matches.Length > MaxReportedPaths ? "yes" : "no") +
                " coverage=" + (partial ? "partial" : "bounded") +
                " semantic=unverified server=unverified");
            return lines;
        }
    }

    private Session? GetOrAdd(int sessionNumber)
    {
        if (_sessions.TryGetValue(sessionNumber, out Session? found))
            return found;
        if (_stage < 0)
            return null;
        if (_sessions.Count >= MaxSessions)
        {
            _capacityExceeded = true;
            return null;
        }
        Session created = new(_stage);
        _sessions.Add(sessionNumber, created);
        return created;
    }

    private static void Scan(ReadOnlySpan<byte> body,
        string prefix, string path, int depth, ref int budget,
        List<NumericField> output)
    {
        if (body.IsEmpty || depth > MaxDepth || budget <= 0 ||
            body.Length > MaxBodyBytes)
            return;

        List<ProtoField>? fields;
        try
        {
            fields = ProtoWire.ReadFields(body);
        }
        catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException)
        {
            return;
        }
        if (fields is null || fields.Count is < 1 or > MaxNodeFields)
            return;

        foreach (ProtoField field in fields)
        {
            if (budget <= 0) break;
            if (field.Number is < 1 or > 128) continue;
            string fieldPath = path + "f" + field.Number;
            if (field.WireType == ProtoWireType.Varint)
            {
                output.Add(new NumericField(prefix + fieldPath, field.Varint));
                budget--;
            }
            else if (field.WireType == ProtoWireType.LengthDelimited &&
                     field.Bytes is { Length: > 0 and <= MaxNestedBytes } &&
                     depth < MaxDepth)
            {
                // Any nested structure is a heuristic: text and packed
                // scalars may resemble protobuf. Never confirm semantics.
                Scan(field.Bytes, prefix, fieldPath + ".", depth + 1,
                    ref budget, output);
            }
        }
    }
}
