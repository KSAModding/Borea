using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Borea.Core.Screenshots;

namespace Borea.App.Views;

/// <summary>
/// Holds the decoded thumbnails of screenshots, the most recent ones up to a size, so a tile that scrolls back shows its picture at once.
/// A thumbnail is found by the path, time and size of its screenshot, so a file that the game wrote again decodes again.
/// A thumbnail that a tile shows stays alive above the size, and the cache disposes it only after the last tile gave it back.
/// Every call comes on the UI thread.
/// </summary>
internal sealed class ScreenshotThumbnailCache
{
    private readonly long _capacity;
    private readonly Dictionary<Screenshot, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _order = new();

    /// <param name="capacity">The most bytes of decoded pixels that the cache holds while no tile shows them.</param>
    internal ScreenshotThumbnailCache(long capacity)
    {
        _capacity = capacity;
    }

    internal long Size { get; private set; }

    internal int Count => _order.Count;

    /// <summary>The thumbnail of <paramref name="screenshot"/> for a tile, which gives it back with <see cref="Release"/>, or null when the cache has none.</summary>
    internal Bitmap? Acquire(Screenshot screenshot)
    {
        if (!_entries.TryGetValue(screenshot, out var node))
            return null;

        node.Value.Users++;
        _order.Remove(node);
        _order.AddFirst(node);
        return node.Value.Bitmap;
    }

    /// <summary>Takes over <paramref name="bitmap"/> and hands it to the tile as <see cref="Acquire"/> does. A thumbnail that the cache has already wins, and the cache disposes the new one.</summary>
    internal Bitmap Add(Screenshot screenshot, Bitmap bitmap)
    {
        if (Acquire(screenshot) is { } held)
        {
            bitmap.Dispose();
            return held;
        }

        var entry = new Entry(screenshot, bitmap, 4L * bitmap.PixelSize.Width * bitmap.PixelSize.Height) { Users = 1 };
        _entries.Add(screenshot, _order.AddFirst(entry));
        Size += entry.Size;
        Trim();
        return bitmap;
    }

    internal void Release(Screenshot screenshot)
    {
        if (!_entries.TryGetValue(screenshot, out var node))
            return;

        node.Value.Users--;
        Trim();
    }

    /// <summary>Disposes the least recent thumbnails that no tile shows until the cache fits its size.</summary>
    private void Trim()
    {
        for (var node = _order.Last; node is not null && Size > _capacity;)
        {
            var newer = node.Previous;
            if (node.Value.Users == 0)
            {
                _order.Remove(node);
                _entries.Remove(node.Value.Screenshot);
                Size -= node.Value.Size;
                node.Value.Bitmap.Dispose();
            }

            node = newer;
        }
    }

    private sealed class Entry(Screenshot screenshot, Bitmap bitmap, long size)
    {
        internal Screenshot Screenshot => screenshot;

        internal Bitmap Bitmap => bitmap;

        internal long Size => size;

        /// <summary>How many tiles show the thumbnail now.</summary>
        internal int Users { get; set; }
    }
}
