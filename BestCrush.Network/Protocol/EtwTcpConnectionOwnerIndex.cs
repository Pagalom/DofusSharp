using System.Collections.Generic;

namespace BestCrush.Network.Protocol;

/// <summary>
/// P3m.14: bounded, ephemeral evidence from Windows ETW TcpIp/Connect.
/// No ETW package dependency in the Network library. The exact IPv4
/// socket tuple AND connect timestamp are required; neither PID nor
/// endpoint is persisted or printed. A conflicting PID quarantines a
/// tuple rather than silently selecting its latest observation.
/// </summary>
public sealed class EtwTcpConnectionOwnerIndex
{
    private const int MaxTuples = 128;
    private static readonly TimeSpan MaxTimeSkew = TimeSpan.FromSeconds(10);
    private readonly object _sync = new();
    private readonly Dictionary<TcpSocketTuple, Entry> _entries = [];
    private bool _overflow;

    private sealed class Entry(int pid, DateTime whenUtc)
    {
        internal readonly int Pid = pid;
        internal readonly DateTime WhenUtc = whenUtc;
        internal bool Conflicted;
    }

    public bool Overflow
    {
        get { lock (_sync) return _overflow; }
    }

    public int Count
    {
        get { lock (_sync) return _entries.Count; }
    }

    /// <summary>
    /// Record ETW event data, not the generic event header's often
    /// unrelated ProcessID. The caller must use the TCP Connect payload
    /// PID and pass a timestamp normalized to UTC.
    /// </summary>
    public void Observe(TcpSocketTuple tuple, int processId, DateTime connectUtc)
    {
        if (processId <= 0 || connectUtc.Kind != DateTimeKind.Utc ||
            tuple.LocalPort == 0 || tuple.RemotePort == 0)
            return;

        lock (_sync)
        {
            if (_entries.TryGetValue(tuple, out Entry? previous))
            {
                if (previous.Pid != processId ||
                    (connectUtc - previous.WhenUtc).Duration() > MaxTimeSkew)
                    previous.Conflicted = true;
                return;
            }
            if (_entries.Count >= MaxTuples)
            {
                _overflow = true;
                return;
            }
            _entries.Add(tuple, new Entry(processId, connectUtc));
        }
    }

    /// <summary>
    /// Returns an owner only for the same tuple with a connect event close
    /// to first Npcap observation, never by temporal adjacency alone.
    /// </summary>
    public int? Resolve(TcpSocketTuple tuple, DateTime firstObservedUtc)
    {
        if (firstObservedUtc.Kind != DateTimeKind.Utc)
            return null;
        lock (_sync)
        {
            if (_overflow || !_entries.TryGetValue(tuple, out Entry? entry) ||
                entry.Conflicted ||
                (firstObservedUtc - entry.WhenUtc).Duration() > MaxTimeSkew)
                return null;
            return entry.Pid;
        }
    }

    public bool IsConflicted(TcpSocketTuple tuple)
    {
        lock (_sync)
            return _entries.TryGetValue(tuple, out Entry? entry) &&
                   entry.Conflicted;
    }
}
