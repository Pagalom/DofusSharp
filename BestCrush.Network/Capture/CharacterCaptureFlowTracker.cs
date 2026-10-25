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
    string? CharacterIdentityFieldSummary = null,
    string? ProfessionFlowSummary = null,
    string? ProfessionContinuitySummary = null,
    string? ProfessionXpStudySummary = null,
    string? AccountWireFieldSummary = null,
    string? RosterContinuitySummary = null,
    string? ConcurrentRosterSummary = null,
    string? ItemEffectsShape = null,
    string? StockSnapshotSummary = null);

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
        internal CharacterWireCandidateSnapshot? KsbDecodedCandidate;
        internal CharacterWireCandidateSnapshot? KscDecodedCandidates;
        internal ProfessionFlowStudy? ProfessionFlow;
    }

    private readonly object _gate = new();
    private readonly Dictionary<CharacterCaptureFlowKey, Session> _flows = new();
    private readonly int _maxFlows;
    private readonly TimeSpan _idleTimeout;
    private readonly HashSet<string> _shapeKeys;
    private readonly bool _itemEffectsShape;
    private readonly bool _stockSnapshotStudy;
    private readonly StockSnapshotDeltaStudy? _stockDeltaStudy;
    private readonly IReadOnlyList<long>? _expectedEffectValues;
    private readonly ItemEffectTypeStabilityStudy? _effectTypeStudy;
    private readonly ItemEffectDofusDbCatalogStudy? _effectCatalogStudy;
    private readonly IReadOnlyList<ProfessionReference>? _professionReferences;
    private readonly bool _decodeIpz;
    private readonly IReadOnlyList<ProfessionStudyReference>? _studyReferences;
    private readonly bool _showCandidateJobIds;
    private readonly IReadOnlyList<ProfessionNamedReference>? _identifyReferences;
    private readonly CharacterNameLevelReference? _characterNameReference;
    private readonly CharacterNameLevelReference? _identityFieldsReference;
    private readonly int? _expectedClassId;
    private readonly bool _studyProfessionFlow;
    private readonly ProfessionContinuityStudy? _professionContinuity;
    private readonly ProfessionXpWireStudy? _professionXpStudy;
    private readonly CharacterIdentityPairStudy? _identityPairStudy;
    private readonly CharacterCrossOpcodeFieldStudy? _crossOpcodeStudy;
    private readonly ServerWireTransitionStudy? _serverWireStudy;
    private readonly KtaFieldScopeStudy? _ktaScopeStudy;
    private readonly KtaSelectedCharacterStudy? _ktaCharacterStudy;
    private readonly ServerNameWireStudy? _serverNameStudy;
    private readonly ConnectionServerSelectionStudy? _connectionSelectionStudy;
    private readonly AccountWireFieldComparisonStudy? _accountWireStudy;
    private readonly CharacterRosterContinuityStudy? _rosterStudy;
    private readonly ConcurrentCharacterRosterStudy? _concurrentRosterStudy;
    private readonly HashSet<string> _characterNameOpCodes;
    private int _nextNumber;

    // P2c research-only: retain at most 16 observed ksb parent f3
    // values across anonymized TCP sessions, in RAM for this probe run.
    // No hashes, values or identity conclusions leave this tracker.
    private const int MaximumIdentityStudySessions = 16;
    private readonly Dictionary<int, ulong> _candidateKsbF3BySession = [];
    private readonly Queue<int> _candidateKsbSessionOrder = new();

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
        CharacterNameLevelReference? identityFieldsReference = null,
        int? expectedClassId = null,
        bool studyProfessionFlow = false,
        bool studyProfessionContinuity = false,
        ProfessionXpWireStudy? professionXpStudy = null,
        CharacterIdentityPairStudy? identityPairStudy = null,
        CharacterCrossOpcodeFieldStudy? crossOpcodeStudy = null,
        ServerWireTransitionStudy? serverWireStudy = null,
        KtaFieldScopeStudy? ktaScopeStudy = null,
        KtaSelectedCharacterStudy? ktaCharacterStudy = null,
        ServerNameWireStudy? serverNameStudy = null,
        ConnectionServerSelectionStudy? connectionSelectionStudy = null,
        AccountWireFieldComparisonStudy? accountWireStudy = null,
        CharacterRosterContinuityStudy? rosterStudy = null,
        ConcurrentCharacterRosterStudy? concurrentRosterStudy = null,
        bool itemEffectsShape = false,
        IReadOnlyList<long>? expectedEffectValues = null,
        ItemEffectTypeStabilityStudy? effectTypeStudy = null,
        ItemEffectDofusDbCatalogStudy? effectCatalogStudy = null,
        bool stockSnapshotStudy = false)
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

        if (stockSnapshotStudy && (itemEffectsShape || effectCatalogStudy is not null ||
            effectTypeStudy is not null || expectedEffectValues is not null))
            throw new ArgumentException(
                "Stock snapshot and effects diagnostics are exclusive.",
                nameof(stockSnapshotStudy));
        _stockSnapshotStudy = stockSnapshotStudy;
        _stockDeltaStudy = stockSnapshotStudy ? new StockSnapshotDeltaStudy() : null;
        _itemEffectsShape = itemEffectsShape;
        if (expectedEffectValues is not null &&
            (!itemEffectsShape || expectedEffectValues.Count is < 1 or > 16))
            throw new ArgumentException(
                "Effect references require the explicit shape probe and 1-16 values.",
                nameof(expectedEffectValues));
        if (effectTypeStudy is not null &&
            (!itemEffectsShape || expectedEffectValues is not null))
            throw new ArgumentException(
                "The cross-item study must be the sole active effect diagnostic.",
                nameof(effectTypeStudy));
        if (effectCatalogStudy is not null &&
            (!itemEffectsShape || expectedEffectValues is not null ||
             effectTypeStudy is not null))
            throw new ArgumentException(
                "The catalog study must be the sole active effect diagnostic.",
                nameof(effectCatalogStudy));
        _effectTypeStudy = effectTypeStudy;
        _effectCatalogStudy = effectCatalogStudy;
        _expectedEffectValues = expectedEffectValues?.ToArray();

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

        if (expectedClassId is not null &&
            (identityFieldsReference is null ||
             expectedClassId is < 1 or > 19))
            throw new ArgumentException(
                "P2i expected class ID requires the exclusive P2b field study.",
                nameof(expectedClassId));
        _expectedClassId = expectedClassId;

        if (identityPairStudy is not null &&
            (characterNameReference is not null || identityFieldsReference is not null ||
             expectedClassId is not null || shapeKeys is not null ||
             professionReferences is not null || studyReferences is not null ||
             identifyReferences is not null || decodeIpz || showCandidateJobIds ||
             _characterNameOpCodes.Count != 0 || studyProfessionFlow ||
             studyProfessionContinuity || professionXpStudy is not null))
            throw new ArgumentException("Pair identity study must run exclusively.",
                nameof(identityPairStudy));
        _identityPairStudy = identityPairStudy;

        if (crossOpcodeStudy is not null &&
            (characterNameReference is not null ||
             identityFieldsReference is not null || identityPairStudy is not null ||
             expectedClassId is not null || shapeKeys is not null ||
             professionReferences is not null || studyReferences is not null ||
             identifyReferences is not null || decodeIpz || showCandidateJobIds ||
             _characterNameOpCodes.Count != 0 || studyProfessionFlow ||
             studyProfessionContinuity || professionXpStudy is not null))
            throw new ArgumentException(
                "Cross-opcode identity study must run exclusively.",
                nameof(crossOpcodeStudy));
        _crossOpcodeStudy = crossOpcodeStudy;

        if (serverWireStudy is not null &&
            (characterNameReference is not null ||
             identityFieldsReference is not null || identityPairStudy is not null ||
             crossOpcodeStudy is not null || expectedClassId is not null ||
             shapeKeys is not null || professionReferences is not null ||
             studyReferences is not null || identifyReferences is not null ||
             decodeIpz || showCandidateJobIds ||
             _characterNameOpCodes.Count != 0 || studyProfessionFlow ||
             studyProfessionContinuity || professionXpStudy is not null))
            throw new ArgumentException(
                "Server-wire study must run exclusively.",
                nameof(serverWireStudy));
        _serverWireStudy = serverWireStudy;

        if (ktaScopeStudy is not null &&
            (characterNameReference is not null ||
             identityFieldsReference is not null || identityPairStudy is not null ||
             crossOpcodeStudy is not null || serverWireStudy is not null ||
             expectedClassId is not null || shapeKeys is not null ||
             professionReferences is not null || studyReferences is not null ||
             identifyReferences is not null || decodeIpz || showCandidateJobIds ||
             _characterNameOpCodes.Count != 0 || studyProfessionFlow ||
             studyProfessionContinuity || professionXpStudy is not null))
            throw new ArgumentException("Kta scope study must run exclusively.",
                nameof(ktaScopeStudy));
        _ktaScopeStudy = ktaScopeStudy;

        if (ktaCharacterStudy is not null &&
            (ktaScopeStudy is not null || characterNameReference is not null ||
             identityFieldsReference is not null || identityPairStudy is not null ||
             crossOpcodeStudy is not null || serverWireStudy is not null ||
             expectedClassId is not null || shapeKeys is not null ||
             professionReferences is not null || studyReferences is not null ||
             identifyReferences is not null || decodeIpz || showCandidateJobIds ||
             _characterNameOpCodes.Count != 0 || studyProfessionFlow ||
             studyProfessionContinuity || professionXpStudy is not null))
            throw new ArgumentException(
                "Kta-to-character study must run exclusively.",
                nameof(ktaCharacterStudy));
        _ktaCharacterStudy = ktaCharacterStudy;

        if (serverNameStudy is not null &&
            (ktaCharacterStudy is not null || ktaScopeStudy is not null ||
             characterNameReference is not null || identityFieldsReference is not null ||
             identityPairStudy is not null || crossOpcodeStudy is not null ||
             serverWireStudy is not null || expectedClassId is not null ||
             shapeKeys is not null || professionReferences is not null ||
             studyReferences is not null || identifyReferences is not null ||
             decodeIpz || showCandidateJobIds || _characterNameOpCodes.Count != 0 ||
             studyProfessionFlow || studyProfessionContinuity ||
             professionXpStudy is not null))
            throw new ArgumentException(
                "Server-name discovery must run exclusively.",
                nameof(serverNameStudy));
        _serverNameStudy = serverNameStudy;

        if (connectionSelectionStudy is not null &&
            (serverNameStudy is not null || ktaCharacterStudy is not null ||
             ktaScopeStudy is not null || characterNameReference is not null ||
             identityFieldsReference is not null || identityPairStudy is not null ||
             crossOpcodeStudy is not null || serverWireStudy is not null ||
             expectedClassId is not null || shapeKeys is not null ||
             professionReferences is not null || studyReferences is not null ||
             identifyReferences is not null || decodeIpz || showCandidateJobIds ||
             _characterNameOpCodes.Count != 0 || studyProfessionFlow ||
             studyProfessionContinuity || professionXpStudy is not null))
            throw new ArgumentException(
                "Connection selection study must run exclusively.",
                nameof(connectionSelectionStudy));
        _connectionSelectionStudy = connectionSelectionStudy;

        if (accountWireStudy is not null &&
            (connectionSelectionStudy is not null ||
             serverNameStudy is not null || ktaCharacterStudy is not null ||
             ktaScopeStudy is not null || serverWireStudy is not null ||
             crossOpcodeStudy is not null || identityPairStudy is not null ||
             expectedClassId is not null || identityFieldsReference is not null ||
             characterNameReference is not null || shapeKeys is not null ||
             professionReferences is not null || studyReferences is not null ||
             identifyReferences is not null || decodeIpz || showCandidateJobIds ||
             studyProfessionFlow || studyProfessionContinuity ||
             professionXpStudy is not null || _characterNameOpCodes.Count > 0))
            throw new ArgumentException(
                "Account-wire candidates require the exclusive passive P3c.4 probe.",
                nameof(accountWireStudy));
        _accountWireStudy = accountWireStudy;

        if (rosterStudy is not null &&
            (accountWireStudy is not null ||
             connectionSelectionStudy is not null ||
             serverNameStudy is not null || ktaCharacterStudy is not null ||
             ktaScopeStudy is not null || serverWireStudy is not null ||
             crossOpcodeStudy is not null || identityPairStudy is not null ||
             expectedClassId is not null || identityFieldsReference is not null ||
             characterNameReference is not null || shapeKeys is not null ||
             professionReferences is not null || studyReferences is not null ||
             identifyReferences is not null || decodeIpz || showCandidateJobIds ||
             studyProfessionFlow || studyProfessionContinuity ||
             professionXpStudy is not null || _characterNameOpCodes.Count > 0))
            throw new ArgumentException(
                "Roster study requires an exclusive passive diagnostic mode.",
                nameof(rosterStudy));
        _rosterStudy = rosterStudy;

        if (concurrentRosterStudy is not null &&
            (rosterStudy is not null || accountWireStudy is not null ||
             connectionSelectionStudy is not null || serverNameStudy is not null ||
             ktaCharacterStudy is not null || ktaScopeStudy is not null ||
             serverWireStudy is not null || crossOpcodeStudy is not null ||
             identityPairStudy is not null || expectedClassId is not null ||
             identityFieldsReference is not null ||
             characterNameReference is not null || shapeKeys is not null ||
             professionReferences is not null || studyReferences is not null ||
             identifyReferences is not null || decodeIpz || showCandidateJobIds ||
             studyProfessionFlow || studyProfessionContinuity ||
             professionXpStudy is not null || _characterNameOpCodes.Count > 0))
            throw new ArgumentException(
                "Concurrent roster study requires its own passive diagnostic.",
                nameof(concurrentRosterStudy));
        _concurrentRosterStudy = concurrentRosterStudy;

        if ((studyProfessionFlow || studyProfessionContinuity || professionXpStudy is not null) &&
            (shapeKeys is not null || professionReferences is not null ||
             decodeIpz || studyReferences is not null ||
             identifyReferences is not null || characterNameReference is not null ||
             identityFieldsReference is not null || expectedClassId is not null ||
             showCandidateJobIds || _characterNameOpCodes.Count > 0 ||
             (studyProfessionFlow && studyProfessionContinuity) ||
             (professionXpStudy is not null &&
              (studyProfessionFlow || studyProfessionContinuity))))
            throw new ArgumentException(
                "P3 diagnostics are exclusive metadata-only modes.",
                nameof(studyProfessionFlow));
        _studyProfessionFlow = studyProfessionFlow;
        _professionContinuity = studyProfessionContinuity
            ? new ProfessionContinuityStudy() : null;
        _professionXpStudy = professionXpStudy;
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
                previous.ProfessionFlow = null;
                _concurrentRosterStudy?.ObserveClosedSession(previous.Number);
                events.Add(new CharacterCaptureEvent(
                    observedAtUtc, previous.Number, direction,
                    CharacterCaptureEventKind.Evicted));
                _stockDeltaStudy?.DropSession(previous.Number);
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
                    oldest.Value.ProfessionFlow = null;
                    _concurrentRosterStudy?.ObserveClosedSession(oldest.Value.Number);
                    events.Add(new CharacterCaptureEvent(
                        observedAtUtc, oldest.Value.Number, direction,
                        CharacterCaptureEventKind.Evicted));
                    _stockDeltaStudy?.DropSession(oldest.Value.Number);
                    _flows.Remove(oldest.Key);
                }

                flow = new Session(++_nextNumber, observedAtUtc);
                _flows.Add(key, flow);
                _accountWireStudy?.ObserveOpenedSession(flow.Number);
                _rosterStudy?.ObserveOpenedSession(flow.Number);
                _concurrentRosterStudy?.ObserveOpenedSession(flow.Number);
                events.Add(new CharacterCaptureEvent(
                    observedAtUtc, flow.Number, direction,
                    CharacterCaptureEventKind.Opened));
            }
            else if (syn && (flow.Closed || flow.HadPayload))
            {
                // Reused socket 4-tuple: never share TCP frame state.
                flow.ProfessionFlow = null;
                _concurrentRosterStudy?.ObserveClosedSession(flow.Number);
                events.Add(new CharacterCaptureEvent(
                    observedAtUtc, flow.Number, direction,
                    CharacterCaptureEventKind.Closed));
                _stockDeltaStudy?.DropSession(flow.Number);
                flow = new Session(++_nextNumber, observedAtUtc);
                _flows[key] = flow;
                _accountWireStudy?.ObserveOpenedSession(flow.Number);
                _rosterStudy?.ObserveOpenedSession(flow.Number);
                _concurrentRosterStudy?.ObserveOpenedSession(flow.Number);
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
                                // P3m.11 must see the raw framed Connection
                                // protobuf before the game-only Any filter.
                                _connectionSelectionStudy?.ObserveFrame(
                                    flow.Number, key, direction, frame, any is not null);
                                if (any is null)
                                    continue;

                                // P3m.6 receives bounded decoded fields only,
                                // never a raw TCP stream or persisted packet.
                                _serverWireStudy?.ObserveMessage(
                                    flow.Number, direction, any.Key, any.Body);
                                _ktaScopeStudy?.ObserveMessage(
                                    flow.Number, key, direction, any.Key, any.Body);
                                _ktaCharacterStudy?.ObserveMessage(
                                    flow.Number, direction, any.Key, any.Body);
                                _serverNameStudy?.ObserveMessage(
                                    flow.Number, direction, any.Key, any.Body);

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
                                    direction == CharacterCaptureDirection.ServerToClient
                                        ? _crossOpcodeStudy is not null
                                            ? _crossOpcodeStudy.Observe(
                                                flow.Number, direction, any.Key, any.Body)
                                            : any.Key is "ksb" or "ksc"
                                                ? _identityPairStudy is not null
                                                    ? _identityPairStudy.Observe(
                                                        flow.Number, any.Key, any.Body)
                                                    : _identityFieldsReference.HasValue
                                                        ? ObserveIdentityFields(
                                                            flow, any.Key, any.Body,
                                                            _identityFieldsReference.Value)
                                                        : null
                                                : null
                                        : null,
                                    _studyProfessionFlow &&
                                    direction == CharacterCaptureDirection.ServerToClient &&
                                    any.Key is "ksb" or "ksc" or "ipz"
                                        ? (flow.ProfessionFlow ??= new ProfessionFlowStudy())
                                            .Observe(any.Key, any.Body)
                                        : null,
                                    _professionContinuity is not null &&
                                    direction == CharacterCaptureDirection.ServerToClient &&
                                    string.Equals(any.Key, "ipz", StringComparison.Ordinal)
                                        ? _professionContinuity.Observe(any.Body)
                                        : null,
                                    _professionXpStudy is not null &&
                                    direction == CharacterCaptureDirection.ServerToClient &&
                                    string.Equals(any.Key, "ipz", StringComparison.Ordinal)
                                        ? _professionXpStudy.Observe(flow.Number, any.Body)
                                        : null,
                                    _accountWireStudy?.Observe(
                                        flow.Number, direction, any.Key, any.Body),
                                    _rosterStudy?.Observe(
                                        flow.Number, direction, any.Key, any.Body),
                                    _concurrentRosterStudy?.Observe(
                                        flow.Number, direction, any.Key, any.Body),
                                    _itemEffectsShape &&
                                    direction == CharacterCaptureDirection.ServerToClient
                                        ? _effectTypeStudy is not null
                                            ? _effectTypeStudy.Observe(
                                                flow.Number, any.Key, any.Body)
                                            : _effectCatalogStudy is not null
                                                ? _effectCatalogStudy.Observe(any.Key, any.Body)
                                                : ItemEffectsWireShapeStudy.Describe(
                                                    any.Key, any.Body, _expectedEffectValues)
                                        : null,
                                    _stockSnapshotStudy &&
                                    direction == CharacterCaptureDirection.ServerToClient
                                        ? _stockDeltaStudy!.Observe(
                                            flow.Number, any.Key, any.Body)
                                        : null));
                            }
                        }
                    }

                    flow.HadPayload = true;
                }
                catch (TcpReassemblyLimitExceededException)
                {
                    flow.KsbDecodedCandidate = null;
                    flow.KscDecodedCandidates = null;
                    flow.ProfessionFlow = null;
                    flow.Closed = true;
                    _stockDeltaStudy?.DropSession(flow.Number);
                    _concurrentRosterStudy?.ObserveClosedSession(flow.Number);
                    events.Add(new CharacterCaptureEvent(
                        observedAtUtc, flow.Number, direction,
                        CharacterCaptureEventKind.Evicted));
                    return events;
                }
            }

            // Even with no payload, a FIN or RST invalidates the session.
            if (fin || rst)
            {
                // Discard P2f names and candidate IDs immediately when the
                // underlying TCP flow closes; nothing is persisted.
                flow.KsbDecodedCandidate = null;
                flow.KscDecodedCandidates = null;
                flow.ProfessionFlow = null;
                flow.Closed = true;
                _stockDeltaStudy?.DropSession(flow.Number);
                _concurrentRosterStudy?.ObserveClosedSession(flow.Number);
                events.Add(new CharacterCaptureEvent(
                    observedAtUtc, flow.Number, direction,
                    CharacterCaptureEventKind.Closed));
            }

            return events;
        }
    }

    private string ObserveIdentityFields(
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
                flow.KsbDecodedCandidate = null;
                flow.KscDecodedCandidates = null;
                _candidateKsbF3BySession.Remove(flow.Number);
                return "status=conflicted identity=unverified";
            }
        }

        if (opcode == "ksb")
            flow.KsbFields = observation;
        else
            flow.KscFields = observation;

        string crossSession = opcode == "ksb"
            ? " " + ObserveKsbAcrossSessions(flow.Number, observation)
            : "";

        // P2f is a separate versioned experimental decoder. Its values
        // never leave this per-TCP-session object. No CharacterIdentityCorrelator
        // or business service receives them.
        string decoded = ObserveCandidateDecode(
            flow, opcode, body, reference, _expectedClassId);

        if (flow.KsbFields is not null && flow.KscFields is not null)
            return report + " " + CharacterIdentityFieldStudy.Compare(
                flow.KsbFields, flow.KscFields) + crossSession + " " + decoded;

        return report + " same-session=pending identity=unverified" +
               crossSession + " " + decoded;
    }

    private static string ObserveCandidateDecode(
        Session flow, string opcode, byte[] body,
        CharacterNameLevelReference reference, int? expectedClassId)
    {
        if (!CharacterWireCandidateDecoder.TryDecode(
                opcode, body, out CharacterWireCandidateSnapshot? snapshot) ||
            snapshot is null)
            return "p2f=layout-rejected identity=unverified";

        if (opcode == "ksb")
            flow.KsbDecodedCandidate = snapshot;
        else
            flow.KscDecodedCandidates = snapshot;

        bool haveBoth = flow.KsbDecodedCandidate is not null &&
                        flow.KscDecodedCandidates is not null;
        string match = haveBoth
            ? CharacterWireCandidateDecoder.HasMatchingCandidate(
                flow.KsbDecodedCandidate!, flow.KscDecodedCandidates!)
                ? "yes" : "no"
            : "pending";

        // Both candidates are held in this TCP session only. The class
        // check remains observational, never semantic confirmation.
        string classCandidateLink = "pending";
        if (haveBoth)
        {
            bool? equal = CharacterWireCandidateDecoder.CompareClassCandidates(
                flow.KsbDecodedCandidate!, flow.KscDecodedCandidates!);
            classCandidateLink = equal is null
                ? "unavailable" : equal.Value ? "yes" : "no";
        }
        string candidatePresent = snapshot.Candidates.All(
            c => c.ClassIdCandidate.HasValue) ? "all"
            : snapshot.Candidates.Any(c => c.ClassIdCandidate.HasValue)
                ? "partial" : "none";
        string p2j = "p2j=class-candidate=" + candidatePresent +
                     " class-link=" + classCandidateLink +
                     " class=unverified";

        // ksc is a list, ksb is a loaded candidate. Wire equality
        // is NOT independently corroborated semantic identity.
        string classStudy = CharacterClassWireShapeStudy.Describe(
            opcode, body, reference);
        string appearanceStudy = CharacterAppearanceWireStudy.Describe(
            opcode, body, reference);
        string classReferenceStudy = expectedClassId.HasValue
            ? " " + CharacterBreedHypothesisStudy.Describe(
                opcode, body, reference, expectedClassId.Value)
            : "";
        return "p2f=decoded candidates=" + snapshot.Candidates.Count +
               " class=unknown candidate-link=" + match +
               " identity=unverified " + classStudy + " " + appearanceStudy +
               classReferenceStudy + " " + p2j;
    }

    private string ObserveKsbAcrossSessions(
        int sessionNumber, CharacterIdentityFieldObservation observation)
    {
        if (!observation.NumericParent.TryGetValue(3, out ulong candidate) ||
            candidate == 0)
            return "cross-session-f3=unavailable id=unverified";

        // Observe only the ksb candidate for the exact locally supplied
        // name and level. ksc may be a list of selectable characters;
        // it does not independently establish an active character.
        if (_candidateKsbF3BySession.TryGetValue(sessionNumber, out ulong existing))
            return existing == candidate
                ? "cross-session-f3=repeat id=unverified"
                : "cross-session-f3=conflicted id=unverified";

        bool hasBaseline = _candidateKsbF3BySession.Count > 0;
        bool everyPreviousEquals = _candidateKsbF3BySession.Values
            .All(previous => previous == candidate);

        while (_candidateKsbSessionOrder.Count >= MaximumIdentityStudySessions)
        {
            int oldest = _candidateKsbSessionOrder.Dequeue();
            _candidateKsbF3BySession.Remove(oldest);
        }

        _candidateKsbSessionOrder.Enqueue(sessionNumber);
        _candidateKsbF3BySession.Add(sessionNumber, candidate);

        string status = !hasBaseline ? "baseline"
            : everyPreviousEquals ? "equal" : "different";
        return "cross-session-f3=" + status +
               " reference-sessions=" + _candidateKsbF3BySession.Count +
               " id=unverified";
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
