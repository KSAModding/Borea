using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Reports tab of the Steward page: the open takedown and id dispute reports of content-index with the sections of their forms,
/// or the reports closed lately. A report sets a state with the status actions, and a steward of content-index comments on it, closes it
/// and reopens it in Borea when the Borea App has the Issues permission. Without it, a steward answers on GitHub.
/// </summary>
public sealed partial class StewardReportsTab : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _load = Task.CompletedTask;
    private Task _owners = Task.CompletedTask;
    private IReadOnlyList<IndexReport> _shown = [];
    private HashSet<string> _owned = new(StringComparer.OrdinalIgnoreCase);
    private string? _ownedLogin;
    private int _ownerLookup;
    private int _issuesLookup;
    private string? _issuesLogin;
    private string? _writableLogin;
    private string? _refusedLogin;
    private readonly Dictionary<int, DateTimeOffset> _reopened = [];

    public StewardReportsTab(MainViewModel owner)
    {
        _owner = owner;
    }

    public ObservableCollection<StewardReport> Reports { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    public bool IsEmpty => IsLoaded && !IsLoading && Error is null && Reports.Count == 0;

    /// <summary>The tab shows the reports closed lately in place of the open ones, so a steward can reopen one on new evidence.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _showClosed;

    public string EmptyText => ShowClosed ? _owner.Localization.StewardReportsClosedEmpty : _owner.Localization.StewardReportsEmpty;

    /// <summary>What the tab shows, and where the signed-in account answers a report.</summary>
    public string HintText =>
        _owner.Localization.StewardReportsHint + " " + (CanWriteIssues ? _owner.Localization.StewardReportsWriteHint : _owner.Localization.StewardReportsGitHubHint);

    /// <summary>
    /// A steward of content-index comments on, closes and reopens a report in Borea when GitHub says that the Borea App has the Issues permission.
    /// Each read of the tab asks GitHub again, because an org owner can give the App the permission meanwhile.
    /// When GitHub refuses an action all the same, the steward answers on GitHub until another account signs in.
    /// </summary>
    public bool CanWriteIssues =>
        _owner.CanEditIndexStatus
        && _owner.GitHubLogin is { } login
        && string.Equals(_writableLogin, login, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(_refusedLogin, login, StringComparison.OrdinalIgnoreCase);

    /// <summary>Waits for the read and for the owner and permission checks that follow it.</summary>
    internal Task WhenLoadedAsync() => _load;

    /// <summary>Waits for the owner and permission checks that a change of the account started.</summary>
    internal Task WhenOwnersCheckedAsync() => _owners;

    /// <summary>Reads the reports again. A second call during a read joins it.</summary>
    internal Task RefreshAsync() => IsLoading ? _load : _load = LoadAsync();

    /// <summary>Builds the texts of the tab again in the language that is now selected.</summary>
    internal void RefreshText()
    {
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(HintText));
        Show(_shown);
    }

    /// <summary>GitHub refused a comment or a state change, so the rows answer on GitHub until another account signs in.</summary>
    internal void OnIssuesRefused()
    {
        _refusedLogin = _owner.GitHubLogin;
        OnRightsChanged();
    }

    /// <summary>
    /// The steward reopened the report. The search behind the closed list can lag some seconds behind, so the list leaves out the report
    /// while it is closed only as it was when the steward reopened it. A later close shows again.
    /// </summary>
    internal void OnReopened(IndexReport report)
    {
        _reopened[report.Number] = report.Closed ?? DateTimeOffset.MaxValue;
        Show(_shown);
    }

    partial void OnShowClosedChanged(bool value) => _ = RefreshAsync();

    /// <summary>
    /// Builds the rows again for the account that is now signed in, because each row says whether that account filed it,
    /// and asks again which ids the account owns when the account changed. A read that runs asks at its end.
    /// </summary>
    internal void RefreshRights()
    {
        if (!string.Equals(_refusedLogin, _owner.GitHubLogin, StringComparison.OrdinalIgnoreCase))
            _refusedLogin = null;

        OnRightsChanged();
        Show(_shown);
        if (IsLoaded && !IsLoading && !string.Equals(_ownedLogin, OwnerLogin(), StringComparison.OrdinalIgnoreCase))
            _owners = Task.WhenAll(CheckOwnersAsync(), CheckIssuesAsync());
    }

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        var issues = CheckIssuesAsync();
        try
        {
            // the index names the content page of each id, and a filter that changes during a read joins it, so the read repeats until it matches
            IReadOnlyList<IndexReport> reports;
            IndexReportFilter filter;
            do
            {
                filter = Filter;
                var read = services.IndexReports.ListAsync(filter);
                await Task.WhenAll(read, _owner.EnsureDiscoverLoadedAsync());
                reports = await read;
            }
            while (filter != Filter);

            Show(reports);
            IsLoaded = true;
        }
        catch (StewardException exception)
        {
            Show([]);
            Error = _owner.StewardIssuesFailureText(IndexReport.Repository, exception);
        }
        finally
        {
            IsLoading = false;
        }

        // an account that changed during the read is asked for again
        var checkedIssues = string.Equals(_issuesLogin, OwnerLogin(), StringComparison.OrdinalIgnoreCase) ? issues : CheckIssuesAsync();
        await (_owners = Task.WhenAll(CheckOwnersAsync(), checkedIssues));
    }

    /// <summary>
    /// Asks GitHub whether the Borea App may write the issues of content-index, so the rows of a steward show the report actions
    /// in place of "Answer on GitHub". A check that fails counts as no.
    /// </summary>
    private async Task CheckIssuesAsync()
    {
        var lookup = ++_issuesLookup;
        var login = OwnerLogin();
        _issuesLogin = login;
        var writable = login is not null && _owner.Services is { } services && await services.ReportActions.CanActAsync();
        if (lookup != _issuesLookup)
            return;

        _writableLogin = writable ? login : null;
        OnRightsChanged();
    }

    private void OnRightsChanged()
    {
        OnPropertyChanged(nameof(CanWriteIssues));
        OnPropertyChanged(nameof(HintText));
    }

    /// <summary>
    /// Asks which ids of the shown reports the signed-in steward owns, so a row warns before the steward answers it on GitHub.
    /// The confirmation of a status action checks the owner again, so a lookup that fails only leaves the warning out.
    /// </summary>
    private async Task CheckOwnersAsync()
    {
        var lookup = ++_ownerLookup;
        var login = OwnerLogin();
        if (!string.Equals(login, _ownedLogin, StringComparison.OrdinalIgnoreCase))
            _owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _ownedLogin = login;
        ShowOwners();

        var ids = Reports.Where(row => row.CanOpenContent).Select(row => row.Report.Id!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (login is null || ids.Count == 0 || _owner.Services is not { } services)
            return;

        IReadOnlyList<string> owned;
        try
        {
            owned = await services.IndexStatusEditor.OwnedAsync(ids);
        }
        catch (StewardException)
        {
            return;
        }

        if (lookup != _ownerLookup)
            return;

        _owned = new HashSet<string>(owned, StringComparer.OrdinalIgnoreCase);
        ShowOwners();
    }

    private IndexReportFilter Filter => ShowClosed ? IndexReportFilter.RecentlyClosed : IndexReportFilter.Open;

    /// <summary>The login that the owner lookup answers for, which is null unless a steward of content-index is signed in.</summary>
    private string? OwnerLogin() => _owner.CanEditIndexStatus ? _owner.GitHubLogin : null;

    private bool IsReopened(IndexReport report) =>
        report.IsClosed && _reopened.TryGetValue(report.Number, out var closed) && (report.Closed ?? DateTimeOffset.MinValue) <= closed;

    private bool IsOwned(IndexReport report) => report.Id is { } id && _owned.Contains(id);

    private void ShowOwners()
    {
        foreach (var row in Reports)
            row.IsOwner = IsOwned(row.Report);
    }

    private void Show(IReadOnlyList<IndexReport> reports)
    {
        _shown = reports;
        Reports.Clear();
        foreach (var report in reports.Where(report => !IsReopened(report)))
            Reports.Add(new StewardReport(_owner, report) { IsOwner = IsOwned(report) });

        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>
/// One report on the Reports tab. Its status actions open the confirmation of a status change that names the report, and its report actions
/// the confirmation of a comment, a close or a reopen. "Answer on GitHub" opens the issue when Borea cannot write to it.
/// </summary>
public sealed partial class StewardReport : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly DiscoverItem? _listing;
    private readonly PackItem? _pack;

    public StewardReport(MainViewModel owner, IndexReport report)
    {
        _owner = owner;
        Report = report;
        (_listing, _pack) = report.Id is { } id ? owner.FindIndexContent(id) : (null, null);
        Sections = SectionsOf(report);
        MissingTexts = report.Missing.Select(owner.Localization.FormatStewardReportMissing).ToList();
        IsReporter = owner.IsReporter(report);
    }

    public IndexReport Report { get; }

    public string NumberText => MainViewModel.RepositoryNumberText(IndexReport.Repository, Report.Number);

    public string KindText => Report.Kind == IndexReportKind.Takedown ? _owner.Localization.StewardReportTakedown : _owner.Localization.StewardReportDispute;

    public string ByText => _owner.Localization.FormatStewardReportBy(Report.Author ?? string.Empty, _owner.AgeText(Report.Created));

    public bool IsOpen => !Report.IsClosed;

    /// <summary>When and why the report was closed, or null while it is open.</summary>
    public string? ClosedText
    {
        get
        {
            if (!Report.IsClosed)
                return null;

            var age = Report.Closed is { } closed ? _owner.AgeText(closed) : string.Empty;
            return Report.CloseReason switch
            {
                IndexReportCloseReason.Completed => _owner.Localization.FormatStewardReportClosedCompleted(age),
                IndexReportCloseReason.NotPlanned => _owner.Localization.FormatStewardReportClosedNotPlanned(age),
                _ => _owner.Localization.FormatStewardReportClosed(age),
            };
        }
    }

    /// <summary>The name of the listing or pack, which opens its content page.</summary>
    public string? ContentName => _listing?.Name ?? _pack?.Name;

    public bool CanOpenContent => ContentName is not null;

    /// <summary>The id is not in the index that Borea read, for example because it is delisted already, so only the id shows.</summary>
    public bool IsIdUnknown => Report.Id is not null && !CanOpenContent;

    public string? VersionText => Report.Version is { } version ? _owner.Localization.FormatStewardVersion(version) : null;

    /// <summary>Says that the listing id field holds a text that names no id, which then shows as it was typed.</summary>
    public string? UnreadableIdText => Report is { Id: null, ListingText: { } text } ? _owner.Localization.FormatStewardReportUnreadableId(text) : null;

    public bool HasId => Report.Id is not null;

    /// <summary>The status actions show on an open report that names an id. A closed report is reopened first.</summary>
    public bool CanChangeStatus => HasId && IsOpen;

    public bool HasVersion => Report.Version is not null;

    /// <summary>A mod author asks to take their mod out of the pack that the report names (RFC 0080).</summary>
    public bool IsLeavePack => Report.IsLeavePack;

    public IReadOnlyList<StewardReportSection> Sections { get; }

    /// <summary>One line for each section that the form requires and the body lacks, because somebody edited it by hand.</summary>
    public IReadOnlyList<string> MissingTexts { get; }

    public bool CanOpenForums => Report.ForumsUrl is not null;

    /// <summary>The signed-in steward filed the report, and POLICY.md asks a party to leave the case to another steward.</summary>
    public bool IsReporter { get; }

    /// <summary>The signed-in steward owns the listing or pack of the report, which the tab looks up after the read.</summary>
    [ObservableProperty]
    private bool _isOwner;

    [RelayCommand]
    private void Answer() => SetError(_owner.TryOpenWithSystem(Report.Url.AbsoluteUri));

    [RelayCommand]
    private void Comment() => _owner.BeginReportAction(this, ReportAction.Comment);

    [RelayCommand]
    private void CloseCompleted() => _owner.BeginReportAction(this, ReportAction.CloseCompleted);

    [RelayCommand]
    private void CloseNotPlanned() => _owner.BeginReportAction(this, ReportAction.CloseNotPlanned);

    [RelayCommand]
    private void Reopen() => _owner.BeginReportAction(this, ReportAction.Reopen);

    [RelayCommand]
    private void OpenForums()
    {
        if (Report.ForumsUrl is { } url)
            SetError(_owner.TryOpenWithSystem(url.AbsoluteUri));
    }

    /// <summary>Looks the row up again, because an index check since the read builds new rows.</summary>
    [RelayCommand]
    private Task OpenContentAsync()
    {
        var (listing, pack) = _owner.FindIndexContent(Report.Id ?? string.Empty);
        return (listing ?? _listing) is { } row ? _owner.OpenContentAsync(row)
            : (pack ?? _pack) is { } found ? _owner.OpenPackAsync(found)
            : Task.CompletedTask;
    }

    [RelayCommand]
    private void Dispute()
    {
        if (Report is { IsClosed: false, Id: { } id })
            _owner.BeginIndexStatusChange(IndexStatusChange.Dispute(id, string.Empty), Report);
    }

    [RelayCommand]
    private void Delist()
    {
        if (Report is { IsClosed: false, Id: { } id })
            _owner.BeginIndexStatusChange(IndexStatusChange.Delist(id, string.Empty), Report);
    }

    [RelayCommand]
    private void LeavePack()
    {
        if (Report is { IsLeavePack: true, IsClosed: false, Id: not null })
            _owner.BeginPackLeave(Report);
    }

    [RelayCommand]
    private void Retract()
    {
        if (Report is { IsClosed: false, Id: { } id, Version: { } version })
            _owner.BeginIndexStatusChange(IndexStatusChange.Retract(id, version, string.Empty), Report);
    }

    private void SetError(string? error) => _owner.StewardPage.Reports.Error = error ?? _owner.StewardPage.Reports.Error;

    private List<StewardReportSection> SectionsOf(IndexReport report)
    {
        var text = _owner.Localization;
        (string Label, string? Text)[] sections = report.Kind == IndexReportKind.Takedown
            ? [(text.StewardReportGround, report.Ground), (text.StewardReportWhatIsWrong, report.WhatIsWrong), (text.StewardReportReporter, report.Reporter)]
            : [(text.StewardReportDisputed, report.Disputed), (text.StewardReportForums, report.ForumsThread), (text.StewardReportClaim, report.Claim), (text.StewardReportOtherParty, report.OtherParty)];
        var shown = new List<StewardReportSection>();
        foreach (var (label, value) in sections)
        {
            if (value is not null)
                shown.Add(new StewardReportSection(label, value));
        }

        return shown;
    }
}

/// <param name="Label">The name of the section in the language that Borea shows.</param>
/// <param name="Text">The answer as the reporter wrote it.</param>
public sealed record StewardReportSection(string Label, string Text);
