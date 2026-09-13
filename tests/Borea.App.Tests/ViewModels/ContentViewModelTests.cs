using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ContentViewModelTests
{
    [Fact]
    public async Task Open_IndexMod_FillsHeaderLinksAndDetails()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");

        await afc.OpenCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.True(viewModel.IsDiscoverSection);
        Assert.False(viewModel.CurrentWindowDiscover);
        Assert.Same(afc, viewModel.SelectedContent);
        Assert.False(viewModel.IsLoaderContent);
        Assert.True(viewModel.IsDescriptionTab);
        Assert.NotNull(viewModel.LatestVersion);
        Assert.StartsWith(">= ", afc.GameVersionText);
        Assert.Contains(harness.Localization.SourceContentIndex, afc.SourceText);
        Assert.Equal(harness.Localization.LinkForum, viewModel.ContentLinks[0].Label);
        Assert.Contains(viewModel.ContentLinks, link => link.Label == harness.Localization.LinkRepository);
        Assert.Contains(viewModel.ContentLinks, link => link.Label == "SpaceDock");
        Assert.True(viewModel.HasContentLinks);
    }

    [Fact]
    public async Task Open_SpaceDockMod_FetchesTheDescriptionPerMod()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var hud = viewModel.DiscoverItems.Single(item => item.ModId == ViewModelHarness.FakeSpaceDock.OwnId);
        Assert.Null(hud.Description);

        await hud.OpenCommand.ExecuteAsync(null);

        Assert.Equal(ViewModelHarness.FakeSpaceDock.OwnDescription, hud.Description);
        Assert.Contains("SpaceDock", hud.SourceText);
        Assert.Null(viewModel.LatestVersion);
        Assert.False(viewModel.HasContentTags);
    }

    [Fact]
    public async Task Versions_ListEveryReleaseNewestFirst()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsVersionsTab);
        Assert.False(viewModel.IsLoadingVersions);
        Assert.Equal(4, viewModel.ContentVersions.Count);
        Assert.Equal(
            viewModel.ContentVersions.OrderByDescending(version => version.ReleaseDate).Select(version => version.Version),
            viewModel.ContentVersions.Select(version => version.Version));
        Assert.All(viewModel.ContentVersions, version =>
        {
            Assert.True(version.CanInstall);
            Assert.False(string.IsNullOrWhiteSpace(version.ChannelText));
            Assert.False(string.IsNullOrWhiteSpace(version.PublishedText));
            Assert.StartsWith(version.GameVersionText.StartsWith(">= ", StringComparison.Ordinal) ? ">= " : "2026.", version.GameVersionText);
        });

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        Assert.Equal(4, viewModel.ContentVersions.Count);

        viewModel.ShowContentDescriptionCommand.Execute(null);
        Assert.True(viewModel.IsDescriptionTab);
    }

    [Fact]
    public async Task Loader_OffersNoInstallButtons()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverLoadersCommand.Execute(null);
        await viewModel.DiscoverItems.Single().OpenCommand.ExecuteAsync(null);

        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderContent);
        Assert.All(viewModel.ContentVersions, version => Assert.False(version.CanInstall));
    }

    [Fact]
    public async Task InstallVersion_DownloadFails_ShowsTheErrorOnTheRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").OpenCommand.ExecuteAsync(null);
        await viewModel.ShowContentVersionsCommand.ExecuteAsync(null);
        var oldest = viewModel.ContentVersions.Last();

        await oldest.InstallCommand.ExecuteAsync(null);

        Assert.NotNull(oldest.InstallError);
        Assert.False(oldest.IsInstalling);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }
}
