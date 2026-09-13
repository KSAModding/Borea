using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Discover page (discover in #8): every listing of the content index,
/// filtered locally by the panel on the right. Other sources such as SpaceDock
/// are not browsed here, so the page shows exactly what the index lists.
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
    private bool _hideIncompatible;

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
            var listings = await services.ContentIndex.GetAvailableModsAsync();
            _listings = listings
                .Select(listing => new DiscoverItem(this, listing))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            OsOptions.Clear();
            foreach (var os in listings.SelectMany(listing => listing.Os ?? []).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(os => os))
                OsOptions.Add(os);

            LicenseOptions.Clear();
            foreach (var license in listings.Select(listing => listing.License).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(license => license))
                LicenseOptions.Add(license);

            await RefreshCompatibilityAsync(services.InstalledVersion.GetInstalledVersion()?.Version);

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
        if (HideIncompatible)
            filtered = filtered.Where(item => item.Compatibility != GameCompatibility.Incompatible);
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

    partial void OnHideIncompatibleChanged(bool value) => ApplyDiscoverFilters();

    /// <summary>
    /// Evaluates every listing against the game the settings point at, for
    /// example after the game directory changed.
    /// </summary>
    internal Task RefreshCompatibilityAsync()
        => _services is null ? Task.CompletedTask : RefreshCompatibilityAsync(_services.InstalledVersion.GetInstalledVersion()?.Version);

    /// <summary>
    /// Evaluates the newest release of every index listing against
    /// <paramref name="installed"/> (RFC 0017). A listing from another source
    /// stays unknown, because its releases would have to be fetched one by one.
    /// </summary>
    internal async Task RefreshCompatibilityAsync(GameVersion? installed)
    {
        if (_services is null)
            return;

        foreach (var item in _listings)
        {
            var latest = item.Source == "index" ? await _services.ContentIndex.GetLatestReleaseAsync(item.ModId) : null;
            item.Compatibility = latest is null ? GameCompatibility.Unknown : Borea.Core.Game.Compatibility.Evaluate(latest, installed);
        }

        ApplyDiscoverFilters();
    }

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
    /// Plans the install of the newest release into the active instance.
    /// </summary>
    internal Task InstallAsync(DiscoverItem item)
    {
        var services = _services;
        return services is null
            ? Task.CompletedTask
            : PlanInstallAsync(item, () => services.Mods.GetLatestReleaseAsync(item.ModId), exact: false);
    }

    /// <summary>
    /// The "Installed" chip names the instance the row acts on, because the
    /// same mod may sit in another instance too.
    /// </summary>
    public string? InstalledInText => ActiveInstance is null ? null : Localization.FormatDiscoverInstalledIn(ActiveInstance.Name);

    /// <summary>
    /// Removes the mod from the active instance, the same way the instance
    /// page does (#164). What blocks the removal lands on the row.
    /// </summary>
    internal async Task RemoveAsync(DiscoverItem item)
    {
        var services = _services;
        var instance = ActiveInstance;
        if (services is null || instance is null || item.IsRemoving)
            return;

        item.IsRemoving = true;
        item.InstallError = null;
        string? error;
        try
        {
            error = await TryRemoveContentAsync(services, instance.InstanceId, item.ModId);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = exception.Message;
        }
        finally
        {
            item.IsRemoving = false;
            item.IsConfirmingRemove = false;
        }

        await ReloadInstancesAsync();
        item.InstallError = error;
    }
}

/// <summary>
/// One row of the Discover list (c/content-row in #8).
/// </summary>
public sealed partial class DiscoverItem : ObservableObject, IInstallRow
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
    /// How the newest release fits the installed game (RFC 0017).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityText))]
    [NotifyPropertyChangedFor(nameof(IsCompatible))]
    [NotifyPropertyChangedFor(nameof(IsUntested))]
    [NotifyPropertyChangedFor(nameof(IsIncompatible))]
    private GameCompatibility _compatibility = GameCompatibility.Unknown;

    public string CompatibilityText => Compatibility switch
    {
        GameCompatibility.Compatible => _owner.Localization.CompatibilityCompatible,
        GameCompatibility.Untested => _owner.Localization.CompatibilityUntested,
        GameCompatibility.Incompatible => _owner.Localization.CompatibilityIncompatible,
        _ => _owner.Localization.CompatibilityUnknown,
    };

    public bool IsCompatible => Compatibility == GameCompatibility.Compatible;

    public bool IsUntested => Compatibility == GameCompatibility.Untested;

    public bool IsIncompatible => Compatibility == GameCompatibility.Incompatible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _progressStatus;

    [ObservableProperty]
    private string? _progressDetail;

    [ObservableProperty]
    private string? _installError;

    /// <summary>True between the Remove menu item and the confirmation.</summary>
    [ObservableProperty]
    private bool _isConfirmingRemove;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    private bool _isRemoving;

    /// <summary>
    /// The planner's warnings while <see cref="PendingPlan"/> waits for a confirmation.
    /// </summary>
    [ObservableProperty]
    private string? _installWarning;

    public InstallPlan? PendingPlan { get; set; }

    /// <summary>
    /// Mods install into an instance; a loader is set up from the settings.
    /// </summary>
    public bool CanInstall => !IsInstalled && !IsInstalling && Type == ContentType.Mod;

    public bool CanRemove => IsInstalled && !IsRemoving;

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

    /// <summary>
    /// Drops what the last install or removal left on the row: the message,
    /// a pending plan and the confirmation. Called when the user moves on.
    /// </summary>
    internal void ClearOutcome()
    {
        InstallError = null;
        InstallWarning = null;
        PendingPlan = null;
        IsConfirmingRemove = false;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(AuthorsText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(CompatibilityText));
    }

    [RelayCommand]
    private Task OpenAsync() => _owner.OpenContentAsync(this);

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallAsync(this);

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmInstallAsync(this);

    [RelayCommand]
    private void CancelInstall() => MainViewModel.CancelInstall(this);

    [RelayCommand]
    private void BeginRemove()
    {
        InstallError = null;
        IsConfirmingRemove = true;
    }

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    [RelayCommand]
    private Task ConfirmRemoveAsync() => _owner.RemoveAsync(this);
}
