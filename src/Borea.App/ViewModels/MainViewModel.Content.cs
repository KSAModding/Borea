using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The content page (discover-content in #8): one listing with its
/// description, versions, and the detail panel on the right.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscoverSection))]
    private bool _currentWindowContent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContentLinks))]
    [NotifyPropertyChangedFor(nameof(HasContentTags))]
    [NotifyPropertyChangedFor(nameof(IsLoaderContent))]
    private DiscoverItem? _selectedContent;

    public ObservableCollection<ContentLink> ContentLinks { get; } = [];

    private readonly List<VersionItem> _contentReleases = [];

    /// <summary>The releases the Show filter lets through, newest first.</summary>
    public ObservableCollection<VersionItem> ContentVersions { get; } = [];

    /// <summary>The Show filter above the Versions table. It starts at the saved channel.</summary>
    [ObservableProperty]
    private ReleaseChannelOption? _versionFilter;

    public string ContentVersionsEmptyText => _contentReleases.Count > 0 ? Localization.ContentNoVersionsInChannel : Localization.ContentNoVersions;

    public bool HasContentLinks => ContentLinks.Count > 0;

    public bool HasContentTags => SelectedContent is { Tags.Count: > 0 };

    /// <summary>
    /// Which tab of the inner nav bar is selected. Versions load on first visit.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDescriptionTab))]
    private bool _isVersionsTab;

    public bool IsDescriptionTab => !IsVersionsTab;

    /// <summary>True for a loader, whose setup lives in the settings modal.</summary>
    public bool IsLoaderContent => SelectedContent?.Type == ContentType.ModLoader;

    [ObservableProperty]
    private bool _isLoadingVersions;

    [ObservableProperty]
    private string? _contentDetailError;

    /// <summary>
    /// The latest release, for the header meta. Null until loaded or when the
    /// listing has no release.
    /// </summary>
    [ObservableProperty]
    private VersionItem? _latestVersion;

    [RelayCommand]
    internal async Task OpenContentAsync(DiscoverItem item)
    {
        if (item is null)
            return;

        SelectedContent?.ClearOutcome();
        item.ClearOutcome();
        SelectedContent = item;
        IsVersionsTab = false;
        ContentDetailError = null;
        LatestVersion = null;
        _contentReleases.Clear();
        ApplyVersionFilter();
        VersionFilter = OptionFor(SavedReleaseChannel);

        ContentLinks.Clear();
        foreach (var link in item.Links.OrderBy(link => LinkOrder(link.Key)))
            ContentLinks.Add(new ContentLink(LinkLabel(link.Key), link.Value));
        OnPropertyChanged(nameof(HasContentLinks));

        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        CurrentWindowTasks = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = true;

        await LoadLatestVersionAsync(item);
    }

    private async Task LoadLatestVersionAsync(DiscoverItem item)
    {
        if (_services is null)
            return;

        try
        {
            // the catalog leaves out per-mod fields such as the description
            var full = await _services.Mods.GetListingAsync(item.ModId);
            if (full is not null)
            {
                item.Update(full);
                ContentLinks.Clear();
                foreach (var link in item.Links.OrderBy(link => LinkOrder(link.Key)))
                    ContentLinks.Add(new ContentLink(LinkLabel(link.Key), link.Value));
                OnPropertyChanged(nameof(HasContentLinks));
                OnPropertyChanged(nameof(HasContentTags));
            }

            var release = await _services.Mods.GetLatestReleaseAsync(item.ModId);
            if (ReferenceEquals(SelectedContent, item) && release is not null)
                LatestVersion = new VersionItem(this, release);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            ContentDetailError = exception.Message;
        }
    }

    // the order and names of the detail panel in #8; unknown keys follow, as written
    private static readonly string[] KnownLinks = ["forums", "repository", "spacedock", "bugtracker", "discussions"];

    private static int LinkOrder(string key)
    {
        var index = Array.FindIndex(KnownLinks, known => string.Equals(known, key, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? KnownLinks.Length : index;
    }

    private string LinkLabel(string key) => key.ToLowerInvariant() switch
    {
        "forums" => Localization.LinkForum,
        "repository" => Localization.LinkRepository,
        "spacedock" => "SpaceDock",
        "bugtracker" => Localization.LinkBugTracker,
        "discussions" => Localization.LinkDiscussions,
        _ => key,
    };

    [RelayCommand]
    private void ShowContentDescription() => IsVersionsTab = false;

    /// <summary>
    /// Leaving the content page forgets the outcome of its last action, so
    /// coming back shows the listing and not an old error.
    /// </summary>
    private void LeaveContentPage()
    {
        if (!CurrentWindowContent)
            return;

        SelectedContent?.ClearOutcome();
        foreach (var version in _contentReleases)
            version.InstallError = null;
    }

    partial void OnVersionFilterChanged(ReleaseChannelOption? value) => ApplyVersionFilter();

    private void ApplyVersionFilter()
    {
        ContentVersions.Clear();
        foreach (var release in _contentReleases.Where(release => VersionFilter is null || VersionFilter.Channel.Includes(release.Status)))
            ContentVersions.Add(release);
        OnPropertyChanged(nameof(ContentVersionsEmptyText));
    }

    [RelayCommand]
    private async Task ShowContentVersionsAsync()
    {
        IsVersionsTab = true;
        if (_contentReleases.Count > 0 || SelectedContent is null || _services is null || IsLoadingVersions)
            return;

        var item = SelectedContent;
        IsLoadingVersions = true;
        try
        {
            var versions = await _services.Mods.GetAvailableVersionsAsync(item.ModId);
            var releases = new List<VersionItem>();
            foreach (var version in versions)
            {
                var release = await _services.Mods.GetReleaseAsync(item.ModId, version);
                if (release is not null)
                    releases.Add(new VersionItem(this, release));
            }

            if (ReferenceEquals(SelectedContent, item))
            {
                _contentReleases.AddRange(releases.OrderByDescending(release => release.ReleaseDate));
                ApplyVersionFilter();
            }
            ContentDetailError = null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            ContentDetailError = exception.Message;
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    [RelayCommand]
    private void OpenLink(ContentLink link)
    {
        if (link is null)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(link.Url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            ContentDetailError = exception.Message;
        }
    }

    /// <summary>
    /// Plans the install of one specific release into the active instance.
    /// </summary>
    internal Task InstallVersionAsync(ModVersionMetadata release, VersionItem row)
        => PlanInstallAsync(row, () => Task.FromResult<ModVersionMetadata?>(release), exact: true);
}

public sealed record ContentLink(string Label, string Url);

/// <summary>
/// One release of a listing (c/table-version-row in #8).
/// </summary>
public sealed partial class VersionItem : ObservableObject, IInstallRow
{
    private readonly MainViewModel _owner;
    private readonly ModVersionMetadata _release;

    public string Version => _release.Version.ToString();

    public ReleaseStatus Status => _release.ReleaseStatus;

    public string ChannelText => _owner.ReleaseStatusText(Status);

    public bool IsTesting => Status == ReleaseStatus.Testing;

    public bool IsDev => Status == ReleaseStatus.Dev;

    /// <summary>">= min" or "min – max", as the compatibility chip shows it.</summary>
    public string GameVersionText => _release.GameMax is null ? $">= {_release.GameMin}" : $"{_release.GameMin} – {_release.GameMax}";

    public DateTimeOffset ReleaseDate => _release.ReleaseDate;

    public string PublishedText => _release.ReleaseDate.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private string? _installError;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _progressStatus;

    [ObservableProperty]
    private string? _progressDetail;

    /// <summary>
    /// The planner's warnings while <see cref="PendingPlan"/> waits for a confirmation.
    /// </summary>
    [ObservableProperty]
    private string? _installWarning;

    public InstallPlan? PendingPlan { get; set; }

    /// <summary>Mods install into an instance; a loader is set up from the settings.</summary>
    public bool CanInstall => _release.Type == ContentType.Mod;

    public VersionItem(MainViewModel owner, ModVersionMetadata release)
    {
        _owner = owner;
        _release = release;
    }

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallVersionAsync(_release, this);

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmInstallAsync(this);

    [RelayCommand]
    private void CancelInstall() => MainViewModel.CancelInstall(this);
}
