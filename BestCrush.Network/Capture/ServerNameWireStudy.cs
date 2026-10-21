using System.Text;
using BestCrush.Network.Protocol;

namespace BestCrush.Network.Capture;

/// <summary>
/// P3m.9: bounded passive study of exact or whole-token PUBLIC server names
/// in decoded Ankama protobuf bodies on the selected TCP port.
/// The two reference names are entered locally. No packet, token or unrecognized
/// text is written to logs, and finding a name does NOT verify a game server.
/// </summary>
public sealed class ServerNameWireStudy
{
    private const int MaxSessions = 24;
    private const int MaxMessagesPerSession = 192;
    private const int MaxBodyBytes = 8192;
    private const int MaxTextBytes = 4096;
    private const int MaxChildBytes = 4096;
    private const int MaxNodeFields = 256;
    private const int MaxDepth = 4;
    private const int MaxHitsPerSession = 96;
    private const int MaxPrintedHitsPerStage = 16;
    private const int MaxPrintedCandidatePaths = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private sealed record NameHit(string Path, string Label, string MatchKind);

    private sealed class Session(int stage)
    {
        internal readonly int Stage = stage;
        internal readonly HashSet<NameHit> Hits = [];
        internal int ScannedMessages;
        internal bool Truncated;
        internal bool Ksc;
        internal bool Ksb;
        internal bool Closed;
        internal bool Evicted;
    }

    private readonly object _sync = new();
    private readonly string _serverA;
    private readonly string _serverB;
    private readonly Dictionary<int, Session> _sessions = [];
    private bool _capacityExceeded;
    private int _stage = -1;

    public ServerNameWireStudy(string serverA, string serverB)
    {
        _serverA = ValidateName(serverA, nameof(serverA));
        _serverB = ValidateName(serverB, nameof(serverB));
        if (string.Equals(_serverA, _serverB, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Deux noms de serveurs distincts sont requis.");
    }

    private static string ValidateName(string? name, string parameter)
    {
        string value = name?.Trim() ?? "";
        if (value.Length is < 2 or > 64 ||
            value.Any(ch => char.IsControl(ch) || ch is '[' or ']' or '=' || ch == ','))
            throw new ArgumentException("Nom de serveur invalide (2-64 caractères).", parameter);
        return value;
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
                    GetOrCreate(evt.SessionNumber);
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

    /// <summary>Called from decoded, isolated TCP flow, before event output.</summary>
    public void ObserveMessage(int sessionNumber, CharacterCaptureDirection direction,
        string opcode, ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 || opcode.Length is < 1 or > 16 ||
            !opcode.All(char.IsAsciiLetterOrDigit))
            return;
        lock (_sync)
        {
            Session? s = GetOrCreate(sessionNumber);
            if (s is null || s.Closed || s.Evicted || s.Ksb)
                return; // selection/initial load scope only

            if (s.ScannedMessages >= MaxMessagesPerSession ||
                body.Length > MaxBodyBytes)
            {
                s.Truncated = true;
                return;
            }
            s.ScannedMessages++;
            if (body.Length == 0)
                return;

            string prefix =
                (direction == CharacterCaptureDirection.ServerToClient ? "S-C:" : "C-S:") +
                opcode + ":";
            Scan(body, prefix, "root", 0, s);
        }
    }

    public IReadOnlyList<string> Summarize()
    {
        lock (_sync)
        {
            List<string> output = [];
            var namedPaths = new Dictionary<string, HashSet<string>>[3];
            bool comparable = _stage == 2 && !_capacityExceeded;
            bool truncated = _capacityExceeded;
            string[] labels = ["A1", "B", "A2"];

            for (int stage = 0; stage < 3; stage++)
            {
                Session[] sessions = _sessions.Values.Where(s => s.Stage == stage).ToArray();
                Session[] gameCandidates = sessions.Where(s => s.Ksc && s.Ksb).ToArray();
                Session[] completed = gameCandidates.Where(s => s.Closed && !s.Evicted).ToArray();
                if (gameCandidates.Length != 1 || completed.Length != 1)
                    comparable = false;
                truncated |= sessions.Any(s => s.Truncated);

                NameHit[] hits = sessions.SelectMany(s => s.Hits).Distinct()
                    .OrderBy(h => h.Path, StringComparer.Ordinal)
                    .ThenBy(h => h.Label, StringComparer.Ordinal)
                    .ToArray();

                var paths = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (NameHit hit in hits)
                {
                    if (!paths.TryGetValue(hit.Path, out HashSet<string>? names))
                        paths.Add(hit.Path, names = []);
                    names.Add(hit.Label);
                }
                namedPaths[stage] = paths;

                string descriptions = hits.Length == 0 ? "none" :
                    string.Join(",", hits.Take(MaxPrintedHitsPerStage).Select(h =>
                        h.Path + "{name=" + h.Label + ",mode=" + h.MatchKind + "}"));

                output.Add("p3m9=stage-" + labels[stage] +
                    " tcp-sessions=" + sessions.Length +
                    " completed-game=" + completed.Length +
                    " scanned-messages=" + sessions.Sum(s => s.ScannedMessages) +
                    " matches=" + hits.Length +
                    " expected-name-seen=" +
                    (hits.Any(h => h.Label == (stage == 1 ? "B" : "A")) ? "yes" : "no") +
                    " other-name-seen=" +
                    (hits.Any(h => h.Label == (stage == 1 ? "A" : "B")) ? "yes" : "no") +
                    " truncated=" + (sessions.Any(s => s.Truncated) ? "yes" : "no") +
                    " paths=[" + descriptions + "]" +
                    " output-truncated=" + (hits.Length > MaxPrintedHitsPerStage ? "yes" : "no") +
                    " server=unverified");
            }

            if (!comparable)
            {
                output.Add("p3m9=summary comparable=no scoped-candidate-paths=[none] " +
                    "reason=" + (_capacityExceeded ? "session-capacity" :
                       "incomplete-or-ambiguous-stage") + " server=unverified");
                return output;
            }

            // At a path, A must occur without B in A1/A2, and B must
            // occur without A in B. A catalog listing BOTH names throughout
            // cannot be confused with selection-specific evidence.
            string[] selected = namedPaths[0]
                .Where(pair =>
                    pair.Value.SetEquals(["A"]) &&
                    namedPaths[1].TryGetValue(pair.Key, out HashSet<string>? b) &&
                    b.SetEquals(["B"]) &&
                    namedPaths[2].TryGetValue(pair.Key, out HashSet<string>? a2) &&
                    a2.SetEquals(["A"]))
                .Select(pair => pair.Key)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            bool any = namedPaths.Any(stage => stage.Count != 0);
            output.Add("p3m9=summary comparable=yes any-name-match=" + (any ? "yes" : "no") +
                " scoped-candidate-count=" + selected.Length +
                " scoped-candidate-paths=[" +
                (selected.Length == 0 ? "none" :
                    string.Join(",", selected.Take(MaxPrintedCandidatePaths))) + "]" +
                " output-truncated=" + (selected.Length > MaxPrintedCandidatePaths ? "yes" : "no") +
                " coverage=" + (truncated ? "partial" : "bounded") +
                " semantic=unverified server=unverified");
            return output;
        }
    }

    private Session? GetOrCreate(int number)
    {
        if (_sessions.TryGetValue(number, out Session? known))
            return known;
        if (_stage < 0)
            return null;
        if (_sessions.Count >= MaxSessions)
        {
            _capacityExceeded = true;
            return null;
        }
        return _sessions[number] = new Session(_stage);
    }

    private void Scan(ReadOnlySpan<byte> data, string prefix, string path,
        int depth, Session session)
    {
        if (depth > MaxDepth || data.IsEmpty || data.Length > MaxBodyBytes)
            return;

        ConsiderText(data, prefix + path, session);
        if (depth == MaxDepth || session.Hits.Count >= MaxHitsPerSession)
            return;

        List<ProtoField>? fields;
        try
        {
            fields = ProtoWire.ReadFields(data);
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            return;
        }
        if (fields is null || fields.Count is < 1 or > MaxNodeFields)
            return;

        foreach (ProtoField field in fields)
        {
            if (session.Hits.Count >= MaxHitsPerSession)
            {
                session.Truncated = true;
                break;
            }
            if (field.Number is < 1 or > 128 ||
                field.WireType != ProtoWireType.LengthDelimited ||
                field.Bytes is not { Length: > 0 })
                continue;
            if (field.Bytes.Length > MaxChildBytes)
            {
                session.Truncated = true;
                continue;
            }
            Scan(field.Bytes, prefix, path == "root"
                ? "f" + field.Number : path + ".f" + field.Number,
                depth + 1, session);
        }
    }

    private void ConsiderText(ReadOnlySpan<byte> data, string path, Session s)
    {
        if (data.Length > MaxTextBytes)
        {
            s.Truncated = true;
            return;
        }
        string value;
        try { value = StrictUtf8.GetString(data); }
        catch (DecoderFallbackException) { return; }
        if (value.Any(ch => char.IsControl(ch) && ch is not ('\r' or '\n' or '\t')))
            return;
        string? exact = string.Equals(value, _serverA, StringComparison.OrdinalIgnoreCase)
            ? "A" : string.Equals(value, _serverB, StringComparison.OrdinalIgnoreCase)
                ? "B" : null;
        if (exact is not null)
        {
            s.Hits.Add(new NameHit(path, exact, "exact"));
            return;
        }
        if (HasToken(value, _serverA))
            s.Hits.Add(new NameHit(path, "A", "embedded"));
        if (HasToken(value, _serverB))
            s.Hits.Add(new NameHit(path, "B", "embedded"));
    }

    private static bool HasToken(string text, string target)
    {
        int start = 0;
        while (start <= text.Length - target.Length)
        {
            int index = text.IndexOf(target, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;
            int end = index + target.Length;
            bool left = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            bool right = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (left && right)
                return true;
            start = index + 1;
        }
        return false;
    }
}
