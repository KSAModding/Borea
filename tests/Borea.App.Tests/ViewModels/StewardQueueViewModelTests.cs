using Borea.App.ViewModels;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

public sealed class StewardQueueViewModelTests
{
    private const string Releases = "KSAModding/content-index-releases";

    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();
    private readonly FakeStewardQueue _queue = new();

    [Fact]
    public async Task StewardPage_OpensOnTheQueue_WithItsCountAndEachItem()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5, verdict: "Validated, and ownership is not verified, so a steward decides."));
        _queue.Items.Add(FakeStewardQueue.Item(9, repository: Releases) with { Kinds = [StewardQueueKind.Release, StewardQueueKind.Amendment], Author = "carol" });
        _queue.Items.Add(FakeStewardQueue.Item(6, needsSteward: false));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var page = viewModel.StewardPage;

        viewModel.OpenStewardPageCommand.Execute(null);
        await page.Queue.WhenLoadedAsync();

        Assert.True(page.IsQueueTab);
        Assert.Equal([StewardQueueFilter.NeedsSteward], _queue.Filters);
        Assert.Equal(0, _editor.Reads);
        Assert.Equal("Queue (2)", page.QueueTabText);
        Assert.Equal(["content-index #5", "content-index-releases #9"], page.Queue.Items.Select(item => item.NumberText));
        var listing = page.Queue.Items[0];
        Assert.Equal(("Pull 5", "by alice, opened " + viewModel.AgeText(listing.Item.Opened), false), (listing.Title, listing.ByText, listing.ShowsWaiting));
        Assert.Equal(["Listing"], listing.KindTexts);
        Assert.Equal(("Validated, and ownership is not verified, so a steward decides.", true), (listing.VerdictText, listing.HasVerdict));
        var amendment = page.Queue.Items[1];
        Assert.Equal(["Release", "Amendment"], amendment.KindTexts);
        Assert.Equal((harness.Localization.StewardQueueNoVerdict, false), (amendment.VerdictText, amendment.HasVerdict));
        Assert.StartsWith("by carol, ", amendment.ByText, StringComparison.Ordinal);
        Assert.False(page.Queue.IsEmpty);
    }

    [Fact]
    public async Task VehicleAndSavePullRequests_ShowAsContentWithTheirKind()
    {
        _queue.Items.Add(FakeStewardQueue.Item(30) with { Kinds = [StewardQueueKind.Vehicle] });
        _queue.Items.Add(FakeStewardQueue.Item(31) with { Kinds = [StewardQueueKind.Save] });
        using var harness = await CreateAsync();
        var page = await OpenAsync(harness.ViewModel);

        Assert.Equal(StewardQueueScope.Content, page.Queue.Scope);
        Assert.Equal([["Vehicle"], ["Save"]], page.Queue.Items.Select(item => item.KindTexts));
    }

    [Fact]
    public async Task Scope_ShowsContentChangesFirst_AndCodeOrEverythingOnChoice_WithoutAnotherRead()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        _queue.Items.Add(FakeStewardQueue.Item(7) with { Kinds = [], HasOtherFiles = true });
        _queue.Items.Add(FakeStewardQueue.Item(8) with { HasOtherFiles = true });
        using var harness = await CreateAsync();
        var page = await OpenAsync(harness.ViewModel);

        Assert.Equal(StewardQueueScope.Content, page.Queue.Scope);
        Assert.Equal([5], page.Queue.Items.Select(item => item.Item.Number));
        Assert.Equal("Queue (1)", page.QueueTabText);

        page.Queue.SelectScopeCommand.Execute(StewardQueueScope.Other);
        Assert.Equal([7, 8], page.Queue.Items.Select(item => item.Item.Number));
        Assert.Equal("Queue (2)", page.QueueTabText);
        Assert.Equal(harness.Localization.StewardQueueScopeOther, page.Queue.ScopeText);

        page.Queue.SelectScopeCommand.Execute(StewardQueueScope.All);
        Assert.Equal([5, 7, 8], page.Queue.Items.Select(item => item.Item.Number));
        Assert.Equal("Queue (3)", page.QueueTabText);
        Assert.Equal([StewardQueueFilter.NeedsSteward], _queue.Filters);
    }

    [Fact]
    public async Task AllOpen_ListsEveryOpenPullRequest_MarksTheWaitingOnes_AndKeepsTheCount()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        _queue.Items.Add(FakeStewardQueue.Item(6, needsSteward: false));
        using var harness = await CreateAsync();
        var page = await OpenAsync(harness.ViewModel);

        page.Queue.ShowAllOpen = true;
        await page.Queue.WhenLoadedAsync();

        Assert.Equal([StewardQueueFilter.NeedsSteward, StewardQueueFilter.AllOpen], _queue.Filters);
        Assert.Equal([(5, true), (6, false)], page.Queue.Items.Select(item => (item.Item.Number, item.ShowsWaiting)));
        Assert.Equal("Queue (1)", page.QueueTabText);
        Assert.Equal(harness.Localization.StewardQueueEmptyAllOpen, page.Queue.EmptyText);
    }

    [Fact]
    public async Task FilterChangedDuringARead_ReadsAgainWithTheNewFilter()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        _queue.Items.Add(FakeStewardQueue.Item(6, needsSteward: false));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var hold = _queue.Hold = new TaskCompletionSource();

        viewModel.OpenStewardPageCommand.Execute(null);
        viewModel.StewardPage.Queue.ShowAllOpen = true;
        _queue.Hold = null;
        hold.SetResult();
        await viewModel.StewardPage.Queue.WhenLoadedAsync();

        Assert.Equal([StewardQueueFilter.NeedsSteward, StewardQueueFilter.AllOpen], _queue.Filters);
        Assert.Equal([5, 6], viewModel.StewardPage.Queue.Items.Select(item => item.Item.Number));
    }

    [Fact]
    public async Task EmptyQueue_SaysThatNothingWaits()
    {
        using var harness = await CreateAsync();
        var page = await OpenAsync(harness.ViewModel);

        Assert.True(page.Queue.IsEmpty);
        Assert.Equal("No pull request waits for a steward.", page.Queue.EmptyText);
        Assert.Equal("Queue (0)", page.QueueTabText);
    }

    [Theory]
    [InlineData(StewardFailure.Forbidden, "Cannot read the pull requests of KSAModding/content-index-releases. GitHub refused access, maybe because the Borea App is not installed on this repository.")]
    [InlineData(StewardFailure.NotFound, "Cannot read the pull requests of KSAModding/content-index-releases. GitHub did not find the repository.")]
    [InlineData(StewardFailure.NetworkError, "Cannot read the pull requests of KSAModding/content-index-releases. Cannot reach GitHub. Try again.")]
    public async Task OneRepositoryFails_SaysWhichAndWhy_AndTheOtherOneShows(StewardFailure failure, string text)
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        _queue.Failures.Add(new StewardQueueFailure(Releases, new StewardException(failure)));
        using var harness = await CreateAsync();
        var page = await OpenAsync(harness.ViewModel);

        Assert.Equal([text], page.Queue.Failures);
        Assert.Equal([5], page.Queue.Items.Select(item => item.Item.Number));
        Assert.Null(page.Queue.Error);
        Assert.False(page.Queue.IsEmpty);
    }

    [Fact]
    public async Task NoRepositoryCanBeRead_TheCountIsUnknown()
    {
        _queue.Failures.Add(new StewardQueueFailure("KSAModding/content-index", new StewardException(StewardFailure.NetworkError)));
        _queue.Failures.Add(new StewardQueueFailure(Releases, new StewardException(StewardFailure.NetworkError)));
        using var harness = await CreateAsync();
        var page = await OpenAsync(harness.ViewModel);

        Assert.Equal(2, page.Queue.Failures.Count);
        Assert.Null(page.Queue.Count);
        Assert.Equal(harness.Localization.StewardTabQueue, page.QueueTabText);
    }

    [Fact]
    public async Task LanguageSwitch_ShowsTheQueueInTheNewLanguage()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        _queue.Failures.Add(new StewardQueueFailure(Releases, new StewardException(StewardFailure.Forbidden)));
        using var harness = await CreateAsync();
        var page = await OpenAsync(harness.ViewModel);
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        page.Queue.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.Equal("Warteschlange (1)", page.QueueTabText);
        Assert.Contains(nameof(StewardPage.QueueTabText), changed);
        Assert.Contains(nameof(StewardQueueTab.EmptyText), changed);
        Assert.Equal(["Eintrag"], Assert.Single(page.Queue.Items).KindTexts);
        Assert.Equal(["Die Pull Requests von KSAModding/content-index-releases sind nicht lesbar. GitHub hat den Zugriff verweigert, vielleicht weil die Borea-App auf diesem Repository nicht installiert ist."], page.Queue.Failures);
    }

    [Fact]
    public async Task SignedOutDuringTheRead_SaysSoAndListsNothing()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        var page = await OpenAsync(harness.ViewModel);
        _queue.Failure = new StewardException(StewardFailure.SignedOut);

        await page.RefreshTabCommand.ExecuteAsync(null);

        Assert.Equal("GitHub signed you out. Sign in again in Settings.", page.Queue.Error);
        Assert.Empty(page.Queue.Items);
        Assert.False(page.Queue.IsEmpty);
        Assert.Equal(harness.Localization.StewardTabQueue, page.QueueTabText);
    }

    [Fact]
    public async Task Refresh_ReadsTheTabThatShows_AndATabReadsOnlyOnceWhenItShowsAgain()
    {
        using var harness = await CreateAsync();
        var page = await OpenAsync(harness.ViewModel);

        await page.RefreshTabCommand.ExecuteAsync(null);
        await page.ShowStatusCommand.ExecuteAsync(null);
        await page.ShowQueueCommand.ExecuteAsync(null);
        await page.ShowStatusCommand.ExecuteAsync(null);
        await page.RefreshTabCommand.ExecuteAsync(null);

        Assert.Equal(2, _queue.Filters.Count);
        Assert.Equal(2, _editor.Reads);
        Assert.True(page.IsStatusTab);
    }

    [Fact]
    public void EveryKind_HasItsText()
    {
        var viewModel = new MainViewModel(new Borea.App.Localization.LocalizationService());

        Assert.Equal(
            ["Listing", "Pack", "Vehicle", "Save", "Release", "Amendment", "Owner record", "Index status", "Tag vocabulary"],
            Enum.GetValues<StewardQueueKind>().Select(viewModel.StewardQueueKindText));
    }

    private static async Task<StewardPage> OpenAsync(MainViewModel viewModel)
    {
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        return viewModel.StewardPage;
    }

    private async Task<ViewModelHarness> CreateAsync()
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
