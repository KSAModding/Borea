using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace Borea.App.Views;

/// <summary>Owns the decoded bitmaps of images that left the screen, the most recent ones up to a size, so a view that shows the same bytes again draws them at once.</summary>
internal sealed class BitmapShelf
{
    private readonly long _capacity;
    private readonly Dictionary<byte[], LinkedListNode<Entry>> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Entry> _order = new();

    /// <param name="capacity">The most bytes of decoded pixels the shelf holds.</param>
    internal BitmapShelf(long capacity)
    {
        _capacity = capacity;
    }

    internal long Size { get; private set; }

    internal int Count => _order.Count;

    /// <summary>Takes over <paramref name="bitmap"/>. Of two bitmaps of the same bytes the wider one stays.</summary>
    internal void Put(byte[] bytes, Bitmap bitmap)
    {
        if (Take(bytes) is { } shelved)
        {
            if (shelved.PixelSize.Width > bitmap.PixelSize.Width)
                (shelved, bitmap) = (bitmap, shelved);
            shelved.Dispose();
        }

        var entry = new Entry(bytes, bitmap, 4L * bitmap.PixelSize.Width * bitmap.PixelSize.Height);
        _entries.Add(bytes, _order.AddFirst(entry));
        Size += entry.Size;
        while (Size > _capacity)
            Remove(_order.Last!).Bitmap.Dispose();
    }

    /// <summary>Hands the bitmap of <paramref name="bytes"/> to the caller, who then disposes it.</summary>
    internal Bitmap? Take(byte[] bytes) => _entries.TryGetValue(bytes, out var node) ? Remove(node).Bitmap : null;

    private Entry Remove(LinkedListNode<Entry> node)
    {
        _order.Remove(node);
        _entries.Remove(node.Value.Bytes);
        Size -= node.Value.Size;
        return node.Value;
    }

    private readonly record struct Entry(byte[] Bytes, Bitmap Bitmap, long Size);
}
