using System.Diagnostics.CodeAnalysis;

namespace Borea.Storage.Preview.Sources;

/// <summary>
/// The decompressed bytes of archive entries, kept while a preview uses them so that the mesh name pass and the scene
/// build decompress an entry only once. It evicts the least recently used entries when the total goes above the
/// capacity. Keys come from <see cref="IPreviewFiles.Describe"/>, so two archives with the same bytes share entries.
/// All members are thread-safe.
/// </summary>
internal sealed class PreviewEntryCache
{
    public const long DefaultCapacityBytes = 256L * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();
    private long _totalBytes;

    public PreviewEntryCache(long capacityBytes = DefaultCapacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityBytes);
        CapacityBytes = capacityBytes;
    }

    public long CapacityBytes { get; }

    public long TotalBytes
    {
        get
        {
            lock (_gate)
                return _totalBytes;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    public bool TryGet(string key, [NotNullWhen(true)] out byte[]? bytes)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                bytes = node.Value.Bytes;
                return true;
            }
        }

        bytes = null;
        return false;
    }

    /// <summary>
    /// Keeps the bytes under the key and evicts older entries until the total fits. Bytes larger than the whole
    /// capacity are not kept, so the caller still has them but a second read decompresses again.
    /// </summary>
    public void Add(string key, byte[] bytes)
    {
        lock (_gate)
        {
            if (_entries.Remove(key, out var existing))
            {
                _order.Remove(existing);
                _totalBytes -= existing.Value.Bytes.Length;
            }

            if (bytes.Length > CapacityBytes)
                return;

            while (_totalBytes + bytes.Length > CapacityBytes && _order.Last is { } oldest)
            {
                _order.RemoveLast();
                _entries.Remove(oldest.Value.Key);
                _totalBytes -= oldest.Value.Bytes.Length;
            }

            _entries[key] = _order.AddFirst(new Entry(key, bytes));
            _totalBytes += bytes.Length;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
            _totalBytes = 0;
        }
    }

    private sealed record Entry(string Key, byte[] Bytes);
}
