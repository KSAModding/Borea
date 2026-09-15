using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Core.Game;
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
    [NotifyPropertyChangedFor(nameof(IsLibrarySection))]
    [NotifyPropertyChangedFor(nameof(IsContentFromInstance))]
    [NotifyPropertyChangedFor(nameof(CanActOnSelectedContent))]
    private bool _currentWindowContent;

    /// <summary>
    /// The instance the content page was opened from, so its breadcrumb leads
    /// back there (#191). Null when it was opened from Discover.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscoverSection))]
    [NotifyPropertyChangedFor(nameof(IsLibrarySection))]
    [NotifyPropertyChangedFor(nameof(IsContentFromInstance))]
    [NotifyPropertyChangedFor(nameof(CanActOnSelectedContent))]
    private InstanceItem? _contentReturnInstance;

    public bool IsContentFromInstance => CurrentWindowContent && ContentReturnInstance is not null;

    /// <summary>
    /// Whether Add and Remove on the content page can be offered. They act on
    /// the active instance, so a page opened from another instance hides them
    /// instead of changing an instance it does not name.
    /// </summary>
    public bool CanActOnSelectedContent => !IsContentFromInstance || CurrentReturnInstance?.IsActive == true;

    /// <summary>
    /// The instance the page was opened from, as the list holds it now. The
    /// list is rebuilt after every change, so the object kept at opening goes stale.
    /// </summary>
    private InstanceItem? CurrentReturnInstance =>
        ContentReturnInstance is { } opened ? Instances.FirstOrDefault(instance => instance.InstanceId == opened.InstanceId) : null;

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

        ContentReturnInstance = null;
        await ShowContentAsync(item);
    }

    /// <summary>
    /// Opens the page of an installed mod from the instance page. The
    /// breadcrumb then names the instance and leads back to it.
    /// </summary>
    internal async Task OpenContentFromInstanceAsync(DiscoverItem item)
    {
        if (item is null || SelectedInstance is null)
            return;

        ContentReturnInstance = SelectedInstance;
        await ShowContentAsync(item);
    }

    [RelayCommand]
    private Task ReturnToInstanceAsync()
    {
        if (CurrentReturnInstance is { } instance)
            return OpenInstanceAsync(instance);

        // the instance was deleted while its content page was open
        SetMainWindowLibrary();
        return Task.CompletedTask;
    }

    private async Task ShowContentAsync(DiscoverItem item)
    {
        LeavePackPage();
        SelectedContent?.ClearOutcome();
        item.ClearOutcome();
        SelectedContent = item;
        ContentDescriptionImages = new DescriptionImages(this, item.Images);
        IsVersionsTab = false;
        ContentDetailError = null;
        LatestVersion = null;
        _contentReleases.Clear();
        ApplyVersionFilter();
        VersionFilter = OptionFor(SavedReleaseChannel);

        FillLinks(ContentLinks, item.Links);
        OnPropertyChanged(nameof(HasContentLinks));

        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        CurrentWindowTasks = false;
        CurrentWindowInstance = false;
        CurrentWindowPack = false;
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
                FillLinks(ContentLinks, item.Links);
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

    // the order and names of the detail panel in #8; unknown keys follow with a capital first letter
    private static readonly string[] KnownLinks = ["forums", "repository", "spacedock", "bugtracker", "homepage", "discussions"];

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
        "homepage" => Localization.LinkHomepage,
        "discussions" => Localization.LinkDiscussions,
        _ => key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..],
    };

    /// <summary>Fills the links of a detail panel in the order of #8. Each link keeps its key, which picks its icon.</summary>
    private void FillLinks(ObservableCollection<ContentLink> links, IReadOnlyDictionary<string, string> source)
    {
        links.Clear();
        foreach (var link in source.OrderBy(link => LinkOrder(link.Key)))
            links.Add(new ContentLink(LinkLabel(link.Key), link.Value, link.Key));
    }

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
        ContentDescriptionImages = DescriptionImages.None;
    }

    partial void OnVersionFilterChanged(ReleaseChannelOption? value) => ApplyVersionFilter();

    [RelayCommand]
    private void SelectVersionFilter(ReleaseChannel channel) => VersionFilter = OptionFor(channel);

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
                foreach (var release in releases)
                {
                    release.RefreshCompatibility(_compatibilityGame);
                    release.RefreshInstalled(ActiveInstance);
                }

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
        if (link is null || TryOpenUrl(link.Url) is not { } error)
            return;

        if (CurrentWindowInstance)
            ContentError = error;
        else
            ContentDetailError = error;
    }

    private static string? TryOpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// Plans the install of one specific release into the active instance.
    /// </summary>
    internal Task InstallVersionAsync(ModVersionMetadata release, VersionItem row)
        => PlanInstallAsync(row, () => Task.FromResult<ModVersionMetadata?>(release), exact: true);
}

/// <summary>A link of the detail panel. <see cref="Key"/> is the key of the listing, such as "forums", and null for a changelog link.</summary>
public sealed record ContentLink(string Label, string Url, string? Key = null);

/// <summary>Markdown text, or a link when the value is an absolute https URI.</summary>
public sealed record ReleaseChangelog(string Title, string? Text, ContentLink? Link)
{
    public static ReleaseChangelog? From(ModVersionMetadata release, string title, string linkLabel)
    {
        if (string.IsNullOrWhiteSpace(release.Changelog))
            return null;

        var value = release.Changelog.Trim();
        return !value.Any(char.IsWhiteSpace) && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? new ReleaseChangelog(title, null, new ContentLink(linkLabel, value))
            : new ReleaseChangelog(title, release.Changelog, null);
    }
}

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

    /// <summary>
    /// How this release fits the installed game (RFC 0017).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityText))]
    [NotifyPropertyChangedFor(nameof(IsCompatible))]
    [NotifyPropertyChangedFor(nameof(IsUntested))]
    [NotifyPropertyChangedFor(nameof(IsIncompatible))]
    private GameCompatibility _compatibility = GameCompatibility.Unknown;

    public string CompatibilityText => _owner.CompatibilityText(Compatibility);

    public bool IsCompatible => Compatibility == GameCompatibility.Compatible;

    public bool IsUntested => Compatibility == GameCompatibility.Untested;

    public bool IsIncompatible => Compatibility == GameCompatibility.Incompatible;

    [ObservableProperty]
    private bool _isInstalled;

    /// <summary>">= min" or "min - max", as the compatibility chip shows it.</summary>
    public string GameVersionText => _release.GameMax is null ? $">= {_release.GameMin}" : $"{_release.GameMin} - {_release.GameMax}";

    public DateTimeOffset ReleaseDate => _release.ReleaseDate;

    /// <summary>How long ago the release came out.</summary>
    public string PublishedText => _owner.AgeText(_release.ReleaseDate);

    public string PublishedDateText => MainViewModel.DateText(_release.ReleaseDate);

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

    public ReleaseChangelog? Changelog { get; }

    [ObservableProperty]
    private bool _isChangelogExpanded;

    public VersionItem(MainViewModel owner, ModVersionMetadata release)
    {
        _owner = owner;
        _release = release;
        Changelog = ReleaseChangelog.From(release, Version, owner.Localization.ContentChangelog);
    }

    [RelayCommand]
    private void ToggleChangelog() => IsChangelogExpanded = !IsChangelogExpanded;

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallVersionAsync(_release, this);

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmInstallAsync(this);

    [RelayCommand]
    private void CancelInstall() => MainViewModel.CancelInstall(this);

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(ChannelText));
        OnPropertyChanged(nameof(CompatibilityText));
        OnPropertyChanged(nameof(PublishedText));
        OnPropertyChanged(nameof(PublishedDateText));
    }

    internal void RefreshCompatibility(GameVersion? installed)
        => Compatibility = Borea.Core.Game.Compatibility.Evaluate(_release, installed);

    internal void RefreshInstalled(InstanceItem? instance)
        => IsInstalled = instance?.InstalledVersionOf(_release.ModId) == _release.Version;
}
