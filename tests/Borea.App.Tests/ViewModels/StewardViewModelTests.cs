using Borea.App.ViewModels;
using Borea.Core.GitHub;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

public sealed class StewardViewModelTests
{
    private static readonly Uri PullOne = new("https://github.com/KSAModding/content-index/pull/1");

    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();
    private readonly FakeStewardQueue _queue = new();

    [Fact]
    public async Task Settings_OpensTheStewardPage_AndItsStatusTabListsTheEntriesOnMain()
    {
        _editor.Entries.Add(new IndexStatusEntry("GoneMod", "delisted", null, "2026-09-20T10:00:00Z", "Taken down on request."));
        _editor.Entries.Add(new IndexStatusEntry("my-pack", "retracted", "1.0.0", null, null));
        _editor.Pulls.Add(new IndexStatusPullRequest(3, new Uri("https://github.com/KSAModding/content-index/pull/3"), "Dispute Other", "alice", Conflicts: true));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowSettings();

        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.ShowStatusCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowSteward);
        Assert.False(viewModel.IsSettingsOpen);
        Assert.False(viewModel.CurrentWindowHome);
        Assert.Equal("Open steward tools", harness.Localization.SettingsGitHubStewardTools);
        Assert.Equal(["GoneMod", "my-pack"], viewModel.StewardPage.StatusEntries.Select(entry => entry.Id));
        var gone = viewModel.StewardPage.StatusEntries[0];
        Assert.Equal(("Delisted", null, "Since " + MainViewModel.DateText(new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero)), "Taken down on request."), (gone.StateText, gone.VersionText, gone.SinceText, gone.Reason));
        Assert.Equal(("Retracted", "Version 1.0.0"), (viewModel.StewardPage.StatusEntries[1].StateText, viewModel.StewardPage.StatusEntries[1].VersionText));
        var pull = Assert.Single(viewModel.StewardPage.StatusPullRequests);
        Assert.Equal(("#3 Dispute Other, by alice", true), (pull.Text, pull.Conflicts));
        Assert.False(viewModel.StewardPage.IsStatusEmpty);

        viewModel.SetMainWindowHomeCommand.Execute(null);

        Assert.False(viewModel.CurrentWindowSteward);
    }

    [Theory]
    [InlineData("never", "never")]
    [InlineData("never", "always")]
    public async Task NoStewardOfContentIndex_OffersNoStatusChange(string index, string releases)
    {
        _session.Bypass["KSAModding/content-index"] = index;
        _session.Bypass["KSAModding/content-index-releases"] = releases;
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");

        viewModel.DelistContentCommand.Execute(null);
        viewModel.OpenStewardPageCommand.Execute(null);

        Assert.Equal(releases == "always", viewModel.IsGitHubSteward);
        Assert.False(viewModel.CanEditIndexStatus);
        Assert.False(viewModel.CanEditContentStatus);
        Assert.Null(viewModel.StewardChange);
        Assert.Equal(releases == "always", viewModel.CurrentWindowSteward);
        Assert.Empty(_editor.Checked);
    }

    [Fact]
    public async Task SignOut_HidesTheStatusActions()
    {
        // built without a synchronization context, so the session and the role refresh the page inline and in order
        using var harness = await Task.Run(() => CreateAsync());
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");
        Assert.True(viewModel.CanEditContentStatus);
        var changed = new List<string?>();
        ((System.ComponentModel.INotifyPropertyChanged)viewModel).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.SignOutOfGitHubCommand.Execute(null);

        Assert.False(viewModel.CanEditIndexStatus);
        Assert.False(viewModel.CanEditContentStatus);
        Assert.Contains(nameof(MainViewModel.CanEditIndexStatus), changed);
        Assert.Contains(nameof(MainViewModel.CanEditContentStatus), changed);
    }

    [Fact]
    public async Task ContentPage_Delist_ChecksFirstAndOpensThePullRequestOnce()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");
        var hold = _editor.HoldOpen = new TaskCompletionSource();

        viewModel.DelistContentCommand.Execute(null);
        var dialog = viewModel.StewardChange!;
        await dialog.WhenDoneAsync();

        Assert.True(viewModel.IsStewardChangeOpen);
        Assert.Equal("Delist MeasureTools", dialog.Title);
        Assert.Equal(harness.Localization.StewardDelistEffect, dialog.Effect);
        Assert.Equal([IndexStatusChange.Delist("MeasureTools", string.Empty)], _editor.Checked);
        Assert.False(dialog.CanOpen);

        dialog.Reason = "  The author asked for it.  ";
        Assert.True(dialog.CanOpen);
        var first = dialog.OpenPullRequestCommand.ExecuteAsync(null);
        var second = dialog.OpenPullRequestCommand.ExecuteAsync(null);
        hold.SetResult();
        await Task.WhenAll(first, second);
        await dialog.WhenDoneAsync();

        Assert.Equal([IndexStatusChange.Delist("MeasureTools", "  The author asked for it.  ")], _editor.Opened);
        Assert.True(dialog.IsOpened);
        Assert.False(dialog.CanOpen);
        Assert.Equal("Pull request #1 is open. It waits for a steward to merge it.", dialog.OpenedText);

        dialog.CloseCommand.Execute(null);

        Assert.Null(viewModel.StewardChange);
    }

    [Fact]
    public async Task CheckOfTheRealRules_LeavesTheReasonOut_AndTheButtonWaitsForAOneLineReason()
    {
        var document = IndexStatusDocument.Parse("entries = []\n");
        var contents = IndexContents.FromPaths(["listings/MeasureTools.toml"]);
        _editor.Check = change =>
        {
            try
            {
                document.Check(change, contents);
                return new IndexStatusCheck(null, [], [], IsOwner: false);
            }
            catch (IndexStatusRefusedException exception)
            {
                return new IndexStatusCheck(exception.Refusal, [], [], IsOwner: false);
            }
        };
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");

        viewModel.DelistContentCommand.Execute(null);
        var dialog = viewModel.StewardChange!;
        await dialog.WhenDoneAsync();

        Assert.Null(dialog.Refusal);
        Assert.Null(dialog.InvalidReasonText);
        Assert.False(dialog.CanOpen);

        dialog.Reason = "A pasted\treason.";
        Assert.False(dialog.CanOpen);
        Assert.Equal(harness.Localization.StewardRefusedInvalidReason, dialog.InvalidReasonText);

        dialog.Reason = "The author asked for it.";
        Assert.True(dialog.CanOpen);
        Assert.Null(dialog.InvalidReasonText);
        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal([IndexStatusChange.Delist("MeasureTools", "The author asked for it.")], _editor.Opened);
        Assert.True(dialog.IsOpened);
    }

    [Fact]
    public async Task RefusedReason_IsNoLongerTheRefusalOnceTheStewardChangesIt()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");
        viewModel.DelistContentCommand.Execute(null);
        var dialog = viewModel.StewardChange!;
        await dialog.WhenDoneAsync();
        dialog.Reason = "Taken down.";
        _editor.OpenFailure = new IndexStatusRefusedException(IndexStatusRefusal.InvalidReason, "MeasureTools");

        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.StewardRefusedInvalidReason, dialog.Refusal);
        Assert.False(dialog.CanOpen);

        _editor.OpenFailure = null;
        dialog.Reason = "Taken down on request.";

        Assert.Null(dialog.Refusal);
        Assert.True(dialog.CanOpen);
    }

    [Fact]
    public async Task WhileThePullRequestOpens_TheDialogCannotClose_AndShowsHowItEnded()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");
        viewModel.DelistContentCommand.Execute(null);
        var dialog = viewModel.StewardChange!;
        await dialog.WhenDoneAsync();
        dialog.Reason = "Taken down.";
        var hold = _editor.HoldOpen = new TaskCompletionSource();

        var opening = dialog.OpenPullRequestCommand.ExecuteAsync(null);
        Assert.True(dialog.IsOpening);
        Assert.False(dialog.CloseCommand.CanExecute(null));
        dialog.CloseCommand.Execute(null);

        Assert.Same(dialog, viewModel.StewardChange);

        hold.SetResult();
        await opening;

        Assert.True(dialog.IsOpened);
        Assert.True(dialog.CloseCommand.CanExecute(null));
        dialog.CloseCommand.Execute(null);
        Assert.Null(viewModel.StewardChange);
    }

    [Fact]
    public async Task OwnDelist_IsTheOwnRequestOfTheOwner_AndOnlyADisputeAsksForAnotherSteward()
    {
        _editor.Check = change => new IndexStatusCheck(null, [], ["alice"], IsOwner: true);
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");

        viewModel.DelistContentCommand.Execute(null);
        var delist = viewModel.StewardChange!;
        await delist.WhenDoneAsync();
        Assert.Equal(harness.Localization.StewardOwnerOwnRequest, delist.OwnerText);
        delist.CloseCommand.Execute(null);

        viewModel.DisputeContentCommand.Execute(null);
        var dispute = viewModel.StewardChange!;
        await dispute.WhenDoneAsync();
        Assert.Equal(harness.Localization.StewardOwnerWarning, dispute.OwnerText);
    }

    [Fact]
    public async Task Check_ShowsTheRefusalTheOpenPullRequestsTheOwnerAndTheMention()
    {
        _editor.Check = change => new IndexStatusCheck(
            IndexStatusRefusal.Duplicate,
            [new IndexStatusPullRequest(4, PullOne, "Dispute Other", "bob"), new IndexStatusPullRequest(6, PullOne, "Delist Third", "bob")],
            ["alice", "carol"],
            IsOwner: true);
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");

        viewModel.DisputeContentCommand.Execute(null);
        var dialog = viewModel.StewardChange!;
        await dialog.WhenDoneAsync();
        dialog.Reason = "Two authors claim it.";

        Assert.Equal("Set MeasureTools as disputed", dialog.Title);
        Assert.Equal("MeasureTools already has this state or another one in index-status.toml. Lift it first.", dialog.Refusal);
        Assert.Equal("Pull request #4, #6 also changes index-status.toml. The one that merges second will have a conflict.", dialog.OpenPullRequestsText);
        Assert.Equal(harness.Localization.StewardOwnerWarning, dialog.OwnerText);
        Assert.Equal("The pull request mentions @alice, @carol, so the owner is told.", dialog.MentionText);
        Assert.False(dialog.CanOpen);
        Assert.False(dialog.OpenPullRequestCommand.CanExecute(null));
    }

    [Fact]
    public async Task OwnOpenPullRequest_TheCheckSaysTheChangeGoesOntoIt_AndTheDialogSaysItWasAdded()
    {
        var own = new IndexStatusPullRequest(4, PullOne, "Delist Other", "octocat");
        _editor.Check = change => new IndexStatusCheck(null, [own, new IndexStatusPullRequest(6, PullOne, "Dispute Third", "bob")], [], IsOwner: false) { Reuses = own };
        _editor.AddTo = own;
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");

        viewModel.DelistContentCommand.Execute(null);
        var dialog = viewModel.StewardChange!;
        await dialog.WhenDoneAsync();
        var openPullRequests = dialog.OpenPullRequestsText;
        dialog.Reason = "Taken down.";
        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal(
            "Borea adds this change to your open pull request #4 instead of opening a new one, so that the changes do not conflict. "
            + "Pull request #6 also changes index-status.toml. The one that merges second will have a conflict.",
            openPullRequests);
        Assert.Equal("Borea added the change to pull request #4. It waits for a steward to merge it.", dialog.OpenedText);
    }

    [Fact]
    public async Task Check_WithoutTheOpenPullRequests_SaysThatItCouldNotCheckThem()
    {
        _editor.Check = change => new IndexStatusCheck(null, null, [], IsOwner: false);
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");

        viewModel.DelistContentCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();

        Assert.Equal(harness.Localization.StewardOpenPullRequestsUnknown, viewModel.StewardChange.OpenPullRequestsText);
        Assert.Null(viewModel.StewardChange.OwnerText);
        Assert.Null(viewModel.StewardChange.MentionText);
    }

    [Fact]
    public async Task PackPage_RetractThisVersion_TakesTheVersionThePageShows()
    {
        using var harness = await CreateAsync(PackViewModelTests.WithPacks(
            PackViewModelTests.Pack("tools-pack", "Tools Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("MeasureTools", "1.1.9")), PackViewModelTests.Version("1.1.0", PackViewModelTests.Pin("MeasureTools", "1.1.10")))));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await Assert.Single(viewModel.DiscoverPacks).OpenCommand.ExecuteAsync(null);

        viewModel.RetractPackVersionCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();
        viewModel.StewardChange.CloseCommand.Execute(null);
        viewModel.DelistPackCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();
        viewModel.StewardChange.CloseCommand.Execute(null);
        viewModel.DisputePackCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();

        Assert.True(viewModel.CanEditIndexStatus);
        Assert.Equal(
            [IndexStatusChange.Retract("tools-pack", "1.1.0", string.Empty), IndexStatusChange.Delist("tools-pack", string.Empty), IndexStatusChange.Dispute("tools-pack", string.Empty)],
            _editor.Checked);
        Assert.Equal("Set tools-pack as disputed", viewModel.StewardChange.Title);
    }

    [Fact]
    public async Task StatusTab_LiftOfADelistedId_OpensItsPullRequestAndReadsTheListAgain()
    {
        var delisted = new IndexStatusEntry("GoneMod", "delisted", null, "2026-09-20T10:00:00Z", "Taken down.");
        _editor.Entries.Add(delisted);
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.ShowStatusCommand.ExecuteAsync(null);

        viewModel.StewardPage.StatusEntries.Single().LiftCommand.Execute(null);
        var dialog = viewModel.StewardChange!;
        await dialog.WhenDoneAsync();
        dialog.Reason = "The takedown did not hold.";
        await dialog.OpenPullRequestCommand.ExecuteAsync(null);
        await viewModel.StewardPage.WhenLoadedAsync();

        Assert.Equal("Lift the state of GoneMod", dialog.Title);
        Assert.Equal("The state \"Delisted\" goes away with the next snapshot of the index.", dialog.Effect);
        Assert.Equal(harness.Localization.StewardLiftReasonHint, dialog.ReasonHint);
        Assert.Equal([IndexStatusChange.Lift(delisted, "The takedown did not hold.")], _editor.Opened);
        Assert.Equal(2, _editor.Reads);
        Assert.Equal(["#1 Lift GoneMod, by octocat"], viewModel.StewardPage.StatusPullRequests.Select(pull => pull.Text));
    }

    [Fact]
    public async Task TwoStatusPullRequests_TheSecondIsWarned_AndShowsTheConflictOnceTheFirstMerges()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");

        viewModel.DelistContentCommand.Execute(null);
        var first = viewModel.StewardChange!;
        await first.WhenDoneAsync();
        first.Reason = "Taken down.";
        await first.OpenPullRequestCommand.ExecuteAsync(null);
        first.CloseCommand.Execute(null);
        viewModel.DisputeContentCommand.Execute(null);
        var second = viewModel.StewardChange!;
        await second.WhenDoneAsync();
        second.Reason = "Claimed twice.";
        await second.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("Pull request #1 also changes index-status.toml. The one that merges second will have a conflict.", second.OpenPullRequestsText);

        _editor.Merge(1);
        second.CloseCommand.Execute(null);
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.ShowStatusCommand.ExecuteAsync(null);

        var pull = Assert.Single(viewModel.StewardPage.StatusPullRequests);
        Assert.Equal(("#2 Dispute MeasureTools, by octocat", true), (pull.Text, pull.Conflicts));
    }

    [Fact]
    public async Task FailedCheck_SaysWhyAndChecksAgain_AndARefusedPullRequestSaysWhy()
    {
        _editor.CheckFailure = new StewardException(StewardFailure.NetworkError);
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");

        viewModel.DelistContentCommand.Execute(null);
        var dialog = viewModel.StewardChange!;
        await dialog.WhenDoneAsync();
        dialog.Reason = "Taken down.";

        Assert.Equal("Cannot reach GitHub. Try again.", dialog.Error);
        Assert.True(dialog.CanRetry);
        Assert.False(dialog.CanOpen);

        _editor.CheckFailure = null;
        _editor.OpenFailure = new IndexStatusRefusedException(IndexStatusRefusal.Duplicate, "MeasureTools");
        await dialog.RetryCommand.ExecuteAsync(null);
        Assert.Null(dialog.Error);
        Assert.True(dialog.CanOpen);
        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("MeasureTools already has this state or another one in index-status.toml. Lift it first.", dialog.Refusal);
        Assert.False(dialog.IsOpened);
        Assert.False(dialog.CanOpen);
    }

    [Theory]
    [InlineData(StewardFailure.SignedOut, "GitHub signed you out. Sign in again in Settings.")]
    [InlineData(StewardFailure.NotSteward, "GitHub does not let this account bypass the rules of content-index, so it cannot do this.")]
    [InlineData(StewardFailure.UnreadableFile, "index-status.toml has a form that Borea does not edit. Change it on GitHub. Line 3 of index-status.toml has a form that Borea does not edit.")]
    [InlineData(StewardFailure.Refused, "GitHub refused the change. Reference already exists")]
    public async Task StatusTab_FailedRead_SaysWhy(StewardFailure failure, string text)
    {
        _editor.ReadFailure = new StewardException(failure, failure switch
        {
            StewardFailure.UnreadableFile => "Line 3 of index-status.toml has a form that Borea does not edit.",
            StewardFailure.Refused => "Reference already exists",
            _ => null,
        });
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.ShowStatusCommand.ExecuteAsync(null);

        Assert.Equal(text, viewModel.StewardPage.Error);
        Assert.False(viewModel.StewardPage.IsStatusEmpty);
    }

    private async Task<ViewModelHarness> CreateAsync(Func<string, string>? editSnapshot = null)
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: editSnapshot, gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }

    private static async Task OpenListingAsync(MainViewModel viewModel, string id)
    {
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.OpenContentAsync(viewModel.DiscoverItems.First(item => item.ModId == id));
    }
}
