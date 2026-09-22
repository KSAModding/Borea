using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.History;
using Borea.Core.Index;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Preferences;
using Borea.Core.Tags;
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
    /// <summary>
    /// A library does nothing on its own, so it gets no category row, and a
    /// listing tagged only as a library shows under Other.
    /// </summary>
    private const string LibraryTag = "library";

    /// <summary>A list with fewer rows shows every compatibility chip.</summary>
    private const int CommonCompatibilityMinimumRows = 5;

    /// <summary>The share of rows, in percent, at which a compatibility state counts as the common one.</summary>
    private const int CommonCompatibilityPercent = 90;

    private IReadOnlyList<DiscoverItem> _listings = [];
    private GameVersion? _compatibilityGame;
    private Task? _discoverLoad;
    private CuratedTagVocabulary _categoryVocabulary = CuratedTagVocabulary.Empty;
    private DiscoverSortOrder? _discoverSort;

    internal CuratedTagVocabulary TagVocabulary { get; private set; } = CuratedTagVocabulary.Empty;

    public ObservableCollection<DiscoverItem> DiscoverItems { get; } = [];

    public ObservableCollection<string> OsOptions { get; } = [];

    public ObservableCollection<string> LicenseOptions { get; } = [];

    public ObservableCollection<DiscoverCategory> CategoryOptions { get; } = [];

    public ObservableCollection<DiscoverCategory> SelectedCategories { get; } = [];

    [ObservableProperty]
    private bool _isDiscoverLoading;

    [ObservableProperty]
    private string? _discoverError;

    /// <summary>
    /// Which tab of the inner nav bar is selected. The vehicle and save tabs
    /// stay disabled until those content types have listings.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModsTab))]
    [NotifyPropertyChangedFor(nameof(IsLoadersTab))]
    [NotifyPropertyChangedFor(nameof(IsModpacksTab))]
    private ContentType _discoverType = ContentType.Mod;

    public bool IsModsTab => DiscoverType == ContentType.Mod;

    public bool IsLoadersTab => DiscoverType == ContentType.ModLoader;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private bool _hideInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private bool _hideIncompatible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private string? _selectedOs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private string? _selectedLicense;

    /// <summary>The public builds of the snapshot, newest first, for the Game version filter.</summary>
    public ObservableCollection<GameVersionOption> GameVersionOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    [NotifyPropertyChangedFor(nameof(DiscoverGameVersionRangeText))]
    private GameVersionOption? _discoverGameMin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    [NotifyPropertyChangedFor(nameof(DiscoverGameVersionRangeText))]
    private GameVersionOption? _discoverGameMax;

    private bool HasGameVersionRange => DiscoverGameMin is not null || DiscoverGameMax is not null;

    public string? DiscoverGameVersionRangeText => (DiscoverGameMin, DiscoverGameMax) switch
    {
        (null, null) => null,
        ({ } min, null) => $">= {min.Text}",
        (null, { } max) => $"<= {max.Text}",
        ({ } min, { } max) when min.Revision == max.Revision => min.Text,
        ({ } min, { } max) => $"{min.Text} - {max.Text}",
    };

    public bool HasDiscoverFilters => HideInstalled || HideIncompatible || SelectedOs is not null || SelectedLicense is not null || SelectedCategories.Count > 0 || HasGameVersionRange;

    /// <summary>The saved Sort by choice of the Mods and Modpacks tabs.</summary>
    public DiscoverSortOrder DiscoverSort => _discoverSort ?? _appPreferences.DiscoverSortOrder;

    public string DiscoverSortText => DiscoverSort switch
    {
        DiscoverSortOrder.RecentlyUpdated => Localization.DiscoverSortRecentlyUpdated,
        DiscoverSortOrder.Name => Localization.DiscoverSortName,
        _ => Localization.DiscoverSortPopularity,
    };

    public bool HasDiscoverItems => DiscoverItems.Count > 0 || DiscoverPacks.Count > 0;

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
            var snapshot = await services.IndexSnapshots.GetSnapshotAsync();
            TagVocabulary = snapshot.Tags;
            _gameReleases = GameReleaseList.From(snapshot.GameVersions);

            // the download counts and the first release dates sit next to a listing in the snapshot, not inside it
            var listingEntries = new Dictionary<string, ContentIndexListing>(ModIds.Comparer);
            foreach (var entry in snapshot.Listings)
                listingEntries.TryAdd(entry.Id, entry);
            var packEntries = new Dictionary<string, ContentIndexPack>(ModIds.Comparer);
            foreach (var entry in snapshot.Packs)
                packEntries.TryAdd(entry.Id, entry);

            var listings = await services.ContentIndex.GetAvailableModsAsync();
            _listings = listings
                .Select(listing => new DiscoverItem(this, listing, listing.Source == "index" ? listingEntries.GetValueOrDefault(listing.ModId) : null))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var packs = (await services.ModPacks.GetAvailableModPacksAsync())
                .Select(pack => pack.Metadata)
                .OfType<ModPackMetadata>()
                .ToList();
            _packs = packs
                .Select(pack => new PackItem(this, pack, packEntries.GetValueOrDefault(pack.ModPackId)))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            OsOptions.Clear();
            foreach (var os in listings.SelectMany(listing => listing.Os ?? []).Concat(packs.SelectMany(pack => pack.Os ?? [])).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(os => os))
                OsOptions.Add(os);

            LicenseOptions.Clear();
            foreach (var license in listings.Select(listing => listing.License).Concat(packs.Select(pack => pack.License)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(license => license))
                LicenseOptions.Add(license);

            LoadCategoryOptions(listings, packs);
            LoadGameVersionOptions(snapshot.GameVersions);

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

        UpdateIndexRefreshStatus();
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
        if (HasGameVersionRange)
            filtered = filtered.Where(item => item.LatestInChannel is { } release
                && Borea.Core.Game.Compatibility.SupportsAnyBuild(release.GameMinRevision, release.GameMaxRevision, DiscoverGameMin?.Revision, DiscoverGameMax?.Revision));
        if (SelectedCategories.Count > 0)
        {
            var matching = ContentTagFilter.Filter(
                filtered.Select(item => item.Listing),
                _categoryVocabulary,
                DiscoverType,
                SelectedCategories.Where(category => !category.IsOther).Select(category => category.Tag!),
                includeOther: SelectedCategories.Any(category => category.IsOther));
            var matchingSet = new HashSet<ModMetadata>(matching, ReferenceEqualityComparer.Instance);
            filtered = filtered.Where(item => matchingSet.Contains(item.Listing));
        }

        var rows = SortDiscover(filtered).ToList();
        var common = CommonCompatibility(rows.Select(item => item.Compatibility).ToList());
        foreach (var item in rows)
            item.ShowsCompatibility = item.Compatibility != common;
        Arrange(DiscoverItems, rows);
        ApplyPackFilters(query);
        OnPropertyChanged(nameof(HasDiscoverItems));
    }

    /// <summary>The state whose chip a row of the list leaves out, or null when every row shows its chip.</summary>
    private static GameCompatibility? CommonCompatibility(IReadOnlyCollection<GameCompatibility> states)
    {
        if (states.Count < CommonCompatibilityMinimumRows)
            return null;

        var largest = states.GroupBy(state => state).MaxBy(group => group.Count())!;
        return largest.Count() * 100 >= states.Count * CommonCompatibilityPercent ? largest.Key : null;
    }

    /// <summary>The Loaders tab has no Sort by dropdown, so it keeps the name order.</summary>
    private IEnumerable<DiscoverItem> SortDiscover(IEnumerable<DiscoverItem> items) => (IsLoadersTab ? DiscoverSortOrder.Name : DiscoverSort) switch
    {
        DiscoverSortOrder.Popularity => items.OrderBy(item => item.Downloads is null).ThenByDescending(item => item.Downloads).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
        DiscoverSortOrder.RecentlyUpdated => items.OrderBy(item => item.UpdatedAt is null).ThenByDescending(item => item.UpdatedAt).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
        _ => items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
    };

    /// <summary>A reload keeps a chosen bound while its build is still listed.</summary>
    private void LoadGameVersionOptions(ContentIndexGameVersions? gameVersions)
    {
        var options = (gameVersions?.Versions ?? [])
            .Select(text => GameVersion.TryParse(text, out var version) ? new GameVersionOption(text, version.Revision) : null)
            .OfType<GameVersionOption>()
            .OrderByDescending(option => option.Revision)
            .ToList();
        if (options.SequenceEqual(GameVersionOptions))
            return;

        var min = DiscoverGameMin?.Revision;
        var max = DiscoverGameMax?.Revision;
        GameVersionOptions.Clear();
        foreach (var option in options)
            GameVersionOptions.Add(option);
        DiscoverGameMin = GameVersionOptions.FirstOrDefault(option => option.Revision == min);
        DiscoverGameMax = GameVersionOptions.FirstOrDefault(option => option.Revision == max);
    }

    private void LoadCategoryOptions(IReadOnlyList<ModMetadata> listings, IReadOnlyList<ModPackMetadata> packs)
    {
        var selectedTags = SelectedCategories.Select(category => category.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        SelectedCategories.Clear();
        CategoryOptions.Clear();

        _categoryVocabulary = new CuratedTagVocabulary(
            TagVocabulary.SpecVersion,
            TagVocabulary.ModTags.Where(tag => !string.Equals(tag.Tag, LibraryTag, StringComparison.OrdinalIgnoreCase)).ToList());

        var tagged = listings.Select(listing => (listing.Type, listing.Tags))
            .Concat(packs.Select(pack => (Type: ContentType.ModPack, pack.Tags)))
            .ToList();
        var vocabulary = tagged.Select(entry => entry.Type).Distinct()
            .SelectMany(_categoryVocabulary.GetTags)
            .DistinctBy(tag => tag.Tag, StringComparer.OrdinalIgnoreCase);
        foreach (var tag in vocabulary)
        {
            if (tagged.Any(entry => entry.Tags.Contains(tag.Tag, StringComparer.OrdinalIgnoreCase)))
                CategoryOptions.Add(new DiscoverCategory(this, tag));
        }

        if (CategoryOptions.Count > 0 && tagged.Any(entry => !HasCuratedTag(entry.Type, entry.Tags)))
            CategoryOptions.Add(new DiscoverCategory(this, tag: null));

        foreach (var category in CategoryOptions.Where(category => selectedTags.Contains(category.Tag)))
        {
            category.IsSelected = true;
            SelectedCategories.Add(category);
        }

        OnPropertyChanged(nameof(HasDiscoverFilters));
    }

    private bool HasCuratedTag(ContentType type, IReadOnlyList<string> tags)
        => _categoryVocabulary.GetTags(type).Any(tag => tags.Contains(tag.Tag, StringComparer.OrdinalIgnoreCase));

    /// <summary>A tag that this version does not translate keeps the name the index gives it.</summary>
    internal string CategoryName(string tag, string indexName) => tag.ToLowerInvariant() switch
    {
        "parts" => Localization.DiscoverCategoryParts,
        "celestial" => Localization.DiscoverCategoryCelestial,
        "gameplay" => Localization.DiscoverCategoryGameplay,
        "user-interface" => Localization.DiscoverCategoryUserInterface,
        "visual" => Localization.DiscoverCategoryVisual,
        "audio" => Localization.DiscoverCategoryAudio,
        "tools" => Localization.DiscoverCategoryTools,
        "library" => Localization.DiscoverCategoryLibrary,
        _ => indexName,
    };

    /// <summary>
    /// Marks listings that the active instance already holds.
    /// </summary>
    private void RefreshInstalledFlags()
    {
        var installed = new HashSet<string>(ActiveInstance?.ModIds ?? [], ModIds.Comparer);
        foreach (var item in _listings)
        {
            item.IsInstalled = installed.Contains(item.ModId);
            var mod = _activeInstanceEntity?.Mods.FirstOrDefault(entry => ModIds.Equals(entry.ModId, item.ModId));
            item.RemoveBlockedText = item.IsInstalled ? RemoveBlockedReason(_activeInstanceEntity, mod) : null;
        }
        foreach (var release in _contentReleases)
            release.RefreshInstalled(ActiveInstance);
        RefreshPackInstalledFlags();
        RefreshContentDependencies();
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
    /// Evaluates the newest release of every index listing and every row of
    /// the Versions table against <paramref name="installed"/> (RFC 0017). A
    /// listing from another source stays unknown, because its releases would
    /// have to be fetched one by one. A pack is evaluated by the bounds of its
    /// newest usable version.
    /// </summary>
    internal async Task RefreshCompatibilityAsync(GameVersion? installed)
    {
        if (_services is null)
            return;

        _compatibilityGame = installed;
        foreach (var item in _listings)
        {
            var latest = item.Source == "index" ? await _services.ContentIndex.GetLatestReleaseInChannelAsync(item.ModId, _services.Settings.ReleaseChannel) : null;
            item.Compatibility = latest is null ? GameCompatibility.Unknown : Borea.Core.Game.Compatibility.Evaluate(latest, installed);

            // the age on the row belongs to the same release as the chip and Add, so a newer release in another channel does not show there
            item.UpdatedAt = latest?.ReleaseDate;
            item.LatestInChannel = latest;
        }

        foreach (var release in _contentReleases)
            release.RefreshCompatibility(installed);

        foreach (var pack in _packs)
            pack.Compatibility = Borea.Core.Game.Compatibility.Evaluate(pack.Metadata, installed, _gameReleases);

        ApplyDiscoverFilters();
    }

    internal string CompatibilityText(GameCompatibility compatibility) => compatibility switch
    {
        GameCompatibility.Compatible => Localization.CompatibilityCompatible,
        GameCompatibility.Untested => Localization.CompatibilityUntested,
        GameCompatibility.Incompatible => Localization.CompatibilityIncompatible,
        _ => Localization.CompatibilityUnknown,
    };

    partial void OnSelectedOsChanged(string? value) => ApplyDiscoverFilters();

    partial void OnSelectedLicenseChanged(string? value) => ApplyDiscoverFilters();

    // a range whose Min is above its Max would match nothing, so the other bound follows
    partial void OnDiscoverGameMinChanged(GameVersionOption? value)
    {
        if (value is not null && DiscoverGameMax is { } max && max.Revision < value.Revision)
            DiscoverGameMax = value;
        ApplyDiscoverFilters();
    }

    partial void OnDiscoverGameMaxChanged(GameVersionOption? value)
    {
        if (value is not null && DiscoverGameMin is { } min && min.Revision > value.Revision)
            DiscoverGameMin = value;
        ApplyDiscoverFilters();
    }

    [RelayCommand]
    private void ShowDiscoverMods() => DiscoverType = ContentType.Mod;

    [RelayCommand]
    private void ShowDiscoverLoaders() => DiscoverType = ContentType.ModLoader;

    [RelayCommand]
    private void ShowDiscoverModpacks() => DiscoverType = ContentType.ModPack;

    [RelayCommand]
    private void SelectOs(string? os) => SelectedOs = os;

    [RelayCommand]
    private void SelectLicense(string? license) => SelectedLicense = license;

    [RelayCommand]
    private void ClearDiscoverGameVersionRange()
    {
        DiscoverGameMin = null;
        DiscoverGameMax = null;
    }

    [RelayCommand]
    private void ClearHideInstalled() => HideInstalled = false;

    [RelayCommand]
    private void ClearHideIncompatible() => HideIncompatible = false;

    [RelayCommand]
    private void SelectDiscoverSort(DiscoverSortOrder order)
    {
        if (order == DiscoverSort)
            return;

        _discoverSort = order;
        OnPropertyChanged(nameof(DiscoverSort));
        OnPropertyChanged(nameof(DiscoverSortText));
        QueuePreferenceSave(preferences => preferences.WithDiscoverSortOrder(order));
        ApplyDiscoverFilters();
    }

    [RelayCommand]
    private void ToggleCategory(DiscoverCategory? category)
    {
        if (category is null)
            return;

        category.IsSelected = !category.IsSelected;
        if (category.IsSelected)
            SelectedCategories.Add(category);
        else
            SelectedCategories.Remove(category);

        OnPropertyChanged(nameof(HasDiscoverFilters));
        ApplyDiscoverFilters();
    }

    [RelayCommand]
    private void ClearDiscoverFilters()
    {
        HideInstalled = false;
        HideIncompatible = false;
        SelectedOs = null;
        SelectedLicense = null;
        DiscoverGameMin = null;
        DiscoverGameMax = null;
        foreach (var category in SelectedCategories)
            category.IsSelected = false;
        SelectedCategories.Clear();
        OnPropertyChanged(nameof(HasDiscoverFilters));
        ApplyDiscoverFilters();
    }

    /// <summary>
    /// Plans the install of the newest release into the active instance.
    /// </summary>
    internal Task InstallAsync(DiscoverItem item)
    {
        var services = _services;
        return services is null
            ? Task.CompletedTask
            : PlanInstallAsync(item, () => services.Mods.GetLatestReleaseAsync(item.ModId), exactVersion: null);
    }

    /// <summary>
    /// The Installed chip and the Discover checkmark name the instance the row
    /// acts on, because the same mod may sit in another instance too.
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

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            item.InstallError = Localization.LibraryFolderBusy;
            return;
        }

        item.IsRemoving = true;
        item.InstallError = null;
        var task = StartTask(TaskKind.ModRemoval, item.Name, instance.InstanceId, item.ModId);
        var completed = false;
        string? error = null;
        try
        {
            error = await TryRemoveContentAsync(services, instance.InstanceId, item.ModId);
            completed = true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = exception.Message;
        }
        finally
        {
            item.IsRemoving = false;
            item.IsConfirmingRemove = false;
            EndTask(task, completed, stopped: false, error);
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

    /// <summary>The curated tags in the words and order of the vocabulary, then the free-form tags.</summary>
    public IReadOnlyList<string> AllTags { get; private set; }

    internal ModMetadata Listing => _listing;

    private ModVersionMetadata? _latestInChannel;

    /// <summary>The release Add installs, which the size and the age on the row belong to.</summary>
    internal ModVersionMetadata? LatestInChannel
    {
        get => _latestInChannel;
        set
        {
            _latestInChannel = value;
            OnPropertyChanged(nameof(DownloadSizeText));
        }
    }

    /// <summary>The archive size of <see cref="LatestInChannel"/>, or null when the index states none.</summary>
    public string? DownloadSizeText => LatestInChannel?.Download.SizeBytes is { } size ? MainViewModel.SizeText(size) : null;

    public string? Description => _listing.Description;

    public string Source => _listing.Source;

    public IReadOnlyDictionary<string, string> Links => _listing.Links;

    /// <summary>">= min" or "min - max", as the compatibility chip shows it.</summary>
    public string GameVersionText => _listing.GameMax is null ? $">= {_listing.GameMin}" : $"{_listing.GameMin} - {_listing.GameMax}";

    public string TypeText => Type switch
    {
        ContentType.ModLoader => _owner.Localization.ContentTypeModLoader,
        _ => _owner.Localization.ContentTypeMod,
    };

    public string AuthorNames => string.Join(", ", _listing.Authors);

    public string AuthorsText => _owner.Localization.FormatContentByAuthor(AuthorNames);

    public ListingImage? Icon { get; }

    public string? IconAttribution => Icon?.Attribution;

    public string? IconSource => Icon?.Source;

    /// <summary>The live images of an index listing, which every description of the listing resolves against.</summary>
    internal ContentImages? Images { get; }

    /// <summary>The download count the content index reports, or null when it reports none.</summary>
    public long? Downloads { get; }

    /// <summary>The count as the row shows it, "1.2k". The exact number is <see cref="DownloadsExactText"/>.</summary>
    public string? DownloadsText => Downloads is { } count ? MainViewModel.CompactCount(count) : null;

    public string? DownloadsExactText => Downloads is { } count ? _owner.Localization.FormatContentDownloadsExact(count.ToString("N0", CultureInfo.CurrentCulture)) : null;

    /// <summary>The date of the first release the index reports, or null when it reports none.</summary>
    public DateTimeOffset? PublishedAt { get; }

    public string? PublishedText => PublishedAt is { } at ? _owner.Localization.FormatContentPublished(_owner.AgeText(at)) : null;

    public string? PublishedDateText => PublishedAt is { } at ? MainViewModel.DateText(at) : null;

    /// <summary>The date of the newest release in the saved release channel, the one Add installs. Null when the channel has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText))]
    [NotifyPropertyChangedFor(nameof(UpdatedDateText))]
    private DateTimeOffset? _updatedAt;

    public string? UpdatedText => UpdatedAt is { } at ? _owner.ShortAgeText(at) : null;

    public string? UpdatedDateText => UpdatedAt is { } at ? MainViewModel.DateText(at) : null;

    /// <summary>
    /// How the newest release fits the installed game (RFC 0017).
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

    /// <summary>False when most rows of the Discover list share this state, so the row leaves the chip out.</summary>
    [ObservableProperty]
    private bool _showsCompatibility = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyCanExecuteChangedFor(nameof(BeginRemoveCommand))]
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
    private InstallRun? _run;

    [ObservableProperty]
    private string? _installError;

    private bool _isOpening;

    /// <summary>True between the Remove menu item and the confirmation.</summary>
    [ObservableProperty]
    private bool _isConfirmingRemove;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyCanExecuteChangedFor(nameof(BeginRemoveCommand))]
    private bool _isRemoving;

    /// <summary>
    /// The planner's warnings while <see cref="PendingPlan"/> waits for a confirmation.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    private string? _installWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    [NotifyPropertyChangedFor(nameof(AddedModsText))]
    [NotifyPropertyChangedFor(nameof(AddedModsToolTip))]
    private InstallPlan? _pendingPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    private InstallChoices? _choices;

    public bool IsConfirmingInstall => PendingPlan is not null || Choices is not null;

    /// <summary>The button of the confirmation, with what the plan downloads: "Add (38.0 MB)".</summary>
    public string ConfirmInstallText => _owner.ConfirmInstallText(InstallWarning, PendingPlan);

    public string? AddedModsText => _owner.AddedModsText(PendingPlan, Choices);

    public string? AddedModsToolTip => _owner.AddedModsText(PendingPlan, Choices, all: true);

    private Func<string>? _linkRequest;

    /// <summary>What a borea:// link asked for while its confirmation waits, naming the instance. Null otherwise.</summary>
    public string? LinkRequestText => _linkRequest?.Invoke();

    /// <summary>
    /// Mods install into an instance; a loader is set up from the settings.
    /// </summary>
    public bool CanInstall => !IsInstalled && !IsInstalling && Type == ContentType.Mod;

    /// <summary>
    /// Only a mod has a version to pick, because the Versions table of a
    /// loader offers no install button.
    /// </summary>
    public bool CanChangeVersion => Type == ContentType.Mod;

    public bool CanRemove => IsInstalled && !IsRemoving && RemoveBlockedText is null;

    /// <summary>Why Remove is disabled, shown next to it. Null when the mod can be removed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(RemoveToolTip))]
    [NotifyCanExecuteChangedFor(nameof(BeginRemoveCommand))]
    private string? _removeBlockedText;

    public string RemoveToolTip => RemoveBlockedText ?? _owner.Localization.ContentRemove;

    public string InstalledAutomationName => _owner.Localization.FormatDiscoverInstalledMod(Name);

    /// <param name="indexEntry">The snapshot entry of an index listing, for its download count and the date of its first release. Null for any other listing.</param>
    public DiscoverItem(MainViewModel owner, ModMetadata listing, ContentIndexListing? indexEntry = null)
    {
        _owner = owner;
        _listing = listing;
        AllTags = DisplayTags(owner, listing.Type, listing.Tags);
        Tags = AllTags.Take(3).ToList();
        Images = indexEntry?.Images;
        Icon = owner.IconFor(Images?.Icon);
        Downloads = indexEntry?.Downloads?.Total;
        PublishedAt = indexEntry?.PublishedAt;
    }

    internal static List<string> DisplayTags(MainViewModel owner, ContentType type, IReadOnlyList<string> tags)
    {
        var curated = owner.TagVocabulary.GetTags(type)
            .Where(tag => tags.Contains(tag.Tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return curated.Select(tag => owner.CategoryName(tag.Tag, tag.Name))
            .Concat(tags.Where(value => !curated.Any(tag => string.Equals(tag.Tag, value, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    internal bool Matches(string query)
        => Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Abstract.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || _listing.Authors.Any(author => author.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            || _listing.Tags.Concat(AllTags).Any(tag => tag.Contains(query, StringComparison.CurrentCultureIgnoreCase));

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
        AllTags = DisplayTags(_owner, full.Type, full.Tags);
        Tags = AllTags.Take(3).ToList();
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
        Choices = null;
        IsConfirmingRemove = false;
    }

    internal void RefreshText()
    {
        AllTags = DisplayTags(_owner, _listing.Type, _listing.Tags);
        Tags = AllTags.Take(3).ToList();
        OnPropertyChanged(nameof(AllTags));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(AuthorsText));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(CompatibilityText));
        OnPropertyChanged(nameof(DownloadsText));
        OnPropertyChanged(nameof(DownloadsExactText));
        OnPropertyChanged(nameof(PublishedText));
        OnPropertyChanged(nameof(PublishedDateText));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(UpdatedDateText));
        OnPropertyChanged(nameof(ConfirmInstallText));
        OnPropertyChanged(nameof(AddedModsText));
        OnPropertyChanged(nameof(AddedModsToolTip));
        OnPropertyChanged(nameof(InstalledAutomationName));
        OnPropertyChanged(nameof(LinkRequestText));
    }

    internal void ShowLinkRequest(Func<string>? request)
    {
        _linkRequest = request;
        OnPropertyChanged(nameof(LinkRequestText));
    }

    partial void OnPendingPlanChanged(InstallPlan? value) => ForgetLinkRequestWhenDone();

    partial void OnChoicesChanged(InstallChoices? value) => ForgetLinkRequestWhenDone();

    private void ForgetLinkRequestWhenDone()
    {
        if (PendingPlan is null && Choices is null && _linkRequest is not null)
            ShowLinkRequest(null);
    }

    /// <summary>
    /// The whole row is this command, so it stays executable while the page
    /// loads, because a command that cannot execute greys out every control on
    /// the row. The flag takes over the job of dropping a second click.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenAsync()
    {
        if (_isOpening)
            return;

        _isOpening = true;
        try
        {
            await _owner.OpenContentAsync(this);
        }
        finally
        {
            _isOpening = false;
        }
    }

    [RelayCommand]
    private Task ChangeVersionAsync() => _owner.OpenContentVersionsAsync(this);

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallAsync(this);

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmInstallAsync(this);

    [RelayCommand]
    private void CancelInstall() => MainViewModel.CancelInstall(this);

    [RelayCommand(CanExecute = nameof(CanRemove))]
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

/// <summary>
/// One row of the Category filter, a curated tag or Other.
/// </summary>
public sealed partial class DiscoverCategory : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly CuratedTag? _tag;

    public string? Tag => _tag?.Tag;

    public bool IsOther => _tag is null;

    public string Name => _tag is null ? _owner.Localization.DiscoverCategoryOther : _owner.CategoryName(_tag.Tag, _tag.Name);

    public string? Meaning => _tag?.Meaning;

    [ObservableProperty]
    private bool _isSelected;

    public DiscoverCategory(MainViewModel owner, CuratedTag? tag)
    {
        _owner = owner;
        _tag = tag;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(Name));
}

public sealed record GameVersionOption(string Text, int Revision);
