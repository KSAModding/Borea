using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Steward page, opened from the GitHub account in Settings. Its Queue tab lists the pull requests that wait for a steward,
/// and each one opens its review in place of the tabs. Its Reports tab lists the takedown and id dispute reports.
/// Its Status tab lists the states of index-status.toml on the base branch with Lift on each, which is the only way back
/// for a delisted listing, because it has no content page. Its Watcher tab lists the issues of the watcher and its watchdog, and the releases gone from their host.
/// A tab reads GitHub when it shows for the first time.
/// </summary>
public sealed partial class StewardPage : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _load = Task.CompletedTask;

    public StewardPage(MainViewModel owner)
    {
        _owner = owner;
        Queue = new StewardQueueTab(owner);
        Reports = new StewardReportsTab(owner);
        Watcher = new StewardWatcherTab(owner);
    }

    public StewardQueueTab Queue { get; }

    public StewardReportsTab Reports { get; }

    public StewardWatcherTab Watcher { get; }

    /// <summary>The review that shows in place of the tabs, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReviewOpen))]
    private StewardReview? _review;

    public bool IsReviewOpen => Review is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueueTab))]
    [NotifyPropertyChangedFor(nameof(IsReportsTab))]
    [NotifyPropertyChangedFor(nameof(IsStatusTab))]
    [NotifyPropertyChangedFor(nameof(IsWatcherTab))]
    private StewardPageTab _tab;

    public bool IsQueueTab => Tab == StewardPageTab.Queue;

    public bool IsReportsTab => Tab == StewardPageTab.Reports;

    public bool IsStatusTab => Tab == StewardPageTab.Status;

    public bool IsWatcherTab => Tab == StewardPageTab.Watcher;

    /// <summary>The name of the Queue tab, with the number of pull requests that wait for a steward once it is known.</summary>
    public string QueueTabText => Queue.Count is { } count
        ? _owner.Localization.FormatStewardTabQueueCount(count.ToString(CultureInfo.CurrentCulture))
        : _owner.Localization.StewardTabQueue;

    public ObservableCollection<StewardStatusEntry> StatusEntries { get; } = [];

    /// <summary>The open pull requests that change index-status.toml, and whether GitHub still merges each one.</summary>
    public ObservableCollection<StewardPullRequest> StatusPullRequests { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusEmpty))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusEmpty))]
    private bool _isLoaded;

    public bool IsStatusEmpty => IsLoaded && !IsLoading && Error is null && StatusEntries.Count == 0;

    public bool HasStatusPullRequests => StatusPullRequests.Count > 0;

    internal Task WhenLoadedAsync() => _load;

    /// <summary>Reads the file again. A second call during a read joins it.</summary>
    internal Task RefreshAsync() => IsLoading ? _load : _load = LoadAsync();

    /// <summary>Reads the review or the tab that shows again.</summary>
    [RelayCommand]
    internal Task RefreshTabAsync() => Review is { } review ? review.RefreshAsync() : Tab switch
    {
        StewardPageTab.Queue => Queue.RefreshAsync(),
        StewardPageTab.Reports => Reports.RefreshAsync(),
        StewardPageTab.Watcher => Watcher.RefreshAsync(),
        _ => RefreshAsync(),
    };

    /// <summary>Shows the tab, and reads it when it has not been read yet.</summary>
    internal Task ShowTabAsync(StewardPageTab tab)
    {
        Tab = tab;
        return tab switch
        {
            StewardPageTab.Queue => Queue.IsLoaded ? Task.CompletedTask : Queue.RefreshAsync(),
            StewardPageTab.Reports => Reports.IsLoaded ? Task.CompletedTask : Reports.RefreshAsync(),
            StewardPageTab.Watcher => Watcher.IsLoaded ? Task.CompletedTask : Watcher.RefreshAsync(),
            _ => IsLoaded ? Task.CompletedTask : RefreshAsync(),
        };
    }

    /// <summary>Shows the review of the pull request in place of the tabs and reads it.</summary>
    internal Task OpenReviewAsync(StewardQueueItem item)
    {
        var review = new StewardReview(_owner, item);
        Review = review;
        return review.RefreshAsync();
    }

    /// <summary>Goes back to the tab that showed before the review.</summary>
    [RelayCommand]
    internal void CloseReview() => Review = null;

    [RelayCommand]
    private Task ShowQueue() => ShowTabAsync(StewardPageTab.Queue);

    [RelayCommand]
    private Task ShowReports() => ShowTabAsync(StewardPageTab.Reports);

    [RelayCommand]
    private Task ShowStatus() => ShowTabAsync(StewardPageTab.Status);

    [RelayCommand]
    private Task ShowWatcher() => ShowTabAsync(StewardPageTab.Watcher);

    internal void OnQueueCountChanged() => OnPropertyChanged(nameof(QueueTabText));

    /// <summary>Builds the texts of the tabs and of the open review again in the language that is now selected.</summary>
    internal void RefreshText()
    {
        Queue.RefreshText();
        Reports.RefreshText();
        Watcher.RefreshText();
        Review?.RefreshText();
        OnPropertyChanged(nameof(QueueTabText));
    }

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            var overview = await services.IndexStatusEditor.ReadAsync();
            StatusEntries.Clear();
            foreach (var entry in overview.Entries.OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase))
                StatusEntries.Add(new StewardStatusEntry(_owner, entry));

            StatusPullRequests.Clear();
            foreach (var pull in overview.OpenPullRequests)
                StatusPullRequests.Add(new StewardPullRequest(_owner, pull));

            IsLoaded = true;
        }
        catch (StewardException exception)
        {
            StatusEntries.Clear();
            StatusPullRequests.Clear();
            Error = _owner.StewardErrorText(exception);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasStatusPullRequests));
        }
    }
}

/// <summary>One entry of index-status.toml on the Status tab.</summary>
public sealed partial class StewardStatusEntry(MainViewModel owner, IndexStatusEntry entry)
{
    public IndexStatusEntry Entry { get; } = entry;

    public string Id => Entry.Id;

    public string StateText => owner.IndexStatusStateText(Entry.State);

    public string? VersionText => Entry.Version is { } version ? owner.Localization.FormatStewardVersion(version) : null;

    public string? SinceText =>
        DateTimeOffset.TryParse(Entry.Since, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var since)
            ? owner.Localization.FormatStewardSince(MainViewModel.DateText(since))
            : null;

    public string? Reason => Entry.Reason;

    [RelayCommand]
    private void Lift() => owner.BeginIndexStatusChange(IndexStatusChange.Lift(Entry, string.Empty));
}

/// <summary>An open pull request that changes index-status.toml.</summary>
public sealed partial class StewardPullRequest(MainViewModel owner, IndexStatusPullRequest pullRequest)
{
    public IndexStatusPullRequest PullRequest { get; } = pullRequest;

    public string Text => owner.Localization.FormatStewardPullRequestBy(
        PullRequest.Number.ToString(CultureInfo.InvariantCulture),
        PullRequest.Title,
        PullRequest.Author ?? string.Empty);

    public bool Conflicts => PullRequest.Conflicts == true;

    [RelayCommand]
    private void Open() => owner.StewardPage.Error = owner.TryOpenWithSystem(PullRequest.Url.AbsoluteUri) ?? owner.StewardPage.Error;
}

public enum StewardPageTab
{
    Queue,
    Reports,
    Status,
    Watcher,
}
