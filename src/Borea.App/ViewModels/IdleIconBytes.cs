using System.Collections.Generic;

namespace Borea.App.ViewModels;

/// <summary>
/// The loaded icons that no view shows, the most recent ones up to a size of their bytes.
/// An older icon gives its bytes back, and loads them again from the image cache when a view shows it.
/// </summary>
internal sealed class IdleIconBytes
{
    private readonly long _capacity;
    private readonly Dictionary<ListingImage, LinkedListNode<Entry>> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Entry> _order = new();

    /// <param name="capacity">The most bytes the idle icons keep.</param>
    internal IdleIconBytes(long capacity)
    {
        _capacity = capacity;
    }

    internal long Size { get; private set; }

    internal int Count => _order.Count;

    internal void Put(ListingImage icon, long size)
    {
        Remove(icon);
        var entry = new Entry(icon, size);
        _entries.Add(icon, _order.AddFirst(entry));
        Size += size;
        while (Size > _capacity)
        {
            var oldest = _order.Last!.Value.Icon;
            Remove(oldest);
            oldest.Bytes = null;
        }
    }

    internal void Remove(ListingImage icon)
    {
        if (!_entries.Remove(icon, out var node))
            return;

        _order.Remove(node);
        Size -= node.Value.Size;
    }

    /// <summary>Gives the bytes of every idle icon back.</summary>
    internal void Clear()
    {
        while (_order.Last is { } oldest)
        {
            Remove(oldest.Value.Icon);
            oldest.Value.Icon.Bytes = null;
        }
    }

    private readonly record struct Entry(ListingImage Icon, long Size);
}
