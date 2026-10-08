using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;
using Borea.Core.Listings;
using Borea.Core.Stewardship;
using StewardPageView = Borea.App.Views.Pages.StewardPage;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class StewardViewsTests
{
    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();
    private readonly FakeStewardQueue _queue = new();
    private readonly FakeWatcherIssues _watcher = new();
    private readonly FakeIndexReports _reports = new();
    private readonly FakePullRequestReviews _reviews = new();
    private readonly FakePullRequestActions _actions = new();
    private readonly FakeReleaseAmendments _amendments = new();

    [Fact]
    public async Task StewardPage_ShowsEachStateWithLiftAndTheOpenPullRequests()
    {
        _editor.Entries.Add(new IndexStatusEntry("GoneMod", "delisted", null, "2026-09-20T10:00:00Z", "Taken down on request."));
        _editor.Pulls.Add(new IndexStatusPullRequest(3, new Uri("https://github.com/KSAModding/content-index/pull/3"), "Dispute Other", "alice", Conflicts: true));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.ShowStatusCommand.ExecuteAsync(null);

        var (texts, buttons) = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new StewardPageView(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var visible = window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content as string).ToList();
                return Task.FromResult((shown, visible));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(harness.Localization.StewardHeading, texts);
        Assert.Contains("GoneMod", texts);
        Assert.Contains("Delisted", texts);
        Assert.Contains("Taken down on request.", texts);
        Assert.Contains("#3 Dispute Other, by alice", texts);
        Assert.Contains(harness.Localization.StewardStatusConflict, texts);
        Assert.Contains(harness.Localization.StewardLift, buttons);
        Assert.DoesNotContain(harness.Localization.StewardQueueHint, texts);
    }

    [Fact]
    public async Task StewardPage_OpensOnTheQueue_AndShowsEachPullRequestWithItsKindsAndVerdict()
    {
        _queue.Items.Add(FakeStewardQueue.Item(26) with { IsDraft = true, HasOtherFiles = true, Kinds = [StewardQueueKind.Listing, StewardQueueKind.IndexStatus] });
        _queue.Items.Add(FakeStewardQueue.Item(27, verdict: "Validated, and ownership is not verified, so a steward decides."));
        _queue.Failures.Add(new StewardQueueFailure("KSAModding/content-index-releases", new StewardException(StewardFailure.Forbidden)));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        viewModel.StewardPage.Queue.SelectScopeCommand.Execute(StewardQueueScope.All);

        var (texts, buttons) = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new StewardPageView(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var visible = window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content as string).ToList();
                return Task.FromResult((shown, visible));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("Queue (2)", buttons);
        Assert.Contains(harness.Localization.StewardTabStatus, buttons);
        Assert.Contains(harness.Localization.StewardQueueScopeAll, texts);
        Assert.Contains(harness.Localization.StewardQueueAllOpen, texts);
        Assert.Contains("content-index #26", texts);
        Assert.Contains("Pull 26", texts);
        Assert.Contains(harness.Localization.StewardQueueDraft, texts);
        Assert.Contains(harness.Localization.StewardQueueOtherFiles, texts);
        Assert.Contains("Index status", texts);
        Assert.Equal(2, texts.Count(text => text == "Listing"));
        Assert.Contains(harness.Localization.StewardQueueNoVerdict, texts);
        Assert.Contains("Validated, and ownership is not verified, so a steward decides.", texts);
        Assert.Contains(viewModel.StewardQueueFailureText(_queue.Failures[0]), texts);
        Assert.DoesNotContain(harness.Localization.StewardQueueWaiting, texts);
        Assert.DoesNotContain(harness.Localization.StewardStatusHint, texts);
    }

    [Fact]
    public async Task WatcherTab_ShowsTheWatchdogIssue_EachListingIssue_AndTheFailures()
    {
        _watcher.Watchdog.Add(FakeWatcherIssues.WatchdogIssue(81));
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _watcher.Listings.Add(FakeWatcherIssues.Listing(103, null));
        _watcher.Listings.Add(FakeWatcherIssues.Listing(104, "GoneMod"));
        _watcher.Failures.Add(new WatcherIssuesFailure("KSAModding/content-index", new StewardException(StewardFailure.RateLimited, retryAt: DateTimeOffset.Now.AddMinutes(20))));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.ShowWatcherCommand.ExecuteAsync(null);
        var name = viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools").Name;

        var (texts, buttons) = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new StewardPageView(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var visible = window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content as string).ToList();
                return Task.FromResult((shown, visible));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(harness.Localization.StewardTabWatcher, buttons);
        Assert.Contains(harness.Localization.StewardWatcherHint, texts);
        Assert.Contains(harness.Localization.StewardWatcherWorkflow, texts);
        Assert.Contains(harness.Localization.StewardWatcherNotTicking, texts);
        Assert.Contains("content-index-releases #81", texts);
        Assert.Contains("The watcher is not ticking", texts);
        Assert.DoesNotContain(harness.Localization.StewardWatcherTicking, texts);
        Assert.Contains(name, texts);
        Assert.Contains("MeasureTools: the watcher found a problem", texts);
        Assert.Contains(harness.Localization.StewardWatcherNoListing, texts);
        Assert.Contains("GoneMod", texts);
        Assert.Contains(harness.Localization.StewardWatcherUnknownListing, texts);
        Assert.Contains(viewModel.StewardWatcherFailureText(_watcher.Failures[0]), texts);
        Assert.DoesNotContain(harness.Localization.StewardWatcherListingsEmpty, texts);
        Assert.DoesNotContain(harness.Localization.StewardQueueHint, texts);
    }

    [Fact]
    public async Task ReportsTab_ShowsEachReportWithItsSections_AndTheStatusActions()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools", author: "octocat"));
        _reports.Reports.Add(FakeIndexReports.Dispute(11, "GoneMod"));
        _reports.Reports.Add(FakeIndexReports.Report(12, "[Takedown] by hand", "### Listing id\n\nthe flight mod of bob\n\n### What is wrong\n\nIt is broken."));
        _editor.Owned.Add("MeasureTools");
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.ShowReportsCommand.ExecuteAsync(null);
        var name = viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools").Name;

        var (texts, buttons) = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new StewardPageView(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var visible = window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content as string).ToList();
                return Task.FromResult((shown, visible));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(harness.Localization.StewardTabReports, buttons);
        Assert.Contains(harness.Localization.StewardReportsHint, texts);
        Assert.Contains(name, texts);
        Assert.Contains("[Takedown] MeasureTools", texts);
        Assert.Contains("Ground", texts);
        Assert.Contains("The installer runs a script.", texts);
        Assert.Contains(harness.Localization.StewardReporterWarning, texts);
        Assert.Single(texts, text => text == harness.Localization.StewardOwnerWarning);
        Assert.Contains("GoneMod", texts);
        Assert.Contains(harness.Localization.StewardWatcherUnknownListing, texts);
        Assert.Contains("I announced it first.", texts);
        Assert.Contains("Borea cannot read an id in \"the flight mod of bob\", so the status actions are off.", texts);
        Assert.Contains("The report has no section \"Ground\". Somebody changed its text by hand.", texts);
        Assert.Equal(3, buttons.Count(button => button == harness.Localization.StewardReportAnswer));
        Assert.Equal(1, buttons.Count(button => button == harness.Localization.StewardReportOpenForums));
        Assert.Equal(2, buttons.Count(button => button == harness.Localization.StewardDelist));
        Assert.DoesNotContain(harness.Localization.StewardRetract, buttons);
        Assert.DoesNotContain(harness.Localization.StewardReportsEmpty, texts);
        Assert.DoesNotContain(harness.Localization.StewardQueueHint, texts);
    }

    [Fact]
    public async Task IndexStatusModal_FromAReport_SaysThatItsMergeClosesTheReport()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools", author: "octocat"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.ShowReportsCommand.ExecuteAsync(null);
        viewModel.StewardPage.Reports.Reports[0].DelistCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();

        var texts = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new IndexStatusModal();
            var window = new Window { Width = 1280, Height = 832, Content = modal, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                return Task.FromResult(modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("Delist MeasureTools", texts);
        Assert.Contains("The pull request says Closes #10, so its merge closes report #10.", texts);
        Assert.Contains(harness.Localization.StewardReporterWarning, texts);
    }

    [Fact]
    public async Task ReleaseAmendmentModal_ShowsTheReleasesAndThePreviewPerFile_AndOpensThePullRequestOnAClick()
    {
        _amendments.Yanked.Add("1.1.0");
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.OpenContentAsync(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools"));
        viewModel.AmendContentReleasesCommand.Execute(null);
        var dialog = viewModel.StewardAmendment!;
        await dialog.WhenDoneAsync();
        dialog.IsScopeUpTo = true;
        dialog.UpTo = "1.2.0";
        dialog.Yank = true;
        dialog.Reason = "The archive carries malware.";
        await dialog.PreviewCommand.ExecuteAsync(null);

        var texts = await HeadlessApp.RunAsync(harness, async () =>
        {
            var modal = new ReleaseAmendmentModal();
            var window = new Window { Width = 1280, Height = 832, Content = modal, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var open = modal.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && (button.Content as TextBlock)?.Text == harness.Localization.ListingPublish);
                var point = open.TranslatePoint(new Point(open.Bounds.Width / 2, open.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                await dialog.WhenDoneAsync();
                window.UpdateLayout();
                shown.AddRange(modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text));
                return shown;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("Amend releases of MeasureTools", texts);
        Assert.Contains(harness.Localization.StewardAmendExplanation, texts);
        Assert.Contains(harness.Localization.StewardAmendOnBehalf, texts);
        Assert.DoesNotContain(harness.Localization.StewardAmendAuthorRequest, texts);
        Assert.DoesNotContain(harness.Localization.StewardAmendRemoveGameMax, texts);
        Assert.DoesNotContain(harness.Localization.StewardAmendUnyank, texts);
        Assert.DoesNotContain(harness.Localization.StewardAmendChangeOs, texts);
        Assert.Contains(harness.Localization.StewardAmendPreviewHeading, texts);
        Assert.Contains("releases/MeasureTools/1.2.0.json", texts);
        Assert.Contains("+  \"yanked\": true", texts);
        Assert.Contains("releases/MeasureTools/1.1.0.json", texts);
        Assert.Contains(harness.Localization.StewardAmendUnchanged, texts);
        Assert.Contains("The pull request mentions @alice, so the owner is told.", texts);
        Assert.Contains("Pull request #1 is open. It waits for a steward to merge it.", texts);
        Assert.Single(_amendments.Opened);
    }

    [Fact]
    public async Task ReleaseAmendmentModal_OnBehalfOfTheAuthor_AsksForTheLinkAndOffersANewKind()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.OpenContentAsync(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools"));
        viewModel.AmendContentReleasesCommand.Execute(null);
        var dialog = viewModel.StewardAmendment!;
        await dialog.WhenDoneAsync();
        dialog.OnBehalfOfAuthor = true;
        dialog.AuthorRequest = "github.com/issues/1";
        dialog.BoundDependencyCommand.Execute(null);

        var (texts, kinds) = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new ReleaseAmendmentModal();
            var window = new Window { Width = 1280, Height = 832, Content = modal, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var placeholders = modal.GetVisualDescendants().OfType<ComboBox>().Where(box => box.IsEffectivelyVisible).Select(box => box.PlaceholderText).ToList();
                return Task.FromResult((shown, placeholders));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(harness.Localization.StewardAmendAuthorRequest, texts);
        Assert.Contains(harness.Localization.StewardAmendInvalidAuthorRequest, texts);
        Assert.Contains(harness.Localization.StewardAmendGameMaxAuthor, texts);
        Assert.Contains(harness.Localization.StewardAmendBoundDependencyAuthor, texts);
        Assert.Contains(harness.Localization.StewardAmendKindUnchanged, kinds);
        Assert.Contains(harness.Localization.StewardAmendRemoveGameMax, texts);
        Assert.Contains(harness.Localization.StewardAmendRemoveLoaderMin, texts);
        Assert.Contains(harness.Localization.StewardAmendRemoveLoaderMax, texts);
        Assert.Contains(harness.Localization.StewardAmendUnyank, texts);
        Assert.Contains(harness.Localization.StewardAmendChangeOs, texts);
        Assert.Contains(harness.Localization.StewardAmendRemoveMin, texts);
        Assert.Contains(harness.Localization.StewardAmendRemoveMax, texts);
        Assert.DoesNotContain(harness.Localization.StewardAmendOsHint, texts);
    }

    [Fact]
    public async Task ReleaseAmendmentModal_APickFromTheGameMaxList_FillsTheField_AndSaysThatItWidens()
    {
        _amendments.GameVersions.Add("2026.10.7.5541");
        _amendments.Texts["1.2.0"] = FakeReleaseAmendments.Stamped("MeasureTools", "1.2.0", "2026.9.7.5402");
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.OpenContentAsync(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools"));
        viewModel.AmendContentReleasesCommand.Execute(null);
        var dialog = viewModel.StewardAmendment!;
        await dialog.WhenDoneAsync();
        dialog.Versions[0].IsSelected = true;

        var (texts, picks) = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new ReleaseAmendmentModal();
            var window = new Window { Width = 1280, Height = 832, Content = modal, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var gameMax = modal.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == dialog.GameMaxLabel);
                var picks = gameMax.Items.OfType<ReleaseAmendmentChoice>().Select(choice => choice.Value).ToList();
                gameMax.SelectedIndex = picks.IndexOf("2026.10.7.5541");
                window.UpdateLayout();
                var shown = modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                return Task.FromResult((shown, picks));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal("2026.10.7.5541", picks[0]);
        Assert.Equal("2026.10.7.5541", dialog.GameMax);
        Assert.Contains("Now: 2026.9.7.5402", texts);
        Assert.Contains(harness.Localization.StewardAmendWidens, texts);
        Assert.Contains("Now: 0.4.5", texts);
    }

    [Fact]
    public async Task ReleaseAmendmentModal_ADependencyRow_ShowsEachHintUnderItsOwnField()
    {
        _amendments.Texts["1.2.0"] = FakeReleaseAmendments.Stamped("MeasureTools", "1.2.0", "2026.9.7.5402");
        using var harness = await CreateAsync();
        var localization = harness.Localization;
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.OpenContentAsync(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools"));
        viewModel.AmendContentReleasesCommand.Execute(null);
        var dialog = viewModel.StewardAmendment!;
        await dialog.WhenDoneAsync();
        dialog.Versions[0].IsSelected = true;
        dialog.BoundDependencyCommand.Execute(null);
        dialog.Dependencies[0].Id = "KittenExtensions";
        dialog.Dependencies[0].Max = "2.0.0";

        var lefts = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new ReleaseAmendmentModal();
            var window = new Window { Width = 860, Height = 832, Content = modal, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                double Left(Control control) => control.TranslatePoint(default, window)!.Value.X;
                var visible = modal.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible).ToList();
                var id = visible.OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == localization.ListingId);
                var max = visible.Single(control => control is TextBox or ComboBox && AutomationProperties.GetName(control) == localization.ListingDependencyMax);
                var stated = visible.OfType<TextBlock>().Single(text => text.Text == "In the release: optional");
                var narrows = visible.OfType<TextBlock>().Single(text => text.Text == localization.StewardAmendNarrows);
                return Task.FromResult((Id: Left(id), Stated: Left(stated), Max: Left(max), Narrows: Left(narrows), IdWidth: id.Bounds.Width));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(lefts.Id, lefts.Stated);
        Assert.Equal(lefts.Max, lefts.Narrows);
        Assert.True(lefts.Max > lefts.Id);
        Assert.Equal(220, lefts.IdWidth);
    }

    [Fact]
    public async Task ReviewPage_ShowsThePullRequestInPlaceOfTheTabs_WithItsDocumentAndFiles()
    {
        const string Listing = "spec_version = 1\nid = \"MyMod\"\nname = \"My Mod\"\nabstract = \"Adds a thing.\"\ndescription = \"It adds a thing.\"\n";
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        var pull = FakePullRequestReviews.Review(5, headRepository: "alice/content-index") with { Title = "Add My Mod", Verdict = "Validated, and ownership is not verified." };
        _reviews.Reviews[("KSAModding/content-index", 5)] = pull with
        {
            Files = [FakePullRequestReviews.File(pull, "listings/MyMod.toml", "@@ -0,0 +1,2 @@\n+id = \"MyMod\""), FakePullRequestReviews.File(pull, "packs/my-pack/icon.png", null)],
            Documents =
            [
                new PullRequestDocument("listings/MyMod.toml", StewardQueueKind.Listing, Listing, harness.Services.ListingFormat.Read(Listing), null, new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: "alice/MyMod")),
                new PullRequestDocument("listings/Broken.toml", StewardQueueKind.Listing, "name = ", null, "Expected a value.", null),
            ],
        };
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.Queue.Items[0].OpenCommand.ExecuteAsync(null);

        var texts = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new StewardPageView(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text);
                var markdown = window.GetVisualDescendants().OfType<MarkdownView>().Where(view => view.IsEffectivelyVisible).Select(view => view.Markdown);
                return Task.FromResult(shown.Concat(markdown).ToList());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("content-index #5", texts);
        Assert.Contains("Add My Mod", texts);
        Assert.Contains("from alice/content-index", texts);
        Assert.Contains("Head commit 0123456", texts);
        Assert.Contains("validate: passed", texts);
        Assert.Contains(harness.Localization.StewardReviewRunChecks, texts);
        Assert.Contains("Validated, and ownership is not verified.", texts);
        Assert.Contains("My Mod", texts);
        Assert.Contains("Adds a thing.", texts);
        Assert.Contains("It adds a thing.", texts);
        Assert.Contains("alice owns alice/MyMod.", texts);
        Assert.Contains("The document does not parse: Expected a value.", texts);
        Assert.Contains("Changed files (2)", texts);
        Assert.Contains("+id = \"MyMod\"", texts);
        Assert.Contains(harness.Localization.StewardReviewNoPatch, texts);
        Assert.DoesNotContain(harness.Localization.StewardQueueHint, texts);
    }

    [Fact]
    public async Task IndexStatusModal_NamesTheChangeAndOpensThePullRequestOnAClick()
    {
        _editor.Check = _ => new IndexStatusCheck(null, [new IndexStatusPullRequest(4, new Uri("https://github.com/KSAModding/content-index/pull/4"), "Dispute Other", "bob")], ["alice"], IsOwner: false);
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.OpenContentAsync(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools"));
        viewModel.DelistContentCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();
        viewModel.StewardChange.Reason = "The author asked for it.";

        var texts = await HeadlessApp.RunAsync(harness, async () =>
        {
            var modal = new IndexStatusModal();
            var window = new Window { Width = 1280, Height = 832, Content = modal, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var open = modal.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && (button.Content as TextBlock)?.Text == harness.Localization.ListingPublish);
                var point = open.TranslatePoint(new Point(open.Bounds.Width / 2, open.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                await viewModel.StewardChange.WhenDoneAsync();
                window.UpdateLayout();
                shown.AddRange(modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text));
                return shown;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("Delist MeasureTools", texts);
        Assert.Contains(harness.Localization.StewardDelistEffect, texts);
        Assert.Contains(harness.Localization.StewardPullRequestExplanation, texts);
        Assert.Contains("Pull request #4 also changes index-status.toml. The one that merges second will have a conflict.", texts);
        Assert.Contains("The pull request mentions @alice, so the owner is told.", texts);
        Assert.Contains("Pull request #1 is open. It waits for a steward to merge it.", texts);
        Assert.Single(_editor.Opened);
    }

    [Fact]
    public async Task ReviewPage_ShowsTheActions_WithMergeOffAndWhy()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        _reviews.Reviews[("KSAModding/content-index", 5)] = FakePullRequestReviews.Review(5) with { Validate = new ValidateStatus(ValidateState.Pending), Author = "octocat" };
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.Queue.Items[0].OpenCommand.ExecuteAsync(null);

        var (texts, buttons, merge) = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new StewardPageView(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var visible = window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
                var mergeButton = visible.Single(button => (button.Content as TextBlock)?.Text == harness.Localization.StewardActionMerge);
                return Task.FromResult((shown, visible.Select(button => button.Content as string).ToList(), mergeButton.IsEffectivelyEnabled));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.False(merge);
        Assert.Contains(harness.Localization.StewardActionApprove, buttons);
        Assert.Contains(harness.Localization.StewardActionRequestChanges, buttons);
        Assert.Contains(harness.Localization.StewardActionComment, buttons);
        Assert.Contains(harness.Localization.StewardActionClose, buttons);
        Assert.Contains("Merge is off until validate passed on the head commit. Merge is off until the checks left their verdict.", texts);
        Assert.Contains(harness.Localization.StewardActionOwnWarning, texts);
    }

    [Fact]
    public async Task PullRequestActionModal_NamesThePullRequest_AndADoubleClickSendsOnce()
    {
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        _reviews.Reviews[("KSAModding/content-index", 5)] = FakePullRequestReviews.Review(5) with { Title = "Add My Mod" };
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.Queue.Items[0].OpenCommand.ExecuteAsync(null);
        viewModel.StewardPage.Review!.CommentCommand.Execute(null);
        viewModel.StewardAction!.Text = "Is MyMod the right id?";

        var texts = await HeadlessApp.RunAsync(harness, async () =>
        {
            var modal = new PullRequestActionModal();
            var window = new Window { Width = 1280, Height = 832, Content = modal, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var send = modal.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && (button.Content as TextBlock)?.Text == harness.Localization.StewardActionComment);
                var point = send.TranslatePoint(new Point(send.Bounds.Width / 2, send.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                await viewModel.StewardAction.WhenDoneAsync();
                window.UpdateLayout();
                shown.AddRange(modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text));
                return shown;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("Comment on KSAModding/content-index #5", texts);
        Assert.Contains("Add My Mod", texts);
        Assert.Contains("Borea sends your text as a review of commit 0123456.", texts);
        Assert.Contains(harness.Localization.StewardActionReviewed, texts);
        Assert.Equal(("Comment", "Is MyMod the right id?"), (Assert.Single(_actions.Sent).Action, _actions.Sent[0].Text));
    }

    private async Task<ViewModelHarness> CreateAsync()
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue, watcherIssues: _watcher, pullRequestReviews: _reviews, pullRequestActions: _actions, indexReports: _reports, releaseAmendments: _amendments);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
