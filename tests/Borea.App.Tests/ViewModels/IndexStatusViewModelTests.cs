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

    [Fact]
    public async Task Refresh_ReadsTheChangedIndexAgainAndSaysWhatItFound()
    {
        var renamed = false;
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => renamed ? Rename(json) : json);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.Contains(viewModel.DiscoverItems, item => item.Name == "Advanced Flight Computer");

        renamed = true;
        await viewModel.RefreshContentIndexCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.DiscoverItems, item => item.Name == NewName);
        Assert.Contains(viewModel.RecentItems, item => item.Name == NewName);
        Assert.Contains(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.ToastIndexRefreshed);
    }

    [Fact]
    public async Task Refresh_ReadsTheOpenModPageAgain()
    {
        var renamed = false;
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => renamed ? Rename(json) : json);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.OpenContentCommand.ExecuteAsync(viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer"));

        renamed = true;
        await viewModel.RefreshContentIndexCommand.ExecuteAsync(null);

        Assert.Equal(NewName, viewModel.SelectedContent?.Name);
    }

    [Fact]
    public async Task Refresh_ThatFails_KeepsTheCachedListAndSaysNothingFound()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var shown = viewModel.DiscoverItems.Select(item => item.ModId).ToList();

        harness.IndexOffline = true;
        await viewModel.RefreshContentIndexCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsIndexStale);
        Assert.Equal(shown, viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.DoesNotContain(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.ToastIndexRefreshed);
    }

    [Fact]
    public async Task Refresh_OfAnUnchangedIndex_KeepsTheListAndSaysItIsUpToDate()
    {
        using var harness = await ViewModelHarness.CreateAsync(indexEtag: true);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var shown = viewModel.DiscoverItems.ToList();

        await viewModel.RefreshContentIndexCommand.ExecuteAsync(null);

        Assert.Equal(ContentIndexRefreshOutcome.NotModified, viewModel.IndexRefreshStatus?.Outcome);
        Assert.Equal(shown, viewModel.DiscoverItems);
        Assert.Contains(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.ToastIndexUpToDate);
        Assert.DoesNotContain(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.ToastIndexRefreshed);
    }

    [Fact]
    public async Task Refresh_DuringABackgroundCheck_StillSaysWhatItFound()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        var checks = IndexRequests(harness);
        var held = new TaskCompletionSource();
        harness.IndexRequest = () => held.Task;
        viewModel.Clock = new ShiftedClock(harness.Services.IndexRefresh.RevalidationInterval + TimeSpan.FromMinutes(1));
        viewModel.SetMainWindowDiscoverCommand.Execute(null);

        var refresh = viewModel.RefreshContentIndexCommand.ExecuteAsync(null);
        harness.IndexRequest = null;
        held.SetResult();
        await refresh;

        Assert.Equal(checks + 1, IndexRequests(harness));
        Assert.Contains(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.ToastIndexRefreshed);
    }

    [Fact]
    public async Task OpeningAPage_ChecksTheIndexAgainOnceTheLastCheckIsOldEnough()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var checks = IndexRequests(harness);

        viewModel.SetMainWindowDiscoverCommand.Execute(null);
        await viewModel.WhenIndexCheckedAsync();
        Assert.Equal(checks, IndexRequests(harness));

        viewModel.Clock = new ShiftedClock(harness.Services.IndexRefresh.RevalidationInterval + TimeSpan.FromMinutes(1));
        viewModel.SetMainWindowLibraryCommand.Execute(null);
        await viewModel.WhenIndexCheckedAsync();

        Assert.Equal(checks + 1, IndexRequests(harness));
    }

    private const string NewName = "Flight Computer Deluxe";

    private static string Rename(string json) =>
        json.Replace("\"name\": \"Advanced Flight Computer\"", "\"name\": \"" + NewName + "\"", StringComparison.Ordinal);

    private static int IndexRequests(ViewModelHarness harness) =>
        harness.Requests.Count(uri => uri.AbsoluteUri.StartsWith("https://ksamodding.github.io/content-index-releases/", StringComparison.Ordinal));

    /// <summary>A clock that reads a fixed span ahead of the real one.</summary>
    private sealed class ShiftedClock(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + offset;
    }

    private static Task CacheFromTwoDaysAgo(BoreaServices services)
    {
        var indexPath = services.Paths.GetIndexPath();
        File.Copy(ViewModelHarness.SnapshotFixturePath, indexPath);
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow - TimeSpan.FromDays(2) - TimeSpan.FromHours(1));
        return Task.CompletedTask;
    }
}
