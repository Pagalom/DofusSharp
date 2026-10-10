namespace BestCrush.Services;

/// <summary>
/// Bounded cache for per-connection TCP assembly. Callers synchronize access.
/// A discarded flow starts a fresh assembler on its next packet, never
/// extending a previous capture lease.
/// </summary>
internal sealed class NetworkFlowCache<TKey, TReader>
    where TKey : notnull
{
    private sealed class Entry(
        NetworkCaptureLease lease,
        TReader reader,
        DateTime lastSeenUtc)
    {
        internal NetworkCaptureLease Lease = lease;
        internal TReader Reader = reader;
        internal DateTime LastSeenUtc = lastSeenUtc;
    }

    private readonly Dictionary<TKey, Entry> _entries = [];
    private readonly int _capacity;
    private readonly TimeSpan _idleTimeout;

    internal NetworkFlowCache(
        int capacity = 256,
        TimeSpan? idleTimeout = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _idleTimeout = idleTimeout ?? TimeSpan.FromHours(1);

        if (_idleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));
    }

    internal int Count => _entries.Count;

    internal void Clear() => _entries.Clear();

    internal TReader GetOrCreate(
        TKey key,
        NetworkCaptureLease lease,
        DateTime nowUtc,
        Func<TReader> createReader)
    {
        if (_entries.TryGetValue(key, out Entry? existing))
        {
            // If the same flow was idle for too long, its old sequence
            // state cannot be trusted even if the 5-tuple is reused.
            if (existing.Lease == lease &&
                nowUtc - existing.LastSeenUtc < _idleTimeout)
            {
                existing.LastSeenUtc = nowUtc;
                return existing.Reader;
            }

            _entries.Remove(key);
        }

        // This work happens only on a newly encountered / expired flow,
        // not on every packet of a healthy Dofus connection.
        DateTime cutoffUtc = nowUtc - _idleTimeout;
        foreach (TKey staleKey in _entries
            .Where(pair =>
                pair.Value.Lease != lease ||
                pair.Value.LastSeenUtc <= cutoffUtc)
            .Select(pair => pair.Key)
            .ToArray())
        {
            _entries.Remove(staleKey);
        }

        if (_entries.Count >= _capacity)
        {
            TKey oldestKey = _entries
                .MinBy(pair => pair.Value.LastSeenUtc)
                .Key;

            _entries.Remove(oldestKey);
        }

        TReader reader = createReader();
        _entries.Add(key, new Entry(lease, reader, nowUtc));
        return reader;
    }
}
