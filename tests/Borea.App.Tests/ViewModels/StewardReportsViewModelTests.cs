using Borea.App.ViewModels;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

public sealed class StewardReportsViewModelTests
{
    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();
    private readonly FakeStewardQueue _queue = new();
    private readonly FakeIndexReports _reports = new();

    [Fact]
    public async Task ReportsTab_ShowsBothForms_WithTheSectionsOfEach()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _reports.Reports.Add(FakeIndexReports.Dispute(11, "MeasureTools"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        var tab = await OpenAsync(viewModel);

        Assert.True(viewModel.StewardPage.IsReportsTab);
        var takedown = tab.Reports[0];
        Assert.Equal(("content-index #10", "Takedown", "[Takedown] MeasureTools"), (takedown.NumberText, takedown.KindText, takedown.Report.Title));
        Assert.Equal("filed by alice, " + viewModel.AgeText(takedown.Report.Created), takedown.ByText);
        Assert.Equal(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools").Name, takedown.ContentName);
        Assert.Equal((true, false, true, false), (takedown.CanOpenContent, takedown.IsIdUnknown, takedown.HasId, takedown.HasVersion));
        Assert.Equal(
            [("Ground", "The archive carries something harmful"), ("What is wrong", "The installer runs a script."), ("Who the reporter is", "A player.")],
            takedown.Sections.Select(section => (section.Label, section.Text)));
        Assert.Empty(takedown.MissingTexts);
        Assert.False(takedown.CanOpenForums);

        var dispute = tab.Reports[1];
        Assert.Equal("Id dispute", dispute.KindText);
        Assert.Equal(
            [("What is disputed", "The id is the folder name of my content, and somebody else listed it"), ("Forums thread of the reporter", "https://forums.ahwoo.com/threads/measure-tools.123/"), ("Claim", "I announced it first.")],
            dispute.Sections.Select(section => (section.Label, section.Text)));
        Assert.True(dispute.CanOpenForums);
        Assert.False(tab.IsEmpty);
        Assert.Null(tab.Error);
    }

    [Fact]
    public async Task ReportEditedByHand_NamesTheSectionItLacks()
    {
        _reports.Reports.Add(FakeIndexReports.Report(10, "[Takedown] MeasureTools", "### Listing id\n\nMeasureTools\n\n### What is wrong\n\nThe installer runs a script.\n\n### Who you are\n\nA player."));
        using var harness = await CreateAsync();

        var report = Assert.Single((await OpenAsync(harness.ViewModel)).Reports);

        Assert.Equal(["The report has no section \"Ground\". Somebody changed its text by hand."], report.MissingTexts);
        Assert.Equal(["What is wrong", "Who the reporter is"], report.Sections.Select(section => section.Label));
        Assert.True(report.HasId);
    }

    [Fact]
    public async Task IdThatIsNotInTheIndex_ShowsOnlyTheId_AndItsCheckSaysWhy()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "GoneMod"));
        _editor.Check = change => new IndexStatusCheck(IndexStatusRefusal.UnknownId, [], [], IsOwner: false);
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.DelistCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();

        Assert.Equal((false, true, (string?)null), (report.CanOpenContent, report.IsIdUnknown, report.ContentName));
        Assert.Equal("Delist GoneMod", viewModel.StewardChange.Title);
        Assert.Equal("GoneMod is neither a listing nor a pack in the content index.", viewModel.StewardChange.Refusal);
        Assert.False(viewModel.StewardChange.CanOpen);
    }

    [Fact]
    public async Task ListingFieldWithoutAnId_TurnsTheActionsOff()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "the flight mod of bob"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.DelistCommand.Execute(null);
        report.DisputeCommand.Execute(null);

        Assert.Equal("Borea cannot read an id in \"the flight mod of bob\", so the status actions are off.", report.UnreadableIdText);
        Assert.Equal((false, false, false), (report.HasId, report.IsIdUnknown, report.CanOpenContent));
        Assert.Null(viewModel.StewardChange);
    }

    [Fact]
    public async Task PackIdWithAVersion_RetractsThatVersion_AndOpensThePackPage()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "tools-pack 1.0.0"));
        using var harness = await CreateAsync(ToolsPack());
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.RetractCommand.Execute(null);
        var dialog = viewModel.StewardChange!;
        await dialog.WhenDoneAsync();
        dialog.Reason = "The archive carries something harmful.";
        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal(("Tools Pack", "Version 1.0.0", true), (report.ContentName, report.VersionText, report.HasVersion));
        Assert.Equal("Retract tools-pack 1.0.0", dialog.Title);
        var opened = Assert.Single(_editor.Opened);
        Assert.Equal((IndexStatusAction.Retract, "tools-pack", "1.0.0", (int?)10), (opened.Action, opened.Id, opened.Version, opened.Report));
        Assert.Contains("\n\nCloses #10", opened.Body([]), StringComparison.Ordinal);
        Assert.Equal("The pull request says Closes #10, so its merge closes report #10.", dialog.ReportText);

        viewModel.CloseIndexStatusChange(dialog);
        await report.OpenContentCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowPack);
        Assert.Equal("tools-pack", viewModel.SelectedPack?.PackId);
    }

    [Fact]
    public async Task DelistFromAReport_ClosesTheReport_AndADisputeLeavesItOpen()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _reports.Reports.Add(FakeIndexReports.Dispute(11, "MeasureTools"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        await OpenChangeAsync(viewModel, tab.Reports[0].DelistCommand, "The archive carries something harmful.");
        var delistText = viewModel.StewardChange!.ReportText;
        viewModel.CloseIndexStatusChange(viewModel.StewardChange);
        await OpenChangeAsync(viewModel, tab.Reports[1].DisputeCommand, "The id is contested.");
        var disputeText = viewModel.StewardChange!.ReportText;

        Assert.Equal("The pull request says Closes #10, so its merge closes report #10.", delistText);
        Assert.Equal("The pull request names report #11, which stays open until a steward decides.", disputeText);
        Assert.Equal([(IndexStatusAction.Delist, (int?)10), (IndexStatusAction.Dispute, (int?)11)], _editor.Opened.Select(change => (change.Action, change.Report)));
        Assert.Equal("Sets `delisted` on `MeasureTools` in `index-status.toml`.\n\nReason: The archive carries something harmful.\n\nCloses #10", _editor.Opened[0].Body([]));
        Assert.Equal("Sets `disputed` on `MeasureTools` in `index-status.toml`.\n\nReason: The id is contested.\n\nFor report #11.", _editor.Opened[1].Body([]));
        Assert.All(_editor.Checked, change => Assert.NotNull(change.Report));
    }

    [Fact]
    public async Task ChangeFromTheContentPage_NamesNoReport()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.OpenContentAsync(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools"));

        viewModel.DelistContentCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();

        Assert.Null(viewModel.StewardChange.Change.Report);
        Assert.Null(viewModel.StewardChange.ReportText);
        Assert.Null(viewModel.StewardChange.ReporterText);
    }

    [Fact]
    public async Task ReportOfTheSignedInSteward_WarnsOnTheReportAndInTheDialog()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools", author: "OctoCat"));
        _reports.Reports.Add(FakeIndexReports.Takedown(11, "MeasureTools", author: "alice"));
        _editor.Check = change => new IndexStatusCheck(null, [], [], IsOwner: change.Report == 11);
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        tab.Reports[0].DelistCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();
        var own = (viewModel.StewardChange.ReporterText, viewModel.StewardChange.OwnerText);
        viewModel.CloseIndexStatusChange(viewModel.StewardChange);
        tab.Reports[1].DelistCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();
        var owner = (viewModel.StewardChange.ReporterText, viewModel.StewardChange.OwnerText);

        Assert.Equal((true, false), (tab.Reports[0].IsReporter, tab.Reports[1].IsReporter));
        Assert.Equal((harness.Localization.StewardReporterWarning, (string?)null), own);
        Assert.Equal(((string?)null, harness.Localization.StewardOwnerWarning), owner);
    }

    [Fact]
    public async Task OwnerOfTheListing_IsWarnedOnTheReport_BeforeAnyAction()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _reports.Reports.Add(FakeIndexReports.Dispute(11, "measuretools"));
        _reports.Reports.Add(FakeIndexReports.Takedown(12, "tools-pack 1.1.0"));
        _reports.Reports.Add(FakeIndexReports.Takedown(13, "GoneMod"));
        _editor.Owned.Add("MeasureTools");
        using var harness = await CreateAsync(ToolsPack());

        var tab = await OpenAsync(harness.ViewModel);

        Assert.Equal([true, true, false, false], tab.Reports.Select(report => report.IsOwner));
        Assert.Equal(["MeasureTools", "tools-pack"], Assert.Single(_editor.OwnedAsked));
        Assert.Empty(_editor.Checked);
    }

    [Fact]
    public async Task OwnerLookupThatFails_LeavesTheWarningOut_AndTheReportsStay()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _editor.Owned.Add("MeasureTools");
        _editor.OwnedFailure = new StewardException(StewardFailure.RateLimited);
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.False(Assert.Single(tab.Reports).IsOwner);
        Assert.Null(tab.Error);
    }

    [Fact]
    public async Task SignOut_TakesTheOwnerWarningAway_AndASignInAsksAgain()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _editor.Owned.Add("MeasureTools");

        // built without a synchronization context, so the session and the role refresh the page inline and in order
        using var harness = await Task.Run(() => CreateAsync());
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);
        Assert.True(tab.Reports[0].IsOwner);

        viewModel.SignOutOfGitHubCommand.Execute(null);
        var signedOut = Assert.Single(tab.Reports).IsOwner;
        _session.SignInDirectly();
        await viewModel.WhenStewardRoleCheckedAsync();
        await tab.WhenOwnersCheckedAsync();

        Assert.False(signedOut);
        Assert.True(Assert.Single(tab.Reports).IsOwner);
        Assert.Equal(2, _editor.OwnedAsked.Count);
        Assert.Equal(1, _reports.Reads);
    }

    [Fact]
    public async Task SignOut_TakesTheReporterWarningAway()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools", author: "octocat"));

        // built without a synchronization context, so the session and the role refresh the page inline and in order
        using var harness = await Task.Run(() => CreateAsync());
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);
        Assert.True(tab.Reports[0].IsReporter);

        viewModel.SignOutOfGitHubCommand.Execute(null);

        Assert.False(Assert.Single(tab.Reports).IsReporter);
        Assert.Equal(1, _reports.Reads);
    }

    [Fact]
    public async Task AnswerAndForums_OpenOnGitHubAndTheForums()
    {
        _reports.Reports.Add(FakeIndexReports.Dispute(11, "MeasureTools"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.AnswerCommand.Execute(null);
        report.OpenForumsCommand.Execute(null);

        Assert.Equal(["https://github.com/KSAModding/content-index/issues/11", "https://forums.ahwoo.com/threads/measure-tools.123/"], opened);
    }

    [Fact]
    public async Task ReportOnAListing_OpensItsContentPage()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "measuretools"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        await report.OpenContentCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.False(viewModel.CurrentWindowSteward);
        Assert.Equal("MeasureTools", viewModel.SelectedContent?.ModId);
    }

    [Fact]
    public async Task StewardOfTheReleasesOnly_SeesTheReportsWithoutTheStatusActions()
    {
        _session.Bypass["KSAModding/content-index"] = "never";
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.DelistCommand.Execute(null);

        Assert.False(viewModel.CanEditIndexStatus);
        Assert.Null(viewModel.StewardChange);
    }

    [Fact]
    public async Task NoOpenReport_SaysSo()
    {
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.True(tab.IsEmpty);
        Assert.Empty(tab.Reports);
    }

    [Theory]
    [InlineData(StewardFailure.Forbidden, "Cannot read the issues of KSAModding/content-index. GitHub refused access.")]
    [InlineData(StewardFailure.NetworkError, "Cannot read the issues of KSAModding/content-index. Cannot reach GitHub. Try again.")]
    public async Task ReadFails_SaysWhy(StewardFailure failure, string text)
    {
        _reports.Failure = new StewardException(failure);
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.Equal(text, tab.Error);
        Assert.False(tab.IsEmpty);
    }

    [Fact]
    public async Task Refresh_ReadsTheReportsTab_AndTheTabReadsOnlyOnceWhenItShowsAgain()
    {
        using var harness = await CreateAsync();
        var page = harness.ViewModel.StewardPage;
        await OpenAsync(harness.ViewModel);

        await page.ShowQueueCommand.ExecuteAsync(null);
        await page.ShowReportsCommand.ExecuteAsync(null);
        await page.RefreshTabCommand.ExecuteAsync(null);

        Assert.Equal(2, _reports.Reads);
        Assert.Single(_queue.Filters);
        Assert.Equal(0, _editor.Reads);
    }

    [Fact]
    public async Task LanguageSwitch_ShowsTheReportsInTheNewLanguage()
    {
        _reports.Reports.Add(FakeIndexReports.Report(10, "[Takedown] MeasureTools", "### Listing id\n\nMeasureTools\n\n### What is wrong\n\nThe installer runs a script."));
        _editor.Owned.Add("MeasureTools");
        using var harness = await CreateAsync();
        var tab = await OpenAsync(harness.ViewModel);

        harness.Localization.TrySetCulture("de");

        var report = Assert.Single(tab.Reports);
        Assert.Equal("ID-Streitfall", harness.Localization.StewardReportDispute);
        Assert.Equal(["Was falsch ist"], report.Sections.Select(section => section.Label));
        Assert.StartsWith("Der Meldung fehlt der Abschnitt \"Ground\".", report.MissingTexts[0], StringComparison.Ordinal);
        Assert.StartsWith("eingereicht von alice, ", report.ByText, StringComparison.Ordinal);
        Assert.True(report.IsOwner);
        Assert.Single(_editor.OwnedAsked);
        Assert.Equal(1, _reports.Reads);
    }

    [Fact]
    public async Task LeavePack_ReporterWhoOwnsTheListing_RetractsEveryVersionThatPinsTheModInOnePullRequest()
    {
        _reports.Reports.Add(FakeIndexReports.LeavePack(12, "tools-pack", "MeasureTools"));
        _editor.AddToolsPack();
        _editor.ReporterOwns.Add("MeasureTools");
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.LeavePackCommand.Execute(null);
        var dialog = viewModel.StewardLeave!;
        await dialog.WhenDoneAsync();

        Assert.True(report.IsLeavePack);
        Assert.True(viewModel.IsStewardLeaveOpen);
        Assert.Equal(["MeasureTools", "Other"], dialog.Members);
        Assert.Equal("MeasureTools", dialog.ModId);
        Assert.Equal("Retract every version of tools-pack that pins MeasureTools", dialog.Title);
        Assert.Equal(("Versions to retract: 1.0.0, 1.2.0", (string?)null), (dialog.VersionsText, dialog.RetractedText));
        Assert.Equal(("alice owns the listing of MeasureTools by its ownership proof.", "alice owns alice/MeasureTools.", true, false), (dialog.OwnershipText, dialog.OwnershipDetail, dialog.IsProven, dialog.NeedsConfirmation));
        Assert.Equal("The author of MeasureTools asked to take it out of this pack in https://github.com/KSAModding/content-index/issues/12", dialog.Reason);
        Assert.Equal("The pull request says Closes #12, so its merge closes report #12.", dialog.ReportText);
        Assert.True(dialog.CanOpen);

        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        var leave = Assert.Single(_editor.LeaveOpened);
        Assert.Equal(("tools-pack", "MeasureTools", 12, new GitHubAccount("alice", 5), false), (leave.PackId, leave.ModId, leave.Report, leave.Reporter, leave.ConfirmedByForums));
        Assert.Equal([("tools-pack", "1.0.0"), ("tools-pack", "1.2.0")], _editor.Opened.Select(change => (change.Id, change.Version)));
        Assert.Equal((harness.Localization.FormatStewardOpened("1"), false), (dialog.OpenedText, dialog.CanOpen));
    }

    [Fact]
    public async Task LeavePack_ReporterWhoDoesNotOwnTheListing_OpensOnlyAfterTheStewardConfirmsItByTheForumsAccount()
    {
        _reports.Reports.Add(FakeIndexReports.LeavePack(12, "tools-pack", "MeasureTools"));
        _editor.AddToolsPack();
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.LeavePackCommand.Execute(null);
        var dialog = viewModel.StewardLeave!;
        await dialog.WhenDoneAsync();

        Assert.Equal(
            ("The ownership proof of the listing of MeasureTools does not name alice. Confirm by the forums account of the listing that alice owns it before you go on.", false, true, true, false),
            (dialog.OwnershipText, dialog.IsProven, dialog.NeedsConfirmation, dialog.CanOpenForums, dialog.CanOpen));
        Assert.Equal(
            ("alice does not own someone/MeasureTools, and someone/MeasureTools has neither the topic ksa-index-alice nor a marker file that names alice.", false),
            (dialog.OwnershipDetail, dialog.CanRetry));
        Assert.Equal("I confirmed by the forums account that alice owns MeasureTools", dialog.ConfirmText);
        dialog.OpenForumsCommand.Execute(null);
        Assert.Equal(["https://forums.ahwoo.com/threads/my-mod.42/"], opened);

        dialog.ConfirmedByForums = true;
        Assert.True(dialog.CanOpen);

        // another mod needs its own check and its own confirmation
        dialog.ModId = "Other";
        await dialog.WhenDoneAsync();
        Assert.Equal((false, false, "Versions to retract: 1.0.0, 1.1.0"), (dialog.ConfirmedByForums, dialog.CanOpen, dialog.VersionsText));

        dialog.ModId = "MeasureTools";
        await dialog.WhenDoneAsync();
        dialog.ConfirmedByForums = true;
        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_editor.LeaveOpened).ConfirmedByForums);
        Assert.Equal(3, _editor.LeaveChecked.Count);
    }

    [Fact]
    public async Task LeavePack_CheckOfTheModChosenBeforeThatEndsLast_DoesNotReplaceTheCheckOfTheModChosenNow()
    {
        _reports.Reports.Add(FakeIndexReports.LeavePack(12, "tools-pack", "MeasureTools"));
        _editor.AddToolsPack();
        var hold = new TaskCompletionSource();
        _editor.HoldLeaveCheck = hold;
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.LeavePackCommand.Execute(null);
        var dialog = viewModel.StewardLeave!;
        var first = dialog.WhenDoneAsync();
        dialog.ModId = "Other";
        await dialog.WhenDoneAsync();
        hold.SetResult();
        await first;

        Assert.Equal(("Other", "Versions to retract: 1.0.0, 1.1.0", false), (dialog.ModId, dialog.VersionsText, dialog.IsChecking));
        Assert.Equal(["MeasureTools", "Other"], _editor.LeaveChecked.Select(leave => leave.ModId));
    }

    [Fact]
    public async Task LeavePack_ProofThatHeldAtTheCheckButNotAtTheOpen_AsksTheStewardToConfirmItByTheForumsAccount()
    {
        _reports.Reports.Add(FakeIndexReports.LeavePack(12, "tools-pack", "MeasureTools"));
        _editor.AddToolsPack();
        _editor.ReporterOwns.Add("MeasureTools");
        _editor.OwnershipAtOpen = ListingOwnership.Unknown;
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.LeavePackCommand.Execute(null);
        var dialog = viewModel.StewardLeave!;
        await dialog.WhenDoneAsync();
        Assert.True(dialog.IsProven);
        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Empty(_editor.LeaveOpened);
        Assert.Equal("Confirm by the forums account that alice owns MeasureTools first.", dialog.Refusal);
        Assert.Equal(
            ("Borea could not check the ownership proof of alice.", false, true, false),
            (dialog.OwnershipText, dialog.IsProven, dialog.NeedsConfirmation, dialog.CanOpen));

        dialog.ConfirmedByForums = true;
        Assert.Equal(((string?)null, true), (dialog.Refusal, dialog.CanOpen));
        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_editor.LeaveOpened).ConfirmedByForums);
        Assert.True(dialog.IsOpened);
    }

    [Fact]
    public async Task LeavePack_MembersThatCouldNotBeRead_SayNothingAboutThePack_AndAreReadAgain()
    {
        _reports.Reports.Add(FakeIndexReports.LeavePack(12, "tools-pack", "MeasureTools"));
        _editor.AddToolsPack();
        _editor.ReporterOwns.Add("MeasureTools");
        _editor.MembersFailure = new StewardException(StewardFailure.NetworkError);
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.LeavePackCommand.Execute(null);
        var dialog = viewModel.StewardLeave!;
        await dialog.WhenDoneAsync();

        Assert.NotNull(dialog.Error);
        Assert.Equal(((string?)null, false, true), (dialog.MembersText, dialog.HasMembers, dialog.CanRetry));

        await dialog.RetryCommand.ExecuteAsync(null);

        Assert.Equal(((string?)null, "MeasureTools", "Choose the mod that the report asks to take out of the pack.", false), (dialog.Error, dialog.ModId, dialog.MembersText, dialog.CanRetry));
        Assert.True(dialog.CanOpen);
    }

    [Fact]
    public async Task LeavePack_PackThatNoLongerPinsTheMod_SaysSo_AndOpensNothing()
    {
        _reports.Reports.Add(FakeIndexReports.LeavePack(12, "tools-pack", "MeasureTools"));
        _editor.Packs["tools-pack"] = [new("1.0.0", ["MeasureTools"]), new("1.1.0", ["Other"])];
        _editor.Entries.Add(new IndexStatusEntry("tools-pack", IndexStatusEntry.Retracted, "1.0.0", "2026-09-25T12:00:00Z", "Broken."));
        _editor.ReporterOwns.Add("MeasureTools");
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.LeavePackCommand.Execute(null);
        var dialog = viewModel.StewardLeave!;
        await dialog.WhenDoneAsync();
        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("No version of tools-pack that is not retracted pins MeasureTools.", dialog.Refusal);
        Assert.Equal(((string?)null, "Retracted already: 1.0.0", false), (dialog.VersionsText, dialog.RetractedText, dialog.CanOpen));
        Assert.Empty(_editor.LeaveOpened);
    }

    [Fact]
    public async Task LeavePack_ReportThatNamesNoMemberOrAnotherGroundOrIsClosed_LetsTheStewardChooseOrOffersNothing()
    {
        _reports.Reports.Add(FakeIndexReports.LeavePack(12, "tools-pack", "the flight mod"));
        _reports.Reports.Add(FakeIndexReports.Takedown(13, "tools-pack"));
        _reports.Reports.Add(FakeIndexReports.ClosedReport(FakeIndexReports.LeavePack(14, "tools-pack", "MeasureTools"), IndexReportCloseReason.Completed));
        _editor.AddToolsPack();
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);
        var other = tab.Reports.Single(report => report.Report.Number == 13);
        var closed = tab.Reports.Single(report => report.Report.Number == 14);

        other.LeavePackCommand.Execute(null);
        closed.LeavePackCommand.Execute(null);
        Assert.Null(viewModel.StewardLeave);
        tab.Reports.Single(report => report.Report.Number == 12).LeavePackCommand.Execute(null);
        var dialog = viewModel.StewardLeave!;
        await dialog.WhenDoneAsync();

        Assert.False(other.IsLeavePack);
        Assert.Equal(((string?)null, "Take a mod out of tools-pack", "Choose the mod that the report asks to take out of the pack."), (dialog.ModId, dialog.Title, dialog.MembersText));
        Assert.Empty(_editor.LeaveChecked);
        Assert.False(dialog.CanOpen);

        dialog.CloseCommand.Execute(null);
        Assert.False(viewModel.IsStewardLeaveOpen);
    }

    private static async Task OpenChangeAsync(MainViewModel viewModel, CommunityToolkit.Mvvm.Input.IRelayCommand command, string reason)
    {
        command.Execute(null);
        var dialog = viewModel.StewardChange!;
        await dialog.WhenDoneAsync();
        dialog.Reason = reason;
        await dialog.OpenPullRequestCommand.ExecuteAsync(null);
    }

    private static Func<string, string> ToolsPack() => PackViewModelTests.WithPacks(
        PackViewModelTests.Pack("tools-pack", "Tools Pack", PackViewModelTests.Version("1.0.0", PackViewModelTests.Pin("MeasureTools", "1.1.9")), PackViewModelTests.Version("1.1.0", PackViewModelTests.Pin("MeasureTools", "1.1.10"))));

    private static async Task<StewardReportsTab> OpenAsync(MainViewModel viewModel)
    {
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.ShowReportsCommand.ExecuteAsync(null);
        await viewModel.StewardPage.Reports.WhenLoadedAsync();
        return viewModel.StewardPage.Reports;
    }

    private async Task<ViewModelHarness> CreateAsync(Func<string, string>? editSnapshot = null)
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(editSnapshot: editSnapshot, gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue, indexReports: _reports);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
