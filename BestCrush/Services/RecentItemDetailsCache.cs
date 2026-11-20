using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// Bounds UID-to-equipment correlations kept for future crush results.
/// Recently updated or used UIDs are retained first.
/// The cache belongs to a single, serial message-processing worker.
/// </summary>
internal sealed class RecentItemDetailsCache
{
    private readonly int _capacity;
    private readonly Dictionary<ulong, ItemDetailObservation> _items = [];
    private readonly Dictionary<ulong,(ItemDetailObservation Item,DateTime At)> _removed = [];
    private static readonly TimeSpan CrushRemovalWindow=TimeSpan.FromSeconds(15);
    private readonly LinkedList<ulong> _recency = [];
    private readonly Dictionary<ulong, LinkedListNode<ulong>> _nodes = [];

    internal RecentItemDetailsCache(int capacity = 16384)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    internal int Count => _items.Count;

    internal IReadOnlyDictionary<ulong, ItemDetailObservation> Items => _items;

    internal void Clear()
    {
        _items.Clear();
        _removed.Clear();
        _recency.Clear();
        _nodes.Clear();
    }

    internal void Remember(ItemDetailObservation item)
    {
        ulong uid = item.ItemUid;
        _items[uid] = item;

        if (_nodes.TryGetValue(uid, out LinkedListNode<ulong>? node))
        {
            _recency.Remove(node);
            _recency.AddLast(node);
            return;
        }

        _nodes.Add(uid, _recency.AddLast(uid));

        if (_items.Count > _capacity)
        {
            ulong oldest = _recency.First!.Value;
            _recency.RemoveFirst();
            _nodes.Remove(oldest);
            _items.Remove(oldest);
        }
    }

    internal void RememberRemoval(ulong uid,DateTime at)
    {
        foreach(var stale in _removed.Where(p=>at-p.Value.At>CrushRemovalWindow).Select(p=>p.Key).ToArray())_removed.Remove(stale);
        if(!_items.TryGetValue(uid,out var item))return;
        _removed[uid]=(item,at);
        if(_removed.Count>_capacity)_removed.Remove(_removed.MinBy(p=>p.Value.At).Key);
    }
    internal IReadOnlyDictionary<ulong,ItemDetailObservation> TakeForCrush(IEnumerable<ulong> uids,DateTime at)
    {
        Dictionary<ulong,ItemDetailObservation> result=[];
        foreach(var uid in uids.Distinct())
        {
            // A yielded rune can reuse the destroyed item's UID before kag arrives.
            if(_removed.Remove(uid,out var removed) && at>=removed.At && at-removed.At<=CrushRemovalWindow)
                result[uid]=removed.Item;
            else if(_items.TryGetValue(uid,out var current))result[uid]=current;
        }
        return result;
    }

    internal bool TryGetValue(ulong uid, out ItemDetailObservation? item)
    {
        if (!_items.TryGetValue(uid, out item))
            return false;

        LinkedListNode<ulong> node = _nodes[uid];
        _recency.Remove(node);
        _recency.AddLast(node);
        return true;
    }
}
