using System.Net;
using System.Net.Sockets;
using BestCrush.Network.Capture;
using BestCrush.Network.Protocol;
using PacketDotNet;
using SharpPcap;

namespace BestCrush.NetworkProbe.Capture;

/// <summary>
/// Isolated metadata-only protocol discovery. Does not run semantic market
/// decoders or write raw captures, payloads, endpoints or character details.
/// A per-client TCP tracker is required to avoid cross-account mixing.
/// </summary>
internal sealed class CharacterMetadataProbe(
    ICaptureDevice device, int port,
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
    bool professionJobStudy = false,
    CharacterIdentityPairStudy? identityPairStudy = null,
    CharacterCrossOpcodeFieldStudy? crossOpcodeStudy = null,
    ServerRouteTransitionStudy? serverRouteStudy = null,
    ServerWireTransitionStudy? serverWireStudy = null,
    KtaFieldScopeStudy? ktaScopeStudy = null,
    KtaSelectedCharacterStudy? ktaCharacterStudy = null,
    ServerNameWireStudy? serverNameStudy = null,
    ConnectionServerSelectionStudy? connectionSelectionStudy = null,
    AccountWireFieldComparisonStudy? accountWireStudy = null,
    CharacterRosterContinuityStudy? rosterStudy = null,
    ConcurrentCharacterRosterStudy? concurrentRosterStudy = null,
    ConcurrentRosterWindowEvidenceStudy? windowEvidenceStudy = null,
    bool itemEffectsShape = false) : IDisposable
{
    private const int MaximumVariantsPerOpcode = 3;
    private readonly CharacterProbeTrafficDiagnostics _traffic = new();
    private readonly ServerRouteTransitionStudy? _serverRouteStudy = serverRouteStudy;
    private readonly ServerWireTransitionStudy? _serverWireStudy = serverWireStudy;
    private readonly KtaFieldScopeStudy? _ktaScopeStudy = ktaScopeStudy;
    private readonly KtaSelectedCharacterStudy? _ktaCharacterStudy = ktaCharacterStudy;
    private readonly ServerNameWireStudy? _serverNameStudy = serverNameStudy;
    private readonly ConnectionServerSelectionStudy? _connectionSelectionStudy = connectionSelectionStudy;
    private readonly bool _accountWireProbe = accountWireStudy is not null;
    private readonly bool _rosterProbe = rosterStudy is not null;
    private readonly bool _concurrentRosterProbe = concurrentRosterStudy is not null;
    private readonly ConcurrentCharacterRosterStudy? _concurrentRosterStudy =
        concurrentRosterStudy;
    private readonly ConcurrentRosterWindowEvidenceStudy? _windowEvidence =
        windowEvidenceStudy;
    private readonly object _windowEvidenceSync = new();
    private readonly Dictionary<int, TcpSocketTuple> _sessionSockets = [];
    private const int MaxWindowStudySessions = 128;
    private readonly CharacterCaptureFlowTracker _flows = new(
        shapeKeys: shapeKeys, professionReferences: professionReferences,
        decodeIpz: decodeIpz, studyReferences: studyReferences,
        showCandidateJobIds: showCandidateJobIds,
        identifyReferences: identifyReferences,
        characterNameReference: characterNameReference,
        characterNameOpCodes: characterNameOpCodes,
        identityFieldsReference: identityFieldsReference,
        expectedClassId: expectedClassId,
        studyProfessionFlow: studyProfessionFlow,
        studyProfessionContinuity: studyProfessionContinuity,
        professionXpStudy: professionXpStudy,
        identityPairStudy: identityPairStudy,
        crossOpcodeStudy: crossOpcodeStudy,
        serverWireStudy: serverWireStudy,
        ktaScopeStudy: ktaScopeStudy,
        ktaCharacterStudy: ktaCharacterStudy,
        serverNameStudy: serverNameStudy,
        connectionSelectionStudy: connectionSelectionStudy,
        accountWireStudy: accountWireStudy,
        rosterStudy: rosterStudy,
        concurrentRosterStudy: concurrentRosterStudy,
        itemEffectsShape: itemEffectsShape);
    private readonly bool _shapeMode = shapeKeys is not null;
    private readonly bool _itemEffectsShapeMode = itemEffectsShape;
    private readonly Dictionary<int, int> _reportedEffectLines = new();
    private const int MaximumEffectReportsPerSession = 24;
    private readonly bool _professionMode = professionReferences is not null && !decodeIpz;
    private readonly bool _decodeIpz = decodeIpz;
    private readonly bool _studyIpz = studyReferences is not null;
    private readonly bool _identifyIpz = identifyReferences is not null;
    private readonly bool _characterNameProbe = characterNameReference is not null;
    private readonly bool _identityFieldProbe = identityFieldsReference is not null ||
        identityPairStudy is not null || crossOpcodeStudy is not null;
    private readonly bool _professionFlowProbe = studyProfessionFlow;
    private readonly bool _professionContinuityProbe = studyProfessionContinuity;
    private readonly bool _professionXpProbe = professionXpStudy is not null;
    private readonly bool _professionJobProbe = professionJobStudy;
    private readonly Dictionary<(int Session, string Opcode), HashSet<string>>
        _reportedShapes = new();

    public void Start()
    {
        device.OnPacketArrival += OnPacketArrival;
        try
        {
            device.Open(DeviceModes.Promiscuous, 1000);
            device.Filter = $"tcp port {port}";
            device.StartCapture();
        }
        catch
        {
            device.OnPacketArrival -= OnPacketArrival;
            throw;
        }
    }

    public void Stop()
    {
        try
        {
            device.StopCapture();
        }
        catch
        {
            // Capture shutdown is best-effort.
        }

        device.OnPacketArrival -= OnPacketArrival;
    }

    /// <summary>
    /// Counts only. The Windows OS socket count is limited to IPv4 and
    /// remote port; it does not identify the owner or prove Dofus identity.
    /// </summary>
    /// <summary>
    /// A P3c.7 study only: collect categorical evidence for exact Game
    /// tuple -> unique Windows socket PID -> unique live Dofus HWND.
    /// Do not print or persist tuple, PID or HWND, even on failure.
    /// </summary>
    public void WriteConcurrentWindowSummary()
    {
        if (_windowEvidence is null)
            return;

        // A correct OS PID/HWND join is not enough if the character-roster
        // negative control failed. Publish one positive final conclusion
        // ONLY when both independent diagnostics found their candidate.
        bool rosterCandidate = _concurrentRosterStudy?.Summarize().LastOrDefault()
            ?.Contains("status=concurrent-roster-group-candidate",
                StringComparison.Ordinal) == true;

        IReadOnlyList<string> windowSummary = _windowEvidence.Summarize();
        foreach (string line in windowSummary.Take(windowSummary.Count - 1))
            Console.WriteLine(line);

        string final = windowSummary.Last();
        if (rosterCandidate)
            Console.WriteLine(final);
        else
            Console.WriteLine("p3c7=summary status=incomplete " +
                "reason=roster-pattern-not-confirmed " +
                "account=unverified server=unverified semantic=unverified");
    }

    public void WriteTrafficStatus(string phase)
    {
        int sockets;
        try
        {
            sockets = WindowsTcpSocketTableReader.ReadEstablishedIPv4(port).Count;
        }
        catch
        {
            // OS socket inventory is supporting context only and must
            // never stop an otherwise functioning passive capture.
            sockets = -1;
        }
        Console.WriteLine(_traffic.Snapshot().Format(phase, sockets));
    }

    private void OnPacketArrival(object sender, PacketCapture capture)
    {
        _traffic.PacketArrived();
        try
        {
            RawCapture raw = capture.GetPacket();
            Packet packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            IPPacket? ip = packet.Extract<IPPacket>();
            TcpPacket? tcp = packet.Extract<TcpPacket>();

            if (ip is null || tcp is null ||
                (tcp.SourcePort != port && tcp.DestinationPort != port))
                return;

            _traffic.DofusTcpPacket(tcp.PayloadData.Length);

            // No address or port is ever included in emitted metadata.
            bool incoming = tcp.SourcePort == port;
            CharacterCaptureFlowKey key = incoming
                ? new CharacterCaptureFlowKey(
                    ip.DestinationAddress.ToString(), tcp.DestinationPort,
                    ip.SourceAddress.ToString(), tcp.SourcePort)
                : new CharacterCaptureFlowKey(
                    ip.SourceAddress.ToString(), tcp.SourcePort,
                    ip.DestinationAddress.ToString(), tcp.DestinationPort);

            IReadOnlyList<CharacterCaptureEvent> events = _flows.Observe(
                key,
                incoming
                    ? CharacterCaptureDirection.ServerToClient
                    : CharacterCaptureDirection.ClientToServer,
                tcp.SequenceNumber,
                tcp.PayloadData,
                DateTime.UtcNow,
                syn: tcp.Synchronize,
                fin: tcp.Finished,
                rst: tcp.Reset);

            // Count before mode-specific filtering and duplicate suppression,
            // so missing 'ksc' can be distinguished from a silent interface.
            _traffic.EventsObserved(events);
            _serverRouteStudy?.Observe(key, events);
            _serverWireStudy?.ObserveEvents(events);
            _ktaScopeStudy?.ObserveEvents(key, events);
            _ktaCharacterStudy?.ObserveEvents(events);
            _serverNameStudy?.ObserveEvents(events);
            _connectionSelectionStudy?.ObserveEvents(key, events);
            // Compute OS ownership while the Game socket is still live,
            // but print each result only AFTER its corresponding ksb line.
            // Otherwise an unrelated preceding ksc appears to have
            // triggered WINDOW_ROSTER in the console transcript.
            string?[]? windowReports = _windowEvidence is not null
                ? ObserveConcurrentWindowEvidence(ip, tcp, events)
                : null;

            for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                CharacterCaptureEvent entry = events[eventIndex];
                // P3m.5/6 show only session lifecycle + the two marker
                // opcodes; never dump other messages or their contents.
                if ((_serverRouteStudy is not null || _ktaScopeStudy is not null ||
                     _ktaCharacterStudy is not null || _serverNameStudy is not null ||
                     _connectionSelectionStudy is not null) &&
                    entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.Opcode is not ("ksc" or "ksb"))
                    continue;

                if (_professionMode && entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.ProfessionCandidates is null)
                    continue;

                if (_decodeIpz && entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.ProfessionIpzSummary is null)
                    continue;

                if (_studyIpz && entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.ProfessionStudySummary is null)
                    continue;

                if (_identifyIpz && entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.ProfessionIdentifySummary is null)
                    continue;

                if (_professionFlowProbe && entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.ProfessionFlowSummary is null)
                    continue;
                if (_professionContinuityProbe &&
                    entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.ProfessionContinuitySummary is null)
                    continue;
                if (_accountWireProbe &&
                    entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.AccountWireFieldSummary is null)
                    continue;
                if (_rosterProbe &&
                    entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.RosterContinuitySummary is null)
                    continue;
                if (_concurrentRosterProbe &&
                    entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.ConcurrentRosterSummary is null)
                    continue;
                if (_professionXpProbe &&
                    entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.ProfessionXpStudySummary is null)
                    continue;

                if (_itemEffectsShapeMode && entry.Kind == CharacterCaptureEventKind.Message)
                {
                    if (entry.ItemEffectsShape is null)
                        continue;
                    _reportedEffectLines.TryGetValue(entry.SessionNumber, out int count);
                    if (count >= MaximumEffectReportsPerSession)
                        continue;
                    _reportedEffectLines[entry.SessionNumber] = count + 1;
                }

                if (_characterNameProbe && entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.CharacterNameProbeSummary is null)
                    continue;
                if (_identityFieldProbe && entry.Kind == CharacterCaptureEventKind.Message &&
                    entry.CharacterIdentityFieldSummary is null)
                    continue;

                // P3a must report every transition; identical summaries
                // across A → B → A would otherwise hide a new ipz event.
                if ((_shapeMode || _characterNameProbe || _identityFieldProbe) &&
                    entry.Kind == CharacterCaptureEventKind.Message)
                {
                    // Shape scan shows only selected opcodes, no routine noise.
                    string? summary = _shapeMode
                        ? entry.Shape
                        : _characterNameProbe
                            ? entry.CharacterNameProbeSummary
                            : _professionFlowProbe
                                ? entry.ProfessionFlowSummary
                                : entry.CharacterIdentityFieldSummary;
                    if (summary is null || entry.Opcode is null)
                        continue;

                    var group = (entry.SessionNumber, entry.Opcode);
                    if (!_reportedShapes.TryGetValue(group, out HashSet<string>? seen))
                    {
                        seen = new HashSet<string>(StringComparer.Ordinal);
                        _reportedShapes.Add(group, seen);
                    }

                    if (seen.Count >= MaximumVariantsPerOpcode ||
                        !seen.Add(summary))
                        continue;
                }

                string direction = entry.Direction ==
                    CharacterCaptureDirection.ServerToClient ? "S→C" : "C→S";

                string detail = entry.Kind == CharacterCaptureEventKind.Message
                    ? $"{entry.Opcode} {entry.BodyLength} octets" +
                      (_shapeMode ? $" shape=[{entry.Shape}]" : "") +
                      (_itemEffectsShapeMode ? $" ITEM_EFFECTS=[{entry.ItemEffectsShape}]" : "") +
                      (_professionMode ? $" CANDIDAT=[{entry.ProfessionCandidates}]" : "") +
                      (_decodeIpz ? $" IPZ=[{entry.ProfessionIpzSummary}]" : "") +
                      (_studyIpz ? $" IPZ_STUDY=[{entry.ProfessionStudySummary}]" : "") +
                      (_identifyIpz ? $" IPZ_IDENTIFY=[{entry.ProfessionIdentifySummary}]" : "") +
                      (_characterNameProbe ? $" IDENTITY_NAME=[{entry.CharacterNameProbeSummary}]" : "") +
                      (_identityFieldProbe ? $" IDENTITY_FIELDS=[{entry.CharacterIdentityFieldSummary}]" : "") +
                      (_professionFlowProbe ? $" PROFESSION_FLOW=[{entry.ProfessionFlowSummary}]" : "") +
                      (_professionContinuityProbe ? $" PROFESSION_CONTINUITY=[{entry.ProfessionContinuitySummary}]" : "") +
                      (_accountWireProbe ? $" ACCOUNT_WIRE=[{entry.AccountWireFieldSummary}]" : "") +
                      (_rosterProbe ? $" ROSTER_STUDY=[{entry.RosterContinuitySummary}]" : "") +
                      (_concurrentRosterProbe ? $" CONCURRENT_ROSTER=[{entry.ConcurrentRosterSummary}]" : "") +
                      (_professionXpProbe
                          ? $" {(_professionJobProbe ? "PROFESSION_JOB" : "PROFESSION_XP")}=[{entry.ProfessionXpStudySummary}]" : "")
                    : entry.Kind.ToString().ToUpperInvariant();

                Console.WriteLine(
                    $"[{entry.ObservedAtUtc:HH:mm:ss.fff} UTC] " +
                    $"Session #{entry.SessionNumber} {direction} {detail}");

                if (windowReports is not null &&
                    windowReports[eventIndex] is { } windowReport)
                    Console.WriteLine("WINDOW_ROSTER=[" + windowReport + "]");

                if (entry.Kind is CharacterCaptureEventKind.Closed or
                    CharacterCaptureEventKind.Evicted)
                {
                    _reportedEffectLines.Remove(entry.SessionNumber);
                    foreach (var group in _reportedShapes.Keys
                        .Where(group => group.Session == entry.SessionNumber).ToArray())
                        _reportedShapes.Remove(group);
                }
            }
        }
        catch (Exception ex)
        {
            _traffic.PacketError();
            // Deliberately never print packet data, exceptions' captured
            // byte contents, TCP endpoint details, or stack traces.
            Console.Error.WriteLine(
                $"[metadata probe] Paquet ignoré : {ex.GetType().Name}.");
        }
    }

    private string?[] ObserveConcurrentWindowEvidence(
        IPPacket ip, TcpPacket tcp, IReadOnlyList<CharacterCaptureEvent> events)
    {
        // Indexed to the input event batch: a single captured TCP packet can
        // yield several decoded messages and lifecycle events.
        string?[] reports = new string?[events.Count];
        if (_windowEvidence is null || _concurrentRosterStudy is null ||
            ip.SourceAddress.AddressFamily != AddressFamily.InterNetwork ||
            ip.DestinationAddress.AddressFamily != AddressFamily.InterNetwork)
            return reports;

        TcpSocketTuple? socket = TcpSocketTuple.FromPacket(
            ip.SourceAddress, tcp.SourcePort,
            ip.DestinationAddress, tcp.DestinationPort, port);
        if (socket is null)
            return reports;

        lock (_windowEvidenceSync)
        {
            for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                CharacterCaptureEvent entry = events[eventIndex];
                if (entry.Kind == CharacterCaptureEventKind.Opened)
                {
                    if (_sessionSockets.Count < MaxWindowStudySessions &&
                        !_sessionSockets.ContainsKey(entry.SessionNumber))
                        _sessionSockets.Add(entry.SessionNumber, socket.Value);
                    continue;
                }

                if (entry.Kind is CharacterCaptureEventKind.Closed or
                    CharacterCaptureEventKind.Evicted)
                {
                    _windowEvidence.ObserveClosed(entry.SessionNumber);
                    _sessionSockets.Remove(entry.SessionNumber);
                    continue;
                }

                if (entry.Kind != CharacterCaptureEventKind.Message ||
                    entry.Opcode != "ksb" ||
                    entry.ConcurrentRosterSummary is null ||
                    !entry.ConcurrentRosterSummary.Contains(
                        "status=linked", StringComparison.Ordinal) ||
                    !_sessionSockets.TryGetValue(
                        entry.SessionNumber, out TcpSocketTuple saved) ||
                    saved != socket.Value)
                    continue;

                for (int stage = 0; stage < 3; stage++)
                {
                    if (_concurrentRosterStudy.LinkedGameSession(stage) !=
                        entry.SessionNumber)
                        continue;

                    IReadOnlyList<TcpSocketOwner> owners;
                    IReadOnlyList<ConcurrentRosterWindowEvidenceStudy.WindowCandidate>
                        windows;
                    try
                    {
                        owners = WindowsDofusSocketTable.EstablishedIPv4(port);
                        windows = WindowsDofusSocketTable.VisibleWindows()
                            .Select(w => new ConcurrentRosterWindowEvidenceStudy
                                .WindowCandidate(w.Handle, w.ProcessId))
                            .ToArray();
                    }
                    catch (Exception ex) when (
                        ex is System.ComponentModel.Win32Exception or
                              InvalidOperationException or
                              System.Runtime.InteropServices.ExternalException)
                    {
                        owners = [];
                        windows = [];
                    }

                    string status = _windowEvidence.ObserveLinked(
                        stage, entry.SessionNumber, socket.Value,
                        owners, windows);
                    // Never print the PID/HWND/tuple or an account ID.
                    // Defer reporting until the corresponding ksb line.
                    reports[eventIndex] = status;
                    break;
                }
            }
        }
        return reports;
    }

    public void Dispose()
    {
        Stop();
        device.Dispose();
    }
}
