using System.Text.Json.Nodes;
using Borea.App.ViewModels;

namespace Borea.App.Tests.ViewModels;

public sealed class StewardStatisticsViewModelTests
{
    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();
    private readonly FakeStewardQueue _queue = new();

    [Fact]
    public async Task StatisticsTab_ShowsTheFourCardsInOrder_WithoutARequest()
    {
        using var harness = await CreateAsync();
        var requests = harness.IndexDocumentRequests;

        var tab = await OpenAsync(harness.ViewModel);

        Assert.True(harness.ViewModel.StewardPage.IsStatisticsTab);
        Assert.Equal(["Game support", "Dependencies", "Upkeep", "Overview"], tab.Cards.Select(card => card.Heading));
        Assert.Equal(requests, harness.IndexDocumentRequests);
        Assert.Null(tab.Error);

        // the Discover load fetches the index once the snapshot is due for a check, so the tab must not start it
        Assert.Empty(harness.ViewModel.DiscoverItems);
    }

    [Fact]
    public async Task GameSupportCard_SaysHowManyModsSupportTheBuild_AndListsTheOthers()
    {
        using var harness = await CreateAsync(ViewModelHarness.MarkGone("KSArmory", "0.8.44"));

        var card = (await OpenAsync(harness.ViewModel)).Cards[0];

        Assert.Equal("2 of 3 mods support game build 2026.9.7.5402.", card.Summary);
        var section = Assert.Single(card.Sections);
        Assert.Equal("Mods that do not support it (1)", section.Title);
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        Assert.Equal(harness.ViewModel.DiscoverItems.First(item => item.ModId == "KSArmory").Name, Assert.Single(section.Rows).Name);
    }

    [Fact]
    public async Task SnapshotWithoutABuild_SaysSo_AndNamesNoCountForTheBuild()
    {
        using var harness = await CreateAsync(json =>
        {
            var root = JsonNode.Parse(json)!;
            root["game_versions"]!["versions"] = new JsonArray();
            return root.ToJsonString();
        });

        var tab = await OpenAsync(harness.ViewModel);

        Assert.Equal(harness.Localization.StewardStatisticsNoBuild, tab.Cards[0].Summary);
        Assert.Empty(tab.Cards[0].Sections);
        Assert.Equal(3, tab.Cards[2].Sections.Count);
        Assert.DoesNotContain(tab.Cards[2].Sections, section => section.Title == harness.Localization.FormatStewardStatisticsNoCurrentRelease("0"));
    }

    [Fact]
    public async Task OverviewCard_NamesEachTypeWithItsCount()
    {
        using var harness = await CreateAsync();

        var card = (await OpenAsync(harness.ViewModel)).Cards[3];

        Assert.Equal(
            [("Mods", "3"), ("Mod loaders", "1"), ("Modpacks", "0"), ("Vehicles", "0"), ("Saves", "0")],
            card.Sections[0].Rows.Select(row => (row.Name, row.Detail)));
    }

    [Fact]
    public async Task RowOfAListing_OpensItsContentPage_AndACountOpensNothing()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);
        var license = tab.Cards[3].Sections[2].Rows[0];
        var listing = tab.Cards[1].Sections[0].Rows.First(row => row.CanOpen);

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

        var rows = (await OpenAsync(harness.ViewModel)).Cards[1].Sections[1].Rows;

        Assert.False(Assert.Single(rows, row => row.Name == "KittenExtensions").CanOpen);
    }

    [Fact]
    public async Task LanguageSwitch_ShowsTheCardsInTheNewLanguage()
    {
        using var harness = await CreateAsync();
        var tab = await OpenAsync(harness.ViewModel);

        harness.Localization.TrySetCulture("de");

        Assert.Equal("Pflege", tab.Cards[2].Heading);
    }

    [Fact]
    public async Task IndexCannotBeRead_SaysWhy_AndShowsNoCard()
    {
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(indexOffline: true, gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.StartsWith("Cannot read the content index. ", tab.Error, StringComparison.Ordinal);
        Assert.Empty(tab.Cards);
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
