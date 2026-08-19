using Microsoft.UI.Xaml.Media.Imaging;

namespace ClipboardManager.App;

/// <summary>
/// 缩略图 LRU 缓存：限制图片缓存内存无上限增长（规范 §37）。
/// </summary>
public sealed class LruImageCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Key, BitmapImage Image)>> _map;
    private readonly LinkedList<(string Key, BitmapImage Image)> _order;
    private readonly object _gate = new();

    public LruImageCache(int capacity = 256)
    {
        _capacity = Math.Max(16, capacity);
        _map = new Dictionary<string, LinkedListNode<(string, BitmapImage)>>(StringComparer.OrdinalIgnoreCase);
        _order = new LinkedList<(string, BitmapImage)>();
    }

    public BitmapImage? Get(string key)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                return node.Value.Image;
            }

            return null;
        }
    }

    public void Put(string key, BitmapImage image)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                existing.Value = (key, image);
                _order.AddFirst(existing);
                return;
            }

            _map[key] = _order.AddFirst((key, image));

            while (_order.Count > _capacity)
            {
                var last = _order.Last;
                _order.RemoveLast();
                if (last is not null)
                {
                    _map.Remove(last.Value.Key);
                }
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }
}
