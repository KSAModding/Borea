using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.History;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Licenses;
using Borea.Core.ModLoaders;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Preferences;
using Borea.Core.Preview;
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

    /// <summary>The share of compatible rows, in percent, at which those rows leave out their Compatible chip.</summary>
    private const int CommonCompatibilityPercent = 90;

    private IReadOnlyList<DiscoverItem> _listings = [];
    private GameVersion? _compatibilityGame;

    /// <summary>The loader setting and loader bounds of the active instance that the chips were checked against, see <see cref="LoaderInputsOf"/>.</summary>
    private string _compatibilityLoaderInputs = string.Empty;

    /// <summary>The loader checks of the last compatibility refresh, or null when the game is unknown or Borea holds no index.</summary>
    private LoaderChecks? _loaderChecks;
    private Task? _discoverLoad;
    private CuratedTagVocabulary _categoryVocabulary = CuratedTagVocabulary.Empty;
    private DiscoverSortOrder? _discoverSort;

    internal CuratedTagVocabulary TagVocabulary { get; private set; } = CuratedTagVocabulary.Empty;

    public ObservableCollection<DiscoverItem> DiscoverItems { get; } = [];

    public ObservableCollection<DiscoverChoice> OsOptions { get; } = [];

    /// <summary>The groups of the License filter that the entries of the current tab fall in, and every chosen group that the tab does not use.</summary>
    public ObservableCollection<DiscoverLicenseGroup> LicenseGroupOptions { get; } = [];

    /// <summary>The groups the License filter holds. A listing shows when its license falls in one of them.</summary>
    public ObservableCollection<DiscoverLicenseGroup> SelectedLicenseGroups { get; } = [];

    /// <summary>
    /// The exact licenses of the current tab, the most used first, and every chosen license that the tab does not use.
    /// These are the licenses that put the entries in their groups, so a group chosen shows the licenses of the line under that group.
    /// </summary>
    public ObservableCollection<LicenseChoice> LicenseOptions { get; } = [];

    /// <summary>
    /// The exact licenses the License filter holds. A listing shows when one of them puts its license in its group, and when a group
    /// is chosen too, its license must also fall in a chosen group.
    /// </summary>
    public ObservableCollection<string> SelectedLicenses { get; } = [];

    /// <summary>The text on the closed box of exact licenses, which names the chosen license, or how many are chosen.</summary>
    public string LicenseFilterText => SelectedLicenses.Count switch
    {
        0 => Localization.DiscoverLicenseExact,
        1 => SelectedLicenses[0],
        var count => Localization.FormatDiscoverLicensesChosen(count),
    };

    /// <summary>The line above the exact licenses, which says whether they narrow the chosen groups or filter on their own.</summary>
    public string LicenseListHint => SelectedLicenseGroups.Count == 0 ? Localization.DiscoverLicenseListAll : Localization.DiscoverLicenseListInGroups;

    public ObservableCollection<DiscoverCategory> CategoryOptions { get; } = [];

    public ObservableCollection<DiscoverCategory> SelectedCategories { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNothingSharedYet))]
    private bool _isDiscoverLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNothingSharedYet))]
    private string? _discoverError;

    /// <summary>Which tab of the inner nav bar is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModsTab))]
    [NotifyPropertyChangedFor(nameof(IsLoadersTab))]
    [NotifyPropertyChangedFor(nameof(IsModpacksTab))]
    [NotifyPropertyChangedFor(nameof(IsVehiclesTab))]
    [NotifyPropertyChangedFor(nameof(IsSavesTab))]
    [NotifyPropertyChangedFor(nameof(ShowsCategories))]
    [NotifyPropertyChangedFor(nameof(ShowsModAuthors))]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private ContentType _discoverType = ContentType.Mod;

    public bool IsModsTab => DiscoverType == ContentType.Mod;

    public bool IsLoadersTab => DiscoverType == ContentType.ModLoader;

    public bool IsVehiclesTab => DiscoverType == ContentType.Vehicle;

    public bool IsSavesTab => DiscoverType == ContentType.Save;

    /// <summary>Vehicles and saves have free-form tags only, so their tabs show no categories.</summary>
    public bool ShowsCategories => !DiscoverType.IsVehicleOrSave();

    /// <summary>The panel for mod authors asks for a mod, so the vehicle and save tabs leave it out.</summary>
    public bool ShowsModAuthors => !DiscoverType.IsVehicleOrSave();

    /// <summary>The tab of vehicles or saves offers a share only while the schema in use accepts its type.</summary>
    public bool ShowsDiscoverShare => DiscoverType switch
    {
        ContentType.Vehicle => VehiclesSection.CanShare,
        ContentType.Save => SavesSection.CanShare,
        _ => false,
    };

    public string DiscoverShareHeading => IsSavesTab ? Localization.DiscoverShareSaveHeading : Localization.DiscoverShareVehicleHeading;

    public string DiscoverShareText => IsSavesTab ? Localization.DiscoverShareSaveText : Localization.DiscoverShareVehicleText;

    public string DiscoverShareAction => IsSavesTab ? Localization.DiscoverShareSave : Localization.DiscoverShareVehicle;

    /// <summary>Whether the tab of vehicles or saves has no listing in an index that the last refresh reached, because an older copy cannot say that nothing is shared.</summary>
    public bool ShowsNothingSharedYet => DiscoverType.IsVehicleOrSave() && DiscoverTotal == 0 && !IsDiscoverLoading && DiscoverError is null && !IsIndexUnreachable && !IsIndexStale;

    public string DiscoverNothingSharedText => IsSavesTab ? Localization.DiscoverNoSaves : Localization.DiscoverNoVehicles;

    public string DiscoverNothingSharedHint => IsSavesTab ? Localization.DiscoverNoSavesHint : Localization.DiscoverNoVehiclesHint;

    /// <summary>The tab has listings and shows none, which only a search or a filter can cause.</summary>
    public bool ShowsClearSearchAndFilters => !HasDiscoverItems && DiscoverTotal > 0;

    /// <summary>The search text of the shown tab. Each tab keeps its own, so a search for a mod does not hide the vehicles.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The search text of each tab, so that a tab shows its own search again when the player comes back to it.</summary>
    private readonly Dictionary<ContentType, string> _searchByTab = [];

    /// <summary>True while a tab change puts the search of the new tab in the field, which filters the list only once.</summary>
    private bool _showingTabSearch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private bool _hideInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private bool _hideIncompatible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private bool _favoritesOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private bool _installedInOtherInstances;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    private string? _selectedOs;

    /// <summary>The author whose content Discover shows, chosen from an author name on a row or a side panel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiscoverFilters))]
    [NotifyPropertyChangedFor(nameof(SelectedAuthorText))]
    private string? _selectedAuthor;

    public string? SelectedAuthorText => SelectedAuthor is { } author ? Localization.FormatDiscoverAuthorFilter(author) : null;

    /// <summary>The text before the author links of a row, "by" in English.</summary>
    public string ByAuthorBefore => Localization.SplitContentByAuthor().Before.Trim();

    public string ByAuthorAfter => Localization.SplitContentByAuthor().After.Trim();

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

    public bool HasDiscoverFilters => HideInstalled || HideIncompatible || FavoritesOnly || InstalledInOtherInstances || SelectedOs is not null || SelectedAuthor is not null || SelectedLicenseGroups.Count > 0 || SelectedLicenses.Count > 0 || (ShowsCategories && SelectedCategories.Count > 0) || HasGameVersionRange;

    /// <summary>The Sort by choice of every tab except Loaders, the saved one until the menu or a link on Home changes it.</summary>
    public DiscoverSortOrder DiscoverSort => _discoverSort ?? _appPreferences.DiscoverSortOrder;

    public string DiscoverSortText => DiscoverSort switch
    {
        DiscoverSortOrder.RecentlyUpdated => Localization.DiscoverSortRecentlyUpdated,
        DiscoverSortOrder.Name => Localization.DiscoverSortName,
        DiscoverSortOrder.RecentlyListed => Localization.DiscoverSortRecentlyListed,
        _ => Localization.DiscoverSortPopularity,
    };

    public bool HasDiscoverItems => DiscoverItems.Count > 0 || DiscoverPacks.Count > 0;

    public string? DiscoverCountText
    {
        get
        {
            if (DiscoverTotalText is not { } totalText)
                return null;

            var shown = IsModpacksTab ? DiscoverPacks.Count : DiscoverItems.Count;
            return HasDiscoverFilters || !string.IsNullOrWhiteSpace(SearchText) ? Localization.FormatDiscoverCountShown(shown, totalText) : totalText;
        }
    }

    /// <summary>The placeholder of the search field, which names what the tab holds once the listings are loaded.</summary>
    public string DiscoverSearchPlaceholderText => DiscoverTotalText is { } totalText
        ? Localization.FormatDiscoverSearchCount(totalText)
        : DiscoverType switch
        {
            ContentType.Vehicle => Localization.DiscoverSearchVehicles,
            ContentType.Save => Localization.DiscoverSearchSaves,
            _ => Localization.DiscoverSearchPlaceholder,
        };

    private int DiscoverTotal => IsModpacksTab ? _packs.Count : _listings.Count(item => item.Type == DiscoverType);

    /// <summary>How many entries the tab holds, such as "3 mods", or null when it holds none.</summary>
    private string? DiscoverTotalText
    {
        get
        {
            var total = DiscoverTotal;
            if (total == 0)
                return null;

            return DiscoverType switch
            {
                ContentType.ModLoader => Localization.FormatDiscoverLoaderCount(total),
                ContentType.ModPack => Localization.FormatDiscoverPackCount(total),
                ContentType.Vehicle => Localization.FormatDiscoverVehicleCount(total),
                ContentType.Save => Localization.FormatDiscoverSaveCount(total),
                _ => Localization.FormatDiscoverModCount(total),
            };
        }
    }

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
            _shareHosts = snapshot.ShareHosts;

            // the download counts and the first release dates sit next to a listing in the snapshot, not inside it
            var listingEntries = new Dictionary<string, ContentIndexListing>(ModIds.Comparer);
            foreach (var entry in snapshot.Listings.Concat(snapshot.Vehicles).Concat(snapshot.Saves))
                listingEntries.TryAdd(entry.Id, entry);
            var packEntries = new Dictionary<string, ContentIndexPack>(ModIds.Comparer);
            foreach (var entry in snapshot.Packs)
                packEntries.TryAdd(entry.Id, entry);

            await EnsureFavoritesLoadedAsync(services);
            var listings = await services.ContentIndex.GetAvailableModsAsync();
            var content = await services.UserContent.GetAvailableAsync();
            _listings = listings.Concat(content)
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
                OsOptions.Add(new DiscoverChoice(os));
            MarkChoice(OsOptions, SelectedOs);

            LoadLicenseOptions();

            LoadCategoryOptions(listings, packs);
            LoadGameVersionOptions(snapshot.GameVersions);

            await RefreshCompatibilityAsync(GameVersionOf(services, _activeInstanceEntity));

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
        if (FavoritesOnly)
            filtered = filtered.Where(item => item.IsFavorite);
        if (InstalledInOtherInstances)
            filtered = filtered.Where(item => item.IsInOtherInstance);
        if (SelectedOs is not null)
            filtered = filtered.Where(item => item.SupportsOs(SelectedOs));
        if (SelectedAuthor is { } author)
            filtered = filtered.Where(item => item.Listing.Authors.Contains(author, StringComparer.OrdinalIgnoreCase));
        if (SelectedLicenseGroups.Count > 0 || SelectedLicenses.Count > 0)
            filtered = filtered.Where(item => MatchesLicenseFilter(item.License));
        if (HasGameVersionRange)
            filtered = filtered.Where(item => item.LatestInChannel is { } release
                && Borea.Core.Game.Compatibility.SupportsAnyBuild(release.GameMinRevision, release.GameMaxRevision, DiscoverGameMin?.Revision, DiscoverGameMax?.Revision));
        if (ShowsCategories && SelectedCategories.Count > 0)
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
        OnPropertyChanged(nameof(DiscoverCountText));
        OnPropertyChanged(nameof(DiscoverSearchPlaceholderText));
        OnPropertyChanged(nameof(ShowsNothingSharedYet));
        OnPropertyChanged(nameof(DiscoverNothingSharedText));
        OnPropertyChanged(nameof(DiscoverNothingSharedHint));
        OnPropertyChanged(nameof(ShowsClearSearchAndFilters));
        OnPropertyChanged(nameof(ShowsDiscoverShare));
        OnPropertyChanged(nameof(DiscoverShareHeading));
        OnPropertyChanged(nameof(DiscoverShareText));
        OnPropertyChanged(nameof(DiscoverShareAction));
    }

    /// <summary>
    /// Compatible when almost every row of the list is compatible, so those rows leave out a chip that says the same
    /// on each of them, or null when every row shows its chip. An Incompatible or Unknown chip always shows, because it
    /// warns about the row even when every row has it.
    /// </summary>
    private static GameCompatibility? CommonCompatibility(IReadOnlyCollection<GameCompatibility> states)
    {
        if (states.Count < CommonCompatibilityMinimumRows)
            return null;

        var compatible = states.Count(state => state == GameCompatibility.Compatible);
        return compatible * 100 >= states.Count * CommonCompatibilityPercent ? GameCompatibility.Compatible : null;
    }

    /// <summary>The Loaders tab has no Sort by dropdown, so it always shows the most downloaded loader first.</summary>
    private IEnumerable<DiscoverItem> SortDiscover(IEnumerable<DiscoverItem> items) => (IsLoadersTab ? DiscoverSortOrder.Popularity : DiscoverSort) switch
    {
        DiscoverSortOrder.Popularity => MostDownloadedFirst(items),
        DiscoverSortOrder.RecentlyUpdated => items.OrderBy(item => item.UpdatedAt is null).ThenByDescending(item => item.UpdatedAt).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
        DiscoverSortOrder.RecentlyListed => items.OrderBy(item => item.ListedAt is null).ThenByDescending(item => item.ListedAt).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
        _ => items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
    };

    /// <summary>A listing without a download count comes after every listing with one, and the name breaks a tie.</summary>
    private static IOrderedEnumerable<DiscoverItem> MostDownloadedFirst(IEnumerable<DiscoverItem> items)
        => items.OrderBy(item => item.Downloads is null).ThenByDescending(item => item.Downloads).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>A reload keeps a chosen bound while its build is still listed.</summary>
    private void LoadGameVersionOptions(ContentIndexGameVersions? gameVersions)
    {
        var options = GameVersionOptionsOf(gameVersions?.Versions ?? []);
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

    /// <summary>The builds that parse, newest first.</summary>
    internal static List<GameVersionOption> GameVersionOptionsOf(IEnumerable<string> versions) =>
        [.. versions
            .Select(text => GameVersion.TryParse(text, out var version) ? new GameVersionOption(text, version.Revision) : null)
            .OfType<GameVersionOption>()
            .OrderByDescending(option => option.Revision)];

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
    /// Marks listings that the active instance already holds, and those that another instance holds.
    /// </summary>
    private void RefreshInstalledFlags()
    {
        var installed = new HashSet<string>(ActiveInstance is { } active ? active.ModIds.Concat(active.ContentIds) : [], ModIds.Comparer);
        var installedElsewhere = new HashSet<string>(OtherInstances.SelectMany(instance => instance.ModIds.Concat(instance.ContentIds)), ModIds.Comparer);
        var elsewhereChanged = false;
        foreach (var item in _listings)
        {
            item.IsInstalled = installed.Contains(item.ModId);
            var mod = _activeInstanceEntity?.Mods.FirstOrDefault(entry => ModIds.Equals(entry.ModId, item.ModId));
            item.RemoveBlockedText = item.IsInstalled ? RemoveBlockedReason(_activeInstanceEntity, mod) : null;
            item.NeededByText = mod is not null && _activeInstanceEntity is { } entity ? ContentNeededByText(entity, mod.ModId) : null;
            var inOtherInstance = installedElsewhere.Contains(item.ModId);
            elsewhereChanged |= item.IsInOtherInstance != inOtherInstance;
            item.IsInOtherInstance = inOtherInstance;
        }
        foreach (var release in _contentReleases)
            release.RefreshInstalled(ActiveInstance);
        RefreshInstalledWhereNotices();
        elsewhereChanged |= RefreshPackInstalledFlags();
        RefreshContentDependencies();

        // the list is filtered again only when another instance changed,
        // so a row the user just added stays under Hide already installed
        if (elsewhereChanged && InstalledInOtherInstances)
            ApplyDiscoverFilters();
    }

    partial void OnDiscoverTypeChanged(ContentType value)
    {
        LoadLicenseOptions();
        _showingTabSearch = true;
        try
        {
            SearchText = _searchByTab.GetValueOrDefault(value, string.Empty);
        }
        finally
        {
            _showingTabSearch = false;
        }

        ApplyDiscoverFilters();
        if (value.IsVehicleOrSave())
            StartShareSchemaLoad();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchByTab[DiscoverType] = value;
        if (!_showingTabSearch)
            ApplyDiscoverFilters();
    }

    partial void OnHideInstalledChanged(bool value) => ApplyDiscoverFilters();

    partial void OnHideIncompatibleChanged(bool value) => ApplyDiscoverFilters();

    partial void OnFavoritesOnlyChanged(bool value) => ApplyDiscoverFilters();

    partial void OnInstalledInOtherInstancesChanged(bool value) => ApplyDiscoverFilters();

    /// <summary>
    /// Evaluates every listing against the build of the active instance, which is where Add installs, or against the
    /// game the settings point at when no instance is active, for example after the game directory changed.
    /// </summary>
    internal Task RefreshCompatibilityAsync()
        => _services is null ? Task.CompletedTask : RefreshCompatibilityAsync(GameVersionOf(_services, _activeInstanceEntity));

    /// <summary>
    /// Evaluates the newest release of every index listing and every row of
    /// the Versions table against <paramref name="installed"/> (RFC 0017). A
    /// listing from another source stays unknown, because its releases would
    /// have to be fetched one by one. A pack is evaluated by the bounds of its
    /// newest usable version and of the releases that version pins.
    /// </summary>
    internal async Task RefreshCompatibilityAsync(GameVersion? installed)
    {
        if (_services is null)
            return;

        _compatibilityGame = installed;
        _compatibilityLoaderInputs = LoaderInputsOf(_activeInstanceEntity);
        _loaderChecks = installed is { } game && await HeldLoaderReleasesAsync(_services) is { } held
            ? new LoaderChecks(_activeInstanceEntity, held, _services.Settings.ReleaseChannel, game)
            : null;
        var previewBuild = PreviewStillBuild(_services);
        foreach (var item in _listings)
        {
            var latest = item.Source != "index" ? null
                : item.Type.IsVehicleOrSave() ? await _services.UserContent.GetLatestReleaseAsync(item.ModId, _services.Settings.ReleaseChannel)
                : await _services.ContentIndex.GetLatestReleaseInChannelAsync(item.ModId, _services.Settings.ReleaseChannel);
            (item.Compatibility, item.LoaderProblem) = latest is null ? (GameCompatibility.Unknown, null) : await CompatibilityOfAsync(latest, installed);

            // the age on the row belongs to the same release as the chip and Add, so a newer release in another channel does not show there
            item.UpdatedAt = latest?.ReleaseDate;
            item.LatestInChannel = latest;

            // only a lookup of a stored file, because listing a vehicle fetches nothing, reads no part and plans nothing
            ShowStoredStill(item, _services.PreviewThumbnails, previewBuild);
        }

        RefreshPreviewStills();

        // the Required by and Conflicts with cards read the releases set here, and a page without a release in the channel has nothing else that refreshes it
        RefreshContentDependencies();

        foreach (var release in _contentReleases)
            (release.Compatibility, release.LoaderProblem) = await CompatibilityOfAsync(release.Release, installed);

        if (_packs.Count > 0)
        {
            // the newer releases of the members come from the snapshot alone, so every player sees the same count (RFC 0080)
            var snapshot = await _services.IndexSnapshots.GetSnapshotAsync();
            foreach (var pack in _packs)
            {
                pack.ShowCompatibility(await PinnedReleasesAsync(_services.ContentIndex, pack.Metadata), installed, _gameReleases);
                pack.NewerMembers = ModPackMemberReleases.Find(pack.Metadata, snapshot);
            }
        }

        foreach (var member in PackMembers)
            member.RefreshCompatibility(installed);

        ShowNewerMembers();
        ApplyDiscoverFilters();
    }

    /// <summary>The build whose stills the rows show, which is the build of the active instance, the same as for their chips.</summary>
    private string? PreviewStillBuild(BoreaServices services) =>
        PreviewGameBuild.Of(_activeInstanceEntity is { } active ? services.InstanceGames.Resolve(active).Version : services.InstalledVersion.GetInstalledVersion());

    /// <summary>Shows the still that the vehicle page just stored on every Discover row of that vehicle archive.</summary>
    private void ShowPreviewStill(string vehicleSha256)
    {
        if (_services is not { } services)
            return;

        var build = PreviewStillBuild(services);
        foreach (var item in _listings)
        {
            if (item.Type != ContentType.Vehicle || !string.Equals(item.LatestInChannel?.Download.Sha256, vehicleSha256, StringComparison.OrdinalIgnoreCase))
                continue;

            // a new still has the path of the old one, so the row lets go of the old bitmap first
            item.PreviewStillPath = null;
            ShowStoredStill(item, services.PreviewThumbnails, build);
        }

        foreach (var tile in _newVehicles.Items.Where(tile => string.Equals(tile.VehicleSha256, vehicleSha256, StringComparison.OrdinalIgnoreCase) && tile.Icon is null))
        {
            tile.StillPath = null;
            tile.StillPath = services.PreviewThumbnails.TryGetPath(vehicleSha256, build);
        }
    }

    /// <summary>
    /// How <paramref name="release"/> fits the active instance on <paramref name="game"/>. Besides the game bounds, a release is
    /// incompatible when no version of the loader it needs can start that instance, and then the warning says why.
    /// </summary>
    private async Task<(GameCompatibility Compatibility, PlanningMessage? LoaderProblem)> CompatibilityOfAsync(ModVersionMetadata release, GameVersion? game)
    {
        var compatibility = Borea.Core.Game.Compatibility.Evaluate(release, game);
        if (compatibility == GameCompatibility.Incompatible || release.Loader is null || _loaderChecks is not { } checks)
            return (compatibility, null);

        return LoaderNeedPlanner.Warning(release, await checks.ProblemAsync(release)) is { } warning
            ? (GameCompatibility.Incompatible, warning)
            : (compatibility, null);
    }

    /// <summary>What the loader checks read of the active instance, so a change of its mods or its loader setting checks the chips again.</summary>
    private static string LoaderInputsOf(Instance? instance) => instance is null
        ? string.Empty
        : $"{instance.Loader.LoaderId} {instance.Loader.Version}|" + string.Join("|", LoaderNeed.For(instance).Loaders
            .SelectMany(loader => loader.Requirements.Select(requirement => $"{loader.LoaderId} {requirement.ModId} {requirement.MinVersion} {requirement.MaxVersion}")));

    /// <summary>
    /// The loader checks of one compatibility refresh. They read the index that Borea holds, so a row asks the network for nothing,
    /// and each range of a loader is checked once, because most mods share their bounds.
    /// </summary>
    private sealed class LoaderChecks
    {
        private readonly Instance? _instance;
        private readonly IModRepository _mods;
        private readonly ReleaseChannel _channel;
        private readonly GameVersion _game;
        private readonly Dictionary<(string LoaderId, ModVersion Min, ModVersion? Max), LoaderVersionProblem> _answers = [];

        public LoaderChecks(Instance? instance, IModRepository mods, ReleaseChannel channel, GameVersion game)
        {
            _instance = instance;
            _mods = mods;
            _channel = channel;
            _game = game;
        }

        /// <summary>Why no loader version can start the instance with <paramref name="release"/> in place of its copy of that mod.</summary>
        public async Task<LoaderVersionProblem> ProblemAsync(ModVersionMetadata release)
        {
            var need = LoaderNeed.With(_instance, [release]).Loaders.Single(loader => ModIds.Equals(loader.LoaderId, release.Loader!.LoaderId));
            var key = (need.LoaderId.ToUpperInvariant(), need.MinVersion, need.MaxVersion);
            if (!_answers.TryGetValue(key, out var problem))
                _answers[key] = problem = await LoaderVersionChoice.CheckNeedAsync(_instance?.Loader ?? LoaderSetting.Automatic, need, _mods, _channel, _game);

            return problem;
        }
    }

    internal string CompatibilityText(GameCompatibility compatibility) => compatibility switch
    {
        GameCompatibility.Compatible => Localization.CompatibilityCompatible,
        GameCompatibility.Incompatible => Localization.CompatibilityIncompatible,
        _ => Localization.CompatibilityUnknown,
    };

    partial void OnSelectedOsChanged(string? value)
    {
        MarkChoice(OsOptions, value);
        ApplyDiscoverFilters();
    }

    partial void OnSelectedAuthorChanged(string? value) => ApplyDiscoverFilters();

    private static void MarkChoice(IEnumerable<DiscoverChoice> choices, string? selected)
    {
        foreach (var choice in choices)
            choice.IsSelected = string.Equals(choice.Value, selected, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<LicenseGroup> ChosenLicenseGroups => SelectedLicenseGroups.Select(chosen => chosen.Group);

    private bool MatchesLicenseFilter(string license) =>
        LicenseFilter.InGroups(license, ChosenLicenseGroups) && LicenseFilter.Matches(license, SelectedLicenses);

    private void MarkLicenses()
    {
        var groups = ChosenLicenseGroups.ToHashSet();
        foreach (var option in LicenseGroupOptions)
            option.IsSelected = groups.Contains(option.Group);
        foreach (var choice in LicenseOptions)
            choice.IsSelected = SelectedLicenses.Contains(choice.Value, StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(LicenseFilterText));
        OnPropertyChanged(nameof(LicenseListHint));
    }

    /// <summary>
    /// Fills the License filter with the groups and the licenses of the current tab, so that each count is a number of rows
    /// that the tab can show. A chosen group or license that the tab does not use stays with no rows, so that a click can
    /// still take it away.
    /// </summary>
    private void LoadLicenseOptions()
    {
        var expressions = LicensesOfTab();
        var groups = LicenseFilter.GroupOptions(expressions).ToList();
        groups.AddRange(ChosenLicenseGroups
            .Where(group => groups.All(option => option.Group != group))
            .Select(group => new LicenseGroupOption(group, 0, [])));

        LicenseGroupOptions.Clear();
        foreach (var option in groups.OrderBy(option => option.Group))
            LicenseGroupOptions.Add(new DiscoverLicenseGroup(this, option));
        ShowExactLicenses(ExactLicensesOfTab(expressions));
    }

    private List<string> LicensesOfTab() => DiscoverType == ContentType.ModPack
        ? [.. _packs.Select(pack => pack.License)]
        : [.. _listings.Where(item => item.Type == DiscoverType).Select(item => item.License)];

    private List<LicenseOption> ExactLicensesOfTab(IEnumerable<string> expressions)
    {
        var groups = ChosenLicenseGroups.ToList();
        return [.. LicenseFilter.Options(expressions.Where(expression => LicenseFilter.InGroups(expression, groups)))];
    }

    private void ShowExactLicenses(IReadOnlyList<LicenseOption> offered)
    {
        var unused = SelectedLicenses
            .Where(license => !offered.Any(option => string.Equals(option.License, license, StringComparison.OrdinalIgnoreCase)))
            .Select(license => new LicenseOption(license, 0))
            .ToList();

        LicenseOptions.Clear();
        foreach (var option in offered.Concat(unused))
            LicenseOptions.Add(new LicenseChoice(option.License, option.Count));
        MarkLicenses();
    }

    internal void RefreshLicenseText()
    {
        foreach (var option in LicenseGroupOptions.Union(SelectedLicenseGroups))
            option.RefreshText();
        OnPropertyChanged(nameof(LicenseFilterText));
        OnPropertyChanged(nameof(LicenseListHint));
    }

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
    private void ShowDiscoverVehicles() => DiscoverType = ContentType.Vehicle;

    [RelayCommand]
    private void ShowDiscoverSaves() => DiscoverType = ContentType.Save;

    // choosing the chosen one again, or null from its chip, clears the filter
    [RelayCommand]
    private void SelectOs(string? os) => SelectedOs = string.Equals(os, SelectedOs, StringComparison.OrdinalIgnoreCase) ? null : os;

    /// <summary>Shows Discover with only the content of the author, on the tab of the row or page that names the author.</summary>
    [RelayCommand]
    private void FilterByAuthor(AuthorLink? link)
    {
        if (link is null)
            return;

        SelectedAuthor = link.Name;
        OpenDiscoverTab(link.Type);
    }

    internal void OpenDiscoverTab(ContentType type)
    {
        DiscoverType = type;
        SetMainWindowDiscover();
    }

    /// <summary>Shows a tab in the order of a Home group. The saved Sort by choice stays, because only the menu on Discover changes it.</summary>
    internal void OpenDiscoverTab(ContentType type, DiscoverSortOrder order)
    {
        ShowDiscoverSort(order);
        OpenDiscoverTab(type);
    }

    [RelayCommand]
    private void ClearSelectedAuthor() => SelectedAuthor = null;

    [RelayCommand]
    private void ToggleLicense(string? license)
    {
        if (license is null)
            return;

        if (SelectedLicenses.FirstOrDefault(selected => string.Equals(selected, license, StringComparison.OrdinalIgnoreCase)) is { } chosen)
            SelectedLicenses.Remove(chosen);
        else
            SelectedLicenses.Add(license);

        MarkLicenses();
        OnPropertyChanged(nameof(HasDiscoverFilters));
        ApplyDiscoverFilters();
    }

    // a reload builds new rows, so a chip that stays from before is found by its group and not by its row
    [RelayCommand]
    private void ToggleLicenseGroup(LicenseGroup? group)
    {
        if (group is null)
            return;

        if (SelectedLicenseGroups.FirstOrDefault(chosen => chosen.Group == group) is { } chip)
        {
            SelectedLicenseGroups.Remove(chip);

            // a group that the tab does not use only stays so that a click can take it away, so it leaves the list with that click
            if (LicenseGroupOptions.FirstOrDefault(option => option.Group == group) is { Count: 0 } unused)
                LicenseGroupOptions.Remove(unused);
        }
        else
            SelectedLicenseGroups.Add(LicenseGroupOptions.FirstOrDefault(option => option.Group == group) ?? new DiscoverLicenseGroup(this, new LicenseGroupOption(group.Value, 0, [])));

        // an exact license that is not among the licenses of the chosen groups would hide every row, so the groups win and it leaves the filter
        var offered = ExactLicensesOfTab(LicensesOfTab());
        foreach (var license in SelectedLicenses.Where(license => !offered.Any(option => string.Equals(option.License, license, StringComparison.OrdinalIgnoreCase))).ToList())
            SelectedLicenses.Remove(license);
        ShowExactLicenses(offered);

        OnPropertyChanged(nameof(HasDiscoverFilters));
        ApplyDiscoverFilters();
    }

    [RelayCommand]
    private void ClearLicenses()
    {
        SelectedLicenses.Clear();
        MarkLicenses();
        OnPropertyChanged(nameof(HasDiscoverFilters));
        ApplyDiscoverFilters();
    }

    [RelayCommand]
    private void ClearDiscoverGameVersionRange()
    {
        DiscoverGameMin = null;
        DiscoverGameMax = null;
    }

    [RelayCommand]
    private void ClearDiscoverGameMin() => DiscoverGameMin = null;

    [RelayCommand]
    private void ClearDiscoverGameMax() => DiscoverGameMax = null;

    [RelayCommand]
    private void ClearHideInstalled() => HideInstalled = false;

    [RelayCommand]
    private void ClearHideIncompatible() => HideIncompatible = false;

    [RelayCommand]
    private void ClearFavoritesOnly() => FavoritesOnly = false;

    [RelayCommand]
    private void ClearInstalledInOtherInstances() => InstalledInOtherInstances = false;

    [RelayCommand]
    private void SelectDiscoverSort(DiscoverSortOrder order)
    {
        if (order == DiscoverSort && order == _appPreferences.DiscoverSortOrder)
            return;

        ShowDiscoverSort(order);
        QueuePreferenceSave(preferences => preferences.WithDiscoverSortOrder(order));
    }

    private void ShowDiscoverSort(DiscoverSortOrder order)
    {
        _discoverSort = order;
        OnPropertyChanged(nameof(DiscoverSort));
        OnPropertyChanged(nameof(DiscoverSortText));
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
        FavoritesOnly = false;
        InstalledInOtherInstances = false;
        SelectedOs = null;
        SelectedAuthor = null;

        // a chosen group narrows the exact licenses, so only then does their list have to come back whole
        var narrowed = SelectedLicenseGroups.Count > 0;
        SelectedLicenseGroups.Clear();
        SelectedLicenses.Clear();
        if (narrowed)
            LoadLicenseOptions();
        else
            MarkLicenses();
        DiscoverGameMin = null;
        DiscoverGameMax = null;
        foreach (var category in SelectedCategories)
            category.IsSelected = false;
        SelectedCategories.Clear();
        OnPropertyChanged(nameof(HasDiscoverFilters));
        ApplyDiscoverFilters();
    }

    [RelayCommand]
    private void ClearDiscoverSearchAndFilters()
    {
        SearchText = string.Empty;
        ClearDiscoverFilters();
    }

    /// <summary>
    /// Plans the install of the newest release into the active instance.
    /// </summary>
    internal async Task InstallAsync(DiscoverItem item)
    {
        if (_services is not { } services)
            return;

        if (!item.Type.IsVehicleOrSave())
        {
            await PlanInstallAsync(item, () => services.Mods.GetLatestReleaseAsync(item.ModId), exactVersion: null);
            return;
        }

        if (ActiveInstance is not { } instance)
        {
            ShowInstanceHintToast();
            return;
        }

        // the row offers the release of the saved channel, so Add installs that one
        if ((item.LatestInChannel ?? await services.UserContent.GetLatestReleaseAsync(item.ModId, services.Settings.ReleaseChannel)) is { } release)
            await PlanContentAsync(item, instance.InstanceId, release);
        else
            item.InstallError = Localization.DiscoverNoRelease;
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

        if (item.Type.IsVehicleOrSave())
        {
            await RemoveRecordedContentAsync(item, instance);
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
public sealed partial class DiscoverItem : ObservableObject, IContentPlanRow
{
    private readonly MainViewModel _owner;
    private ModMetadata _listing;

    public string ModId => _listing.ModId;

    public string Name => _listing.Name;

    public string Abstract => _listing.Abstract;

    public string License => _listing.License;

    public ContentType Type => _listing.Type;

    public bool IsVehicle => Type == ContentType.Vehicle;

    public bool IsSave => Type == ContentType.Save;

    public bool IsVehicleOrSave => Type.IsVehicleOrSave();

    /// <summary>The curated tags in the words and order of the vocabulary, then the free-form tags.</summary>
    public IReadOnlyList<string> AllTags { get; private set; }

    internal ModMetadata Listing => _listing;

    private ModVersionMetadata? _latestInChannel;

    /// <summary>The release Add installs, which the age on the row belongs to.</summary>
    internal ModVersionMetadata? LatestInChannel
    {
        get => _latestInChannel;
        set
        {
            _latestInChannel = value;
            OnPropertyChanged(nameof(GameVersionText));
        }
    }

    public string? Description => _listing.Description;

    public string Source => _listing.Source;

    public IReadOnlyDictionary<string, string> Links => _listing.Links;

    /// <summary>
    /// ">= min" or "min - max", as the compatibility chip shows it. A vehicle or save states no range of
    /// its own, so it shows the game build its file was saved in.
    /// </summary>
    public string GameVersionText => MainViewModel.GameBoundsText(GameMin, _listing.GameMax);

    // A vehicle or save may author only game_max, and then its release gives the lower bound.
    private string? GameMin => _listing.GameMin ?? LatestInChannel?.GameMin;

    public string TypeText => Type switch
    {
        ContentType.ModLoader => _owner.Localization.ContentTypeModLoader,
        ContentType.Vehicle => _owner.Localization.ContentTypeVehicle,
        ContentType.Save => _owner.Localization.ContentTypeSave,
        _ => _owner.Localization.ContentTypeMod,
    };

    public IReadOnlyList<AuthorLink> AuthorLinks => AuthorLink.Of(_listing.Authors, Type, _owner.Localization);

    public ListingImage? Icon { get; }

    /// <summary>
    /// The still of a vehicle without an icon, which the page of the vehicle stored or the row drew. Null for other
    /// content and when no still is stored for the latest release and the installed game build.
    /// </summary>
    [ObservableProperty]
    private string? _previewStillPath;

    /// <summary>True when the still was drawn while mods that the vehicle needs were missing, so it shows boxes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewStillNote))]
    private bool _previewStillMissingMods;

    public string? PreviewStillNote => PreviewStillMissingMods ? _owner.Localization.DiscoverStillMissingMods : null;

    /// <summary>A listing icon always wins, so only a vehicle without one shows a still.</summary>
    internal bool HasDrawnStill => IsVehicle && Images?.Icon is null;

    /// <summary>Says that the row came near the window or moved in it, so the queue can draw its still.</summary>
    internal void ShowNearWindow(bool inView) => _owner.PreviewStills.Show(this, inView);

    /// <summary>Says that the row left the window, so its draw stops.</summary>
    internal void HideFromWindow() => _owner.PreviewStills.Hide(this);

    public string? IconAttribution => Icon?.Attribution;

    public string? IconSource => Icon?.Source;

    /// <summary>The live images of an index listing, which every description of the listing resolves against.</summary>
    internal ContentImages? Images { get; }

    /// <summary>The download count the content index reports, or null when it reports none.</summary>
    public long? Downloads => DownloadCounts?.Total;

    /// <summary>The download counts of the listing and its releases, or null when the index reports none.</summary>
    internal ListingDownloadCounts? DownloadCounts { get; }

    /// <summary>The count as the row shows it, "1.2k". The exact number is <see cref="DownloadsExactText"/>.</summary>
    public string? DownloadsText => Downloads is { } count ? MainViewModel.CompactCount(count) : null;

    public string? DownloadsExactText => Downloads is { } count ? _owner.Localization.FormatContentDownloadsExact(count.ToString("N0", CultureInfo.CurrentCulture)) : null;

    /// <summary>The date of the first release the index reports, or null when it reports none.</summary>
    public DateTimeOffset? PublishedAt { get; }

    public string? PublishedText => PublishedAt is { } at ? _owner.Localization.FormatContentPublished(_owner.AgeText(at)) : null;

    public string? PublishedDateText => PublishedAt is { } at ? MainViewModel.DateText(at) : null;

    /// <summary>When the listing came into the index, or null when the index does not say.</summary>
    public DateTimeOffset? ListedAt { get; }

    /// <summary>When the forums thread of the listing was found gone (RFC 0110), or null while it is fine.</summary>
    internal DateTimeOffset? ForumsUnavailableSince { get; }

    /// <summary>The warning of the listing page that Borea offers the listing for no new install, or null while the thread is fine.</summary>
    public string? ForumsGoneText => ForumsUnavailableSince is { } since ? _owner.Localization.FormatContentForumsGone(MainViewModel.DateText(since)) : null;

    /// <summary>The date of the newest release in the saved release channel, the one Add installs. Null when the channel has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText))]
    [NotifyPropertyChangedFor(nameof(UpdatedDateText))]
    private DateTimeOffset? _updatedAt;

    public string? UpdatedText => UpdatedAt is { } at ? _owner.ShortAgeText(at) : null;

    public string? UpdatedDateText => UpdatedAt is { } at ? _owner.Localization.FormatContentUpdatedOn(MainViewModel.DateText(at)) : null;

    /// <summary>
    /// How the newest release fits the installed game (RFC 0017).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityText))]
    [NotifyPropertyChangedFor(nameof(IsCompatible))]
    [NotifyPropertyChangedFor(nameof(IsIncompatible))]
    private GameCompatibility _compatibility = GameCompatibility.Unknown;

    public string CompatibilityText => _owner.CompatibilityText(Compatibility);

    public bool IsCompatible => Compatibility == GameCompatibility.Compatible;

    public bool IsIncompatible => Compatibility == GameCompatibility.Incompatible;

    /// <summary>The warning about the loader bounds that makes the newest release incompatible, or null when the loader is not the cause.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompatibilityToolTip))]
    private PlanningMessage? _loaderProblem;

    /// <summary>Why the loader makes the release incompatible, or null when the chip needs no tooltip.</summary>
    public string? CompatibilityToolTip => LoaderProblem is { } problem ? PlanningText.Message(problem) : null;

    /// <summary>False when most rows of the Discover list share this state, so the row leaves the chip out.</summary>
    [ObservableProperty]
    private bool _showsCompatibility = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyCanExecuteChangedFor(nameof(BeginRemoveCommand))]
    private bool _isInstalled;

    /// <summary>True when an instance other than the active one holds the mod.</summary>
    internal bool IsInOtherInstance { get; set; }

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

    private readonly OpeningGate _opening = new();

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
    [NotifyPropertyChangedFor(nameof(PlanSteps))]
    private InstallPlan? _pendingPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingInstall))]
    [NotifyPropertyChangedFor(nameof(ConfirmInstallText))]
    private InstallChoices? _choices;

    /// <summary>The vehicle or save install that waits for a confirmation, see <see cref="IContentPlanRow"/>.</summary>
    public Borea.Core.UserContent.UserContentPlan? PendingContentPlan { get; set; }

    public bool IsConfirmingInstall => PendingPlan is not null || Choices is not null;

    /// <summary>The button of the confirmation, with what the plan downloads: "Add (38.0 MB)".</summary>
    public string ConfirmInstallText => _owner.ConfirmInstallText(InstallWarning, PendingPlan);

    public string? AddedModsText => _owner.AddedModsText(PendingPlan, Choices);

    public string? AddedModsToolTip => _owner.AddedModsText(PendingPlan, Choices, all: true);

    /// <summary>The install steps of every listing the waiting plan installs, which the confirmation shows.</summary>
    public IReadOnlyList<StepList> PlanSteps => _owner.InstallStepsOf(PendingPlan);

    /// <summary>The install steps a finished install left on the row, until the player dismisses them.</summary>
    public IReadOnlyList<StepList> InstallStepsNotice => _owner.InstallStepsNotice(ModId);

    public bool HasInstallStepsNotice => InstallStepsNotice.Count > 0;

    public string? InstalledPlayText => _owner.InstalledPlayText(ModId);

    /// <summary>This row while it offers Play, which builds the button, or null.</summary>
    public DiscoverItem? InstalledPlayRow => InstalledPlayText is null ? null : this;

    public bool CanPlayInstalled => _owner.CanPlayInstalled(ModId);

    /// <summary>The install steps of the listing, for the Description tab.</summary>
    public StepList? InstallSteps => MainViewModel.Steps(_owner.Localization.ContentInstallSteps, _listing.Install?.Steps);

    /// <summary>The uninstall steps of the listing, for the Description tab.</summary>
    public StepList? UninstallSteps => MainViewModel.Steps(_owner.Localization.ContentUninstallSteps, _listing.Install?.Uninstall);

    /// <summary>The uninstall steps under what Borea removes itself, for the remove confirmation.</summary>
    public StepList? RemoveSteps => MainViewModel.Steps(_owner.Localization.ContentRemoveSteps, _listing.Install?.Uninstall);

    private Func<string>? _linkRequest;

    /// <summary>What a borea:// link asked for while its confirmation waits, naming the instance. Null otherwise.</summary>
    public string? LinkRequestText => _linkRequest?.Invoke();

    /// <summary>
    /// Mods, vehicles and saves install into an instance; a loader is set up from the settings. A listing whose forums thread is gone gets no new install (RFC 0110).
    /// </summary>
    public bool CanInstall => !IsInstalled && !IsInstalling && (Type == ContentType.Mod || Type.IsVehicleOrSave()) && ForumsUnavailableSince is null;

    /// <summary>
    /// A loader has no version to pick, because its Versions table offers no install button.
    /// </summary>
    public bool CanChangeVersion => Type == ContentType.Mod || Type.IsVehicleOrSave();

    /// <summary>The files the player changed in a recorded vehicle or save, which a remove found and names before the second go.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveConfirmText))]
    private string? _removeChanges;

    /// <summary>What the game does to the recorded vehicles and saves of the active instance that need the mod, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveConfirmText))]
    private string? _neededByText;

    public string RemoveConfirmText => RemoveChanges is { } files ? _owner.Localization.FormatContentRemoveChanged(Name, files)
        : NeededByText is { } needed ? $"{needed} {_owner.Localization.ContentRemoveConfirm}"
        : _owner.Localization.ContentRemoveConfirm;

    public bool CanRemove => IsInstalled && !IsRemoving && RemoveBlockedText is null;

    /// <summary>Why Remove is disabled, shown next to it. Null when the mod can be removed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(RemoveToolTip))]
    [NotifyCanExecuteChangedFor(nameof(BeginRemoveCommand))]
    private string? _removeBlockedText;

    public string RemoveToolTip => RemoveBlockedText ?? _owner.Localization.ContentRemove;

    public string InstalledAutomationName => _owner.Localization.FormatDiscoverInstalledMod(Name);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteText))]
    private bool _isFavorite;

    /// <summary>What the favorite toggle does, as its label and tooltip.</summary>
    public string FavoriteText => _owner.FavoriteText(IsFavorite);

    /// <param name="indexEntry">The snapshot entry of an index listing, for its download count and its dates. Null for any other listing.</param>
    public DiscoverItem(MainViewModel owner, ModMetadata listing, ContentIndexListing? indexEntry = null)
    {
        _owner = owner;
        _listing = listing;
        AllTags = DisplayTags(owner, listing.Type, listing.Tags);
        Images = indexEntry?.Images;
        Icon = owner.IconFor(Images?.Icon);
        DownloadCounts = indexEntry?.Downloads;
        PublishedAt = indexEntry?.PublishedAt;
        ListedAt = indexEntry?.ListedAt;
        ForumsUnavailableSince = indexEntry?.ForumsUnavailableSince;
        _isFavorite = owner.IsFavoriteMod(listing.ModId);
    }

    internal static List<string> DisplayTags(MainViewModel owner, ContentType type, IReadOnlyList<string> tags)
    {
        var curated = owner.TagVocabulary.GetTags(type)
            .Where(tag => tags.Contains(tag.Tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return curated.Select(tag => owner.CategoryName(tag.Tag, tag.Name))
            .Concat(tags.Where(value => !curated.Any(tag => string.Equals(tag.Tag, value, StringComparison.OrdinalIgnoreCase))).Select(TagText.Display))
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
        PendingContentPlan = null;
        Choices = null;
        IsConfirmingRemove = false;
        RemoveChanges = null;
    }

    internal void RefreshText()
    {
        AllTags = DisplayTags(_owner, _listing.Type, _listing.Tags);
        OnPropertyChanged(nameof(AllTags));
        OnPropertyChanged(nameof(AuthorLinks));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(CompatibilityText));
        OnPropertyChanged(nameof(CompatibilityToolTip));
        OnPropertyChanged(nameof(DownloadsText));
        OnPropertyChanged(nameof(DownloadsExactText));
        OnPropertyChanged(nameof(PreviewStillNote));
        OnPropertyChanged(nameof(PublishedText));
        OnPropertyChanged(nameof(PublishedDateText));
        OnPropertyChanged(nameof(ForumsGoneText));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(UpdatedDateText));
        OnPropertyChanged(nameof(ConfirmInstallText));
        OnPropertyChanged(nameof(AddedModsText));
        OnPropertyChanged(nameof(AddedModsToolTip));
        OnPropertyChanged(nameof(PlanSteps));
        OnPropertyChanged(nameof(InstallStepsNotice));
        OnPropertyChanged(nameof(InstalledPlayText));
        OnPropertyChanged(nameof(InstallSteps));
        OnPropertyChanged(nameof(UninstallSteps));
        OnPropertyChanged(nameof(RemoveSteps));
        OnPropertyChanged(nameof(RemoveConfirmText));
        OnPropertyChanged(nameof(InstalledAutomationName));
        OnPropertyChanged(nameof(LinkRequestText));
        OnPropertyChanged(nameof(FavoriteText));
    }

    internal void RefreshInstallStepsNotice()
    {
        OnPropertyChanged(nameof(InstallStepsNotice));
        OnPropertyChanged(nameof(HasInstallStepsNotice));
        OnPropertyChanged(nameof(InstalledPlayText));
        OnPropertyChanged(nameof(InstalledPlayRow));
        OnPropertyChanged(nameof(CanPlayInstalled));
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
    /// the row. The gate takes over the job of dropping a second click.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OpenAsync() => _opening.RunAsync(() => _owner.OpenContentAsync(this));

    [RelayCommand]
    private Task ChangeVersionAsync() => _owner.OpenContentVersionsAsync(this);

    [RelayCommand]
    private Task InstallAsync() => _owner.InstallAsync(this);

    [RelayCommand]
    private Task ConfirmInstallAsync() => _owner.ConfirmInstallAsync(this);

    [RelayCommand]
    private void CancelInstall() => MainViewModel.CancelInstall(this);

    [RelayCommand]
    private void DismissInstallStepsNotice() => _owner.DismissInstallStepsNotice(ModId);

    [RelayCommand]
    private Task PlayInstalledAsync() => _owner.PlayInstalledAsync(ModId);

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void BeginRemove()
    {
        InstallError = null;
        RemoveChanges = null;
        IsConfirmingRemove = true;
    }

    [RelayCommand]
    private void CancelRemove()
    {
        IsConfirmingRemove = false;
        RemoveChanges = null;
    }

    [RelayCommand]
    private Task ConfirmRemoveAsync() => _owner.RemoveAsync(this);

    [RelayCommand]
    private Task ToggleFavoriteAsync() => _owner.ToggleFavoriteAsync(this);
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

/// <summary>One row of the Operating system filter, which holds one choice at a time.</summary>
public sealed partial class DiscoverChoice(string value) : ObservableObject
{
    public string Value { get; } = value;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One group row of the License filter, which holds several, or the chip of a chosen group.</summary>
public sealed partial class DiscoverLicenseGroup(MainViewModel owner, LicenseGroupOption option) : ObservableObject
{
    public LicenseGroup Group => option.Group;

    /// <summary>The number of entries of the tab in the group.</summary>
    public int Count => option.Count;

    /// <summary>
    /// The licenses that put the entries in the group, shown under the row so that it says what it holds without a hover.
    /// Each license but the last carries its comma, so a line breaks between two licenses and never inside an id such as GPL-3.0-or-later.
    /// </summary>
    public IReadOnlyList<string> Licenses { get; } = [.. option.Licenses.Select((license, index) => index < option.Licenses.Count - 1 ? license + "," : license)];

    public bool HasLicenses => option.Licenses.Count > 0;

    public string Name => Group switch
    {
        LicenseGroup.Free => owner.Localization.DiscoverLicenseFree,
        LicenseGroup.NonCommercial => owner.Localization.DiscoverLicenseNonCommercial,
        _ => owner.Localization.DiscoverLicenseOther,
    };

    /// <summary>The short name on the chip, because the chip row next to Sort by has less room than the sidebar.</summary>
    public string ChipName => Group switch
    {
        LicenseGroup.Free => owner.Localization.DiscoverLicenseFreeChip,
        LicenseGroup.NonCommercial => owner.Localization.DiscoverLicenseNonCommercialChip,
        _ => owner.Localization.DiscoverLicenseOther,
    };

    public string Meaning => Group switch
    {
        LicenseGroup.Free => owner.Localization.DiscoverLicenseFreeMeaning,
        LicenseGroup.NonCommercial => owner.Localization.DiscoverLicenseNonCommercialMeaning,
        _ => owner.Localization.DiscoverLicenseOtherMeaning,
    };

    [ObservableProperty]
    private bool _isSelected;

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ChipName));
        OnPropertyChanged(nameof(Meaning));
    }
}

/// <summary>One check box of the exact licenses of the License filter, with the number of entries that the license puts in their group.</summary>
public sealed partial class LicenseChoice(string value, int count) : ObservableObject
{
    public string Value { get; } = value;

    public int Count { get; } = count;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed record GameVersionOption(string Text, int Revision);

/// <summary>One author name on a row or a side panel, with the comma that follows it in a list of authors and the hint that tells what a click does.</summary>
/// <param name="RowSeparator">The comma before the name on the one line of a row, where a name that does not fit drops out with its comma.</param>
/// <param name="RowHint">The hint with the whole list, because a row can drop names.</param>
public sealed record AuthorLink(string Name, string Separator, string RowSeparator, ContentType Type, string Hint, string RowHint)
{
    internal static IReadOnlyList<AuthorLink> Of(IReadOnlyList<string> authors, ContentType type, LocalizationService localization)
    {
        var everyAuthor = authors.Count > 1 ? "\n" + string.Join(", ", authors) : string.Empty;
        return [.. authors.Select((name, index) =>
        {
            var hint = localization.FormatDiscoverAuthorLink(name);
            return new AuthorLink(name, index < authors.Count - 1 ? "," : string.Empty, index > 0 ? ", " : string.Empty, type, hint, hint + everyAuthor);
        })];
    }
}
