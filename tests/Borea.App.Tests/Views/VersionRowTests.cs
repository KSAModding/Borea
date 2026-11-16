using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views.Pages;
using Borea.Core.Game;
using Borea.Core.Mods;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class VersionRowTests
{
    private sealed record Row(string Version, double? FirstButtonX, double? AddButtonX);

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task ModPage_InstalledVersionHasNoAddButtonAndTheChangelogButtonsStayInOneColumn(double width)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenVersionsAsync(harness);

        var rows = await RenderAsync(harness, () => new Borea.App.Views.Pages.ContentPage(), width, page => ContentRows(viewModel, page));

        AssertAligned(rows, installed: "0.7.3");
    }

    private sealed record DownloadsColumn(List<(double X, string? Text)> Cells, int SizeCells, bool ButtonsInsideRows, bool HeadersApart);

    /// <summary>
    /// The page body is 488 at 860 (small), 728 at 1100 (just wider than narrow), 748 at 1120 (the default window of the app,
    /// which also has the navigation rail), 828 at 1280 and the widest at 1920.
    /// </summary>
    [Theory]
    [InlineData(860, false, false)]
    [InlineData(1100, true, false)]
    [InlineData(1120, true, false)]
    [InlineData(1280, true, true)]
    [InlineData(1920, true, true)]
    public async Task ModPage_DownloadsColumnLinesUpWithItsHeader_AndTakesThePlaceOfSizeOnAMediumPage(double width, bool showsDownloads, bool showsSize)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ContentViewModelTests.WithReleaseDownloads);
        var viewModel = harness.ViewModel;
        await OpenVersionsAsync(harness);

        var (rows, column) = await RenderAsync(harness, () => new Borea.App.Views.Pages.ContentPage(), width, rendered => (
            ContentRows(viewModel, rendered),
            ReadDownloadsColumn(rendered)));

        AssertAligned(rows, installed: "0.7.3");
        Assert.True(column.ButtonsInsideRows);
        Assert.True(column.HeadersApart);
        Assert.Equal(showsSize ? viewModel.ContentVersions.Count + 1 : 0, column.SizeCells);
        if (!showsDownloads)
        {
            Assert.Empty(column.Cells);
            return;
        }

        // the header and one cell for each row, each at the same left edge; a release without a count keeps its empty cell
        Assert.Equal(viewModel.ContentVersions.Count + 1, column.Cells.Count);
        Assert.Single(column.Cells.Select(cell => cell.X).Distinct());
        Assert.Equal([harness.Localization.ContentDownloadsHeader, "58", MainViewModel.CompactCount(1503), null, null], column.Cells.Select(cell => cell.Text));
    }

    /// <summary>Size keeps its place on a medium page (748 at 1120) when no Downloads column needs it.</summary>
    [Theory]
    [InlineData(1120)]
    [InlineData(1920)]
    public async Task ModPage_ListingWithoutCounts_HasNoDownloadsColumn_AndKeepsSize(double width)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenVersionsAsync(harness);

        var (rows, column) = await RenderAsync(harness, () => new Borea.App.Views.Pages.ContentPage(), width, rendered => (
            ContentRows(viewModel, rendered),
            ReadDownloadsColumn(rendered)));

        Assert.False(viewModel.HasVersionDownloads);
        AssertAligned(rows, installed: "0.7.3");
        Assert.Empty(column.Cells);
        Assert.Equal(viewModel.ContentVersions.Count + 1, column.SizeCells);
    }

    /// <summary>
    /// The Markdown view of a long changelog holds about a thousand controls, so a row builds it only while the row is open.
    /// </summary>
    [Fact]
    public async Task ModPage_ChangelogView_IsBuiltOnlyWhileTheRowIsOpen()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var changelog = string.Join("\n\n", Enumerable.Range(1, 40).Select(line => $"- Fixed the **item {line}** of the list."));
        harness.SpaceDock.Releases.AddRange(Enumerable.Range(1, 20).Select(minor => new ModVersionMetadata(
            specVersion: 1,
            modId: ViewModelHarness.FakeSpaceDock.OwnId,
            version: ModVersion.Parse($"1.{minor}.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UnixEpoch,
            gameMin: "2026.1.1.1",
            gameMinRevision: 1,
            download: new DownloadInfo($"https://archives.test/1.{minor}.0.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
            installSizeBytes: null,
            dependencies: [],
            changelog: null,
            changelogText: changelog)));
        var listing = await harness.SpaceDock.GetListingAsync(ViewModelHarness.FakeSpaceDock.OwnId);
        await viewModel.OpenContentCommand.ExecuteAsync(new DiscoverItem(viewModel, listing!));
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        var (closed, opened) = await RenderAsync(harness, () => new Borea.App.Views.Pages.ContentPage(), 1280, page =>
        {
            var closedViews = page.GetVisualDescendants().OfType<Borea.App.Views.MarkdownView>().Count(view => view.Markdown == changelog);
            viewModel.ContentVersions[0].ToggleChangelogCommand.Execute(null);
            page.UpdateLayout();
            var openedViews = page.GetVisualDescendants().OfType<Borea.App.Views.MarkdownView>().Where(view => view.Markdown == changelog).ToList();
            return (closedViews, openedViews.Count);
        });

        Assert.Equal(20, viewModel.ContentVersions.Count);
        Assert.Equal(0, closed);
        Assert.Equal(1, opened);
    }

    private static List<Row> ContentRows(MainViewModel viewModel, Control page)
        => viewModel.ContentVersions.Select(version => new Row(version.Version, X(page, version.ToggleChangelogCommand), X(page, version.InstallCommand))).ToList();

    private static async Task OpenVersionsAsync(ViewModelHarness harness)
    {
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, version: "0.7.3");
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// The visible cells of the downloads column, header first, the count of visible size cells, whether every Add button of a row
    /// ends inside its row, and whether each header text fits its cell and ends before the next header starts.
    /// </summary>
    private static DownloadsColumn ReadDownloadsColumn(Control page)
    {
        var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToList();
        var cells = texts
            .Where(text => text.Classes.Contains("downloads"))
            .Select(text => (text.TranslatePoint(default, page)!.Value.X, string.IsNullOrEmpty(text.Text) ? null : text.Text))
            .ToList();
        var sizeCells = texts.Count(text => text.Classes.Contains("hide-medium"));
        var rowGrids = page.GetVisualDescendants().OfType<Grid>().Where(grid => grid.DataContext is VersionItem && grid.ColumnDefinitions.Count == 8).ToList();
        var inside = rowGrids.Count > 0 && rowGrids.All(grid => grid.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .All(button => button.TranslatePoint(new Point(button.Bounds.Width, 0), grid)!.Value.X <= grid.Bounds.Width + 0.5));
        var headerGrid = page.GetVisualDescendants().OfType<Grid>().Single(grid => grid.DataContext is MainViewModel && grid.ColumnDefinitions.Count == 8);
        var headers = headerGrid.Children.OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
            .Select(text => (X: text.TranslatePoint(default, page)!.Value.X, text.Bounds.Width, TextWidth: text.TextLayout.Width))
            .OrderBy(header => header.X)
            .ToList();
        var apart = headers.All(header => header.TextWidth <= header.Width + 0.5)
            && headers.Zip(headers.Skip(1)).All(pair => pair.First.X + pair.First.Width <= pair.Second.X + 0.5);
        return new DownloadsColumn(cells, sizeCells, inside, apart);
    }

    [Theory]
    [InlineData(860)]
    [InlineData(1280)]
    [InlineData(1920)]
    public async Task PackPage_InstalledVersionHasNoAddButtonAndTheNewInstanceButtonsStayInOneColumn(double width)
    {
        using var harness = await OpenPackVersionsAsync();
        var viewModel = harness.ViewModel;

        var rows = await RenderAsync(harness, () => new PackPage(), width, page => viewModel.PackVersions
            .Select(version => new Row(version.Version, X(page, version.NewInstanceCommand), X(page, version.InstallCommand)))
            .ToList());

        AssertAligned(rows, installed: "1.0.0");
    }

    /// <summary>
    /// Every control below a query container adds one handler for each query to one event of the container, and Avalonia
    /// copies the whole list each time one of them leaves. With the Versions table inside such a container, taking its
    /// rows away froze Borea for seconds when the player opened another mod. The pages read the width from
    /// <see cref="Borea.App.Views.PageBodyPanel"/> instead.
    /// </summary>
    [Fact]
    public async Task ModPage_VersionRows_AreInsideNoQueryContainer()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await OpenVersionsAsync(harness);

        var (rows, inside) = await RenderAsync(harness, () => new Borea.App.Views.Pages.ContentPage(), 1280, RowsInsideAQueryContainer<VersionItem>);

        Assert.Equal(harness.ViewModel.ContentVersions.Count, rows);
        Assert.Equal(0, inside);
    }

    [Fact]
    public async Task PackPage_VersionRows_AreInsideNoQueryContainer()
    {
        using var harness = await OpenPackVersionsAsync();

        var (rows, inside) = await RenderAsync(harness, () => new PackPage(), 1280, RowsInsideAQueryContainer<PackVersionItem>);

        Assert.Equal(harness.ViewModel.PackVersions.Count, rows);
        Assert.Equal(0, inside);
    }

    /// <summary>
    /// The rows that show a <typeparamref name="TRow"/>, and how many of them have a query container above them on the page.
    /// The window is a container of its own, which no query of Borea names.
    /// </summary>
    private static (int Rows, int Inside) RowsInsideAQueryContainer<TRow>(Control page)
    {
        var rows = page.GetVisualDescendants().OfType<ContentPresenter>().Where(presenter => presenter.Content is TRow).ToList();
        return (rows.Count, rows.Count(row => row.GetVisualAncestors().TakeWhile(ancestor => ancestor != page).Append(page).OfType<Layoutable>()
            .Any(ancestor => Container.GetSizing(ancestor) != ContainerSizing.Normal)));
    }

    private static async Task<ViewModelHarness> OpenPackVersionsAsync()
    {
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: PackViewModelTests.WithPacks(PackViewModelTests.Pack(
            "starter-pack",
            "Starter Pack",
            PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("AdvancedFlightComputer", "0.7.4")),
            PackViewModelTests.Version("1.1.0", PackViewModelTests.Pin("AdvancedFlightComputer", "0.7.5")))));
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, version: "0.7.4");
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverPacks).OpenCommand.ExecuteAsync(null);
        viewModel.ShowPackVersionsCommand.Execute(null);
        return harness;
    }

    private static void AssertAligned(List<Row> rows, string installed)
    {
        Assert.True(rows.Count > 1);
        Assert.Null(rows.Single(row => row.Version == installed).AddButtonX);
        var others = rows.Where(row => row.Version != installed).ToList();
        Assert.All(others, row => Assert.NotNull(row.AddButtonX));
        Assert.Single(others.Select(row => row.AddButtonX).Distinct());
        Assert.All(rows, row => Assert.NotNull(row.FirstButtonX));
        Assert.Single(rows.Select(row => row.FirstButtonX).Distinct());
    }

    /// <summary>The left edge of the visible button that runs <paramref name="command"/>, or null when none shows.</summary>
    private static double? X(Control page, ICommand command) => page.GetVisualDescendants().OfType<Button>()
        .Where(button => button.IsEffectivelyVisible && button.Command == command)
        .Select(button => (double?)button.TranslatePoint(default, page)!.Value.X)
        .SingleOrDefault();

    private static Task<T> RenderAsync<T>(ViewModelHarness harness, Func<Control> createPage, double width, Func<Control, T> read) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = createPage();
            var window = new Window { Width = width, Height = 800, DataContext = harness.ViewModel, Content = page };
            window.Show();
            try
            {
                page.UpdateLayout();
                return Task.FromResult(read(page));
            }
            finally
            {
                window.Close();
            }
        });
}
