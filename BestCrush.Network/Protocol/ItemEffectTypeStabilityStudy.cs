namespace BestCrush.Network.Protocol;

/// <summary>
/// Research-only cross-item comparison. No item identifier or effect code
/// leaves this class. Reference values come from the user's local game UI.
/// For each pair of common characteristics A[i] / B[i], tests whether f10
/// is stable even when f5 differs between two independently observed items.
/// </summary>
public sealed class ItemEffectTypeStabilityStudy
{
    private const int MaximumSessions = 16;
    private readonly long[] _first;
    private readonly long[] _second;
    private readonly Dictionary<int, SessionObservations> _sessions = [];

    private sealed class SessionObservations
    {
        internal Dictionary<long, ulong>? First;
        internal Dictionary<long, ulong>? Second;
    }

    /// <param name="firstValues">All nonzero effects for item A, in common-effect order.</param>
    /// <param name="secondValues">All nonzero effects for item B, corresponding common effects first.</param>
    public ItemEffectTypeStabilityStudy(
        IReadOnlyList<long> firstValues, IReadOnlyList<long> secondValues)
    {
        if (firstValues.Count is < 1 or > 8 ||
            secondValues.Count <= firstValues.Count ||
            secondValues.Count > 16 ||
            firstValues.Contains(0) || secondValues.Contains(0) ||
            firstValues.Distinct().Count() != firstValues.Count ||
            secondValues.Distinct().Count() != secondValues.Count)
            throw new ArgumentException(
                "Enter distinct nonzero effects: 1-8 for A, more for B (maximum 16).");

        _first = firstValues.ToArray();
        _second = secondValues.ToArray();
    }

    public string? Observe(
        int sessionNumber, string opcode, ReadOnlySpan<byte> body)
    {
        if (sessionNumber <= 0)
            return null;

        IReadOnlyList<(long Value, ulong Code)>? pairs =
            ItemEffectsWireShapeStudy.ReadCandidatePairs(opcode, body);
        if (pairs is null)
            return null;

        long[] expected;
        string itemName;
        if (Matches(pairs, _first))
        {
            expected = _first;
            itemName = "A";
        }
        else if (Matches(pairs, _second))
        {
            expected = _second;
            itemName = "B";
        }
        else
            return null;

        // Guard both memory and cross-session attribution.
        if (!_sessions.TryGetValue(sessionNumber, out SessionObservations? session))
        {
            if (_sessions.Count >= MaximumSessions)
                _sessions.Clear();
            session = new SessionObservations();
            _sessions.Add(sessionNumber, session);
        }

        // All values are unique for this bounded study, so each reference
        // identifies exactly one candidate effect in that observed object.
        Dictionary<long, ulong> codes = expected.ToDictionary(
            x => x, x => pairs.Single(p => p.Value == x).Code);

        if (itemName == "A")
            session.First = codes;
        else
            session.Second = codes;

        if (session.First is null || session.Second is null)
            return "item=" + itemName + " other=pending";

        bool distinctA = session.First.Values.Distinct().Count() ==
            session.First.Count;
        bool distinctB = session.Second.Values.Distinct().Count() ==
            session.Second.Count;

        string[] comparisons = Enumerable.Range(0, _first.Length)
            .Select(i => (i + 1) + ":" +
                (session.First[_first[i]] == session.Second[_second[i]]
                    ? "yes" : "no"))
            .ToArray();

        return "item=" + itemName +
            " common-f10=[" + string.Join(" ", comparisons) + "]" +
            " distinct-f10=" + (distinctA && distinctB ? "yes" : "no");
    }

    private static bool Matches(
        IReadOnlyList<(long Value, ulong Code)> pairs, IReadOnlyList<long> expected) =>
        pairs.Count == expected.Count &&
        pairs.Select(p => p.Value).OrderBy(x => x)
            .SequenceEqual(expected.OrderBy(x => x));
}
