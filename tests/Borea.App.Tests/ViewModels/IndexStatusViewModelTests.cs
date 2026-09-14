using Borea.Composition;
using Borea.Core.Index;

namespace Borea.App.Tests.ViewModels;

public sealed class IndexStatusViewModelTests
{
    [Fact]
    public async Task FailedRefresh_WithACache_ShowsTheCacheAgeUntilARetrySucceeds()
    {
        using var harness = await ViewModelHarness.CreateAsync(seed: CacheFromTwoDaysAgo, indexOffline: true);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.True(viewModel.IsIndexStale);
        Assert.False(viewModel.IsIndexUnreachable);
        Assert.Equal(harness.Localization.FormatDiscoverIndexStale("2 days ago"), viewModel.IndexStaleText);
        Assert.NotEmpty(viewModel.DiscoverItems);

        var requests = harness.Requests.Count;
        await viewModel.RetryContentIndexCommand.ExecuteAsync(null);
        Assert.True(harness.Requests.Count > requests);
        Assert.True(viewModel.IsIndexStale);

        harness.IndexOffline = false;
        await viewModel.RetryContentIndexCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsIndexStale);
        Assert.Null(viewModel.IndexStaleText);
        Assert.Equal(ContentIndexRefreshOutcome.Downloaded, viewModel.IndexRefreshStatus?.Outcome);
        Assert.NotEmpty(viewModel.DiscoverItems);
    }

    [Fact]
    public async Task FailedRetry_KeepsTheSelectedCategories()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.CuratedTags(("parts", "Parts")));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var parts = viewModel.CategoryOptions.Single(category => category.Tag == "parts");
        viewModel.ToggleCategoryCommand.Execute(parts);

        harness.IndexOffline = true;
        await viewModel.RetryContentIndexCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsIndexStale);
        Assert.Equal([parts], viewModel.SelectedCategories);
        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));
    }

    [Fact]
    public async Task SuccessfulRetry_SelectsTheSameCategoriesAgain()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: ViewModelHarness.CuratedTags(("parts", "Parts")));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ToggleCategoryCommand.Execute(viewModel.CategoryOptions.Single(category => category.Tag == "parts"));

        await viewModel.RetryContentIndexCommand.ExecuteAsync(null);

        Assert.Equal(ContentIndexRefreshOutcome.Downloaded, viewModel.IndexRefreshStatus?.Outcome);
        var parts = Assert.Single(viewModel.SelectedCategories);
        Assert.Equal("parts", parts.Tag);
        Assert.True(parts.IsSelected);
        Assert.Same(parts, viewModel.CategoryOptions.Single(category => category.Tag == "parts"));
        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));
    }

    [Fact]
    public async Task RefreshOutsideDiscover_UpdatesTheLineWhenDiscoverOpens()
    {
        using var harness = await ViewModelHarness.CreateAsync(seed: CacheFromTwoDaysAgo, indexOffline: true);
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowDiscoverCommand.Execute(null);
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(viewModel.IsIndexStale);

        harness.IndexOffline = false;
        await harness.Services.IndexRefresh.RefreshAsync();
        viewModel.SetMainWindowHomeCommand.Execute(null);
        viewModel.SetMainWindowDiscoverCommand.Execute(null);

        Assert.False(viewModel.IsIndexStale);
    }

    [Fact]
    public async Task FailedRefresh_WithoutACache_ShowsTheReasonInsteadOfAnEmptyList()
    {
        using var harness = await ViewModelHarness.CreateAsync(indexOffline: true);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.True(viewModel.IsIndexUnreachable);
        Assert.False(viewModel.IsIndexStale);
        Assert.Equal(harness.Localization.FormatIndexUnreachable(ViewModelHarness.OfflineMessage), viewModel.IndexFailureText);
        Assert.NotNull(viewModel.DiscoverError);
        Assert.Empty(viewModel.DiscoverItems);

        harness.IndexOffline = false;
        await viewModel.RetryContentIndexCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsIndexUnreachable);
        Assert.Null(viewModel.DiscoverError);
        Assert.NotEmpty(viewModel.DiscoverItems);
    }

    [Fact]
    public async Task About_ShowsTheCacheAgeAndTheLastFailure()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.ShowAboutSettingsCommand.Execute(null);

        Assert.Equal(harness.Localization.FormatAboutIndexUpdated("just now"), viewModel.IndexUpdatedText);
        Assert.Null(viewModel.IndexFailureText);
        Assert.Contains("Content index: updated ", viewModel.DiagnosticsText);
        Assert.DoesNotContain("failed", viewModel.DiagnosticsText);

        harness.IndexOffline = true;
        await viewModel.RetryContentIndexCommand.ExecuteAsync(null);
        viewModel.ShowAboutSettingsCommand.Execute(null);

        Assert.Equal(harness.Localization.FormatAboutIndexUpdated("just now"), viewModel.IndexUpdatedText);
        Assert.Equal(harness.Localization.FormatIndexUnreachable(ViewModelHarness.OfflineMessage), viewModel.IndexFailureText);
        Assert.Contains(", the last refresh failed: " + ViewModelHarness.OfflineMessage, viewModel.DiagnosticsText);
    }

    private static Task CacheFromTwoDaysAgo(BoreaServices services)
    {
        var indexPath = services.Paths.GetIndexPath();
        File.Copy(ViewModelHarness.SnapshotFixturePath, indexPath);
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow - TimeSpan.FromDays(2) - TimeSpan.FromHours(1));
        return Task.CompletedTask;
    }
}
