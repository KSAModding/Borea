using System.Globalization;
using System.Threading.Tasks;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The confirmation of one steward action on a report of the Reports tab. It names the report, needs a comment, and sends once.
/// When GitHub refuses the action, because the Borea App has no Issues permission, it offers "Answer on GitHub" and keeps the text.
/// </summary>
public sealed partial class ReportActionDialog : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _run = Task.CompletedTask;

    public ReportActionDialog(MainViewModel owner, StewardReport row, ReportAction action)
    {
        _owner = owner;
        Report = row.Report;
        Action = action;
        ReporterWarning = row.IsReporter ? owner.Localization.StewardReporterWarning : null;
        OwnerWarning = row.IsOwner ? owner.Localization.StewardOwnerWarning : null;
    }

    /// <summary>The report as the tab showed it when the steward clicked.</summary>
    public IndexReport Report { get; }

    public ReportAction Action { get; }

    public string Title
    {
        get
        {
            var target = $"{IndexReport.Repository} #{Report.Number.ToString(CultureInfo.InvariantCulture)}";
            return Action switch
            {
                ReportAction.Comment => _owner.Localization.FormatStewardActionCommentTitle(target),
                ReportAction.CloseCompleted => _owner.Localization.FormatStewardReportCloseCompletedTitle(target),
                ReportAction.CloseNotPlanned => _owner.Localization.FormatStewardReportCloseNotPlannedTitle(target),
                _ => _owner.Localization.FormatStewardReportReopenTitle(target),
            };
        }
    }

    public string ReportTitle => Report.Title;

    public string Effect => Action switch
    {
        ReportAction.Comment => _owner.Localization.StewardReportCommentEffect,
        ReportAction.CloseCompleted => _owner.Localization.StewardReportCloseCompletedEffect,
        ReportAction.CloseNotPlanned => _owner.Localization.StewardReportCloseNotPlannedEffect,
        _ => _owner.Localization.StewardReportReopenEffect,
    };

    /// <summary>The signed-in steward filed the report, or null.</summary>
    public string? ReporterWarning { get; }

    /// <summary>The signed-in steward owns the listing or pack of the report, or null.</summary>
    public string? OwnerWarning { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _text = string.Empty;

    /// <summary>While the action is being sent, the dialog cannot close, so the steward sees how it ended.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isSending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(DoneText))]
    [NotifyPropertyChangedFor(nameof(IsSendShown))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isDone;

    /// <summary>The report is no longer open or closed as the tab showed it, so nothing more is sent from this dialog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isStale;

    /// <summary>GitHub refused the action, so the steward answers on GitHub with the text that stays in the dialog.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(IsSendShown))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isRefused;

    [ObservableProperty]
    private string? _error;

    public bool CanSend => !IsSending && !IsDone && !IsStale && !IsRefused && Text.Trim().Length > 0;

    public bool CanClose => !IsSending;

    /// <summary>After a refusal of GitHub, "Answer on GitHub" takes the place of the send button.</summary>
    public bool IsSendShown => !IsDone && !IsRefused;

    public string SendText => Action switch
    {
        ReportAction.Comment => _owner.Localization.StewardActionComment,
        ReportAction.CloseCompleted => _owner.Localization.StewardReportCloseCompleted,
        ReportAction.CloseNotPlanned => _owner.Localization.StewardReportCloseNotPlanned,
        _ => _owner.Localization.StewardReportReopen,
    };

    public string? DoneText => !IsDone ? null : Action switch
    {
        ReportAction.Comment => _owner.Localization.StewardReportCommented,
        ReportAction.Reopen => _owner.Localization.StewardReportReopened,
        _ => _owner.Localization.StewardReportClosed,
    };

    internal Task WhenDoneAsync() => _run;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private Task SendAsync()
    {
        // a second click while the first one runs sends nothing
        if (!CanSend)
            return _run;

        return _run = RunAsync();
    }

    private async Task RunAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsSending = true;
        Error = null;
        try
        {
            var actions = services.ReportActions;
            var text = Text.Trim();
            await (Action switch
            {
                ReportAction.Comment => actions.CommentAsync(Report, text),
                ReportAction.CloseCompleted => actions.CloseAsync(Report, IndexReportCloseReason.Completed, text),
                ReportAction.CloseNotPlanned => actions.CloseAsync(Report, IndexReportCloseReason.NotPlanned, text),
                _ => actions.ReopenAsync(Report, text),
            });
            IsDone = true;
            if (Action == ReportAction.Reopen)
                _owner.StewardPage.Reports.OnReopened(Report);
            _ = _owner.StewardPage.Reports.RefreshAsync();
        }
        catch (ReportRefusedException exception)
        {
            // somebody else closed or reopened the report, so the steward decides again on the report as it is now
            Error = _owner.ReportRefusalText(exception.Refusal);
            IsStale = true;
            _ = _owner.StewardPage.Reports.RefreshAsync();
        }
        catch (StewardException exception) when (exception.Failure == StewardFailure.Forbidden)
        {
            Error = _owner.Localization.StewardReportNoIssuesPermission;
            IsRefused = true;
            _owner.StewardPage.Reports.OnIssuesRefused();
        }
        catch (StewardException exception)
        {
            Error = _owner.StewardReportErrorText(exception);
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void Answer() => Error = _owner.TryOpenWithSystem(Report.Url.AbsoluteUri) ?? Error;

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        if (CanClose)
            _owner.CloseReportAction(this);
    }
}

public enum ReportAction
{
    Comment,
    CloseCompleted,
    CloseNotPlanned,
    Reopen,
}
