using System.Globalization;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Mods;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

public sealed class StewardWatcherViewModelTests
{
    private const string Index = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";

    private static readonly DateTimeOffset Early = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Late = new(2026, 9, 23, 10, 24, 0, TimeSpan.Zero);

    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();
    private readonly FakeStewardQueue _queue = new();
    private readonly FakeWatcherIssues _watcher = new();

    [Fact]
    public async Task WatcherTab_ListsEachListingIssue_WithTheListingOfItsMarker()
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _watcher.Listings.Add(FakeWatcherIssues.Listing(103, null));
        _watcher.Listings.Add(FakeWatcherIssues.Listing(104, "GoneMod"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        var tab = await OpenAsync(viewModel);

        Assert.True(viewModel.StewardPage.IsWatcherTab);
        Assert.Equal(["content-index #102", "content-index #103", "content-index #104"], tab.Listings.Select(issue => issue.NumberText));
        var named = tab.Listings[0];
        Assert.Equal(("MeasureTools: the watcher found a problem", true, false, false), (named.Title, named.CanOpenListing, named.IsListingUnknown, named.HasNoListing));
        Assert.Equal(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools").Name, named.ListingName);
        Assert.Equal("updated " + viewModel.AgeText(named.Issue.Updated), named.UpdatedText);
        var unmarked = tab.Listings[1];
        Assert.Equal((false, false, true), (unmarked.CanOpenListing, unmarked.IsListingUnknown, unmarked.HasNoListing));
        var unknown = tab.Listings[2];
        Assert.Equal((false, true, false), (unknown.CanOpenListing, unknown.IsListingUnknown, unknown.HasNoListing));
        Assert.False(tab.IsListingsEmpty);
        Assert.Empty(tab.Failures);
    }

    [Fact]
    public async Task ListingOfAnIssue_OpensItsContentPage()
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        await tab.Listings[0].OpenListingCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.False(viewModel.CurrentWindowSteward);
        Assert.Equal("MeasureTools", viewModel.SelectedContent?.ModId);
    }

    [Fact]
    public async Task IssueAndWorkflow_OpenOnGitHub()
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _watcher.Watchdog.Add(FakeWatcherIssues.WatchdogIssue(81));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        var tab = await OpenAsync(viewModel);

        tab.Listings[0].OpenCommand.Execute(null);
        tab.Watchdog[0].OpenCommand.Execute(null);
        tab.OpenWorkflowCommand.Execute(null);

        Assert.Equal(
            [$"https://github.com/{Index}/issues/102", $"https://github.com/{Releases}/issues/81", $"https://github.com/{Releases}/actions/workflows/watcher.yml"],
            opened);
        Assert.Null(tab.Error);
    }

    [Fact]
    public async Task OpenWatchdogIssue_ShowsThatTheWatcherDoesNotTick()
    {
        _watcher.Watchdog.Add(FakeWatcherIssues.WatchdogIssue(81));
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        var watchdog = Assert.Single(tab.Watchdog);
        Assert.Equal(("content-index-releases #81", "The watcher is not ticking"), (watchdog.NumberText, watchdog.Title));
        Assert.False(tab.IsTicking);
        Assert.True(tab.IsListingsEmpty);
    }

    [Fact]
    public async Task NoWatchdogIssue_SaysThatTheWatcherTicks()
    {
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.Empty(tab.Watchdog);
        Assert.True(tab.IsTicking);
        Assert.True(tab.IsListingsEmpty);
    }

    [Fact]
    public async Task ListingsFail_SaysWhy_AndTheWatchdogStillShows()
    {
        _watcher.Watchdog.Add(FakeWatcherIssues.WatchdogIssue(81));
        _watcher.Failures.Add(new WatcherIssuesFailure(Index, new StewardException(StewardFailure.NetworkError)));
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.Equal(["Cannot read the issues of KSAModding/content-index. Cannot reach GitHub. Try again."], tab.Failures);
        Assert.Equal([81], tab.Watchdog.Select(issue => issue.Issue.Number));
        Assert.False(tab.IsListingsEmpty);
        Assert.Null(tab.Error);
    }

    [Theory]
    [InlineData(StewardFailure.Forbidden, "Cannot read the issues of KSAModding/content-index-releases. GitHub refused access.")]
    [InlineData(StewardFailure.NotFound, "Cannot read the issues of KSAModding/content-index-releases. GitHub did not find the repository.")]
    public async Task WatchdogFails_SaysWhy_AndTheListingsStillShow(StewardFailure failure, string text)
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _watcher.Failures.Add(new WatcherIssuesFailure(Releases, new StewardException(failure)));
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.Equal([text], tab.Failures);
        Assert.Equal([102], tab.Listings.Select(issue => issue.Issue.Number));
        Assert.False(tab.IsTicking);
    }

    [Fact]
    public async Task Refresh_ReadsTheWatcherTab_AndTheTabReadsOnlyOnceWhenItShowsAgain()
    {
        using var harness = await CreateAsync();
        var page = harness.ViewModel.StewardPage;
        await OpenAsync(harness.ViewModel);

        await page.ShowQueueCommand.ExecuteAsync(null);
        await page.ShowWatcherCommand.ExecuteAsync(null);
        await page.RefreshTabCommand.ExecuteAsync(null);

        Assert.Equal(2, _watcher.Reads);
        Assert.Single(_queue.Filters);
        Assert.Equal(0, _editor.Reads);
    }

    [Fact]
    public async Task LanguageSwitch_ShowsTheWatcherTabInTheNewLanguage()
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _watcher.Failures.Add(new WatcherIssuesFailure(Releases, new StewardException(StewardFailure.Forbidden)));
        using var harness = await CreateAsync(Gone(("MeasureTools", "1.1.9", Early)));
        var tab = await OpenAsync(harness.ViewModel);

        harness.Localization.TrySetCulture("de");

        Assert.Equal(["Die Issues von KSAModding/content-index-releases sind nicht lesbar. GitHub hat den Zugriff verweigert."], tab.Failures);
        Assert.StartsWith("aktualisiert ", Assert.Single(tab.Listings).UpdatedText, StringComparison.Ordinal);
        Assert.Equal("Seit " + MainViewModel.DateText(Early) + " nicht mehr herunterladbar.", Assert.Single(Assert.Single(tab.Gone).Releases).SinceText);
        Assert.Equal(1, _watcher.Reads);
    }

    [Fact]
    public async Task WatcherTab_ListsTheReleasesGoneFromTheirHost_ByListingAndDate()
    {
        using var harness = await CreateAsync(Gone(("MeasureTools", "1.1.9", Early), ("MeasureTools", "1.1.8", Late), ("AdvancedFlightComputer", "0.7.2", Early)));
        var viewModel = harness.ViewModel;

        var tab = await OpenAsync(viewModel);

        Assert.Equal(["MeasureTools", "AdvancedFlightComputer"], tab.Gone.Select(listing => listing.Gone.ListingId));
        var measure = tab.Gone[0];
        Assert.Equal(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools").Name, measure.Name);
        Assert.Equal((true, false), (measure.CanOpenListing, measure.IsListingUnknown));
        Assert.Equal(
            [("1.1.8", "No longer downloadable since " + MainViewModel.DateText(Late) + "."), ("1.1.9", "No longer downloadable since " + MainViewModel.DateText(Early) + ".")],
            measure.Releases.Select(release => (release.Version, release.SinceText)));
        Assert.Equal(["0.7.2"], tab.Gone[1].Releases.Select(release => release.Version));
        Assert.False(tab.IsGoneEmpty);
        Assert.Empty(tab.Failures);
    }

    [Fact]
    public async Task NoReleaseWithTheMark_SaysThatNoReleaseIsGone()
    {
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.Empty(tab.Gone);
        Assert.True(tab.IsGoneEmpty);
    }

    [Fact]
    public async Task ListingOfAGoneRelease_OpensItsContentPage()
    {
        using var harness = await CreateAsync(Gone(("MeasureTools", "1.1.9", Early)));
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        await tab.Gone[0].OpenListingCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Equal("MeasureTools", viewModel.SelectedContent?.ModId);
    }

    [Fact]
    public async Task GoneListingNewerThanTheDiscoverRows_ShowsItsNameFromTheSnapshot_WithoutALink()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenAsync(viewModel);

        var listing = new StewardGoneListing(viewModel, new GoneListing("NewMod", "New Mod", [new GoneRelease(ModVersion.Parse("1.0.0"), Early)]));

        Assert.Null(viewModel.IndexListing("NewMod"));
        Assert.Equal(("New Mod", false, true), (listing.Name, listing.CanOpenListing, listing.IsListingUnknown));
        await listing.OpenListingCommand.ExecuteAsync(null);
        Assert.False(viewModel.CurrentWindowContent);
    }

    [Fact]
    public async Task GoneListingWithoutAnAuthoredDocument_ShowsItsId_WithoutALink()
    {
        using var harness = await CreateAsync(ViewModelHarness.WithoutAuthored("MeasureTools", Gone(("MeasureTools", "1.1.9", Early))));
        var viewModel = harness.ViewModel;

        var tab = await OpenAsync(viewModel);

        Assert.Null(viewModel.IndexListing("MeasureTools"));
        var listing = Assert.Single(tab.Gone);
        Assert.Equal(("MeasureTools", false, true), (listing.Name, listing.CanOpenListing, listing.IsListingUnknown));
        Assert.Equal(["1.1.9"], listing.Releases.Select(release => release.Version));
    }

    [Fact]
    public async Task IndexCannotBeRead_SaysWhy_AndTheIssuesStillShow()
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _session.SignInDirectly();
        using var harness = await ViewModelHarness.CreateAsync(indexOffline: true, gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue, watcherIssues: _watcher);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.StartsWith("Cannot read the content index. ", Assert.Single(tab.Failures), StringComparison.Ordinal);
        Assert.Equal([102], tab.Listings.Select(issue => issue.Issue.Number));
        Assert.Empty(tab.Gone);
        Assert.False(tab.IsGoneEmpty);
    }

    private static async Task<StewardWatcherTab> OpenAsync(MainViewModel viewModel)
    {
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.ShowWatcherCommand.ExecuteAsync(null);
        await viewModel.StewardPage.Watcher.WhenLoadedAsync();
        return viewModel.StewardPage.Watcher;
    }

    /// <summary>Marks each release as gone from its host since its date (RFC 0078).</summary>
    private static Func<string, string> Gone(params (string ModId, string Version, DateTimeOffset Since)[] releases) => json =>
    {
        var root = JsonNode.Parse(json)!;
        foreach (var (modId, version, since) in releases)
        {
            var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == modId)!;
            var release = listing["releases"]!.AsArray().Single(node => (string?)node!["version"] == version)!;
            release["download"]!["unavailable_since"] = since.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        return root.ToJsonString();
    };

    private async Task<ViewModelHarness> CreateAsync(Func<string, string>? editSnapshot = null)
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: editSnapshot, gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue, watcherIssues: _watcher);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
