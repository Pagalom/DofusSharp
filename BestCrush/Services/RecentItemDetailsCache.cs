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
