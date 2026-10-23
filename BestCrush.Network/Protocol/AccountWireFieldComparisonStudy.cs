using BestCrush.Network.Capture;

namespace BestCrush.Network.Protocol;

/// <summary>
/// P3c.4, opt-in ONLY: passive comparison of numeric paths in the S->C krg
/// game-state message across four operator-controlled phases:
/// A/character X, A/character Y, B/character Z, A/character X.
/// No account identity is asserted, exported, hashed or persisted.
/// Never examines client->server authentication messages.
/// </summary>
public sealed class AccountWireFieldComparisonStudy
{
    private const int Phases = 4;
    private const int MaximumBodyBytes = 8192;
    private const int MaximumFieldsPerNode = 256;
    private const int MaximumScalarPaths = 64;
    private const int MaximumNestedBytes = 2048;
    private const int MaximumDepth = 3;

    private sealed class Phase
    {
        internal readonly HashSet<int> NewSessions = [];
        internal int? GameSession;
        internal Dictionary<string, ulong>? Fields;
        internal bool Conflict;
        internal int Rejected;
    }

    private readonly object _gate = new();
    private readonly Phase[] _phases =
        Enumerable.Range(0, Phases).Select(_ => new Phase()).ToArray();
    private int _current = -1;

    public override string ToString() => "[account wire candidates: redacted]";

    public bool StartPhase(int phase)
    {
        lock (_gate)
        {
            if (phase != _current + 1 || phase < 0 || phase >= Phases)
                return false;
            _current = phase;
            return true;
        }
    }

    /// <summary>Only a TCP flow newly opened DURING the active phase is eligible.</summary>
    public void ObserveOpenedSession(int sessionNumber)
    {
        lock (_gate)
        {
            if (_current >= 0 && sessionNumber > 0 &&
                _phases[_current].NewSessions.Count < 32)
                _phases[_current].NewSessions.Add(sessionNumber);
        }
    }

    public string? Observe(
        int sessionNumber, CharacterCaptureDirection direction,
        string opcode, ReadOnlySpan<byte> body)
    {
        if (direction != CharacterCaptureDirection.ServerToClient ||
            opcode != "krg" || sessionNumber <= 0)
            return null;

        lock (_gate)
        {
            if (_current < 0 ||
                !_phases[_current].NewSessions.Contains(sessionNumber))
                return null;

            Phase phase = _phases[_current];
            if (phase.Conflict)
                return Report("conflicted");

            if (phase.GameSession is { } previous && previous != sessionNumber)
            {
                phase.Conflict = true;
                phase.Fields = null;
                return Report("multiple-game-sessions");
            }

            Dictionary<string, ulong>? fields = ExtractUniqueScalars(body);
            if (fields is null || fields.Count == 0)
            {
                phase.Rejected++;
                return Report("layout-rejected");
            }

            if (phase.Fields is not null &&
                (phase.Fields.Count != fields.Count ||
                 phase.Fields.Any(kv =>
                     !fields.TryGetValue(kv.Key, out ulong value) ||
                     value != kv.Value)))
            {
                phase.Conflict = true;
                phase.Fields = null;
                return Report("contradictory-snapshot");
            }

            phase.GameSession = sessionNumber;
            phase.Fields ??= fields;
            return Report("observed");
        }
    }

    /// <summary>
    /// Only wire PATHS matching A-X == A-Y == A-X2 != B-Z are exposed.
    /// Constant/game-character fields can match accidentally: candidate ONLY.
    /// </summary>
    public IReadOnlyList<string> Summarize()
    {
        lock (_gate)
        {
            List<string> report = [];
            foreach (Phase phase in _phases.Select((value, index) => value))
            {
                int i = Array.IndexOf(_phases, phase);
                report.Add($"p3c4=phase={i + 1} status=" +
                    (phase.Conflict ? "conflicted" :
                     phase.Fields is null ? "unavailable" : "recorded") +
                    $" fields={phase.Fields?.Count ?? 0} " +
                    "account=unverified server=unverified");
            }

            if (_current != Phases - 1 ||
                _phases.Any(p => p.Conflict || p.Fields is null))
            {
                report.Add("p3c4=summary status=incomplete account=unverified " +
                    "server=unverified semantic=unverified");
                return report;
            }

            Dictionary<string, ulong> a1 = _phases[0].Fields!;
            Dictionary<string, ulong> ay = _phases[1].Fields!;
            Dictionary<string, ulong> bz = _phases[2].Fields!;
            Dictionary<string, ulong> a2 = _phases[3].Fields!;

            string[] candidates = a1.Where(kv =>
                    kv.Value != 0 &&
                    ay.TryGetValue(kv.Key, out ulong ayValue) &&
                    a2.TryGetValue(kv.Key, out ulong a2Value) &&
                    bz.TryGetValue(kv.Key, out ulong bzValue) &&
                    kv.Value == ayValue && kv.Value == a2Value &&
                    kv.Value != bzValue)
                .Select(kv => "krg:" + kv.Key)
                .OrderBy(x => x, StringComparer.Ordinal)
                .Take(12).ToArray();

            report.Add("p3c4=summary status=compared " +
                "account-shaped-paths=[" +
                (candidates.Length == 0 ? "none" : string.Join(",", candidates)) +
                "] account=unverified server=unverified semantic=unverified");
            return report;
        }
    }

    private string Report(string result) =>
        $"p3c4=phase={_current + 1} status={result} " +
        "account=unverified server=unverified";

    private static Dictionary<string, ulong>? ExtractUniqueScalars(
        ReadOnlySpan<byte> body)
    {
        if (body.Length is < 1 or > MaximumBodyBytes)
            return null;

        Dictionary<string, ulong> found = new(StringComparer.Ordinal);
        HashSet<string> duplicate = new(StringComparer.Ordinal);
        int budget = MaximumScalarPaths;
        Scan(body, "", 0, ref budget, found, duplicate);
        foreach (string key in duplicate)
            found.Remove(key);
        return found;
    }

    private static void Scan(
        ReadOnlySpan<byte> bytes, string prefix, int depth,
        ref int budget, Dictionary<string, ulong> found,
        HashSet<string> duplicate)
    {
        if (budget <= 0 || depth > MaximumDepth ||
            bytes.Length is < 1 or > MaximumBodyBytes)
            return;

        List<ProtoField>? fields;
        try { fields = ProtoWire.ReadFields(bytes); }
        catch (Exception ex) when (
            ex is OverflowException or ArgumentOutOfRangeException)
        { return; }

        if (fields is null || fields.Count > MaximumFieldsPerNode)
            return;

        // Repeated containers commonly represent inventory rows or job
        // collections. They cannot be stable singleton account candidates.
        var repeated = fields.GroupBy(f => f.Number)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet();

        foreach (ProtoField field in fields)
        {
            if (budget <= 0)
                break;
            if (field.Number is < 1 or > 128 || repeated.Contains(field.Number))
                continue;

            string path = prefix + "f" + field.Number;
            if (field.WireType == ProtoWireType.Varint)
            {
                if (!found.TryAdd(path, field.Varint))
                    duplicate.Add(path);
                budget--;
            }
            else if (field.WireType == ProtoWireType.LengthDelimited &&
                     field.Bytes is { Length: > 0 and <= MaximumNestedBytes } &&
                     depth < MaximumDepth)
            {
                Scan(field.Bytes, path + ".", depth + 1,
                    ref budget, found, duplicate);
            }
        }
    }
}
