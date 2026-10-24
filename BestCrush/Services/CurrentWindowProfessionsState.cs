using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// P3d.2: provisional profession data observed on an exact, verified
/// Game TCP connection/window. This is NOT an Ankama account identity.
/// All snapshots are transient and never written to SQLite.
/// </summary>
public sealed class CurrentWindowProfessionsState(CurrentServerState serverState)
{
    private const int MaximumWindows = 16;
    private readonly object _sync = new();
    private readonly Dictionary<DofusClientWindowKey, WindowSnapshot> _snapshots = [];
    private long _epoch = -1;

    private sealed record WindowSnapshot(
        NetworkCaptureLease Lease,
        IReadOnlyDictionary<ulong, AccountProfessionProgress> Professions);

    private static readonly IReadOnlyDictionary<ulong, string> NamesByPublicId =
        ProfessionPublicJobIdReference.ByName.ToDictionary(
            entry => entry.Value, entry => entry.Key);

    public event Action? Changed;

    /// <summary>
    /// The caller may already have a verified connection, but the state
    /// revalidates that exact lease independently. A focus or SQL filter
    /// never authorizes these observations.
    /// </summary>
    internal bool Observe(DofusWireMessage message)
    {
        if (message.Direction != "S→C" || message.Key != "ipz" ||
            message.CandidateWindow is not { } window ||
            message.CaptureLease is not { } lease ||
            lease.Window != window ||
            lease.CaptureEpoch != message.CaptureEpoch ||
            lease.ConnectionId != message.ConnectionId ||
            !serverState.AutomaticRoutingEnabled ||
            !serverState.IsCaptureLeaseActive(lease))
            return false;

        bool fullRoster =
            ProfessionIpzDecoder.TryDecode(message.Body, out ProfessionIpzSnapshot? roster);

        IReadOnlyDictionary<ulong, AccountProfessionProgress>? initial = null;
        ProfessionIpzRow? single = null;

        if (fullRoster && roster is not null)
        {
            Dictionary<ulong, AccountProfessionProgress> mapped = [];
            HashSet<ulong> expected =
                ProfessionPublicJobIdReference.ReferenceTwenty.ToHashSet();
            foreach (ProfessionIpzRow row in roster.Rows)
            {
                if (!row.UnidentifiedVarints.TryGetValue(5, out ulong id) ||
                    !expected.Contains(id) ||
                    !NamesByPublicId.TryGetValue(id, out string? name) ||
                    !mapped.TryAdd(id, new AccountProfessionProgress(
                        name, row.Level, row.ExperienceCounter)))
                    return false;
            }
            if (mapped.Count != expected.Count)
                return false;

            initial = mapped;
        }
        else if (!ProfessionIpzDecoder.TryDecodeSingleRow(message.Body, out single) ||
                 single is null)
        {
            return false;
        }

        bool changed = false;
        lock (_sync)
        {
            if (message.CaptureEpoch < _epoch)
                return false;
            if (message.CaptureEpoch > _epoch)
            {
                _snapshots.Clear();
                _epoch = message.CaptureEpoch;
            }

            if (initial is not null)
            {
                if (!_snapshots.ContainsKey(window) &&
                    _snapshots.Count >= MaximumWindows)
                    return false;

                _snapshots[window] = new WindowSnapshot(lease, initial);
                changed = true;
            }
            else if (single is not null &&
                     _snapshots.TryGetValue(window, out WindowSnapshot? old) &&
                     old.Lease == lease &&
                     single.UnidentifiedVarints.TryGetValue(5, out ulong id) &&
                     old.Professions.TryGetValue(id, out AccountProfessionProgress? prior) &&
                     single.Level >= prior.Level &&
                     (single.Level > prior.Level ||
                      single.ExperienceCounter >= prior.CumulativeExperience))
            {
                Dictionary<ulong, AccountProfessionProgress> updated =
                    new(old.Professions);
                updated[id] = prior with
                {
                    Level = single.Level,
                    CumulativeExperience = single.ExperienceCounter
                };
                _snapshots[window] = new WindowSnapshot(lease, updated);
                changed = true;
            }
        }

        if (changed)
            Changed?.Invoke();
        return changed;
    }

    public IReadOnlyList<AccountProfessionProgress>? ForWindow(
        DofusClientWindowKey? window)
    {
        if (window is not { } key)
            return null;

        // Acquiring the live lease BEFORE our private lock avoids lock-order
        // inversion with the network worker.
        NetworkCaptureLease? live = serverState.GetWindowCaptureLease(key);
        if (live is not { } lease || !serverState.IsCaptureLeaseActive(lease))
            return null;

        lock (_sync)
            return _snapshots.TryGetValue(key, out WindowSnapshot? snapshot) &&
                snapshot.Lease == lease
                    ? snapshot.Professions.Values.ToArray()
                    : null;
    }

    /// <summary>Prune even if closing the Dofus window generates no TCP traffic.</summary>
    public void RemoveMissingWindows(
        IReadOnlyCollection<DofusClientWindowKey> liveWindows)
    {
        ArgumentNullException.ThrowIfNull(liveWindows);
        HashSet<DofusClientWindowKey> live = liveWindows.ToHashSet();
        bool changed = false;
        lock (_sync)
        {
            foreach (DofusClientWindowKey key in _snapshots.Keys
                .Where(key => !live.Contains(key)).ToArray())
                changed |= _snapshots.Remove(key);
        }
        if (changed)
            Changed?.Invoke();
    }
}
