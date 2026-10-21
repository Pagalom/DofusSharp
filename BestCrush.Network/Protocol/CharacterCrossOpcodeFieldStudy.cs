using BestCrush.Network.Capture;

namespace BestCrush.Network.Protocol;

/// <summary>
/// P3m.4: bounded, RAM-only search for OTHER opcode paths containing a
/// character's ksc/ksb parent.f3 candidate. Equality is exploratory and
/// NEVER constitutes an independently verified character or server identity.
/// </summary>
public sealed class CharacterCrossOpcodeFieldStudy
{
    public static IReadOnlySet<string> StudiedOpCodes { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "krg", "kes", "jev", "jgi", "knc", "kod",
            "ifg", "kmz", "ica", "irl"
        };

    private const int MaxSessions = 8;
    private const int MaxMessagesPerSession = 96;
    private const int MaxValuesPerMessage = 48;
    private const int MaxBodyBytes = 8192;
    private const int MaxNestedBytes = 2048;
    private const int MaxFieldsPerNode = 256;
    private const int MaxDepth = 3;

    private sealed record ObservedValue(string Opcode, string Path, ulong Value);

    private sealed class Session
    {
        internal ulong? Ksc;
        internal ulong? Ksb;
        internal bool Conflicted;
        internal int Messages;
        internal readonly List<ObservedValue> Observed = [];
    }

    private readonly object _sync = new();
    private readonly CharacterNameLevelReference _reference;
    private readonly Dictionary<int, Session> _sessions = [];

    public CharacterCrossOpcodeFieldStudy(CharacterNameLevelReference reference)
    {
        CharacterNameLevelReferenceInput.Validate(reference);
        _reference = reference;
    }

    public override string ToString() => "[cross-opcode character study: redacted]";

    public string? Observe(
        int sessionNumber, CharacterCaptureDirection direction,
        string opcode, ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0 ||
            direction != CharacterCaptureDirection.ServerToClient ||
            (opcode is not ("ksb" or "ksc") && !StudiedOpCodes.Contains(opcode)))
            return null;

        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionNumber, out Session? session))
            {
                if (_sessions.Count >= MaxSessions)
                    return opcode is "ksb" or "ksc"
                        ? "p3m4=session-limit identity=unverified"
                        : null;
                session = new Session();
                _sessions.Add(sessionNumber, session);
            }

            if (opcode is "ksb" or "ksc")
            {
                string status = CharacterIdentityFieldStudy.Observe(
                    opcode, body, _reference,
                    out CharacterIdentityFieldObservation? observation);

                if (status is "match=ambiguous" or "rejected-layout")
                    session.Conflicted = true;

                if (observation is not null &&
                    observation.NumericParent.TryGetValue(3, out ulong candidate) &&
                    candidate > 0)
                {
                    ulong? prior = opcode == "ksb" ? session.Ksb : session.Ksc;
                    if (prior.HasValue && prior.Value != candidate)
                        session.Conflicted = true;
                    else if (opcode == "ksb")
                        session.Ksb = candidate;
                    else
                        session.Ksc = candidate;
                }

                if (session.Conflicted ||
                    session.Ksb.HasValue && session.Ksc.HasValue &&
                    session.Ksb != session.Ksc)
                {
                    session.Conflicted = true;
                    return "p3m4=conflicted identity=unverified";
                }

                bool linked = session.Ksb.HasValue &&
                              session.Ksc.HasValue &&
                              session.Ksb == session.Ksc;
                return "p3m4=reference-observed family=" + opcode +
                       " linked=" + (linked ? "yes" : "pending") +
                       " identity=unverified";
            }

            // Retain only bounded numeric wire observations, never payloads,
            // strings, endpoints, hashes or full messages.
            if (session.Messages < MaxMessagesPerSession &&
                body.Length is > 0 and <= MaxBodyBytes)
            {
                session.Messages++;
                var values = new List<ObservedValue>();
                int budget = MaxValuesPerMessage;
                Scan(opcode, body, "", 0, ref budget, values);
                session.Observed.AddRange(values);
            }
            return null;
        }
    }

    public IReadOnlyList<string> Summarize()
    {
        lock (_sync)
        {
            List<string> result = [];
            List<ulong> confirmed = [];
            foreach (var pair in _sessions.OrderBy(p => p.Key))
            {
                Session session = pair.Value;
                bool linked = !session.Conflicted &&
                    session.Ksb.HasValue && session.Ksc.HasValue &&
                    session.Ksb == session.Ksc;
                if (!linked)
                {
                    result.Add($"p3m4=session={pair.Key} linked=no " +
                               $"observed={session.Messages} identity=unverified");
                    continue;
                }

                ulong candidate = session.Ksb!.Value;
                confirmed.Add(candidate);
                var hits = session.Observed
                    .Where(v => v.Value == candidate)
                    .Select(v => v.Opcode + ":" + v.Path)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .Take(20)
                    .ToArray();

                result.Add($"p3m4=session={pair.Key} linked=yes " +
                           $"observed={session.Messages} " +
                           $"candidate-paths=[{(hits.Length == 0 ? "none" : string.Join(",", hits))}] " +
                           "meaning=unverified identity=unverified server=unverified");
            }

            result.Add("p3m4=summary linked-sessions=" + confirmed.Count +
                " cross-session-f3=" +
                (confirmed.Count < 2 ? "unavailable"
                : confirmed.All(v => v == confirmed[0]) ? "equal" : "different") +
                " meaning=unverified server=unverified");

            return result;
        }
    }

    private static void Scan(
        string opcode, ReadOnlySpan<byte> body, string prefix,
        int depth, ref int budget, List<ObservedValue> output)
    {
        if (depth > MaxDepth || budget <= 0 ||
            body.Length is < 1 or > MaxBodyBytes)
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
        if (fields is null || fields.Count > MaxFieldsPerNode)
            return;

        foreach (ProtoField field in fields)
        {
            if (budget <= 0)
                return;
            if (field.Number is < 1 or > 128)
                continue;

            string path = prefix + "f" + field.Number;
            if (field.WireType == ProtoWireType.Varint)
            {
                output.Add(new ObservedValue(opcode, path, field.Varint));
                budget--;
            }
            else if (depth < MaxDepth &&
                     field.WireType == ProtoWireType.LengthDelimited &&
                     field.Bytes is { Length: > 0 and <= MaxNestedBytes })
            {
                // Child interpretation is heuristic; a text/packed field can
                // look like a protobuf message, hence candidate-only output.
                Scan(opcode, field.Bytes, path + ".", depth + 1,
                    ref budget, output);
            }
        }
    }
}
