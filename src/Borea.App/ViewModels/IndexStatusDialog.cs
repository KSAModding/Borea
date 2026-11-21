using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The confirmation of one change of index-status.toml. It names the repository, the file and what the state does,
/// checks the change against the base branch before anything is written, and opens the pull request once.
/// </summary>
public sealed partial class IndexStatusDialog : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly IndexStatusChange _change;
    private readonly IndexReport? _report;
    private Task _run = Task.CompletedTask;
    private IndexStatusRefusal? _refused;

    /// <param name="report">The report that the change answers, which <paramref name="change"/> names, or null.</param>
    public IndexStatusDialog(MainViewModel owner, IndexStatusChange change, IndexReport? report = null)
    {
        _owner = owner;
        _change = change;
        _report = report;
    }

    internal IndexStatusChange Change => _change with { Reason = Reason };

    public string Title => _change.Action switch
    {
        IndexStatusAction.Dispute => _owner.Localization.FormatStewardDisputeTitle(_change.Id),
        IndexStatusAction.Delist => _owner.Localization.FormatStewardDelistTitle(_change.Id),
        IndexStatusAction.Retract => _owner.Localization.FormatStewardRetractTitle(_change.Id, _change.Version ?? string.Empty),
        _ => _owner.Localization.FormatStewardLiftTitle(_change.Id),
    };

    public string Effect => _change.Action switch
    {
        IndexStatusAction.Dispute => _owner.Localization.StewardDisputeEffect,
        IndexStatusAction.Delist => _owner.Localization.StewardDelistEffect,
        IndexStatusAction.Retract => _owner.Localization.StewardRetractEffect,
        _ => _owner.Localization.FormatStewardLiftEffect(_owner.IndexStatusStateText(_change.State)),
    };

    public string ReasonHint => _change.Action == IndexStatusAction.Lift ? _owner.Localization.StewardLiftReasonHint : _owner.Localization.StewardReasonHint;

    /// <summary>The reason the steward types after the check, which checks everything but the reason.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(InvalidReasonText))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private string _reason = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private IndexStatusCheck? _check;

    /// <summary>Why the checks of content-index would refuse the change.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private string? _refusal;

    /// <summary>While the pull request is being opened, the dialog cannot close, so the steward sees how it ended.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isOpening;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpen))]
    [NotifyPropertyChangedFor(nameof(IsOpened))]
    [NotifyPropertyChangedFor(nameof(OpenedText))]
    [NotifyCanExecuteChangedFor(nameof(OpenPullRequestCommand))]
    private IndexStatusPullRequest? _opened;

    /// <summary>
    /// Warns about the open pull requests that change the file too and conflict with a new one. The steward's own pull request that
    /// the change goes onto conflicts with nothing, so the text names it apart.
    /// </summary>
    public string? OpenPullRequestsText
    {
        get
        {
            if (Check is not { } check)
                return null;
            if (check.OpenPullRequests is not { } open)
                return _owner.Localization.StewardOpenPullRequestsUnknown;

            var others = open.Where(pull => pull.Number != check.Reuses?.Number).ToList();
            var conflicts = others.Count == 0 ? null : _owner.Localization.FormatStewardOpenPullRequests(string.Join(", ", others.Select(pull => "#" + pull.Number.ToString(CultureInfo.InvariantCulture))));
            if (check.Reuses is not { } own)
                return conflicts;

            var reuse = _owner.Localization.FormatStewardReusesPullRequest(own.Number.ToString(CultureInfo.InvariantCulture));
            return conflicts is null ? reuse : reuse + " " + conflicts;
        }
    }

    /// <summary>
    /// A steward who is a party to a takedown or a dispute leaves it to another steward, and a report against their own content
    /// makes them one. Without a report they delist or retract their own content on their own request (POLICY.md).
    /// </summary>
    public string? OwnerText => Check?.IsOwner != true ? null
        : _change.Report is not null || _change.State == IndexStatusEntry.Disputed ? _owner.Localization.StewardOwnerWarning
        : _owner.Localization.StewardOwnerOwnRequest;

    /// <summary>What the pull request does with the report that the change answers.</summary>
    public string? ReportText => _change.Report is { } number
        ? _change.ClosesReport
            ? _owner.Localization.FormatStewardReportCloses(number.ToString(CultureInfo.InvariantCulture))
            : _owner.Localization.FormatStewardReportStaysOpen(number.ToString(CultureInfo.InvariantCulture))
        : null;

    public string? ReporterText => _report is { } report && _owner.IsReporter(report) ? _owner.Localization.StewardReporterWarning : null;

    public string? MentionText => Check is { Owners.Count: > 0 } check
        ? _owner.Localization.FormatStewardMention(string.Join(", ", check.Owners.Select(login => "@" + login)))
        : null;

    public bool CanOpen => Check is not null && Refusal is null && !IsChecking && !IsOpening && Opened is null && IndexStatusChange.IsValidReason(Reason);

    /// <summary>Says why a typed reason cannot go into the file, such as a pasted tab or a second line. An empty reason only keeps the button off.</summary>
    public string? InvalidReasonText =>
        Reason.Trim().Length > 0 && !IndexStatusChange.IsValidReason(Reason) ? _owner.Localization.StewardRefusedInvalidReason : null;

    public bool CanClose => !IsOpening;

    /// <summary>A check that failed can run again. After a failed pull request, the button that opens it stays.</summary>
    public bool CanRetry => Error is not null && Check is null && !IsChecking;

    public bool IsOpened => Opened is not null;

    public string? OpenedText => Opened switch
    {
        null => null,
        { IsReused: true } pull => _owner.Localization.FormatStewardAddedToPullRequest(pull.Number.ToString(CultureInfo.InvariantCulture)),
        { } pull => _owner.Localization.FormatStewardOpened(pull.Number.ToString(CultureInfo.InvariantCulture)),
    };

    internal Task WhenDoneAsync() => _run;

    internal void Start() => _run = CheckAsync();

    /// <summary>A refused reason stops being the refusal once the steward changes it.</summary>
    partial void OnReasonChanged(string value)
    {
        if (_refused == IndexStatusRefusal.InvalidReason)
            SetRefusal(null);
    }

    private void SetRefusal(IndexStatusRefusal? refusal)
    {
        _refused = refusal;
        Refusal = refusal is { } value ? _owner.IndexStatusRefusalText(value, _change.Id, _change.Version) : null;
    }

    partial void OnCheckChanged(IndexStatusCheck? value)
    {
        OnPropertyChanged(nameof(OpenPullRequestsText));
        OnPropertyChanged(nameof(OwnerText));
        OnPropertyChanged(nameof(MentionText));
    }

    private async Task CheckAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsChecking = true;
        Error = null;
        try
        {
            var check = await services.IndexStatusEditor.CheckAsync(Change);
            SetRefusal(check.Refusal);
            Check = check;
        }
        catch (StewardException exception)
        {
            Error = _owner.StewardErrorText(exception);
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenPullRequestAsync()
    {
        // a second click while the first one runs sends nothing
        if (!CanOpen)
            return _run;

        return _run = OpenAsync();
    }

    private async Task OpenAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsOpening = true;
        Error = null;
        try
        {
            Opened = await services.IndexStatusEditor.OpenAsync(Change);
            _owner.OnIndexStatusPullRequestOpened();
        }
        catch (IndexStatusRefusedException exception)
        {
            SetRefusal(exception.Refusal);
        }
        catch (StewardException exception)
        {
            Error = _owner.StewardErrorText(exception);
        }
        finally
        {
            IsOpening = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync() => CanRetry ? _run = CheckAsync() : _run;

    [RelayCommand]
    private void ShowPullRequest()
    {
        if (Opened is { } pull)
            Error = _owner.TryOpenWithSystem(pull.Url.AbsoluteUri);
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        if (CanClose)
            _owner.CloseIndexStatusChange(this);
    }
}
