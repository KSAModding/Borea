using Borea.App.ViewModels;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

public sealed class StewardReportActionsViewModelTests
{
    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();
    private readonly FakeStewardQueue _queue = new();
    private readonly FakeIndexReports _reports = new();
    private readonly FakeReportActions _actions = new();

    [Fact]
    public async Task Comment_NeedsAText_SendsItOnce_AndReadsTheReportsAgain()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        tab.Reports[0].CommentCommand.Execute(null);
        var dialog = viewModel.StewardReportAction!;
        var empty = dialog.CanSend;
        dialog.Text = "  Please answer here, @alice.  ";
        var send = dialog.SendCommand.ExecuteAsync(null);
        var second = dialog.SendCommand.ExecuteAsync(null);
        await Task.WhenAll(send, second);
        await tab.WhenLoadedAsync();

        Assert.False(empty);
        Assert.True(viewModel.IsStewardReportActionOpen);
        Assert.Equal(("Comment on KSAModding/content-index #10", "[Takedown] MeasureTools"), (dialog.Title, dialog.ReportTitle));
        Assert.Equal(harness.Localization.StewardReportCommentEffect, dialog.Effect);
        Assert.Equal([("Comment", 10, "Please answer here, @alice.")], _actions.Sent);
        Assert.Equal((true, false, harness.Localization.StewardReportCommented), (dialog.IsDone, dialog.CanSend, dialog.DoneText));
        Assert.Equal(2, _reports.Reads);

        dialog.CloseCommand.Execute(null);
        Assert.Null(viewModel.StewardReportAction);
    }

    [Theory]
    [InlineData(ReportAction.CloseCompleted, "Close KSAModding/content-index #10 as completed", "Close Completed")]
    [InlineData(ReportAction.CloseNotPlanned, "Close KSAModding/content-index #10 as not planned", "Close NotPlanned")]
    public async Task Close_SendsTheReasonWithTheComment(ReportAction action, string title, string sent)
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        (action == ReportAction.CloseCompleted ? report.CloseCompletedCommand : report.CloseNotPlannedCommand).Execute(null);
        var dialog = viewModel.StewardReportAction!;
        dialog.Text = "The takedown does not hold.";
        await dialog.SendCommand.ExecuteAsync(null);

        Assert.Equal(title, dialog.Title);
        Assert.Equal([(sent, 10, "The takedown does not hold.")], _actions.Sent);
        Assert.Equal(harness.Localization.StewardReportClosed, dialog.DoneText);

        // an open report has no reopen
        viewModel.CloseReportAction(dialog);
        report.ReopenCommand.Execute(null);
        Assert.Null(viewModel.StewardReportAction);
    }

    [Fact]
    public async Task ClosedReports_ShowWhyTheyClosed_AndReopenOnNewEvidence()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _reports.Closed.Add(FakeIndexReports.ClosedReport(FakeIndexReports.Dispute(11, "MeasureTools"), IndexReportCloseReason.NotPlanned));
        _reports.Closed.Add(FakeIndexReports.ClosedReport(FakeIndexReports.Takedown(12, "MeasureTools"), null));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        tab.ShowClosed = true;
        await tab.WhenLoadedAsync();
        var shown = tab.Reports.Select(report => report.Report.Number).ToList();
        var closed = tab.Reports[0];
        closed.CloseCompletedCommand.Execute(null);
        closed.DelistCommand.Execute(null);
        var closeOpened = (viewModel.StewardReportAction, viewModel.StewardChange);
        closed.ReopenCommand.Execute(null);
        var dialog = viewModel.StewardReportAction!;
        dialog.Text = "The author posted the original thread.";
        await dialog.SendCommand.ExecuteAsync(null);
        await tab.WhenLoadedAsync();

        // the search still finds #11 closed, as it can for some seconds after the reopen, so only a later close shows it again
        var afterReopen = tab.Reports.Select(report => report.Report.Number).ToList();
        _reports.Closed[0] = _reports.Closed[0] with { Closed = DateTimeOffset.UtcNow };
        await tab.RefreshAsync();

        Assert.Equal([IndexReportFilter.Open, IndexReportFilter.RecentlyClosed, IndexReportFilter.RecentlyClosed, IndexReportFilter.RecentlyClosed], _reports.Filters);
        Assert.Equal([11, 12], shown);
        Assert.Equal([12], afterReopen);
        Assert.Equal([11, 12], tab.Reports.Select(report => report.Report.Number));
        Assert.StartsWith("closed as not planned, ", closed.ClosedText, StringComparison.Ordinal);
        Assert.StartsWith("closed, ", tab.Reports[1].ClosedText, StringComparison.Ordinal);
        Assert.Equal((false, false), (closed.IsOpen, closed.CanChangeStatus));
        Assert.Equal(((ReportActionDialog?)null, (IndexStatusDialog?)null), closeOpened);
        Assert.Equal("Reopen KSAModding/content-index #11", dialog.Title);
        Assert.Equal([("Reopen", 11, "The author posted the original thread.")], _actions.Sent);
        Assert.Equal(harness.Localization.StewardReportReopened, dialog.DoneText);
        Assert.Equal(harness.Localization.StewardReportsClosedEmpty, tab.EmptyText);
    }

    [Fact]
    public async Task GitHubRefusesTheAction_FallsBackToAnswerOnGitHub_UntilAnotherAccountSignsIn()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _actions.Failures.Enqueue(new StewardException(StewardFailure.Forbidden, "Resource not accessible by integration"));

        // built without a synchronization context, so the session and the role refresh the page inline and in order
        using var harness = await Task.Run(() => CreateAsync());
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        var tab = await OpenAsync(viewModel);
        var before = tab.CanWriteIssues;

        tab.Reports[0].CloseNotPlannedCommand.Execute(null);
        var dialog = viewModel.StewardReportAction!;
        dialog.Text = "The takedown does not hold.";
        await dialog.SendCommand.ExecuteAsync(null);
        dialog.AnswerCommand.Execute(null);
        viewModel.CloseReportAction(dialog);
        tab.Reports[0].CommentCommand.Execute(null);
        var afterRefusal = (tab.CanWriteIssues, viewModel.StewardReportAction);
        await viewModel.StewardPage.RefreshTabCommand.ExecuteAsync(null);
        var afterRead = tab.CanWriteIssues;
        viewModel.SignOutOfGitHubCommand.Execute(null);
        _session.SignInDirectly();
        await viewModel.WhenStewardRoleCheckedAsync();
        await tab.WhenOwnersCheckedAsync();

        Assert.True(before);
        Assert.Equal(harness.Localization.StewardReportNoIssuesPermission, dialog.Error);
        Assert.Equal((true, false, false, false), (dialog.IsRefused, dialog.CanSend, dialog.IsSendShown, dialog.IsDone));
        Assert.Equal("The takedown does not hold.", dialog.Text);
        Assert.Equal(["https://github.com/KSAModding/content-index/issues/10"], opened);
        Assert.Equal((false, (ReportActionDialog?)null), afterRefusal);
        Assert.False(afterRead);
        Assert.True(tab.CanWriteIssues);
        Assert.Single(_actions.Sent);
    }

    [Fact]
    public async Task AppWithoutTheIssuesPermission_AnswersOnGitHub_UntilARead_FindsThePermission()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _actions.CanAct = false;
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        var without = (tab.CanWriteIssues, tab.HintText);
        tab.Reports[0].CommentCommand.Execute(null);
        var opened = viewModel.StewardReportAction;
        _actions.CanAct = true;
        await viewModel.StewardPage.RefreshTabCommand.ExecuteAsync(null);

        var text = harness.Localization;
        Assert.Equal((false, $"{text.StewardReportsHint} {text.StewardReportsGitHubHint}"), without);
        Assert.Null(opened);
        Assert.Equal((true, $"{text.StewardReportsHint} {text.StewardReportsWriteHint}"), (tab.CanWriteIssues, tab.HintText));
        Assert.Equal(2, _actions.Checks);
        Assert.Empty(_actions.Sent);
    }

    [Fact]
    public async Task ReportClosedMeanwhile_SaysSo_AndReadsTheReportsAgain()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _actions.Failures.Enqueue(new ReportRefusedException(ReportRefusal.Closed));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        tab.Reports[0].CloseCompletedCommand.Execute(null);
        var dialog = viewModel.StewardReportAction!;
        dialog.Text = "Done.";
        await dialog.SendCommand.ExecuteAsync(null);
        await tab.WhenLoadedAsync();

        Assert.Equal(harness.Localization.StewardReportRefusedClosed, dialog.Error);
        Assert.Equal((true, false), (dialog.IsStale, dialog.CanSend));
        Assert.Equal(2, _reports.Reads);
        Assert.True(tab.CanWriteIssues);
    }

    [Theory]
    [InlineData(StewardFailure.NetworkError, "Cannot reach GitHub. Try again.")]
    [InlineData(StewardFailure.NotFound, "GitHub did not find this report in KSAModding/content-index. Somebody moved or deleted it.")]
    public async Task OtherFailure_SaysWhy_AndTheStewardCanTryAgain(StewardFailure failure, string text)
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _actions.Failures.Enqueue(new StewardException(failure));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        tab.Reports[0].CommentCommand.Execute(null);
        var dialog = viewModel.StewardReportAction!;
        dialog.Text = "Please answer.";
        await dialog.SendCommand.ExecuteAsync(null);
        var error = dialog.Error;
        await dialog.SendCommand.ExecuteAsync(null);

        Assert.Equal(text, error);
        Assert.Equal(2, _actions.Sent.Count);
        Assert.True(dialog.IsDone);
        Assert.Null(dialog.Error);
    }

    [Fact]
    public async Task WhileSending_TheDialogStaysOpen()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        _actions.Hold = new TaskCompletionSource();
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);
        tab.Reports[0].CommentCommand.Execute(null);
        var dialog = viewModel.StewardReportAction!;
        dialog.Text = "Please answer.";

        var send = dialog.SendCommand.ExecuteAsync(null);
        dialog.CloseCommand.Execute(null);
        var sending = (dialog.IsSending, dialog.CanClose, viewModel.StewardReportAction);
        _actions.Hold.SetResult();
        await send;

        Assert.Equal((true, false, dialog), sending);
        Assert.False(dialog.IsSending);
    }

    [Fact]
    public async Task ReportOfTheSteward_WarnsInTheDialog()
    {
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools", author: "octocat"));
        _reports.Reports.Add(FakeIndexReports.Takedown(11, "GoneMod"));
        _editor.Owned.Add("MeasureTools");
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        tab.Reports[0].CommentCommand.Execute(null);
        var own = viewModel.StewardReportAction!;
        viewModel.CloseReportAction(own);
        tab.Reports[1].CommentCommand.Execute(null);
        var other = viewModel.StewardReportAction!;

        Assert.Equal((harness.Localization.StewardReporterWarning, harness.Localization.StewardOwnerWarning), (own.ReporterWarning, own.OwnerWarning));
        Assert.Equal(((string?)null, (string?)null), (other.ReporterWarning, other.OwnerWarning));
    }

    [Fact]
    public async Task StewardOfTheReleasesOnly_AnswersOnGitHub()
    {
        _session.Bypass["KSAModding/content-index"] = "never";
        _reports.Reports.Add(FakeIndexReports.Takedown(10, "MeasureTools"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var report = Assert.Single((await OpenAsync(viewModel)).Reports);

        report.CommentCommand.Execute(null);
        report.CloseCompletedCommand.Execute(null);

        Assert.False(viewModel.StewardPage.Reports.CanWriteIssues);
        Assert.Null(viewModel.StewardReportAction);
    }

    private static async Task<StewardReportsTab> OpenAsync(MainViewModel viewModel)
    {
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.ShowReportsCommand.ExecuteAsync(null);
        await viewModel.StewardPage.Reports.WhenLoadedAsync();
        return viewModel.StewardPage.Reports;
    }

    private async Task<ViewModelHarness> CreateAsync()
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue, indexReports: _reports, reportActions: _actions);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
