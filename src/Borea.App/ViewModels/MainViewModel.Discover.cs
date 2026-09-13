using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Discover page (discover in #8): every listing the mod repository
/// knows, filtered locally by the panel on the right.
/// </summary>
public partial class MainViewModel
{
    private IReadOnlyList<DiscoverItem> _listings = [];
    private Task? _discoverLoad;

    public ObservableCollection<DiscoverItem> DiscoverItems { get; } = [];

    public ObservableCollection<string> OsOptions { get; } = [];

    public ObservableCollection<string> LicenseOptions { get; } = [];

    [ObservableProperty]
    private bool _isDiscoverLoading;

    [ObservableProperty]
    private string? _discoverError;

    /// <summary>
    /// Which tab of the inner nav bar is selected. Only mods and mod loaders
    /// have listings today; the other tabs wait for their content types.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModsTab))]
    [NotifyPropertyChangedFor(nameof(IsLoadersTab))]
    private ContentType _discoverType = ContentType.Mod;

    public bool IsModsTab => DiscoverType == ContentType.Mod;

    public bool IsLoadersTab => DiscoverType == ContentType.ModLoader;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hideInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private string? _selectedOs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private string? _selectedLicense;

    public bool HasDiscoverFilters => SelectedOs is not null || SelectedLicense is not null;

    public bool HasDiscoverItems => DiscoverItems.Count > 0;

    /// <summary>
    /// Loads the listings once. Every caller awaits the same load, so a page
    /// that opens while it runs sees the result instead of an empty list.
    /// </summary>
    internal Task EnsureDiscoverLoadedAsync()
    {
        if (_services is null)
            return Task.CompletedTask;

        return _discoverLoad ??= LoadDiscoverAsync(_services);
    }

    private async Task LoadDiscoverAsync(BoreaServices services)
    {
        IsDiscoverLoading = true;
        try
        {
            var listings = await services.Mods.GetAvailableModsAsync();
            var items = listings.Select(listing => new DiscoverItem(this, listing)).ToList();

            // a mod in the content index that also releases on SpaceDock shows once, from the index
            var mirrored = new HashSet<string>(items.Where(item => item.Source != "spacedock").SelectMany(item => item.SpaceDockReferences), StringComparer.OrdinalIgnoreCase);
            _listings = items
                .Where(item => item.Source != "spacedock" || !mirrored.Contains(item.ModId))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            OsOptions.Clear();
            foreach (var os in listings.SelectMany(listing => listing.Os ?? []).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(os => os))
                OsOptions.Add(os);

            LicenseOptions.Clear();
            foreach (var license in listings.Select(listing => listing.License).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(license => license))
                LicenseOptions.Add(license);

            DiscoverError = null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            DiscoverError = exception.Message;
            _discoverLoad = null; // the next visit tries again
        }
        finally
        {
            IsDiscoverLoading = false;
        }

        RefreshInstalledFlags();
        ApplyDiscoverFilters();
    }

    private void ApplyDiscoverFilters()
    {
        var query = SearchText.Trim();
        var filtered = _listings.Where(item => item.Type == DiscoverType);

        if (query.Length > 0)
            filtered = filtered.Where(item => item.Matches(query));
        if (HideInstalled)
            filtered = filtered.Where(item => !item.IsInstalled);
        if (SelectedOs is not null)
            filtered = filtered.Where(item => item.SupportsOs(SelectedOs));
        if (SelectedLicense is not null)
            filtered = filtered.Where(item => string.Equals(item.License, SelectedLicense, StringComparison.OrdinalIgnoreCase));

        DiscoverItems.Clear();
        foreach (var item in filtered)
            DiscoverItems.Add(item);
        OnPropertyChanged(nameof(HasDiscoverItems));
    }

    /// <summary>
    /// Marks listings that the active instance already holds.
    /// </summary>
    private void RefreshInstalledFlags()
    {
        var installed = new HashSet<string>(ActiveInstance?.ModIds ?? [], ModIds.Comparer);
        foreach (var item in _listings)
            item.IsInstalled = installed.Contains(item.ModId);
    }

    partial void OnDiscoverTypeChanged(ContentType value) => ApplyDiscoverFilters();

    partial void OnSearchTextChanged(string value) => ApplyDiscoverFilters();

    partial void OnHideInstalledChanged(bool value) => ApplyDiscoverFilters();

    partial void OnSelectedOsChanged(string? value) => ApplyDiscoverFilters();

    partial void OnSelectedLicenseChanged(string? value) => ApplyDiscoverFilters();

    [RelayCommand]
    private void ShowDiscoverMods() => DiscoverType = ContentType.Mod;

    [RelayCommand]
    private void ShowDiscoverLoaders() => DiscoverType = ContentType.ModLoader;

    [RelayCommand]
    private void SelectOs(string? os) => SelectedOs = os;

    [RelayCommand]
    private void SelectLicense(string? license) => SelectedLicense = license;

    [RelayCommand]
    private void ClearDiscoverFilters()
    {
        SelectedOs = null;
        SelectedLicense = null;
    }

    /// <summary>
    /// Installs the latest release into the active instance, the way the CLI
    /// would. Dependencies are not resolved yet; that needs the resolver from #8.
    /// </summary>
    internal async Task InstallAsync(DiscoverItem item)
    {
        if (_services is null || ActiveInstance is null || item.IsInstalling)
            return;

        item.IsInstalling = true;
        item.InstallError = null;
        var progress = new Progress<DownloadProgress>(value => item.Progress = value.PercentComplete);
        try
        {
            var release = await _services.Mods.GetLatestReleaseAsync(item.ModId)
                ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
            await _services.Installer.InstallAsync(ActiveInstance.InstanceId, release, InstallReason.Manual, enable: true, progress);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException or DownloadFailedException or NotSupportedException or TaskCanceledException)
        {
            item.InstallError = exception.Message;
        }
        finally
        {
            item.IsInstalling = false;
            item.Progress = 0;
        }

        await ReloadInstancesAsync();
    }
}

/// <summary>
/// One row of the Discover list (c/content-row in #8).
/// </summary>
public sealed partial class DiscoverItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private ModMetadata _listing;

    public string ModId => _listing.ModId;

    public string Name => _listing.Name;

    public string Abstract => _listing.Abstract;

    public string License => _listing.License;

    public ContentType Type => _listing.Type;

    public IReadOnlyList<string> Tags { get; private set; }

    public string? Description => _listing.Description;

    public string Source => _listing.Source;

    public IReadOnlyDictionary<string, string> Links => _listing.Links;

    /// <summary>">= min" or "min – max", as the compatibility chip shows it.</summary>
    public string GameVersionText => _listing.GameMax is null ? $">= {_listing.GameMin}" : $"{_listing.GameMin} – {_listing.GameMax}";

    public string TypeText => Type switch
    {
        ContentType.ModLoader => _owner.Localization.ContentTypeModLoader,
        _ => _owner.Localization.ContentTypeMod,
    };

    public string AuthorNames => string.Join(", ", _listing.Authors);

    public string AuthorsText => _owner.Localization.FormatContentByAuthor(AuthorNames);

    public string SourceText => _owner.Localization.FormatContentSource(Source switch
    {
        "index" => _owner.Localization.SourceContentIndex,
        "spacedock" => "SpaceDock",
        _ => Source,
    });

    /// <summary>
    /// The SpaceDock ids this listing also releases under, so the same mod
    /// from SpaceDock can be hidden next to it.
    /// </summary>
    internal IEnumerable<string> SpaceDockReferences
        => _listing.Releases?.Hosts.Where(host => string.Equals(host.Host, "spacedock", StringComparison.OrdinalIgnoreCase)).Select(host => host.Reference) ?? [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _installError;

    /// <summary>
    /// Mods install into an instance; a loader is set up from the settings.
    /// </summary>
    public bool CanInstall => !IsInstalled && !IsInstalling && Type == ContentType.Mod;

    public DiscoverItem(MainViewModel owner, ModMetadata listing)
    {
        _owner = owner;
        _listing = listing;
        Tags = listing.Tags.Take(3).ToList();
    }

    internal bool Matches(string query)
        => Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Abstract.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || _listing.Authors.Any(author => author.Contains(query, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>
    /// A listing without an OS list runs everywhere.
    /// </summary>
    internal bool SupportsOs(string os)
        => _listing.Os is null || _listing.Os.Contains(os, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces the catalog entry with the full listing of the same mod.
    /// </summary>
    internal void Update(ModMetadata full)
    {
        _listing = full;
        Tags = full.Tags.Take(3).ToList();
        OnPropertyChanged(string.Empty);
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(AuthorsText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(TypeText));
    }

    [RelayCommand]
    private Task OpenAsync() => _owner.OpenContentAsync(this);

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallAsync(this);
}
