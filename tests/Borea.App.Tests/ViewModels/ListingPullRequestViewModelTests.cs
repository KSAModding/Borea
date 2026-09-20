using Borea.App.ViewModels;
using Borea.Core.GitHub;
using Borea.Core.Listings;

namespace Borea.App.Tests.ViewModels;

public sealed class ListingPullRequestViewModelTests
{
    private static readonly Uri PullRequestUrl = new("https://github.com/KSAModding/content-index/pull/90");

    private readonly FakeSession _session = new();
    private readonly FakePublisher _publisher = new();

    [Fact]
    public async Task SignInUnavailable_ShowsOnlyTheBrowserPath()
    {
        _session.IsAvailable = false;
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);

        Assert.True(editor.UsesBrowserOnly);
        Assert.False(editor.CanSignIn);
        Assert.False(editor.NeedsSignIn);
        Assert.False(editor.IsSignedIn);
        Assert.False(editor.CanPublish);
        Assert.Empty(_publisher.Checks);
    }

    [Fact]
    public async Task SignedOut_OpenPullRequestSignsInFromThePageAndGoesOn()
    {
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        var modalOpened = false;
        harness.ViewModel.PropertyChanged += (_, e) => modalOpened |= e.PropertyName == nameof(MainViewModel.IsGitHubSignInOpen) && harness.ViewModel.IsGitHubSignInOpen;

        Assert.True(editor.NeedsSignIn);
        Assert.True(editor.CanPublish);
        Assert.Equal("Borea signs you in to GitHub and opens the pull request from your account.", editor.PublishText);
        Assert.Empty(_publisher.Checks);

        await editor.PublishCommand.ExecuteAsync(null);
        await editor.OwnershipCheck;

        Assert.True(modalOpened);
        Assert.False(harness.ViewModel.IsGitHubSignInOpen);
        Assert.True(editor.IsSignedIn);
        Assert.Equal("Borea opens the pull request from your GitHub account octocat.", editor.PublishText);
        Assert.Single(_publisher.Submissions);
        Assert.Equal("Opened pull request #90.", editor.OutputMessage);
        Assert.Equal(harness.Localization.ListingOwnershipVerified, editor.OwnershipText);
        harness.ViewModel.SetMainWindowLibrary();
        await editor.Following;
    }

    [Fact]
    public async Task SignInFromThePageCancelled_PublishesNothing()
    {
        _session.Outcome = GitHubSignInOutcome.AccessDenied;
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);

        var publishing = editor.PublishCommand.ExecuteAsync(null);
        await harness.ViewModel.WhenGitHubSignInDoneAsync();
        Assert.True(harness.ViewModel.IsGitHubSignInOpen);
        harness.ViewModel.CancelGitHubSignInCommand.Execute(null);
        await publishing;

        Assert.False(editor.IsSignedIn);
        Assert.Empty(_publisher.Submissions);
        Assert.Null(editor.PublishError);
        Assert.True(editor.CanPublish);
    }

    [Fact]
    public async Task SignedIn_OwnershipVerified_NamesTheProof()
    {
        _session.SignIn();
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);

        await editor.OwnershipCheck;

        Assert.True(editor.IsOwnershipVerified);
        Assert.False(editor.IsCheckingOwnership);
        Assert.Equal("Your listing can merge itself.", editor.OwnershipText);
        Assert.Equal("owner/MyMod has the topic ksa-index-octocat.", editor.OwnershipDetail);
        Assert.Null(editor.OwnershipFixUrl);
        Assert.Null(_publisher.Checks[^1].Listed);
    }

    [Fact]
    public async Task SignedIn_NoProof_NamesTheStepAndOpensTheRepository()
    {
        _session.SignIn();
        _publisher.Ownership = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "Studio/MyMod");
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;

        await editor.OwnershipCheck;
        editor.OpenOwnershipFixCommand.Execute(null);

        Assert.False(editor.IsOwnershipVerified);
        Assert.Equal("A steward has to accept it.", editor.OwnershipText);
        Assert.Equal("To let it merge itself, add the topic ksa-index-octocat to Studio/MyMod.", editor.OwnershipDetail);
        Assert.Equal("Open the repository", editor.OwnershipFixLabel);
        Assert.Equal(["https://github.com/Studio/MyMod"], opened);
    }

    [Fact]
    public async Task CheckAgain_RunsTheOwnershipCheckForTheSameHostAgain()
    {
        _session.SignIn();
        _publisher.Ownership = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "Studio/MyMod");
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        await editor.OwnershipCheck;
        var checks = _publisher.Checks.Count;
        Assert.True(editor.CanCheckOwnershipAgain);

        _publisher.Ownership = new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: "Studio/MyMod");
        editor.CheckOwnershipAgainCommand.Execute(null);
        await editor.OwnershipCheck;

        Assert.Equal(checks + 1, _publisher.Checks.Count);
        Assert.True(editor.IsOwnershipVerified);
        Assert.False(editor.CanCheckOwnershipAgain);
    }

    [Theory]
    [InlineData(ListingOwnershipProblem.RepositoryMissing)]
    [InlineData(ListingOwnershipProblem.RepositoryFork)]
    [InlineData(ListingOwnershipProblem.RepositoryRenamed)]
    public async Task SpaceDockLinkToAnUnusableRepository_AsksToFixTheLinkOnSpaceDock(ListingOwnershipProblem problem)
    {
        _session.SignIn();
        _publisher.Ownership = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: problem, Repository: "Studio/MyMod", SpaceDockMod: "4253", RenamedTo: "Studio/NewName");
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);

        await editor.OwnershipCheck;

        Assert.Equal("Set your GitHub repository as the source code link of SpaceDock mod 4253.", editor.OwnershipDetail);
        Assert.Equal("https://spacedock.info/mod/4253", editor.OwnershipFixUrl);
        Assert.Equal("Open the mod on SpaceDock", editor.OwnershipFixLabel);
    }

    [Fact]
    public async Task OpenPullRequestWithOtherFiles_NamesItAndOpensIt()
    {
        _session.SignIn();
        _publisher.Ownership = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.PullRequestHasOtherFiles, PullRequest: 82);
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);

        await editor.OwnershipCheck;

        Assert.Equal("A steward has to accept it.", editor.OwnershipText);
        Assert.Equal("Borea adds to your open pull request #82, which also changes other files. Take them out of it to let it merge itself.", editor.OwnershipDetail);
        Assert.Equal("https://github.com/KSAModding/content-index/pull/82", editor.OwnershipFixUrl);
        Assert.Equal("Open the pull request", editor.OwnershipFixLabel);
    }

    [Fact]
    public async Task SignedIn_ChangedHost_ChecksAgainButNotForOtherFields()
    {
        _session.SignIn();
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        await editor.OwnershipCheck;
        var checks = _publisher.Checks.Count;

        editor.Abstract = "Does another thing.";
        await editor.OwnershipCheck;
        Assert.Equal(checks, _publisher.Checks.Count);

        editor.ReleasesGitHub = "owner/Other";
        await editor.OwnershipCheck;
        Assert.Equal(checks + 1, _publisher.Checks.Count);
        Assert.Equal("owner/Other", _publisher.Checks[^1].Submitted.Releases!.GitHub);
    }

    [Fact]
    public async Task Publish_OpensThePullRequestAndFollowsItUntilItMerges()
    {
        _session.SignIn();
        _publisher.Statuses.Enqueue(new ListingPullRequestStatus(ListingPullRequestState.ChecksRunning, null));
        _publisher.Statuses.Enqueue(new ListingPullRequestStatus(ListingPullRequestState.ValidatedMerging, "Validated.\n\nThis pull request merges on its own once the checks finish."));
        _publisher.Statuses.Enqueue(new ListingPullRequestStatus(ListingPullRequestState.Merged, null));
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        editor.FollowInterval = TimeSpan.FromMilliseconds(10);
        var states = new List<string?>();
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ListingEditor.PullRequestStateText))
            {
                lock (states)
                    states.Add(editor.PullRequestStateText);
            }
        };

        await editor.PublishCommand.ExecuteAsync(null);
        await editor.Following;

        var submission = Assert.Single(_publisher.Submissions);
        Assert.Equal(new ListingSubmission("MyMod", "My Mod", editor.DocumentText, IsEdit: false), submission);
        Assert.Equal("Opened pull request #90.", editor.OutputMessage);
        Assert.Null(editor.PublishError);
        Assert.True(editor.HasPullRequest);
        Assert.Equal("Pull request #90", editor.PullRequestTitle);
        Assert.Equal(["The checks are running.", "Validated. It merges on its own.", "Merged. Your listing shows in Borea after the next index update."], states);
        Assert.Equal(3, _publisher.StatusReads);
        Assert.True(editor.IsPullRequestGood);
        Assert.Null(editor.PullRequestVerdict);
        Assert.Equal("Open pull request", editor.PublishLabel);
        Assert.Equal(GitHubSessionStatus.SignedOut, _session.State.Status);
        Assert.False(editor.NeedsSignInToFollow);
        Assert.Null(editor.StatusError);
    }

    [Fact]
    public async Task ClosedWithoutAMerge_SignsOut()
    {
        _session.SignIn();
        _publisher.Statuses.Enqueue(new ListingPullRequestStatus(ListingPullRequestState.Closed, null));
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);

        await editor.PublishCommand.ExecuteAsync(null);
        await editor.Following;

        Assert.Equal("Closed without a merge.", editor.PullRequestStateText);
        Assert.Equal(GitHubSessionStatus.SignedOut, _session.State.Status);
        Assert.Equal(1, _publisher.StatusReads);
    }

    [Fact]
    public async Task RefreshOfAClosedPullRequest_KeepsALaterSignIn()
    {
        _session.SignIn();
        _publisher.Statuses.Enqueue(new ListingPullRequestStatus(ListingPullRequestState.Closed, null));
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        await editor.PublishCommand.ExecuteAsync(null);
        await editor.Following;

        _session.SignIn();
        await editor.RefreshStatusCommand.ExecuteAsync(null);

        Assert.Equal(2, _publisher.StatusReads);
        Assert.False(editor.HasOpenPullRequest);
        Assert.Equal(GitHubSessionStatus.SignedIn, _session.State.Status);
    }

    [Fact]
    public async Task RefreshWhileFollowing_StaysBusyUntilBothReadsEnd()
    {
        _session.SignIn();
        var follow = new TaskCompletionSource();
        var refresh = new TaskCompletionSource();
        _publisher.StatusHolds.Enqueue(follow);
        _publisher.StatusHolds.Enqueue(refresh);
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        editor.FollowInterval = TimeSpan.FromHours(1);
        await editor.PublishCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _publisher.StatusReads == 1);

        var refreshing = editor.RefreshStatusCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _publisher.StatusReads == 2);
        refresh.SetResult();
        await refreshing;

        Assert.True(editor.IsRefreshingStatus);
        follow.SetResult();
        await WaitUntilAsync(() => !editor.IsRefreshingStatus);
        harness.ViewModel.SetMainWindowLibrary();
        await editor.Following;
    }

    [Fact]
    public async Task NoFork_ShowsTheStepAndCheckAgainGoesOnAfterTheFix()
    {
        _session.SignIn();
        _publisher.Failure = new ListingPublishException(ListingPublishFailure.NoFork, ListingPublishStep.Fork);
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        editor.FollowInterval = TimeSpan.FromHours(1);
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;

        await editor.PublishCommand.ExecuteAsync(null);
        editor.OpenForkStepCommand.Execute(null);

        Assert.True(editor.HasForkStep);
        Assert.Equal("Borea opens the pull request from your own copy of content-index. Make the copy on GitHub first.", editor.ForkStepText);
        Assert.Equal("Make your copy of the index", editor.ForkStepLabel);
        Assert.Equal(["https://github.com/KSAModding/content-index/fork"], opened);
        Assert.Null(editor.PublishError);
        Assert.False(editor.HasPullRequest);

        _publisher.Failure = null;
        await editor.PublishCommand.ExecuteAsync(null);

        Assert.False(editor.HasForkStep);
        Assert.Equal(2, _publisher.Submissions.Count);
        Assert.Equal("Opened pull request #90.", editor.OutputMessage);
        Assert.True(editor.HasPullRequest);
        harness.ViewModel.SetMainWindowLibrary();
        await editor.Following;
    }

    [Fact]
    public async Task ForkWithoutTheApp_AsksToAllowBoreaOnIt()
    {
        _session.SignIn();
        _publisher.Failure = new ListingPublishException(ListingPublishFailure.AppNotOnFork, ListingPublishStep.Fork, "octocat/content-index", repositoryId: 42);
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;

        await editor.PublishCommand.ExecuteAsync(null);
        editor.OpenForkStepCommand.Execute(null);

        Assert.Equal("Allow Borea on octocat/content-index. On GitHub, choose Only select repositories and pick octocat/content-index. Borea can then write only to that copy.", editor.ForkStepText);
        Assert.Equal("Allow Borea on your copy", editor.ForkStepLabel);
        Assert.Equal([FakeSession.Install + "?repository=42"], opened);
        Assert.Null(editor.PublishError);
    }

    [Fact]
    public async Task ForkStep_HidesTheOwnershipCheckAgain()
    {
        _session.SignIn();
        _publisher.Ownership = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoProof, Repository: "Studio/MyMod");
        _publisher.Failure = new ListingPublishException(ListingPublishFailure.NoFork, ListingPublishStep.Fork);
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        await editor.OwnershipCheck;
        Assert.True(editor.CanCheckOwnershipAgain);

        await editor.PublishCommand.ExecuteAsync(null);

        Assert.True(editor.HasForkStep);
        Assert.False(editor.CanCheckOwnershipAgain);
    }

    [Fact]
    public async Task SignOut_DropsTheForkStep()
    {
        _session.SignIn();
        _publisher.Failure = new ListingPublishException(ListingPublishFailure.AppNotOnFork, ListingPublishStep.Fork, "octocat/content-index");
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        await editor.PublishCommand.ExecuteAsync(null);
        Assert.True(editor.HasForkStep);

        harness.ViewModel.SignOutOfGitHubCommand.Execute(null);

        Assert.False(editor.HasForkStep);
        Assert.Null(editor.ForkStepText);
    }

    [Fact]
    public async Task Following_ShowsTheVerdictAndOffersTheUpdate()
    {
        _session.SignIn();
        _publisher.Statuses.Enqueue(new ListingPullRequestStatus(ListingPullRequestState.Rejected, "The validation rejected this change.\n\n- `schema`: the document: 'x' was unexpected"));
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        editor.FollowInterval = TimeSpan.FromHours(1);
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;

        await editor.PublishCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => editor.PullRequestStatus is not null);
        editor.OpenFollowedPullRequestCommand.Execute(null);

        Assert.Equal("The checks rejected it. Fix what the notes say, then update the pull request.", editor.PullRequestStateText);
        Assert.StartsWith("The validation rejected this change.", editor.PullRequestVerdict, StringComparison.Ordinal);
        Assert.True(editor.IsPullRequestBad);
        Assert.True(editor.HasOpenPullRequest);
        Assert.Equal("Update pull request", editor.PublishLabel);
        Assert.Equal([PullRequestUrl.AbsoluteUri], opened);

        _publisher.Outcome = ListingPublishOutcome.Updated;
        await editor.PublishCommand.ExecuteAsync(null);

        Assert.Equal("Borea added the new file to your open pull request #90.", editor.OutputMessage);
        Assert.Equal(2, _publisher.Submissions.Count);
    }

    [Fact]
    public async Task Refresh_ReadsTheStatusAgain()
    {
        _session.SignIn();
        _publisher.Statuses.Enqueue(new ListingPullRequestStatus(ListingPullRequestState.ChecksRunning, null));
        _publisher.Statuses.Enqueue(new ListingPullRequestStatus(ListingPullRequestState.WaitingForSteward, "Validated, and ownership is not verified, so a steward decides."));
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        editor.FollowInterval = TimeSpan.FromHours(1);
        await editor.PublishCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => editor.PullRequestStatus is not null);

        await editor.RefreshStatusCommand.ExecuteAsync(null);

        Assert.Equal("Validated. A steward has to accept it.", editor.PullRequestStateText);
        Assert.Equal("Validated, and ownership is not verified, so a steward decides.", editor.PullRequestVerdict);
    }

    [Fact]
    public async Task LeavingThePage_SignsOutAndStopsFollowing()
    {
        _session.SignIn();
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        editor.FollowInterval = TimeSpan.FromHours(1);
        await editor.PublishCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _publisher.StatusReads == 1);

        harness.ViewModel.SetMainWindowLibrary();
        await editor.Following;
        await harness.ViewModel.OpenListingAsync();

        Assert.Equal(GitHubSessionStatus.SignedOut, _session.State.Status);
        Assert.True(editor.HasPullRequest);
        Assert.True(editor.Following.IsCompleted);
        Assert.Equal(1, _publisher.StatusReads);
        Assert.Equal("Sign in to GitHub to follow the pull request.", editor.StatusError);
        Assert.True(editor.NeedsSignInToFollow);
    }

    [Fact]
    public async Task LeavingThePageWhilePublishing_SignsOutAfterThePublish()
    {
        _session.SignIn();
        _publisher.Hold = new TaskCompletionSource();
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);

        var publishing = editor.PublishCommand.ExecuteAsync(null);
        harness.ViewModel.SetMainWindowLibrary();
        Assert.Equal(GitHubSessionStatus.SignedIn, _session.State.Status);
        _publisher.Hold.SetResult();
        await publishing;

        Assert.Equal(GitHubSessionStatus.SignedOut, _session.State.Status);
        Assert.True(editor.HasPullRequest);
        Assert.Equal(0, _publisher.StatusReads);
    }

    [Fact]
    public async Task OtherPagesWithoutTheListingPage_KeepTheSignIn()
    {
        _session.SignIn();
        using var harness = await CreateAsync();
        _ = harness.ViewModel.ListingEditor;

        harness.ViewModel.SetMainWindowLibrary();
        harness.ViewModel.SetMainWindowHome();

        Assert.Equal(GitHubSessionStatus.SignedIn, _session.State.Status);
    }

    [Fact]
    public async Task Publishing_ShowsTheStepAndCanBeCancelled()
    {
        _session.SignIn();
        _publisher.Gate = true;
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);

        var publishing = editor.PublishCommand.ExecuteAsync(null);

        Assert.True(editor.IsPublishing);
        Assert.False(editor.CanPublish);
        Assert.Equal("Looking for your copy of content-index...", editor.PublishProgress);

        editor.CancelPublishCommand.Execute(null);
        await publishing;

        Assert.False(editor.IsPublishing);
        Assert.True(editor.CanPublish);
        Assert.Null(editor.PublishError);
        Assert.False(editor.HasPullRequest);
    }

    [Theory]
    [InlineData(ListingPublishFailure.SignedOut, ListingPublishStep.Fork, null, "GitHub signed you out. Sign in again and try once more. Your listing stays here.")]
    [InlineData(ListingPublishFailure.NotFound, ListingPublishStep.Branch, "Not Found", "GitHub did not find the branch.")]
    [InlineData(ListingPublishFailure.Refused, ListingPublishStep.PullRequest, "Validation Failed", "GitHub refused the pull request. Validation Failed")]
    [InlineData(ListingPublishFailure.Refused, ListingPublishStep.Commit, null, "GitHub refused the listing file.")]
    [InlineData(ListingPublishFailure.NetworkError, ListingPublishStep.Fork, null, "Cannot reach GitHub. Your listing stays here. Try again.")]
    [InlineData(ListingPublishFailure.PullRequestNotOnFork, ListingPublishStep.FindPullRequest, "82", "Your pull request #82 already changes this file, but not from your copy of the index. Change it on GitHub, or close it and try again.")]
    public async Task Publish_Failure_SaysWhatFailedAndKeepsTheDraft(ListingPublishFailure failure, ListingPublishStep step, string? detail, string message)
    {
        _session.SignIn();
        _publisher.Failure = new ListingPublishException(failure, step, detail);
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        var text = editor.DocumentText;

        await editor.PublishCommand.ExecuteAsync(null);

        Assert.Equal(message, editor.PublishError);
        Assert.Equal(text, editor.DocumentText);
        Assert.True(editor.CanOpenPullRequest);
        Assert.True(editor.CanPublish);
        Assert.False(editor.HasPullRequest);
    }

    [Fact]
    public async Task Publish_RateLimited_SaysWhenToTryAgain()
    {
        _session.SignIn();
        var retryAt = new DateTimeOffset(2026, 9, 19, 13, 5, 0, TimeSpan.Zero);
        _publisher.Failure = new ListingPublishException(ListingPublishFailure.RateLimited, ListingPublishStep.Fork, retryAt: retryAt);
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);

        await editor.PublishCommand.ExecuteAsync(null);

        Assert.Equal($"GitHub limits how often Borea can ask. Try again after {retryAt.ToLocalTime():t}.", editor.PublishError);
    }

    [Fact]
    public async Task SignOutWhileFollowing_AsksToSignInAgain()
    {
        _session.SignIn();
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        editor.FollowInterval = TimeSpan.FromHours(1);
        await editor.PublishCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _publisher.StatusReads == 1);
        _publisher.StatusFailure = new ListingPublishException(ListingPublishFailure.SignedOut, ListingPublishStep.Status);

        await editor.RefreshStatusCommand.ExecuteAsync(null);
        harness.ViewModel.SignOutOfGitHubCommand.Execute(null);

        Assert.Equal("Sign in to GitHub to follow the pull request.", editor.StatusError);
        Assert.True(editor.NeedsSignIn);
        Assert.Null(editor.Ownership);
    }

    [Fact]
    public async Task SignOutAndSignInAgain_FollowsAgain()
    {
        _session.SignIn();
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        editor.FollowInterval = TimeSpan.FromHours(1);
        await editor.PublishCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _publisher.StatusReads == 1);

        harness.ViewModel.SignOutOfGitHubCommand.Execute(null);
        await editor.Following;
        Assert.Equal("Sign in to GitHub to follow the pull request.", editor.StatusError);

        harness.ViewModel.SignInToGitHubCommand.Execute(null);
        await harness.ViewModel.WhenGitHubSignInDoneAsync();
        await WaitUntilAsync(() => _publisher.StatusReads == 2);

        Assert.Null(editor.StatusError);
        Assert.False(editor.Following.IsCompleted);
        harness.ViewModel.SetMainWindowLibrary();
        await editor.Following;
    }

    [Fact]
    public async Task StartOverWhilePublishing_DropsTheResult()
    {
        _session.SignIn();
        _publisher.Hold = new TaskCompletionSource();
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);

        var publishing = editor.PublishCommand.ExecuteAsync(null);
        editor.StartOverCommand.Execute(null);
        _publisher.Hold.SetResult();
        await publishing;

        Assert.False(editor.IsPublishing);
        Assert.False(editor.HasPullRequest);
        Assert.Null(editor.OutputMessage);
        Assert.Null(editor.PublishError);
        Assert.Equal(0, _publisher.StatusReads);
    }

    [Fact]
    public async Task StartOver_ForgetsThePullRequest()
    {
        _session.SignIn();
        using var harness = await CreateAsync();
        var editor = await ValidNewListingAsync(harness);
        editor.FollowInterval = TimeSpan.FromHours(1);
        await editor.PublishCommand.ExecuteAsync(null);

        editor.StartOverCommand.Execute(null);
        await editor.Following;

        Assert.False(editor.HasPullRequest);
        Assert.Null(editor.PullRequestStatus);
    }

    private Task<ViewModelHarness> CreateAsync() => ViewModelHarness.CreateAsync(gitHub: _session, listingPublisher: _publisher);

    private static async Task<ListingEditor> ValidNewListingAsync(ViewModelHarness harness)
    {
        var editor = harness.ViewModel.ListingEditor;
        editor.OwnershipDelay = TimeSpan.Zero;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";
        editor.Name = "My Mod";
        editor.Authors = "Maxi";
        editor.Abstract = "Does a thing.";
        editor.License = "MIT";
        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        editor.ReleasesGitHub = "owner/MyMod";
        Assert.True(editor.CanOpenPullRequest, string.Join("\n", editor.Errors));
        return editor;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    internal sealed class FakeSession : IGitHubSession
    {
        public const string Install = "https://github.com/apps/borea-test/installations/new";

        public bool IsAvailable { get; set; } = true;

        public string ManageAccessUrl => "https://github.com/settings/apps/authorizations";

        public string InstallUrl => Install;

        public string InstallUrlFor(long repositoryId) => Install + "?repository=" + repositoryId;

        public GitHubSignInOutcome Outcome { get; set; } = GitHubSignInOutcome.SignedIn;

        public GitHubSessionState State { get; private set; } = GitHubSessionState.SignedOut;

        public event EventHandler? StateChanged;

        public Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, CancellationToken cancellationToken = default)
        {
            if (Outcome != GitHubSignInOutcome.SignedIn)
                return Task.FromResult(new GitHubSignInResult(Outcome));

            SignIn();
            return Task.FromResult(new GitHubSignInResult(GitHubSignInOutcome.SignedIn, "octocat"));
        }

        public void SignIn()
        {
            State = GitHubSessionState.SignedInAs("octocat");
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SignOut()
        {
            State = GitHubSessionState.SignedOut;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    internal sealed class FakePublisher : IListingPublisher
    {
        private readonly object _gate = new();
        private int _statusReads;

        public ListingOwnership Ownership { get; set; } = new(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: "owner/MyMod");

        public List<(ListingDraft Submitted, ListingDraft? Listed)> Checks { get; } = [];

        public List<ListingSubmission> Submissions { get; } = [];

        public ListingPublishOutcome Outcome { get; set; } = ListingPublishOutcome.Opened;

        public ListingPublishException? Failure { get; set; }

        /// <summary>Holds the publish until it is cancelled.</summary>
        public bool Gate { get; set; }

        /// <summary>Holds the publish until it is set, whether it is cancelled or not.</summary>
        public TaskCompletionSource? Hold { get; set; }

        /// <summary>The statuses in the order they are read. The last one repeats.</summary>
        public Queue<ListingPullRequestStatus> Statuses { get; } = new();

        public ListingPublishException? StatusFailure { get; set; }

        /// <summary>Each status read waits for the next one of these, while there is one.</summary>
        public Queue<TaskCompletionSource> StatusHolds { get; } = new();

        public int StatusReads => Volatile.Read(ref _statusReads);

        public Task<ListingOwnership> CheckOwnershipAsync(ListingDraft submitted, ListingDraft? listed, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                Checks.Add((submitted, listed));
            return Task.FromResult(Ownership);
        }

        public async Task<ListingPullRequest> PublishAsync(ListingSubmission submission, IProgress<ListingPublishStep>? progress = null, CancellationToken cancellationToken = default)
        {
            Submissions.Add(submission);
            if (Gate)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Hold is not null)
                await Hold.Task;
            if (Failure is not null)
                throw Failure;

            return new ListingPullRequest(90, PullRequestUrl, Outcome);
        }

        public async Task<ListingPullRequestStatus> GetStatusAsync(int number, CancellationToken cancellationToken = default)
        {
            TaskCompletionSource? hold;
            lock (_gate)
                StatusHolds.TryDequeue(out hold);

            Interlocked.Increment(ref _statusReads);
            if (hold is not null)
                await hold.Task.WaitAsync(cancellationToken);
            if (StatusFailure is not null)
                throw StatusFailure;

            lock (_gate)
                return Statuses.Count > 1 ? Statuses.Dequeue() : Statuses.Count == 1 ? Statuses.Peek() : new ListingPullRequestStatus(ListingPullRequestState.ChecksRunning, null);
        }
    }
}
