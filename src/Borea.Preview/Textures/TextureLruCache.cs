using System.Diagnostics.CodeAnalysis;
using Borea.Preview.Scene;

namespace Borea.Preview.Textures;

/// <summary>
/// Decoded texture levels that the least recently used order evicts when their pixel bytes go above a cap. An image
/// that is larger than the whole cap is not kept. All members are safe to call from several threads.
/// </summary>
internal sealed class TextureLruCache
{
    private readonly long _capacityBytes;
    private readonly LinkedList<(string Key, PreviewImage Image)> _order = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, PreviewImage Image)>> _entries = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private long _bytes;

    public TextureLruCache(long capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityBytes);
        _capacityBytes = capacityBytes;
    }

    public long Bytes
    {
        get
        {
            lock (_lock)
            {
                return _bytes;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    public bool TryGet(string key, [NotNullWhen(true)] out PreviewImage? image)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                image = null;
                return false;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            image = node.Value.Image;
            return true;
        }
    }

    public void Add(string key, PreviewImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        lock (_lock)
        {
            if (_entries.Remove(key, out var old))
            {
                _order.Remove(old);
                _bytes -= old.Value.Image.Rgba.Length;
            }

            if (image.Rgba.Length > _capacityBytes)
            {
                return;
            }

            _entries[key] = _order.AddFirst((key, image));
            _bytes += image.Rgba.Length;
            while (_bytes > _capacityBytes && _order.Last is { } last)
            {
                _order.RemoveLast();
                _entries.Remove(last.Value.Key);
                _bytes -= last.Value.Image.Rgba.Length;
            }
        }
    }
}
