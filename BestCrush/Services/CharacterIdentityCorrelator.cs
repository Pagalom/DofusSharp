namespace BestCrush.Services;

/// <summary>
/// These signals are semantic evidence only after their real Dofus opcodes,
/// direction and fields have been characterized from captures.
/// No current protocol-map entry is known to provide them.
/// </summary>
internal enum CharacterIdentityEvidenceSource
{
    CharacterSelection,
    CharacterDetails,
    CharacterCharacteristics
}

internal enum CharacterIdentityStatus
{
    Unknown,
    Pending,
    Confirmed,
    Ambiguous,
    Disconnected,
    Stale
}

/// <summary>
/// A specific game TCP session, not a server-wide selection. ConnectionKey
/// must distinguish concurrent clients; ConnectionEpoch must change on TCP
/// reconnect, CaptureEpoch on Npcap Stop/Start, and ServerGeneration on
/// global server selection changes.
/// </summary>
internal readonly record struct CharacterConnectionScope(
    string ServerName,
    string ConnectionKey,
    long ServerGeneration,
    long CaptureEpoch,
    long ConnectionEpoch);

internal sealed record CharacterIdentityEvidence(
    CharacterConnectionScope Scope,
    CharacterIdentityEvidenceSource Source,
    ulong CharacterId,
    string Name,
    int? ClassId,
    int? Level,
    DateTime ObservedAtUtc);

internal sealed record ConfirmedCharacterIdentity(
    ulong CharacterId,
    string Name,
    int? ClassId,
    int? Level);

internal sealed record CharacterIdentityAssessment(
    CharacterIdentityStatus Status,
    CharacterConnectionScope Scope,
    ConfirmedCharacterIdentity? Character,
    IReadOnlyDictionary<int, int> ProfessionLevels);

/// <summary>
/// Fail-closed character attribution. Multiple independent identity message
/// families with the same stable character ID and name are required before
/// any character-specific profession observation can be attributed.
///
/// Intentionally disconnected from live protocol routing until verified wire
/// messages and reliable per-TCP-connection lifecycle signals are available.
/// </summary>
internal sealed class CharacterIdentityCorrelator
{
    private sealed class Session(CharacterConnectionScope scope)
    {
        internal CharacterConnectionScope Scope = scope;
        internal CharacterIdentityStatus Status = CharacterIdentityStatus.Unknown;
        internal ulong CharacterId;
        internal string Name = string.Empty;
        internal int? ClassId;
        internal int? Level;
        internal DateTime LastObservedAtUtc = DateTime.MinValue;
        internal HashSet<CharacterIdentityEvidenceSource> Sources = [];
        internal Dictionary<int, int> Professions = [];
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    internal CharacterIdentityAssessment Observe(CharacterIdentityEvidence evidence)
    {
        ValidateScope(evidence.Scope);
        lock (_sync)
        {
            Session? session = GetOrStartSession(evidence.Scope);
            if (session is null)
                return Assessment(evidence.Scope, CharacterIdentityStatus.Stale);

            if (session.Status == CharacterIdentityStatus.Disconnected ||
                session.Status == CharacterIdentityStatus.Ambiguous)
                return Snapshot(session);

            if (evidence.CharacterId == 0 ||
                string.IsNullOrWhiteSpace(evidence.Name) ||
                evidence.Level is <= 0 ||
                evidence.ClassId is <= 0)
                return Snapshot(session);

            if (evidence.ObservedAtUtc < session.LastObservedAtUtc)
                return Snapshot(session);

            string name = evidence.Name.Trim();

            // Another ID on the same live connection may indicate a
            // character switch. Invalidate the old one immediately;
            // wait for independent confirmation of the new candidate.
            if (session.CharacterId != 0 &&
                session.CharacterId != evidence.CharacterId)
            {
                ResetCandidate(session);
            }
            else if (session.CharacterId == evidence.CharacterId &&
                     ((!string.Equals(session.Name, name, StringComparison.Ordinal)) ||
                      (session.ClassId is not null && evidence.ClassId is not null &&
                       session.ClassId != evidence.ClassId)))
            {
                session.Status = CharacterIdentityStatus.Ambiguous;
                session.Professions.Clear();
                return Snapshot(session);
            }

            session.CharacterId = evidence.CharacterId;
            session.Name = name;
            session.ClassId ??= evidence.ClassId;
            session.Level = evidence.Level ?? session.Level;
            session.LastObservedAtUtc = evidence.ObservedAtUtc;
            session.Sources.Add(evidence.Source);
            // Confirm only when two independently decoded message families
            // agree on ID/name and at least one supplies real class/level.
            session.Status = session.Sources.Count >= 2 &&
                             session.ClassId is > 0 &&
                             session.Level is > 0
                ? CharacterIdentityStatus.Confirmed
                : CharacterIdentityStatus.Pending;

            return Snapshot(session);
        }
    }

    internal CharacterIdentityAssessment Disconnect(
        CharacterConnectionScope scope,
        DateTime observedAtUtc)
    {
        ValidateScope(scope);
        lock (_sync)
        {
            Session? session = GetOrStartSession(scope);
            if (session is null)
                return Assessment(scope, CharacterIdentityStatus.Stale);

            if (observedAtUtc < session.LastObservedAtUtc)
                return Snapshot(session);

            ResetCandidate(session);
            session.LastObservedAtUtc = observedAtUtc;
            session.Status = CharacterIdentityStatus.Disconnected;
            return Snapshot(session);
        }
    }

    /// <summary>
    /// Profession messages may omit character ID only when confirmed flow
    /// ownership is reliable and explicitly tied to this exact scope.
    /// No such decoder has been activated yet.
    /// </summary>
    internal bool TryRecordProfession(
        CharacterConnectionScope scope,
        ulong? explicitCharacterId,
        int professionId,
        int level,
        DateTime observedAtUtc)
    {
        if (professionId <= 0 || level <= 0)
            return false;

        lock (_sync)
        {
            if (!_sessions.TryGetValue(scope.ConnectionKey, out Session? session) ||
                session.Scope != scope ||
                session.Status != CharacterIdentityStatus.Confirmed ||
                (explicitCharacterId is not null &&
                 explicitCharacterId.Value != session.CharacterId) ||
                observedAtUtc < session.LastObservedAtUtc)
                return false;

            session.Professions[professionId] = level;
            session.LastObservedAtUtc = observedAtUtc;
            return true;
        }
    }

    internal CharacterIdentityAssessment Read(CharacterConnectionScope scope)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(scope.ConnectionKey, out Session? session) &&
                   session.Scope == scope
                ? Snapshot(session)
                : Assessment(scope, CharacterIdentityStatus.Unknown);
        }
    }

    /// <summary>
    /// With more than one confirmed connection, no automatic global choice
    /// is safe. Callers must select a particular confirmed session.
    /// </summary>
    internal CharacterIdentityAssessment? SoleConfirmedSession()
    {
        lock (_sync)
        {
            Session[] confirmed = _sessions.Values.Where(
                s => s.Status == CharacterIdentityStatus.Confirmed).ToArray();

            return confirmed.Length == 1 ? Snapshot(confirmed[0]) : null;
        }
    }

    private Session? GetOrStartSession(CharacterConnectionScope scope)
    {
        if (!_sessions.TryGetValue(scope.ConnectionKey, out Session? current))
        {
            Session created = new(scope);
            _sessions.Add(scope.ConnectionKey, created);
            return created;
        }

        if (current.Scope == scope)
            return current;

        if (Compare(scope, current.Scope) <= 0)
            return null;

        Session fresh = new(scope);
        _sessions[scope.ConnectionKey] = fresh;
        return fresh;
    }

    private static int Compare(CharacterConnectionScope a, CharacterConnectionScope b)
    {
        // A renamed server without an increased selection generation cannot
        // silently replace an existing connection, even with a newer epoch.
        if (!string.Equals(a.ServerName, b.ServerName, StringComparison.Ordinal) &&
            a.ServerGeneration <= b.ServerGeneration)
            return 0;

        int serverGeneration = a.ServerGeneration.CompareTo(b.ServerGeneration);
        if (serverGeneration != 0)
            return serverGeneration;

        int capture = a.CaptureEpoch.CompareTo(b.CaptureEpoch);
        if (capture != 0)
            return capture;

        int connection = a.ConnectionEpoch.CompareTo(b.ConnectionEpoch);
        if (connection != 0)
            return connection;

        // A different server at the same generation is not an authoritative
        // new session: fail closed instead of accepting arbitrary ordering.
        return 0;
    }

    private static void ResetCandidate(Session session)
    {
        session.CharacterId = 0;
        session.Name = string.Empty;
        session.ClassId = null;
        session.Level = null;
        session.Sources.Clear();
        session.Professions.Clear();
        session.Status = CharacterIdentityStatus.Unknown;
    }

    private static CharacterIdentityAssessment Snapshot(Session session) =>
        new(
            session.Status,
            session.Scope,
            session.Status == CharacterIdentityStatus.Confirmed
                ? new ConfirmedCharacterIdentity(
                    session.CharacterId, session.Name, session.ClassId, session.Level)
                : null,
            new Dictionary<int, int>(session.Professions));

    private static CharacterIdentityAssessment Assessment(
        CharacterConnectionScope scope,
        CharacterIdentityStatus status) =>
        new(status, scope, null, new Dictionary<int, int>());

    private static void ValidateScope(CharacterConnectionScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.ServerName) ||
            string.IsNullOrWhiteSpace(scope.ConnectionKey))
            throw new ArgumentException("A named server and unique game connection are required.");
    }
}
