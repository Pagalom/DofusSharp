namespace BestCrush.NetworkProbe.Capture;

internal sealed class TcpReassembler
{
    private readonly SortedDictionary<uint, byte[]> _pending = new();
    private bool _started;
    private uint _nextSequence;

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
            _pending.Add(sequence, payload);

        while (_pending.TryGetValue(_nextSequence, out byte[]? next))
        {
            _pending.Remove(_nextSequence);
            _nextSequence += (uint)next.Length;
            yield return next;
        }
    }
}

internal sealed class VarintFrameBuffer
{
    private readonly List<byte> _buffer = new();

    public void Append(ReadOnlySpan<byte> bytes)
    {
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

        if (length == 0 || length > 16 * 1024 * 1024)
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
