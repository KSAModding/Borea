using System.Net;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Listings;
using Borea.Network.GitHub;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ListingPageTests
{
    [Fact]
    public async Task StartStep_ShowsBothWaysIn()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.ViewModel.OpenListingAsync();

        var texts = await RenderAsync(harness);

        Assert.Contains(harness.Localization.ListingNewTitle, texts);
        Assert.Contains(harness.Localization.ListingChangeTitle, texts);
        Assert.DoesNotContain(harness.Localization.ListingSteps, texts);
    }

    [Fact]
    public async Task SearchField_TypingTheArrowKeysAndEnter_LoadTheChosenListing()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeStarMap);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        var chosen = await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();

            page.FindControl<TextBox>("ListedSearch")!.Focus();
            window.KeyTextInput("s");
            foreach (var key in new[] { PhysicalKey.ArrowDown, PhysicalKey.ArrowDown, PhysicalKey.ArrowUp, PhysicalKey.ArrowDown })
                Press(window, key);
            var chosen = editor.SelectedListed?.Id;
            Press(window, PhysicalKey.Enter);
            await (editor.LoadListedCommand.ExecutionTask ?? Task.CompletedTask);
            window.Close();
            return chosen;
        });

        Assert.Equal("s", editor.ListedQuery);
        Assert.Equal("StarMap", chosen);
        Assert.True(editor.IsFormStep);
        Assert.Equal("StarMap", editor.Id);
    }

    [Fact]
    public async Task ResultList_Enter_LoadsTheChosenListing()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeStarMap);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        await HeadlessApp.RunAsync(harness, async () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();

            page.FindControl<ListBox>("ListedResults")!.GetVisualDescendants().OfType<ListBoxItem>().Last().Focus();
            Press(window, PhysicalKey.ArrowUp);
            Press(window, PhysicalKey.ArrowDown);
            Press(window, PhysicalKey.Enter);
            await (editor.LoadListedCommand.ExecutionTask ?? Task.CompletedTask);
            window.Close();
            return 0;
        });

        Assert.True(editor.IsFormStep);
        Assert.Equal("StarMap", editor.Id);
    }

    [Fact]
    public async Task FormStep_ShowsTheFieldsTheRowsTheChecksAndTheFile()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: new GitHubSession(new HttpClient(), "", ""));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.AddIconCommand.Execute(null);
        editor.AddDescriptionImageCommand.Execute(null);
        editor.AddDependencyCommand.Execute(null);

        var texts = await RenderAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ListingAbout, texts);
        Assert.Contains(localization.ListingImageUrlHint, texts);
        Assert.Contains(localization.ListingImageNotMeasured, texts);
        Assert.Contains(localization.ListingPreview, texts);
        Assert.Contains(localization.ListingSteps, texts);
        Assert.Contains(localization.ListingDependencies, texts);
        Assert.Contains(editor.MissingText, texts);
        Assert.DoesNotContain(localization.ListingErrorsHeading, texts);
        Assert.Contains(localization.ListingOpenPullRequest, texts);
        Assert.Contains(localization.ListingNewPullRequestText, texts);
        Assert.DoesNotContain(localization.ListingEditPullRequestText, texts);
    }

    [Fact]
    public async Task SignedOut_OffersThePullRequestWithTheSignInAndTheBrowserPath()
    {
        using var harness = await ViewModelHarness.CreateAsync(gitHub: new ListingPullRequestViewModelTests.FakeSession(), listingPublisher: new ListingPullRequestViewModelTests.FakePublisher());
        await harness.ViewModel.OpenListingAsync();
        harness.ViewModel.ListingEditor.StartEmptyCommand.Execute(null);

        var texts = await RenderAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ListingSignInText, texts);
        Assert.Contains(localization.ListingPublish, texts);
        Assert.Contains(localization.ListingOpenInBrowser, texts);
        Assert.DoesNotContain(localization.ListingSignIn, texts);
        Assert.DoesNotContain(localization.ListingNewPullRequestText, texts);
    }

    [Fact]
    public async Task NoFork_ShowsOnlyThatStep()
    {
        var session = new ListingPullRequestViewModelTests.FakeSession();
        session.SignIn();
        var publisher = new ListingPullRequestViewModelTests.FakePublisher
        {
            Failure = new ListingPublishException(ListingPublishFailure.NoFork, ListingPublishStep.Fork),
            Ownership = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "owner/MyMod"),
        };
        using var harness = await ViewModelHarness.CreateAsync(gitHub: session, listingPublisher: publisher);
        var editor = harness.ViewModel.ListingEditor;
        editor.OwnershipDelay = TimeSpan.Zero;
        await harness.ViewModel.OpenListingAsync();
        FillValidListing(editor);
        await editor.OwnershipCheck;
        await editor.PublishCommand.ExecuteAsync(null);

        var texts = await RenderAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ListingForkMissing, texts);
        Assert.Contains(localization.ListingMakeFork, texts);
        Assert.Contains(localization.ListingOwnershipSteward, texts);
        Assert.Single(texts, text => text == localization.ListingCheckAgain);
        Assert.DoesNotContain(localization.ListingAllowOnFork, texts);
    }

    [Fact]
    public async Task SignedIn_FollowedPullRequest_ShowsTheOwnershipTheStateAndTheVerdict()
    {
        const string verdict = "The validation rejected this change.";
        var session = new ListingPullRequestViewModelTests.FakeSession();
        session.SignIn();
        var publisher = new ListingPullRequestViewModelTests.FakePublisher();
        publisher.Statuses.Enqueue(new ListingPullRequestStatus(ListingPullRequestState.Rejected, verdict));
        using var harness = await ViewModelHarness.CreateAsync(gitHub: session, listingPublisher: publisher);
        var editor = harness.ViewModel.ListingEditor;
        editor.OwnershipDelay = TimeSpan.Zero;
        editor.FollowInterval = TimeSpan.FromHours(1);
        await harness.ViewModel.OpenListingAsync();
        FillValidListing(editor);
        await editor.OwnershipCheck;
        await editor.PublishCommand.ExecuteAsync(null);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            while (editor.PullRequestStatus is null)
                await Task.Delay(10, timeout.Token);
        }

        var texts = await RenderAsync(harness);

        var localization = harness.Localization;
        Assert.Contains(localization.ListingOwnershipVerified, texts);
        Assert.Contains(localization.ListingUpdatePullRequest, texts);
        Assert.Contains(localization.ListingOpenInBrowser, texts);
        Assert.Contains("Pull request #90", texts);
        Assert.Contains(localization.ListingStateRejected, texts);
        Assert.Contains(verdict, texts);
        Assert.DoesNotContain(localization.ListingSignIn, texts);
    }

    private static void FillValidListing(ListingEditor editor)
    {
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";
        editor.Name = "My Mod";
        editor.Authors = "Maxi";
        editor.Abstract = "Does a thing.";
        editor.License = "MIT";
        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        editor.ReleasesGitHub = "owner/MyMod";
    }

    private static HttpResponseMessage? ServeStarMap(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/listings/StarMap.toml", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListingEditorTests.StarMapListing) }
            : null;

    private static void Press(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
    }

    /// <summary>The visible texts, with the Markdown of every visible Markdown view.</summary>
    private static Task<List<string?>> RenderAsync(ViewModelHarness harness) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var page = new ListingPage { DataContext = harness.ViewModel };
            var window = new Window { Width = 1280, Height = 832, Content = page, DataContext = harness.ViewModel };
            window.Show();
            window.UpdateLayout();
            var texts = page.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text)
                .Concat(page.GetVisualDescendants().OfType<MarkdownView>().Where(view => view.IsEffectivelyVisible).Select(view => view.Markdown))
                .ToList();
            window.Close();
            return Task.FromResult(texts);
        });
}
