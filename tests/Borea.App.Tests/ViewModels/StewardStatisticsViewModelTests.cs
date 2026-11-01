using System.Globalization;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;

namespace Borea.App.Tests.ViewModels;

public sealed class StewardStatisticsViewModelTests
{
    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();
    private readonly FakeStewardQueue _queue = new();

    [Fact]
    public async Task StatisticsTab_ShowsTheReport_WithoutARequest()
    {
        using var harness = await CreateAsync();
        var requests = harness.IndexDocumentRequests;

        var tab = await OpenAsync(harness.ViewModel);

        Assert.True(harness.ViewModel.StewardPage.IsStatisticsTab);
        Assert.NotNull(tab.Report);
        Assert.Equal(requests, harness.IndexDocumentRequests);
        Assert.Null(tab.Error);

        // the Discover load fetches the index once the snapshot is due for a check, so the tab must not start it
        Assert.Empty(harness.ViewModel.DiscoverItems);
    }

    [Fact]
    public async Task BuildFit_GivesTheShareOfTheMods_AndListsTheOthers()
    {
        using var harness = await CreateAsync(ViewModelHarness.MarkGone("KSArmory", "0.8.44"));

        var report = (await OpenAsync(harness.ViewModel)).Report!;

        var fit = report.BuildFit;
        Assert.Equal("2026.9.7.5402", fit.Build);
        Assert.Equal(harness.Localization.FormatStewardStatisticsNotBlocked("2", "3", "2026.9.7.5402"), fit.Summary);
        Assert.StartsWith("2 of 3 mods are not blocked on game build 2026.9.7.5402", fit.Summary, StringComparison.Ordinal);
        Assert.Equal(("2", 2d / 3), (fit.NotBlocked.Count.Text, fit.NotBlocked.Share));
        Assert.Equal(("1", 1d / 3), (fit.Blocked.Count.Text, fit.Blocked.Share));
        Assert.Equal((2d / 3).ToString("P0", CultureInfo.CurrentCulture), report.Tiles[1].Value);
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        Assert.Equal(harness.ViewModel.DiscoverItems.First(item => item.ModId == "KSArmory").Name, Assert.Single(fit.BlockedMods.ShownRows).Name);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 13, 0)]
    [InlineData(9, 13, 0.69)]
    [InlineData(13, 13, 1)]
    [InlineData(1, 1000, 0.01)]
    [InlineData(999, 1000, 0.99)]
    public void Percent_RoundsAPartThatIsNeitherNoneNorAll_NeverTo0Or100(long part, long whole, double shown)
    {
        Assert.Equal(shown.ToString("P0", CultureInfo.CurrentCulture), StewardStatisticsTab.Percent(part, whole));
    }

    [Fact]
    public async Task Tiles_GiveTheHeadlineNumbers_AndTheSentenceBehindThem()
    {
        using var harness = await CreateAsync(ViewModelHarness.MarkGone("KSArmory", "0.8.44"));

        var tiles = (await OpenAsync(harness.ViewModel)).Report!.Tiles;

        Assert.Equal(
            [
                ("4", harness.Localization.StewardStatisticsTotalListings, null),
                ((2d / 3).ToString("P0", CultureInfo.CurrentCulture), harness.Localization.StewardStatisticsModsNotBlocked, harness.Localization.FormatStewardStatisticsNotBlocked("2", "3", "2026.9.7.5402")),
                ("4", harness.Localization.StewardStatisticsUpkeepListings, null),
                ("0", harness.Localization.StewardStatisticsTotalDownloads, harness.Localization.FormatContentDownloadsExact("0")),
            ],
            tiles.Select(tile => (tile.Value, tile.Label, tile.Detail)));
    }

    [Theory]
    [InlineData(999_999, false)]
    [InlineData(1_000_000, true)]
    public async Task DownloadTile_ShowsTheFullNumber_UntilTheShortFormFitsBetter(long downloads, bool isShort)
    {
        using var harness = await CreateAsync(json =>
        {
            var root = JsonNode.Parse(json)!;
            root["listings"]![0]!["downloads"] = JsonNode.Parse($$"""{"total": {{downloads}}, "hosts": {"github": {{downloads}}}, "releases": []}""");
            return root.ToJsonString();
        });

        var tile = (await OpenAsync(harness.ViewModel)).Report!.Tiles[3];

        var exact = downloads.ToString("N0", CultureInfo.CurrentCulture);
        Assert.Equal(isShort ? MainViewModel.CompactCount(downloads) : exact, tile.Value);
        Assert.Equal(harness.Localization.FormatContentDownloadsExact(exact), tile.Detail);
    }

    [Fact]
    public async Task DependencyTables_ShareOneScale_SoALengthMeansOneCountInBoth()
    {
        using var harness = await CreateAsync(json =>
        {
            var root = JsonNode.Parse(json)!;
            var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == "KSArmory")!;
            listing["releases"]![0]!["dependencies"] = JsonNode.Parse("""[{"id": "First", "kind": "required", "source": "derived"}, {"id": "Second", "kind": "required", "source": "derived"}]""");
            return root.ToJsonString();
        });

        var report = (await OpenAsync(harness.ViewModel)).Report!;

        var most = report.MostDependencies.ShownRows.Cast<StewardStatisticsDependencyRow>().ToList();
        var dependedOn = report.MostDependedOn.ShownRows.Cast<StewardStatisticsDependencyRow>().ToList();
        Assert.Equal(1, most[0].RequiredShare);
        Assert.Equal(0.5, most[1].OptionalShare);
        Assert.All(dependedOn, row => Assert.Equal(0.5, row.RequiredShare + row.OptionalShare));
    }

    [Fact]
    public async Task IndexWithABuildAndNoContent_SaysSo_AndDrawsNoBar()
    {
        using var harness = await CreateAsync(json =>
        {
            var root = JsonNode.Parse(json)!;
            root["listings"] = new JsonArray();
            root["packs"] = new JsonArray();
            return root.ToJsonString();
        });

        var report = (await OpenAsync(harness.ViewModel)).Report!;

        Assert.Equal(["0", "-", "0", "0"], report.Tiles.Select(tile => tile.Value));
        Assert.False(report.Tiles[1].IsKnown);
        Assert.Equal(harness.Localization.StewardStatisticsNoMods, report.Tiles[1].Detail);
        Assert.True(report.BuildFit.HasBuild);
        Assert.False(report.BuildFit.IsRated);
        Assert.Equal(harness.Localization.StewardStatisticsNoMods, report.BuildFit.Summary);
        Assert.All(new[] { report.MostDependencies, report.MostDependedOn, report.Types, report.Tags, report.Licenses, report.ReleaseHosts, report.MostDownloaded }, list => Assert.True(list.IsEmpty));
    }

    [Fact]
    public async Task SnapshotWithoutABuild_ShowsADash_AndNoColumnForTheBuild()
    {
        using var harness = await CreateAsync(json =>
        {
            var root = JsonNode.Parse(json)!;
            root["game_versions"]!["versions"] = new JsonArray();
            return root.ToJsonString();
        });

        var report = (await OpenAsync(harness.ViewModel)).Report!;

        Assert.Equal(("-", false), (report.Tiles[1].Value, report.Tiles[1].IsKnown));
        Assert.Equal(harness.Localization.StewardStatisticsNoBuild, report.Tiles[1].Detail);
        Assert.False(report.BuildFit.HasBuild);
        Assert.False(report.BuildFit.IsRated);
        Assert.Equal(harness.Localization.StewardStatisticsNoBuild, report.BuildFit.Summary);
        Assert.True(report.BuildFit.BlockedMods.IsEmpty);
        Assert.Equal(3, report.Upkeep.Columns.Count);
        Assert.DoesNotContain(report.Upkeep.Columns, column => column.Title == harness.Localization.StewardStatisticsNoCurrentRelease);
    }

    [Fact]
    public async Task UpkeepTable_CountsEachProblemInItsHeading_AndKeepsTheColumnsInEachRow()
    {
        using var harness = await CreateAsync(ViewModelHarness.MarkGone("KSArmory", "0.8.44"));

        var upkeep = (await OpenAsync(harness.ViewModel)).Report!.Upkeep;

        var rows = upkeep.Rows.ShownRows.Cast<StewardStatisticsUpkeepRow>().ToList();
        Assert.Equal(["No release for the build", "No update for 180 days", "No image", "No forums thread"], upkeep.Columns.Select(column => column.Title));
        Assert.Equal(["1", "-", "4", "-"], upkeep.Columns.Select(column => column.Count.Text));
        Assert.All(rows, row => Assert.Equal(upkeep.Columns.Select(column => column.Title), row.Cells.Select(cell => cell.Problem)));
        Assert.Equal([true, false, true, false], rows[0].Cells.Select(cell => cell.HasProblem));
    }

    [Fact]
    public async Task ZeroCounts_ShowAsAMutedDash_AndATypeWithoutContentIsLeftOut()
    {
        using var harness = await CreateAsync();

        var report = (await OpenAsync(harness.ViewModel)).Report!;

        Assert.Equal(new StewardStatisticsCount("-", true), report.BuildFit.Blocked.Count);
        var row = Assert.IsType<StewardStatisticsDependencyRow>(Assert.Single(report.MostDependencies.ShownRows));
        Assert.Equal(new StewardStatisticsCount("-", true), row.Required);
        Assert.Equal(new StewardStatisticsCount("1", false), row.Optional);
        Assert.Equal((0, 1), (row.RequiredShare, row.OptionalShare));
        Assert.Equal(
            [("Mods", "3", 1), ("Mod loaders", "1", 1d / 3)],
            report.Types.ShownRows.Cast<StewardStatisticsBarRow>().Select(type => (type.Name, type.Value, type.Share)));
    }

    [Fact]
    public async Task ALongList_ShowsItsFirstRows_UntilShowAll()
    {
        using var harness = await CreateAsync();
        var rows = Enumerable.Range(0, 3).Select(number => new StewardStatisticsRow(harness.ViewModel, $"Row {number}", null)).ToList();

        var list = new StewardStatisticsList(harness.ViewModel, "Title", rows, firstRows: 2);

        Assert.True(list.CanShowAll);
        Assert.Equal(["Row 0", "Row 1"], list.ShownRows.Select(row => row.Name));
        Assert.Equal("Show all 3", list.ShowAllText);
        list.ToggleShowAllCommand.Execute(null);
        Assert.Equal(3, list.ShownRows.Count);
        Assert.Equal(harness.Localization.StewardStatisticsShowFewer, list.ShowAllText);
        Assert.False(new StewardStatisticsList(harness.ViewModel, "Title", rows, firstRows: 3).CanShowAll);
    }

    [Fact]
    public async Task RowOfAListing_OpensItsContentPage_AndACountOpensNothing()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = (await OpenAsync(viewModel)).Report!;
        var license = report.Licenses.ShownRows[0];
        var listing = report.MostDependencies.ShownRows.First(row => row.CanOpen);

        Assert.False(license.CanOpen);
        await listing.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.False(viewModel.CurrentWindowSteward);
        Assert.Equal(listing.Name, viewModel.SelectedContent?.Name);
    }

    [Fact]
    public async Task RowOfAnIdThatIsNotListed_OpensNothing()
    {
        using var harness = await CreateAsync();

        var rows = (await OpenAsync(harness.ViewModel)).Report!.MostDependedOn.ShownRows;

        Assert.False(Assert.Single(rows, row => row.Name == "KittenExtensions").CanOpen);
    }

    [Fact]
    public async Task LanguageSwitch_ShowsTheTextsInTheNewLanguage()
    {
        using var harness = await CreateAsync();
        var tab = await OpenAsync(harness.ViewModel);

        harness.Localization.TrySetCulture("de");

        Assert.Equal("Kein Bild", tab.Report!.Upkeep.Columns[2].Title);
    }

    [Fact]
    public async Task IndexCannotBeRead_SaysWhy_AndShowsNoReport()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(indexOffline: true, gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.StartsWith("Cannot read the content index. ", tab.Error, StringComparison.Ordinal);
        Assert.Null(tab.Report);
    }

    private static async Task<StewardStatisticsTab> OpenAsync(MainViewModel viewModel)
    {
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.ShowStatisticsCommand.ExecuteAsync(null);
        await viewModel.StewardPage.Statistics.WhenLoadedAsync();
        return viewModel.StewardPage.Statistics;
    }

    private async Task<ViewModelHarness> CreateAsync(Func<string, string>? editSnapshot = null)
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: editSnapshot, gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
