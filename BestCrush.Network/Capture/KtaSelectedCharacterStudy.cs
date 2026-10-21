using BestCrush.Network.Protocol;

namespace BestCrush.Network.Capture;

/// <summary>
/// P3m.8: compare the client-side kta.f1 numeric candidate against the
/// ksb-selected, ksc-listed character parent.f3 numeric candidate.
/// One client and one user-declared server, two distinct characters.
/// Research only; never declares confirmed semantic identity.
/// </summary>
public sealed class KtaSelectedCharacterStudy
{
    private const int MaxSessions = 20;
    private const int MaxKtaPerSession = 12;
    private const int MaxBodyBytes = 8192;

    private sealed class Session(int stage)
    {
        internal int Stage = stage;
        internal CharacterWireCandidateSnapshot? Ksc;
        internal CharacterWireCandidateSnapshot? Ksb;
        internal bool SeenKsc;
        internal bool SeenKsb;
        internal bool DecodeConflict;
        internal bool Closed;
        internal bool Evicted;
        internal int KtaMessages;
        internal readonly HashSet<ulong> KtaValues = [];
    }

    private readonly object _sync = new();
    private readonly Dictionary<int, Session> _sessions = [];
    private int _stage = -1;
    private bool _capacityExceeded;

    public bool StartStage(int stage)
    {
        lock (_sync)
        {
            if (stage != _stage + 1 || stage is < 0 or > 1)
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

                if (!_sessions.TryGetValue(evt.SessionNumber, out Session? session))
                    continue;

                if (evt.Kind == CharacterCaptureEventKind.Closed)
                    session.Closed = true;
                else if (evt.Kind == CharacterCaptureEventKind.Evicted)
                    session.Evicted = true;
            }
        }
    }

    public void ObserveMessage(int sessionNumber, CharacterCaptureDirection direction,
        string opcode, ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 ||
            !(direction == CharacterCaptureDirection.ClientToServer && opcode == "kta" ||
              direction == CharacterCaptureDirection.ServerToClient &&
                  opcode is "ksc" or "ksb"))
            return;

        lock (_sync)
        {
            Session? session = GetOrCreate(sessionNumber);
            if (session is null || session.Closed || session.Evicted)
                return;

            if (opcode == "kta")
            {
                if (session.SeenKsb)
                    return; // never attach later unrelated kta traffic

                if (++session.KtaMessages > MaxKtaPerSession ||
                    body.Length is < 1 or > MaxBodyBytes)
                {
                    session.DecodeConflict = true;
                    return;
                }

                try
                {
                    List<ProtoField>? fields = ProtoWire.ReadFields(body);
                    ProtoField[] matches = fields is null ? [] :
                        fields.Where(x => x.Number == 1).ToArray();
                    if (fields is null || fields.Count is < 1 or > 128 ||
                        matches.Length != 1 ||
                        matches[0].WireType != ProtoWireType.Varint ||
                        matches[0].Varint == 0)
                    {
                        session.DecodeConflict = true;
                        return;
                    }
                    session.KtaValues.Add(matches[0].Varint);
                }
                catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
                {
                    session.DecodeConflict = true;
                }
                return;
            }

            bool decoded = CharacterWireCandidateDecoder.TryDecode(opcode, body,
                out CharacterWireCandidateSnapshot? snapshot);
            if (opcode == "ksc")
            {
                if (session.SeenKsc || !decoded || snapshot is null)
                    session.DecodeConflict = true;
                else
                    session.Ksc = snapshot;
                session.SeenKsc = true;
            }
            else
            {
                if (session.SeenKsb || !decoded || snapshot is null)
                    session.DecodeConflict = true;
                else
                    session.Ksb = snapshot;
                session.SeenKsb = true;
            }
        }
    }

    public IReadOnlyList<string> Summarize()
    {
        lock (_sync)
        {
            var lines = new List<string>();
            ulong?[] ids = new ulong?[2];
            bool[] matched = new bool[2];
            bool comparable = _stage == 1 && !_capacityExceeded;

            for (int stage = 0; stage < 2; stage++)
            {
                Session[] sessions = _sessions.Values
                    .Where(x => x.Stage == stage).ToArray();

                // Multiple game sessions are ambiguous even if only one
                // decodes completely. Incomplete preliminary ksc sessions
                // without ksb are not game-session candidates.
                Session[] game = sessions.Where(x => x.SeenKsc && x.SeenKsb)
                    .ToArray();
                Session? selected = game.Length == 1 ? game[0] : null;
                bool rosterMatches = selected?.Ksc is not null &&
                    selected.Ksb is not null &&
                    CharacterWireCandidateDecoder.HasMatchingCandidate(
                        selected.Ksb, selected.Ksc);

                HashSet<ulong> ktaValues = [];
                foreach (Session session in sessions)
                    ktaValues.UnionWith(session.KtaValues);

                bool valid = selected is not null && selected.Closed &&
                    !selected.Evicted && !sessions.Any(s => s.DecodeConflict ||
                        s.Evicted) && rosterMatches &&
                    ktaValues.Count == 1 &&
                    selected.Ksb!.Candidates.Count == 1;

                if (!valid)
                    comparable = false;

                if (valid)
                {
                    ulong ksbId = selected!.Ksb!.Candidates[0].CharacterIdCandidate;
                    ids[stage] = ksbId;
                    matched[stage] = ktaValues.Single() == ksbId;
                }

                lines.Add("p3m8=phase-" + (stage == 0 ? "X" : "Y") +
                    " tcp-sessions=" + sessions.Length +
                    " kta-messages=" + sessions.Sum(x => x.KtaMessages) +
                    " kta-distinct=" + ktaValues.Count +
                    " game-candidates=" + game.Length +
                    " roster-linked=" + (rosterMatches ? "yes" : "no") +
                    " valid=" + (valid ? "yes" : "no") +
                    " kta-vs-ksb=" + (valid
                        ? (matched[stage] ? "equal" : "different")
                        : "unavailable") +
                    " semantic=unverified");
            }

            if (!comparable)
            {
                lines.Add("p3m8=summary comparable=no reason=" +
                    (_capacityExceeded ? "session-capacity" :
                     "missing-ambiguous-or-invalid-data") +
                    " identity=unverified");
                return lines;
            }

            lines.Add("p3m8=summary comparable=yes" +
                " both-match=" + (matched.All(x => x) ? "yes" : "no") +
                " selected-f3-distinct=" + (ids[0] != ids[1] ? "yes" : "no") +
                " interpretation=candidate-only identity=unverified");
            return lines;
        }
    }

    private Session? GetOrCreate(int sessionNumber)
    {
        if (_sessions.TryGetValue(sessionNumber, out Session? s))
            return s;
        if (_stage < 0)
            return null;
        if (_sessions.Count >= MaxSessions)
        {
            _capacityExceeded = true;
            return null;
        }
        s = new Session(_stage);
        _sessions.Add(sessionNumber, s);
        return s;
    }
}
