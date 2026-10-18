using BestCrush.Network.Protocol;

namespace BestCrush.Network.Capture;

/// <summary>
/// Private-in-memory network flow identity. The metadata-only diagnostic
/// never prints addresses, ports, sequences, payloads or character IDs.
/// </summary>
public readonly record struct CharacterCaptureFlowKey(
    string ClientAddress, int ClientPort,
    string ServerAddress, int ServerPort);

public enum CharacterCaptureDirection
{
    ClientToServer,
    ServerToClient
}

public enum CharacterCaptureEventKind
{
    Opened,
    Message,
    Closed,
    Evicted
}

public sealed record CharacterCaptureEvent(
    DateTime ObservedAtUtc,
    int SessionNumber,
    CharacterCaptureDirection Direction,
    CharacterCaptureEventKind Kind,
    string? Opcode = null,
    int BodyLength = 0,
    string? Shape = null,
    string? ProfessionCandidates = null,
    string? ProfessionIpzSummary = null,
    string? ProfessionStudySummary = null,
    string? ProfessionIdentifySummary = null,
    string? CharacterNameProbeSummary = null,
    string? CharacterIdentityFieldSummary = null);

/// <summary>
/// P1 research-only diagnostic. One bidirectional TCP assembler per distinct
/// 4-tuple, with bounded memory and invalidation on SYN/FIN/RST. Output
/// deliberately retains ONLY opcode and byte length; it cannot expose
/// protobuf strings, account IDs, bank contents, tokens, or TCP endpoints.
/// </summary>
public sealed class CharacterCaptureFlowTracker
{
    private sealed class Session(
        int number, DateTime observedAtUtc)
    {
        internal readonly int Number = number;
        internal readonly TcpReassembler ClientToServer = new();
        internal readonly TcpReassembler ServerToClient = new();
        internal DateTime LastSeenUtc = observedAtUtc;
        internal bool Closed;
        internal bool HadPayload;
        internal CharacterIdentityFieldObservation? KsbFields;
        internal CharacterIdentityFieldObservation? KscFields;
        internal bool IdentityFieldsConflicted;
    }

    private readonly object _gate = new();
    private readonly Dictionary<CharacterCaptureFlowKey, Session> _flows = new();
    private readonly int _maxFlows;
    private readonly TimeSpan _idleTimeout;
    private readonly HashSet<string> _shapeKeys;
    private readonly IReadOnlyList<ProfessionReference>? _professionReferences;
    private readonly bool _decodeIpz;
    private readonly IReadOnlyList<ProfessionStudyReference>? _studyReferences;
    private readonly bool _showCandidateJobIds;
    private readonly IReadOnlyList<ProfessionNamedReference>? _identifyReferences;
    private readonly CharacterNameLevelReference? _characterNameReference;
    private readonly CharacterNameLevelReference? _identityFieldsReference;
    private readonly HashSet<string> _characterNameOpCodes;
    private int _nextNumber;

    public CharacterCaptureFlowTracker(
        int maxFlows = 128, TimeSpan? idleTimeout = null,
        IReadOnlySet<string>? shapeKeys = null,
        IReadOnlyList<ProfessionReference>? professionReferences = null,
        bool decodeIpz = false,
        IReadOnlyList<ProfessionStudyReference>? studyReferences = null,
        bool showCandidateJobIds = false,
        IReadOnlyList<ProfessionNamedReference>? identifyReferences = null,
        CharacterNameLevelReference? characterNameReference = null,
        IReadOnlySet<string>? characterNameOpCodes = null,
        CharacterNameLevelReference? identityFieldsReference = null)
    {
        if (maxFlows < 1)
            throw new ArgumentOutOfRangeException(nameof(maxFlows));

        _maxFlows = maxFlows;
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(30);
        if (_idleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));

        _shapeKeys = shapeKeys is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(shapeKeys, StringComparer.Ordinal);

        if (_shapeKeys.Any(key => !IsSafeOpcode(key)))
            throw new ArgumentException(
                "Shape keys must be simple, non-empty protocol opcodes.",
                nameof(shapeKeys));

        if (decodeIpz && professionReferences is null)
            throw new ArgumentException(
                "Ipz comparison requires local reference pairs.",
                nameof(professionReferences));

        if (professionReferences is not null)
        {
            // Validate upfront with a synthetic non-sensitive message.
            ProfessionWireMatcher.Describe([], professionReferences);
            _professionReferences = Array.AsReadOnly(professionReferences.ToArray());
        }

        _decodeIpz = decodeIpz;

        if (studyReferences is not null)
        {
            if (decodeIpz || professionReferences is not null || shapeKeys is not null)
                throw new ArgumentException("P1e is a standalone diagnostic mode.",
                    nameof(studyReferences));
            // Validate before Npcap starts (with non-sensitive empty body).
            ProfessionIpzStudy.Analyze([], studyReferences);
            _studyReferences = Array.AsReadOnly(studyReferences.ToArray());
        }
        if (identifyReferences is not null)
        {
            if (studyReferences is not null || decodeIpz ||
                professionReferences is not null || shapeKeys is not null)
                throw new ArgumentException(
                    "P1f identification is an exclusive diagnostic mode.",
                    nameof(identifyReferences));

            ProfessionNamedReferenceInput.Validate(identifyReferences);
            _identifyReferences =
                Array.AsReadOnly(identifyReferences.ToArray());
        }
        if (showCandidateJobIds &&
            studyReferences is null && identifyReferences is null)
            throw new ArgumentException(
                "Candidate IDs require P1e or P1f references.",
                nameof(showCandidateJobIds));
        _showCandidateJobIds = showCandidateJobIds;

        if (characterNameReference is not null)
        {
            if (decodeIpz || professionReferences is not null ||
                studyReferences is not null || identifyReferences is not null ||
                shapeKeys is not null || showCandidateJobIds)
                throw new ArgumentException("P2a name probe must run exclusively.",
                    nameof(characterNameReference));

            CharacterNameLevelReferenceInput.Validate(characterNameReference.Value);
            _characterNameReference = characterNameReference;
        }
        _characterNameOpCodes = characterNameOpCodes is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(characterNameOpCodes, StringComparer.Ordinal);
        if (_characterNameOpCodes.Any(k => !IsSafeOpcode(k)))
            throw new ArgumentException("Invalid identity probe opcode.",
                nameof(characterNameOpCodes));
        if (_characterNameOpCodes.Count > 16 ||
            (_characterNameReference is null && _characterNameOpCodes.Count > 0))
            throw new ArgumentException("Identity probe requires a local reference and up to 16 opcodes.",
                nameof(characterNameOpCodes));

        if (identityFieldsReference is not null)
        {
            if (characterNameReference is not null ||
                _characterNameOpCodes.Count != 0 ||
                decodeIpz || shapeKeys is not null ||
                professionReferences is not null ||
                studyReferences is not null || identifyReferences is not null ||
                showCandidateJobIds)
                throw new ArgumentException(
                    "P2b field study is an exclusive diagnostic mode.",
                    nameof(identityFieldsReference));
            CharacterNameLevelReferenceInput.Validate(identityFieldsReference.Value);
            _identityFieldsReference = identityFieldsReference;
        }
    }

    public int FlowCount
    {
        get { lock (_gate) return _flows.Count; }
    }

    public IReadOnlyList<CharacterCaptureEvent> Observe(
        CharacterCaptureFlowKey key,
        CharacterCaptureDirection direction,
        uint sequence,
        byte[] payload,
        DateTime observedAtUtc,
        bool syn = false,
        bool fin = false,
        bool rst = false)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(key.ClientAddress) ||
            string.IsNullOrWhiteSpace(key.ServerAddress) ||
            key.ClientPort is < 1 or > 65535 ||
            key.ServerPort is < 1 or > 65535)
            throw new ArgumentException("TCP endpoints must be defined.", nameof(key));

        lock (_gate)
        {
            List<CharacterCaptureEvent> events = [];

            // Expiry is based on observed packet time; reset incomplete
            // assemblies before any 4-tuple can be re-used.
            foreach (CharacterCaptureFlowKey expired in _flows
                .Where(pair => observedAtUtc - pair.Value.LastSeenUtc >= _idleTimeout)
                .Select(pair => pair.Key).ToArray())
            {
                Session previous = _flows[expired];
                events.Add(new CharacterCaptureEvent(
                    observedAtUtc, previous.Number, direction,
                    CharacterCaptureEventKind.Evicted));
                _flows.Remove(expired);
            }

            if (!_flows.TryGetValue(key, out Session? flow))
            {
                if (payload.Length == 0 && !syn)
                    return events;

                if (_flows.Count >= _maxFlows)
                {
                    KeyValuePair<CharacterCaptureFlowKey, Session> oldest =
                        _flows.MinBy(pair => pair.Value.LastSeenUtc);
                    events.Add(new CharacterCaptureEvent(
                        observedAtUtc, oldest.Value.Number, direction,
                        CharacterCaptureEventKind.Evicted));
                    _flows.Remove(oldest.Key);
                }

                flow = new Session(++_nextNumber, observedAtUtc);
                _flows.Add(key, flow);
                events.Add(new CharacterCaptureEvent(
                    observedAtUtc, flow.Number, direction,
                    CharacterCaptureEventKind.Opened));
            }
            else if (syn && (flow.Closed || flow.HadPayload))
            {
                // Reused socket 4-tuple: never share TCP frame state.
                events.Add(new CharacterCaptureEvent(
                    observedAtUtc, flow.Number, direction,
                    CharacterCaptureEventKind.Closed));
                flow = new Session(++_nextNumber, observedAtUtc);
                _flows[key] = flow;
                events.Add(new CharacterCaptureEvent(
                    observedAtUtc, flow.Number, direction,
                    CharacterCaptureEventKind.Opened));
            }

            if (flow.Closed || observedAtUtc < flow.LastSeenUtc)
                return events;

            flow.LastSeenUtc = observedAtUtc;

            if (payload.Length > 0)
            {
                // The TCP SYN consumes one sequence number before payload.
                uint startSequence = syn ? unchecked(sequence + 1) : sequence;
                TcpReassembler stream = direction == CharacterCaptureDirection.ClientToServer
                    ? flow.ClientToServer
                    : flow.ServerToClient;

                try
                {
                    foreach (ReadOnlyMemory<byte> bytes in stream.Push(startSequence, payload))
                    {
                        ReadOnlySpan<byte> remaining = bytes.Span;
                        while (!remaining.IsEmpty)
                        {
                            int chunk = Math.Min(remaining.Length, 64 * 1024);
                            stream.FrameBuffer.Append(remaining[..chunk]);
                            remaining = remaining[chunk..];

                            while (stream.FrameBuffer.TryReadFrame(out byte[] frame))
                            {
                                AnkamaAny? any = AnkamaFrameDecoder.FindAny(frame);
                                if (any is null)
                                    continue;

                                events.Add(new CharacterCaptureEvent(
                                    observedAtUtc, flow.Number, direction,
                                    CharacterCaptureEventKind.Message,
                                    SafeOpcode(any.Key), any.Body.Length,
                                    _shapeKeys.Contains(any.Key)
                                        ? ProtoShapeSummarizer.Describe(any.Body)
                                        : null,
                                    _professionReferences is not null && !_decodeIpz &&
                                    direction == CharacterCaptureDirection.ServerToClient
                                        ? ProfessionWireMatcher.Describe(
                                            any.Body, _professionReferences)
                                        : null,
                                    _decodeIpz &&
                                    direction == CharacterCaptureDirection.ServerToClient &&
                                    string.Equals(any.Key, "ipz", StringComparison.Ordinal)
                                        ? FormatIpz(any.Body, _professionReferences!)
                                        : null,
                                    _studyReferences is not null &&
                                    direction == CharacterCaptureDirection.ServerToClient &&
                                    string.Equals(any.Key, "ipz", StringComparison.Ordinal)
                                        ? ProfessionIpzStudy.Analyze(
                                            any.Body, _studyReferences, _showCandidateJobIds)
                                        : null,
                                    _identifyReferences is not null &&
                                    direction == CharacterCaptureDirection.ServerToClient &&
                                    string.Equals(any.Key, "ipz", StringComparison.Ordinal)
                                        ? ProfessionIpzNameMatcher.Analyze(
                                            any.Body, _identifyReferences, _showCandidateJobIds)
                                        : null,
                                    _characterNameReference.HasValue &&
                                    _characterNameOpCodes.Contains(any.Key)
                                        ? CharacterIdentityNameProbe.Describe(
                                            any.Body, _characterNameReference.Value)
                                        : null,
                                    _identityFieldsReference.HasValue &&
                                    direction == CharacterCaptureDirection.ServerToClient &&
                                    any.Key is "ksb" or "ksc"
                                        ? ObserveIdentityFields(
                                            flow, any.Key, any.Body, _identityFieldsReference.Value)
                                        : null));
                            }
                        }
                    }

                    flow.HadPayload = true;
                }
                catch (TcpReassemblyLimitExceededException)
                {
                    flow.Closed = true;
                    events.Add(new CharacterCaptureEvent(
                        observedAtUtc, flow.Number, direction,
                        CharacterCaptureEventKind.Evicted));
                    return events;
                }
            }

            // Even with no payload, a FIN or RST invalidates the session.
            if (fin || rst)
            {
                flow.Closed = true;
                events.Add(new CharacterCaptureEvent(
                    observedAtUtc, flow.Number, direction,
                    CharacterCaptureEventKind.Closed));
            }

            return events;
        }
    }

    private static string ObserveIdentityFields(
        Session flow, string opcode, byte[] body, CharacterNameLevelReference reference)
    {
        if (flow.IdentityFieldsConflicted)
            return "status=conflicted identity=unverified";

        string report = CharacterIdentityFieldStudy.Observe(
            opcode, body, reference, out CharacterIdentityFieldObservation? observation);

        if (observation is null)
            return report;

        CharacterIdentityFieldObservation? previous = opcode == "ksb"
            ? flow.KsbFields : flow.KscFields;

        if (previous is not null)
        {
            if (previous.NumericSiblings.Count != observation.NumericSiblings.Count ||
                previous.NumericSiblings.Any(pair =>
                    !observation.NumericSiblings.TryGetValue(
                        pair.Key, out ulong other) || pair.Value != other) ||
                previous.NumericParent.Count != observation.NumericParent.Count ||
                previous.NumericParent.Any(pair =>
                    !observation.NumericParent.TryGetValue(
                        pair.Key, out ulong other) || pair.Value != other))
            {
                flow.IdentityFieldsConflicted = true;
                flow.KsbFields = null;
                flow.KscFields = null;
                return "status=conflicted identity=unverified";
            }
        }

        if (opcode == "ksb")
            flow.KsbFields = observation;
        else
            flow.KscFields = observation;

        if (flow.KsbFields is not null && flow.KscFields is not null)
            return report + " " + CharacterIdentityFieldStudy.Compare(
                flow.KsbFields, flow.KscFields);

        return report + " same-session=pending identity=unverified";
    }

    private static string FormatIpz(
        byte[] body, IReadOnlyList<ProfessionReference> references) =>
        ProfessionIpzDecoder.TryDecode(
            body, out ProfessionIpzSnapshot? snapshot, out string? reason)
            ? ProfessionIpzDecoder.DescribeAnonymously(snapshot!, references)
            : $"rejected-layout:{reason ?? "unknown"}";

    private static bool IsSafeOpcode(string key) =>
        key.Length is >= 1 and <= 16 &&
        key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static string SafeOpcode(string key) =>
        IsSafeOpcode(key) ? key : "(invalid)";
}
