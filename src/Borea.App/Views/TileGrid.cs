using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;

namespace Borea.App.Views;

/// <summary>
/// Lays out tiles in rows of as many as fit at <see cref="MinTileWidth"/>, and shows the rows in a virtualizing list, because Avalonia has
/// no virtualizing wrap panel and the screenshots of a library can be thousands. Only the rows near the window exist, and a row that the
/// list recycles keeps the tiles that <see cref="ItemTemplate"/> built, so only their data moves, as in <see cref="RowList"/>.
/// </summary>
public sealed class TileGrid : Decorator
{
    public static readonly StyledProperty<IReadOnlyList<object>?> ItemsProperty =
        AvaloniaProperty.Register<TileGrid, IReadOnlyList<object>?>(nameof(Items));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<TileGrid, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<double> MinTileWidthProperty =
        AvaloniaProperty.Register<TileGrid, double>(nameof(MinTileWidth), 200);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TileGrid, double>(nameof(Spacing), 16);

    private readonly RowList _rows = new()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = 2 }),
    };

    private double _width;
    private int _columns;

    public TileGrid()
    {
        Child = _rows;
    }

    public IReadOnlyList<object>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public double MinTileWidth
    {
        get => GetValue(MinTileWidthProperty);
        set => SetValue(MinTileWidthProperty, value);
    }

    /// <summary>The space between two tiles, across and down.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
            ShowRows();
        else if (change.Property == ItemTemplateProperty || change.Property == MinTileWidthProperty || change.Property == SpacingProperty)
            Arrange(_width, force: true);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Arrange(e.NewSize.Width, force: false);
    }

    /// <summary>Builds the rows again when the number of columns changes, which a new width or new settings can cause.</summary>
    private void Arrange(double width, bool force)
    {
        _width = width;
        var columns = width > 0 ? TileRowPanel.Fit(width, MinTileWidth, Spacing).Columns : 0;
        if (columns == _columns && !force)
            return;

        _columns = columns;
        var spacing = Spacing;
        _rows.ItemTemplate = ItemTemplate is { } template && columns > 0
            ? new FuncDataTemplate<TileGridRow>((_, _) => new Row(columns, spacing, template))
            : null;
        ShowRows();
    }

    private void ShowRows()
    {
        _rows.ItemsSource = Items is { Count: > 0 } items && _columns > 0 && _rows.ItemTemplate is not null
            ? items.Chunk(_columns).Select(tiles => new TileGridRow(tiles)).ToList()
            : null;
    }

    /// <summary>One row of the list, which is a class and not a record, so two rows of the same tiles stay two items.</summary>
    internal sealed class TileGridRow(IReadOnlyList<object> tiles)
    {
        internal IReadOnlyList<object> Tiles => tiles;
    }

    private sealed class Row : UniformGrid
    {
        private readonly IDataTemplate _template;

        internal Row(int columns, double spacing, IDataTemplate template)
        {
            _template = template;
            Rows = 1;
            Columns = columns;
            ColumnSpacing = spacing;

            // a virtualizing panel has no spacing, so each row keeps the gap below itself
            Margin = new Thickness(0, 0, 0, spacing);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            var tiles = (DataContext as TileGridRow)?.Tiles ?? [];
            for (var index = 0; index < Columns; index++)
            {
                var tile = index < tiles.Count ? tiles[index] : null;
                if (index == Children.Count)
                {
                    // the tiles of a row come first, so a row with an empty place has no more tiles after it
                    if (tile is null)
                        return;

                    var built = _template.Build(tile)!;
                    built.DataContext = tile;
                    Children.Add(built);
                    continue;
                }

                Children[index].DataContext = tile;
                Children[index].IsVisible = tile is not null;
            }
        }
    }
}
