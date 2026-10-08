using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Stewardship;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Watcher tab of the Steward page: the listing issues of the watcher, the watchdog issue, and the releases that the watcher
/// marked as gone from their host. The watcher ticks by itself, so the tab has no tick action, and a backfill runs from the
/// workflow page on GitHub.
/// </summary>
public sealed partial class StewardWatcherTab : ObservableObject
{
    private readonly MainViewModel _owner;
    private Task _load = Task.CompletedTask;
    private WatcherIssues _shown = new([], [], []);
    private (IReadOnlyList<GoneListing> Listings, string? Failure) _shownGone = ([], null);

    public StewardWatcherTab(MainViewModel owner)
    {
        _owner = owner;
    }

    public ObservableCollection<StewardWatcherIssue> Listings { get; } = [];

    public ObservableCollection<StewardWatcherIssue> Watchdog { get; } = [];

    /// <summary>The listings with a release that carries download.unavailable_since in the content index that Borea read.</summary>
    public ObservableCollection<StewardGoneListing> Gone { get; } = [];

    /// <summary>One line for each source that could not be read, the two repositories and the content index, while the others still show.</summary>
    public ObservableCollection<string> Failures { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTicking))]
    [NotifyPropertyChangedFor(nameof(IsListingsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsGoneEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTicking))]
    [NotifyPropertyChangedFor(nameof(IsListingsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsGoneEmpty))]
    private bool _isLoaded;

    /// <summary>content-index-releases was read and has no watchdog issue.</summary>
    public bool IsTicking => IsShown(WatcherIssues.WatchdogRepository) && Watchdog.Count == 0;

    /// <summary>content-index was read and has no listing issue.</summary>
    public bool IsListingsEmpty => IsShown(WatcherIssues.ListingsRepository) && Listings.Count == 0;

    /// <summary>The content index was read and no release in it carries the mark.</summary>
    public bool IsGoneEmpty => IsLoaded && !IsLoading && _shownGone.Failure is null && Gone.Count == 0;

    public Uri WorkflowUrl => WatcherIssues.WorkflowUrl;

    internal Task WhenLoadedAsync() => _load;

    /// <summary>Reads the issues of both repositories and the content index again. A second call during a read joins it.</summary>
    internal Task RefreshAsync() => IsLoading ? _load : _load = LoadAsync();

    /// <summary>Builds the texts of the tab again in the language that is now selected.</summary>
    internal void RefreshText() => Show(_shown, _shownGone);

    [RelayCommand]
    private void OpenWorkflow() => Error = _owner.TryOpenWithSystem(WorkflowUrl.AbsoluteUri) ?? Error;

    private bool IsShown(string repository) => IsLoaded && !IsLoading && _shown.Failures.All(failure => failure.Repository != repository);

    private async Task LoadAsync()
    {
        if (_owner.Services is not { } services)
            return;

        IsLoading = true;
        Error = null;
        try
        {
            // the index names the content page of each listing
            var read = services.WatcherIssues.ListAsync();
            await Task.WhenAll(read, _owner.EnsureDiscoverLoadedAsync());
            Show(await read, await GoneAsync(services));
            IsLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// The marked releases of the snapshot that the content pages also read. The snapshot copies the release files verbatim, so it
    /// carries the mark once it is built after the tick of the watcher.
    /// </summary>
    private static async Task<(IReadOnlyList<GoneListing>, string?)> GoneAsync(BoreaServices services)
    {
        try
        {
            return (GoneListing.From(await services.IndexSnapshots.GetSnapshotAsync()), null);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            return ([], exception.Message);
        }
    }

    private void Show(WatcherIssues issues, (IReadOnlyList<GoneListing> Listings, string? Failure) gone)
    {
        _shown = issues;
        _shownGone = gone;
        Listings.Clear();
        foreach (var issue in issues.Listings)
            Listings.Add(new StewardWatcherIssue(_owner, issue));

        Watchdog.Clear();
        foreach (var issue in issues.Watchdog)
            Watchdog.Add(new StewardWatcherIssue(_owner, issue));

        Gone.Clear();
        foreach (var listing in gone.Listings)
            Gone.Add(new StewardGoneListing(_owner, listing));

        Failures.Clear();
        foreach (var failure in issues.Failures)
            Failures.Add(_owner.StewardWatcherFailureText(failure));
        if (gone.Failure is { } reason)
            Failures.Add(_owner.Localization.FormatStewardWatcherGoneFailed(reason));

        OnPropertyChanged(nameof(IsTicking));
        OnPropertyChanged(nameof(IsListingsEmpty));
        OnPropertyChanged(nameof(IsGoneEmpty));
    }
}

/// <summary>One listing with the releases that are gone from their host. The listing opens its content page, which shows each of them as no longer downloadable.</summary>
public sealed partial class StewardGoneListing(MainViewModel owner, GoneListing gone)
{
    private readonly DiscoverItem? _listing = owner.IndexListing(gone.ListingId);

    public GoneListing Gone { get; } = gone;

    /// <summary>The name of the listing in the snapshot, or its id when the snapshot has no authored document for it.</summary>
    public string Name => Gone.Name ?? Gone.ListingId;

    public bool CanOpenListing => _listing is not null;

    /// <summary>
    /// Borea has no content page for the listing, so its name shows without a link. This is a listing that is newer than the
    /// Discover rows, because only an index check builds them again, or one without an authored document in the snapshot.
    /// </summary>
    public bool IsListingUnknown => _listing is null;

    public IReadOnlyList<StewardGoneRelease> Releases { get; } =
        [.. gone.Releases.Select(release => new StewardGoneRelease(release.Version.ToString(), owner.Localization.FormatContentVersionGone(MainViewModel.DateText(release.Since))))];

    /// <summary>Looks the row up again, because an index check since the read builds new rows.</summary>
    [RelayCommand]
    private Task OpenListingAsync() =>
        _listing is null ? Task.CompletedTask : owner.OpenContentAsync(owner.IndexListing(_listing.ModId) ?? _listing);
}

/// <param name="SinceText">When the watcher marked the release, as the content page says it.</param>
public sealed record StewardGoneRelease(string Version, string SinceText);

/// <summary>One issue of the watcher or the watchdog. It opens on GitHub, and a listing issue also opens the content page of its listing.</summary>
public sealed partial class StewardWatcherIssue(MainViewModel owner, WatcherIssue issue)
{
    private readonly DiscoverItem? _listing = issue.ListingId is { } id ? owner.IndexListing(id) : null;

    public WatcherIssue Issue { get; } = issue;

    public string NumberText => MainViewModel.RepositoryNumberText(Issue.Repository, Issue.Number);

    public string Title => Issue.Title;

    public string UpdatedText => owner.Localization.FormatStewardWatcherUpdated(owner.AgeText(Issue.Updated));

    /// <summary>The name of the listing, which opens its content page.</summary>
    public string? ListingName => _listing?.Name;

    public bool CanOpenListing => _listing is not null;

    /// <summary>The marker names a listing that Borea does not list, for example a delisted one, so only its id shows.</summary>
    public bool IsListingUnknown => Issue.ListingId is not null && _listing is null;

    public bool HasNoListing => Issue.ListingId is null;

    [RelayCommand]
    private void Open() => owner.StewardPage.Watcher.Error = owner.TryOpenWithSystem(Issue.Url.AbsoluteUri) ?? owner.StewardPage.Watcher.Error;

    /// <summary>Looks the row up again, because an index check since the read builds new rows.</summary>
    [RelayCommand]
    private Task OpenListingAsync() =>
        _listing is null ? Task.CompletedTask : owner.OpenContentAsync(owner.IndexListing(_listing.ModId) ?? _listing);
}
