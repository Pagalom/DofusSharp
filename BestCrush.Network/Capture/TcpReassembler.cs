namespace BestCrush.Network.Capture;

/// <summary>
/// A single TCP flow exceeded its in-memory reassembly limit.
/// Callers must evict this flow: continuing with partial protobuf framing is unsafe.
/// </summary>
public sealed class TcpReassemblyLimitExceededException(string message)
    : IOException(message)
{
}

public sealed class TcpReassembler
{
    private const int DefaultMaximumPendingBytes = 8 * 1024 * 1024;
    private const int DefaultMaximumPendingSegments = 4096;

    private readonly SortedDictionary<uint, byte[]> _pending = new();
    private readonly int _maximumPendingBytes;
    private readonly int _maximumPendingSegments;
    private long _pendingBytes;
    private bool _started;
    private uint _nextSequence;

    public TcpReassembler(
        int maximumPendingBytes = DefaultMaximumPendingBytes,
        int maximumPendingSegments = DefaultMaximumPendingSegments)
    {
        if (maximumPendingBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingBytes));
        if (maximumPendingSegments <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingSegments));

        _maximumPendingBytes = maximumPendingBytes;
        _maximumPendingSegments = maximumPendingSegments;
    }

    public int PendingSegments => _pending.Count;
    public long PendingBytes => _pendingBytes;

    public VarintFrameBuffer FrameBuffer { get; } = new();

    public IEnumerable<ReadOnlyMemory<byte>> Push(uint sequence, byte[] payload)
    {
        if (payload.Length == 0)
            yield break;

        if (!_started)
        {
            _started = true;
            _nextSequence = sequence;
        }

        ulong segmentEnd = (ulong)sequence + (uint)payload.Length;
        if (segmentEnd <= _nextSequence)
            yield break;

        if (sequence < _nextSequence)
        {
            int skip = checked((int)(_nextSequence - sequence));
            payload = payload[skip..];
            sequence = _nextSequence;
        }

        if (!_pending.ContainsKey(sequence))
        {
            // Only out-of-order payloads may remain queued indefinitely.
            // In-order segments are drained below, even when a large
            // legitimate payload fills an earlier gap.
            if (sequence > _nextSequence &&
                (_pending.Count >= _maximumPendingSegments ||
                 _pendingBytes > (long)_maximumPendingBytes - payload.Length))
            {
                throw new TcpReassemblyLimitExceededException(
                    "Too many out-of-order TCP segments or pending bytes.");
            }

            _pending.Add(sequence, payload);
            _pendingBytes += payload.Length;
        }

        // An out-of-order segment can begin BEFORE _nextSequence after
        // another segment has filled part of the gap. Consume only its
        // unseen suffix instead of leaving it permanently queued.
        while (_pending.Count > 0)
        {
            KeyValuePair<uint, byte[]> first = _pending.First();

            if (first.Key > _nextSequence)
                break;

            _pending.Remove(first.Key);
            _pendingBytes -= first.Value.Length;

            ulong firstEnd = (ulong)first.Key + (uint)first.Value.Length;
            if (firstEnd <= _nextSequence)
                continue;

            int alreadyDelivered =
                checked((int)(_nextSequence - first.Key));

            byte[] remaining =
                alreadyDelivered == 0
                    ? first.Value
                    : first.Value[alreadyDelivered..];

            _nextSequence += (uint)remaining.Length;
            yield return remaining;
        }
    }
}

public sealed class VarintFrameBuffer
{
    // TryReadFrame already rejects frames above 16 MiB. Allow one
    // 64-KiB incoming chunk beyond a complete frame and its prefix:
    // the packet reader drains frames after every such chunk.
    public const int MaximumFrameBytes = 16 * 1024 * 1024;
    public const int DefaultMaximumBufferedBytes =
        MaximumFrameBytes + 64 * 1024 + 10;

    private readonly List<byte> _buffer = new();
    private readonly int _maximumBufferedBytes;

    public VarintFrameBuffer(
        int maximumBufferedBytes = DefaultMaximumBufferedBytes)
    {
        if (maximumBufferedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBufferedBytes));

        _maximumBufferedBytes = maximumBufferedBytes;
    }

    public int BufferedBytes => _buffer.Count;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        // Refuse before allocating: a malformed/incomplete frame must
        // not retain unlimited network data on a long-running session.
        if (bytes.Length > _maximumBufferedBytes - _buffer.Count)
        {
            throw new TcpReassemblyLimitExceededException(
                "Incomplete TCP frame exceeded the framing byte limit.");
        }

        for (int i = 0; i < bytes.Length; i++)
            _buffer.Add(bytes[i]);
    }

    public bool TryReadFrame(out byte[] frame)
    {
        frame = Array.Empty<byte>();
        if (_buffer.Count == 0)
            return false;

        if (!TryReadVarint(_buffer, out ulong length, out int prefixBytes))
            return false;

        if (length == 0 || length > MaximumFrameBytes)
        {
            // Perte de synchro : abandon d'un octet, puis nouvelle tentative.
            _buffer.RemoveAt(0);
            return false;
        }

        long total = prefixBytes + (long)length;
        if (_buffer.Count < total)
            return false;

        frame = _buffer.GetRange(prefixBytes, checked((int)length)).ToArray();
        _buffer.RemoveRange(0, checked((int)total));
        return true;
    }

    private static bool TryReadVarint(IReadOnlyList<byte> data, out ulong value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;
        int shift = 0;

        for (int i = 0; i < Math.Min(data.Count, 10); i++)
        {
            byte b = data[i];
            value |= (ulong)(b & 0x7F) << shift;
            bytesRead++;

            if ((b & 0x80) == 0)
                return true;

            shift += 7;
        }

        return false;
    }
}
