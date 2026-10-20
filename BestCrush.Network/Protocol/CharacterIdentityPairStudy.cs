namespace BestCrush.Network.Protocol;

/// <summary>
/// P3m.3b opt-in, anonymous comparison of two manually identified characters.
/// It never prints, hashes or persists names, levels, candidate IDs or packets.
/// Matching a name/level is a REFERENCE, not a verified game identity.
/// </summary>
public sealed class CharacterIdentityPairStudy
{
    private const int MaximumSessions = 16;
    private readonly object _gate = new();
    private readonly CharacterNameLevelReference[] _references;
    private readonly Dictionary<int, Session> _sessions = [];
    private readonly Dictionary<int, (int SessionNumber, ulong Candidate)> _confirmed = [];
    private bool _conflicted;

    private sealed class Session
    {
        internal readonly ulong?[] Ksb = new ulong?[2];
        internal readonly ulong?[] Ksc = new ulong?[2];
        internal readonly bool[] Invalid = new bool[2];
    }

    public CharacterIdentityPairStudy(
        CharacterNameLevelReference first,
        CharacterNameLevelReference second)
    {
        CharacterNameLevelReferenceInput.Validate(first);
        CharacterNameLevelReferenceInput.Validate(second);
        // Names are NOT globally unique: homonyms on different servers
        // (or with different levels) are valid characters. However two
        // identical Name+Level references cannot be disambiguated by this
        // research-only decoder, regardless of client/window selection.
        if (string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase) &&
            first.Level == second.Level)
            throw new ArgumentException(
                "Identical name and level references are ambiguous without a verified server/character identity.");
        _references = [first, second];
    }

    public override string ToString() => "[two local character references: redacted]";

    /// <summary>
    /// Inputs must be server-to-client ksc/ksb messages within separate TCP
    /// sessions. Two matching independent message families are required for
    /// each reference before testing their f3 equality.
    /// </summary>
    public string Observe(int sessionNumber, string opcode, ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 || opcode is not ("ksc" or "ksb"))
            return "p3m3=ignored identity=unverified";

        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionNumber, out Session? session))
            {
                if (_sessions.Count >= MaximumSessions)
                    return "p3m3=limit-reached identity=unverified";
                session = new Session();
                _sessions.Add(sessionNumber, session);
            }

            // Never compare or store unverified protobuf numeric values
            // unless the exact local name AND level were uniquely matched.
            int matchedKsb = 0;
            for (int i = 0; i < 2; i++)
            {
                string status = CharacterIdentityFieldStudy.Observe(
                    opcode, body, _references[i],
                    out CharacterIdentityFieldObservation? observed);

                if (status is "match=ambiguous" or "rejected-layout")
                {
                    session.Invalid[i] = true;
                    _conflicted = true;
                }

                if (observed is null ||
                    !observed.NumericParent.TryGetValue(3, out ulong value) ||
                    value == 0)
                    continue;

                ulong?[] target = opcode == "ksb" ? session.Ksb : session.Ksc;
                if (target[i] is { } old && old != value)
                {
                    session.Invalid[i] = true;
                    _conflicted = true;
                }
                else
                    target[i] = value;

                if (opcode == "ksb")
                    matchedKsb++;
            }

            if (matchedKsb > 1)
                _conflicted = true;

            for (int i = 0; i < 2; i++)
            {
                if (session.Invalid[i] ||
                    session.Ksb[i] is not { } selected ||
                    session.Ksc[i] is not { } listed)
                    continue;

                if (selected != listed)
                {
                    _conflicted = true;
                    continue;
                }

                if (_confirmed.TryGetValue(i, out var previous))
                {
                    // Same character must be stable, and each reference
                    // must belong to an unambiguous TCP session.
                    if (previous.Candidate != selected)
                        _conflicted = true;
                }
                else
                    _confirmed.Add(i, (sessionNumber, selected));
            }

            if (_conflicted)
                return "p3m3=conflicted comparison=unverified";

            if (_confirmed.Count != 2)
                return "p3m3=reference-pending confirmed=" +
                    _confirmed.Count + "/2 identity=unverified";

            var a = _confirmed[0];
            var b = _confirmed[1];
            if (a.SessionNumber == b.SessionNumber)
                return "p3m3=same-tcp-session comparison=unverified";

            return a.Candidate == b.Candidate
                ? "p3m3=pair-complete f3=same semantic=unverified"
                : "p3m3=pair-complete f3=distinct semantic=unverified";
        }
    }
}
