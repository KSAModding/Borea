using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Borea.App.Views;

/// <summary>
/// Lays out square tiles in rows that fill the width. A row holds as many tiles
/// as fit at <see cref="MinTileSize"/>, and the tiles grow until the row ends at
/// the right edge. The tile edges sit on device pixels, so layout rounding cannot
/// push the last tile of a row past that edge.
/// </summary>
public sealed class TileRowPanel : Panel
{
    public static readonly StyledProperty<double> MinTileSizeProperty =
        AvaloniaProperty.Register<TileRowPanel, double>(nameof(MinTileSize), 171);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TileRowPanel, double>(nameof(Spacing), 5);

    public static readonly StyledProperty<int> MaxRowsProperty =
        AvaloniaProperty.Register<TileRowPanel, int>(nameof(MaxRows));

    /// <summary>The tiles this panel hid because they did not fit in <see cref="MaxRows"/>.</summary>
    private readonly HashSet<Control> _cut = [];

    static TileRowPanel()
    {
        AffectsMeasure<TileRowPanel>(MinTileSizeProperty, SpacingProperty, MaxRowsProperty);
    }

    public double MinTileSize
    {
        get => GetValue(MinTileSizeProperty);
        set => SetValue(MinTileSizeProperty, value);
    }

    /// <summary>The space between two tiles, across and down.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The most rows the panel shows, or 0 for no limit. With a limit, a row shows only when it is full, unless it is the only row.</summary>
    public int MaxRows
    {
        get => GetValue(MaxRowsProperty);
        set => SetValue(MaxRowsProperty, value);
    }

    /// <summary>How many tiles a row that is <paramref name="width"/> wide holds, and the size of each.</summary>
    internal static (int Columns, double Size) Fit(double width, double minTileSize, double spacing)
    {
        if (minTileSize + spacing <= 0 || width <= minTileSize)
            return (1, Math.Max(0, width));

        var columns = (int)((width + spacing) / (minTileSize + spacing));
        return (columns, (width - spacing * (columns - 1)) / columns);
    }

    /// <summary>How many of <paramref name="count"/> tiles show in rows of <paramref name="columns"/>.</summary>
    internal static int Shown(int count, int columns, int maxRows)
    {
        if (maxRows <= 0 || count <= columns)
            return count;

        var shown = Math.Min(count, columns * maxRows);
        return shown - shown % columns;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var tiles = Children.Where(child => child.IsVisible || _cut.Contains(child)).ToList();
        if (tiles.Count == 0)
            return default;

        var width = double.IsInfinity(availableSize.Width)
            ? tiles.Count * (MinTileSize + Spacing) - Spacing
            : availableSize.Width;
        var (columns, size) = Fit(width, MinTileSize, Spacing);
        var shown = Shown(tiles.Count, columns, MaxRows);
        for (var i = 0; i < tiles.Count; i++)
            Cut(tiles[i], i >= shown);

        foreach (var tile in tiles.Take(shown))
            tile.Measure(new Size(size, size));

        var rows = (shown + columns - 1) / columns;
        return new Size(width, rows * size + (rows - 1) * Spacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tiles = VisibleTiles();
        var (columns, size) = Fit(finalSize.Width, MinTileSize, Spacing);
        var scale = LayoutHelper.GetLayoutScale(this);
        double Snap(double value) => UseLayoutRounding ? Math.Round(value * scale) / scale : value;

        for (var i = 0; i < tiles.Count; i++)
        {
            var left = i % columns * (size + Spacing);
            var top = i / columns * (size + Spacing);
            var (x, y) = (Snap(left), Snap(top));
            tiles[i].Arrange(new Rect(x, y, Snap(left + size) - x, Snap(top + size) - y));
        }

        return finalSize;
    }

    protected override void ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        base.ChildrenChanged(sender, e);
        foreach (var removed in e.OldItems?.OfType<Control>() ?? [])
            Cut(removed, false);
    }

    /// <summary>
    /// Hides a tile past the last row instead of leaving it at no size, so it
    /// cannot take the focus. The current value keeps a binding of the tile.
    /// </summary>
    private void Cut(Control tile, bool cut)
    {
        if (cut ? _cut.Add(tile) : _cut.Remove(tile))
            tile.SetCurrentValue(IsVisibleProperty, !cut);
    }

    private List<Control> VisibleTiles() => Children.Where(child => child.IsVisible).ToList();
}
