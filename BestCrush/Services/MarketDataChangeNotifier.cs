using BestCrush.Domain.Models;

namespace BestCrush.Services;

public sealed record MarketDataChangedEventArgs(
    MarketObjectType ObjectType,
    long DofusDbId,
    string ServerName,
    int Quantity
);

public sealed class MarketDataChangeNotifier
{
    private readonly object _sync = new();
    private long _revision;
    private readonly Dictionary<string, Dictionary<(MarketObjectType Type, long Id), (long Revision, MarketDataChangedEventArgs Change)>> _latest = new(StringComparer.OrdinalIgnoreCase);
    public long Revision { get { lock (_sync) return _revision; } }
    public (long Revision, IReadOnlyList<MarketDataChangedEventArgs> Changes) ChangesSince(string serverName, long revision)
    {
        lock (_sync)
            return (_revision, _latest.TryGetValue(serverName, out var latest)
                ? latest.Values.Where(x => x.Revision > revision).Select(x => x.Change).ToArray() : []);
    }

    public event EventHandler<MarketDataChangedEventArgs>?
        Changed;

    public void Notify(
        MarketObjectType objectType,
        long dofusDbId,
        string serverName,
        int quantity = 0)
    {
        var change = new MarketDataChangedEventArgs(objectType, dofusDbId, serverName, quantity);
        lock (_sync)
        {
            if (!_latest.TryGetValue(serverName, out var latest)) _latest[serverName] = latest = [];
            latest[(objectType, dofusDbId)] = (++_revision, change);
        }
        Changed?.Invoke(this, change);
    }
}
