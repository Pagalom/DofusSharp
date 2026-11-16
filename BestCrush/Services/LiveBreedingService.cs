using BestCrush.Network.Protocol;

namespace BestCrush.Services;

internal sealed record LivePaddockReading(PaddockReading Reading, DateTime ObservedAtUtc,
    long CaptureEpoch, long ConnectionId, int? Enclosure = null)
{
    internal bool IsLive(DateTime nowUtc) => nowUtc - ObservedAtUtc <= TimeSpan.FromSeconds(30);
}

internal sealed record LiveStableMount(Guid Id, PaddockMountReading? Reading);
internal sealed record LiveBreedingView(IReadOnlyList<LivePaddockReading> Enclosures,
    IReadOnlyList<LiveStableMount> Stable, bool StableComplete,
    DateTime? StableObservedAtUtc, IReadOnlyDictionary<Guid, int> MountNumbers, int? CurrentEnclosure);

internal sealed class LiveBreedingService(LiveDofusClientNetworkBindings bindings)
{
    private static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private readonly Dictionary<DofusClientWindowKey, Session> _byWindow = [];

    private sealed class Session(long epoch, long connection)
    {
        internal readonly long Epoch = epoch;
        internal readonly long Connection = connection;
        // Zero is reserved for a periodic snapshot received before a numbered selection.
        internal readonly Dictionary<int, LivePaddockReading> Enclosures = [];
        internal readonly Dictionary<Guid, LiveStableMount> Stable = [];
        internal readonly Dictionary<Guid, int> MountNumbers = [];
        internal DateTime? StableObservedAtUtc;
        internal bool StableComplete;
        internal int? CurrentEnclosure;
        internal int? PendingEnclosure;
        internal DateTime RequestedAtUtc;
        internal DateTime LastMessageAtUtc;

        internal void Track(Guid id) => MountNumbers.TryAdd(id, MountNumbers.Count + 1);
    }

    internal void Observe(ProtocolMap? map, DofusWireMessage message)
    {
        if (map is null || message.CandidateWindow is not { } window ||
            !bindings.IsCurrentConnection(message.CaptureEpoch, message.ConnectionId, window))
            return;

        bool request = message.Key == map.BreedingEnclosureRequest;
        bool response = message.Key == map.BreedingEnclosureResponse;
        bool periodic = message.Key == map.CurrentBreedingPaddockSnapshot;
        bool stable = message.Key == map.BreedingStableSnapshot;
        bool into = message.Key == map.BreedingTransferIntoStable;
        bool outOf = message.Key == map.BreedingTransferOutOfStable;
        if (request ? message.Direction != "C→S" :
            message.Direction != "S→C" || !(response || periodic || stable || into || outOf))
            return;

        byte[] body = map.NormalizeBody(message.Key, message.Body);
        lock (_sync)
        {
            if (!bindings.IsCurrentConnection(message.CaptureEpoch, message.ConnectionId, window))
                return;
            if (!_byWindow.TryGetValue(window, out var session) ||
                session.Epoch != message.CaptureEpoch || session.Connection != message.ConnectionId)
            {
                foreach (var old in _byWindow.Where(pair => !bindings.IsCurrentConnection(
                             pair.Value.Epoch, pair.Value.Connection, pair.Key)).Select(pair => pair.Key).ToArray())
                    _byWindow.Remove(old);
                _byWindow[window] = session = new(message.CaptureEpoch, message.ConnectionId);
            }
            DateTime at = message.ObservedAtUtc;
            if (at < session.LastMessageAtUtc)
                return;
            session.LastMessageAtUtc = at;

            bool awaiting = session.PendingEnclosure.HasValue && at - session.RequestedAtUtc <= SelectionTimeout;
            if (request)
            {
                var number = BreedingPaddockDecoder.DecodeEnclosureRequest(body);
                if (number is not null)
                {
                    session.PendingEnclosure = awaiting && session.PendingEnclosure != number ? 0 : number;
                    session.RequestedAtUtc = at;
                    session.CurrentEnclosure = null;
                }
                return;
            }

            if (response)
            {
                if (awaiting && session.PendingEnclosure == 0)
                    return;
                if (BreedingPaddockDecoder.DecodeSelectedEnclosure(body) is { } selected)
                {
                    if (awaiting && session.PendingEnclosure > 0)
                    {
                        session.CurrentEnclosure = session.PendingEnclosure;
                        session.Enclosures.Remove(0);
                    }
                    StoreEnclosure(session, selected, at);
                }
                // Two different selections in flight cannot identify either response.
                if (session.PendingEnclosure != 0 || !awaiting)
                    session.PendingEnclosure = null;
                return;
            }

            if (periodic)
            {
                if (awaiting)
                    return;
                if (BreedingPaddockDecoder.Decode(body) is { } paddock)
                    StoreEnclosure(session, paddock, at);
                else
                    session.Enclosures.Remove(session.CurrentEnclosure ?? 0);
                return;
            }

            if (stable)
            {
                if (BreedingPaddockDecoder.DecodeStable(body) is not { } mounts)
                    return;
                session.Stable.Clear();
                foreach (var mount in mounts)
                {
                    session.Track(mount.Id);
                    session.Stable.Add(mount.Id, new(mount.Id, mount));
                    RemoveFromEnclosures(session, mount.Id);
                }
                session.StableComplete = true;
                session.StableObservedAtUtc = at;
                return;
            }

            if (BreedingPaddockDecoder.DecodeStableTransfer(body, into) is not { Count: > 0 } ids)
                return;
            foreach (Guid id in ids)
            {
                if (into)
                {
                    session.Track(id);
                    var reading = RemoveFromEnclosures(session, id);
                    if (reading is not null || !session.Stable.ContainsKey(id))
                        session.Stable[id] = new(id, reading);
                }
                else
                    session.Stable.Remove(id);
            }
            session.StableObservedAtUtc = at;
        }
    }

    private static void StoreEnclosure(Session session, PaddockReading reading, DateTime at)
    {
        foreach (var mount in reading.Mounts)
        {
            session.Track(mount.Id);
            session.Stable.Remove(mount.Id);
            RemoveFromEnclosures(session, mount.Id, session.CurrentEnclosure ?? 0);
        }
        session.Enclosures[session.CurrentEnclosure ?? 0] = new(reading, at,
            session.Epoch, session.Connection, session.CurrentEnclosure);
    }

    private static PaddockMountReading? RemoveFromEnclosures(Session session, Guid id, int? except = null)
    {
        PaddockMountReading? previous = null;
        foreach (var (number, snapshot) in session.Enclosures.ToArray())
        {
            if (number == except || snapshot.Reading.Mounts.FirstOrDefault(mount => mount.Id == id) is not { } match)
                continue;
            previous ??= match;
            session.Enclosures[number] = snapshot with
            {
                Reading = snapshot.Reading with
                {
                    Mounts = snapshot.Reading.Mounts.Where(mount => mount.Id != id).ToArray()
                }
            };
        }
        return previous;
    }

    internal LivePaddockReading? ForWindow(DofusClientWindowKey? window, DateTime nowUtc)
    {
        lock (_sync)
        {
            var session = CurrentSession(window);
            var snapshot = session?.Enclosures.GetValueOrDefault(session.CurrentEnclosure ?? 0);
            return snapshot?.IsLive(nowUtc) == true ? snapshot : null;
        }
    }

    internal LiveBreedingView? ViewForWindow(DofusClientWindowKey? window)
    {
        lock (_sync)
        {
            var session = CurrentSession(window);
            return session is null ? null : new(
                session.Enclosures.Values.OrderBy(value => value.Enclosure).ToArray(),
                session.Stable.Values.OrderBy(mount => session.MountNumbers[mount.Id]).ToArray(),
                session.StableComplete, session.StableObservedAtUtc, new Dictionary<Guid, int>(session.MountNumbers),
                session.CurrentEnclosure);
        }
    }

    private Session? CurrentSession(DofusClientWindowKey? window) =>
        window is { } key && _byWindow.TryGetValue(key, out var session) &&
        bindings.IsCurrentConnection(session.Epoch, session.Connection, key) ? session : null;
}
