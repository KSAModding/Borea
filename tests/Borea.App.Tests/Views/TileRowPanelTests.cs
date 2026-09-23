using Avalonia;
using Avalonia.Controls;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

public sealed class TileRowPanelTests
{
    [Theory]
    // the page bodies at 860, 1280 and from 1600 wide windows
    [InlineData(732, 4, 179.25)]
    [InlineData(1072, 6, 174.5)]
    [InlineData(1264, 7, 176.286)]
    // exactly two tiles of the smallest size, and a row too narrow for one
    [InlineData(347, 2, 171)]
    [InlineData(100, 1, 100)]
    public void Fit_FillsTheRowWithTilesOfAtLeastTheSmallestSize(double width, int columns, double size)
    {
        var fit = TileRowPanel.Fit(width, 171, 5);

        Assert.Equal(columns, fit.Columns);
        Assert.Equal(size, fit.Size, 3);
    }

    [Fact]
    public void Layout_EndsEachFullRowAtTheRightEdge()
    {
        var tiles = Enumerable.Range(0, 8).Select(_ => new Border()).ToList();
        var hidden = new Border { IsVisible = false };
        var panel = new TileRowPanel { MinTileSize = 171, Spacing = 5 };
        panel.Children.AddRange(tiles.Take(4));
        panel.Children.Add(hidden);
        panel.Children.AddRange(tiles.Skip(4));

        // seven tiles of 176 and six gaps of 5, so layout rounding changes nothing
        panel.Measure(new Size(1262, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));

        Assert.Equal(new Size(1262, 2 * 176 + 5), panel.DesiredSize);
        Assert.Equal(new Rect(0, 0, 176, 176), tiles[0].Bounds);
        Assert.Equal(new Rect(6 * 181, 0, 176, 176), tiles[6].Bounds);
        Assert.Equal(1262, tiles[6].Bounds.Right);
        Assert.Equal(new Rect(0, 181, 176, 176), tiles[7].Bounds);
    }

    [Fact]
    public void Layout_WithoutVisibleTiles_TakesNoRoom()
    {
        var panel = new TileRowPanel { Children = { new Border { IsVisible = false } } };

        panel.Measure(new Size(1264, double.PositiveInfinity));

        Assert.Equal(default, panel.DesiredSize);
    }

    [Theory]
    // no limit, one short row, full rows, and a short last row that goes
    [InlineData(14, 4, 0, 14)]
    [InlineData(3, 4, 2, 3)]
    [InlineData(14, 4, 2, 8)]
    [InlineData(14, 7, 2, 14)]
    [InlineData(10, 4, 2, 8)]
    [InlineData(6, 4, 2, 4)]
    public void Shown_KeepsFullRowsUpToTheLimit(int count, int columns, int maxRows, int shown)
    {
        Assert.Equal(shown, TileRowPanel.Shown(count, columns, maxRows));
    }

    [Fact]
    public void Layout_WithMaxRows_HidesTheTilesPastTheLastRowUntilTheyFit()
    {
        var tiles = Enumerable.Range(0, 14).Select(_ => new Border()).ToList();
        var panel = new TileRowPanel { MinTileSize = 171, Spacing = 5, MaxRows = 2 };
        panel.Children.AddRange(tiles);

        // four tiles of 176 and three gaps of 5, so layout rounding changes nothing
        panel.Measure(new Size(719, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
        var narrow = tiles.Count(tile => tile.IsVisible);
        var height = panel.DesiredSize.Height;

        panel.Measure(new Size(1264, double.PositiveInfinity));
        var wide = tiles.Count(tile => tile.IsVisible);

        Assert.Equal(8, narrow);
        Assert.Equal(2 * 176 + 5, height);
        Assert.Equal(14, wide);
    }

    [Fact]
    public void RemovedTile_ShowsAgain()
    {
        var tiles = Enumerable.Range(0, 5).Select(_ => new Border()).ToList();
        var panel = new TileRowPanel { MinTileSize = 171, Spacing = 5, MaxRows = 1 };
        panel.Children.AddRange(tiles);
        panel.Measure(new Size(732, double.PositiveInfinity));
        Assert.False(tiles[4].IsVisible);

        panel.Children.Remove(tiles[4]);

        Assert.True(tiles[4].IsVisible);
    }
}
