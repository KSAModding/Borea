using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Borea.Core.Index;

namespace Borea.App.Views;

/// <summary>
/// Owns the decoded bitmaps of icons that left the screen, the most recent ones up to a size, so a view that shows the same icon again draws it at once.
/// A bitmap is found by the digest, pixel size and file size of its icon record, which are the facts its bytes were verified against.
/// So an icon that gave its bytes back and an equal record that an index refresh builds again find it, and a record that states other facts for the same digest does not.
/// </summary>
internal sealed class BitmapShelf
{
    private readonly long _capacity;
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _order = new();

    /// <param name="capacity">The most bytes of decoded pixels the shelf holds.</param>
    internal BitmapShelf(long capacity)
    {
        _capacity = capacity;
    }

    internal long Size { get; private set; }

    internal int Count => _order.Count;

    /// <summary>Takes over <paramref name="bitmap"/>. Of two bitmaps of the same icon the wider one stays.</summary>
    internal void Put(IconImage icon, Bitmap bitmap)
    {
        if (Take(icon) is { } shelved)
        {
            if (shelved.PixelSize.Width > bitmap.PixelSize.Width)
                (shelved, bitmap) = (bitmap, shelved);
            shelved.Dispose();
        }

        var entry = new Entry(Key.Of(icon), bitmap, 4L * bitmap.PixelSize.Width * bitmap.PixelSize.Height);
        _entries.Add(entry.Key, _order.AddFirst(entry));
        Size += entry.Size;
        while (Size > _capacity)
            Remove(_order.Last!).Bitmap.Dispose();
    }

    /// <summary>Hands the bitmap of <paramref name="icon"/> to the caller, who then disposes it.</summary>
    internal Bitmap? Take(IconImage icon) => _entries.TryGetValue(Key.Of(icon), out var node) ? Remove(node).Bitmap : null;

    internal void Clear()
    {
        foreach (var entry in _order)
            entry.Bitmap.Dispose();

        _entries.Clear();
        _order.Clear();
        Size = 0;
    }

    private Entry Remove(LinkedListNode<Entry> node)
    {
        _order.Remove(node);
        _entries.Remove(node.Value.Key);
        Size -= node.Value.Size;
        return node.Value;
    }

    /// <summary>The record normalizes its digest to uppercase, so an ordinal comparison finds an equal record.</summary>
    private readonly record struct Key(string Sha256, int Width, int Height, long SizeBytes)
    {
        internal static Key Of(IconImage icon) => new(icon.Sha256, icon.Width, icon.Height, icon.SizeBytes);
    }

    private readonly record struct Entry(Key Key, Bitmap Bitmap, long Size);
}
