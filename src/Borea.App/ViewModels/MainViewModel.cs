using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Formatting;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Preferences;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    /// <summary>
    /// The themes App.axaml defines. Borealis is the dark palette from the design in #8.
    /// </summary>
    internal static IReadOnlyCollection<string> BundledThemeNames { get; } = ["Borealis", "Light"];

    internal const string DefaultThemeName = "Borealis";

    private readonly IAppPreferencesRepository? _appPreferencesRepository;
    private BoreaServices? _services;
    private readonly Func<Task<BoreaServices>> _rebuildServices;
    private IInstanceRepository? _instances;
    private readonly SemaphoreSlim _preferenceSaveLock = new(1, 1);
    private AppPreferences _appPreferences;

    public LocalizationService Localization { get; }

    public RegionalFormatService RegionalFormat { get; }

    public RegionalFormatOption SelectedRegionalFormat
    {
        get => RegionalFormat.SelectedFormat;
        set
        {
            if (value is null)
                return;

            RegionalFormat.SelectedFormat = value;
            OnPropertyChanged();
            QueuePreferenceSave(preferences => preferences.WithRegionalCultureName(RegionalFormat.SelectedCultureName));
        }
    }

    [ObservableProperty]
    private string? _preferenceSaveError;

    /// <summary>
    /// An exception no page expected. Shown at the bottom of the window until
    /// dismissed, instead of ending the process.
    /// </summary>
    [ObservableProperty]
    private string? _unexpectedError;

    [RelayCommand]
    private void DismissUnexpectedError() => UnexpectedError = null;

    //windows
    [ObservableProperty]
    private bool _currentWindowHome = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscoverSection))]
    private bool _currentWindowDiscover = false;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibrarySection))]
    private bool _currentWindowLibrary = false;
    [ObservableProperty]
    private bool _currentWindowTasks = false;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibrarySection))]
    private bool _currentWindowInstance = false;

    /// <summary>
    /// The library rail item stays lit on the instance page too, and on a
    /// content page opened from an instance.
    /// </summary>
    public bool IsLibrarySection => CurrentWindowLibrary || CurrentWindowInstance || IsContentFromInstance;

    /// <summary>
    /// The discover rail item stays lit on a content or pack page too.
    /// </summary>
    public bool IsDiscoverSection => CurrentWindowDiscover || (CurrentWindowContent && !IsContentFromInstance) || CurrentWindowPack;
    [RelayCommand]
    public void SetMainWindowHome() // used to set whatever is on the main window (discover, library, etc.)
    {
        LeaveContentPage();
        LeavePackPage();
        CurrentWindowHome = true;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        CurrentWindowTasks = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
    }
    [RelayCommand]
    public void SetMainWindowDiscover() // used to set whatever is on the main window (discover, library, etc.)
    {
        LeaveContentPage();
        UpdateIndexRefreshStatus();
        LeavePackPage();
        _ = EnsureDiscoverLoadedAsync();
        CurrentWindowHome = false;
        CurrentWindowDiscover = true;
        CurrentWindowLibrary = false;
        CurrentWindowTasks = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
    }
    [RelayCommand]
    public void SetMainWindowLibrary() // used to set whatever is on the main window (discover, library, etc.)
    {
        LeaveContentPage();
        LeavePackPage();
        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = true;
        CurrentWindowTasks = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
    }
    [RelayCommand]
    public void SetMainWindowTasks()
    {
        LeaveContentPage();
        LeavePackPage();
        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        CurrentWindowTasks = true;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
    }
    /// <summary>
    /// Settings open as a modal over the current page (modal: settings in #8).
    /// </summary>
    [RelayCommand]
    public void SetMainWindowSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [ObservableProperty]
    private bool _isSettingsOpen;

    //home
    /// <summary>
    /// The installed build as KSA reports it, or null when no game directory is set
    /// or the version file could not be read.
    /// </summary>
    [ObservableProperty]
    private string? _installedVersionText;

    /// <summary>
    /// The row of the active instance, shared with the library list so the same
    /// actions work from the Current Install card. Null when none is active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveInstance), nameof(EnableActiveInstance))]
    private InstanceItem? _activeInstance;

    public bool HasActiveInstance => ActiveInstance is not null;

    public bool EnableActiveInstance => ActiveInstance is not null && !IsLaunching;

    /// <summary>
    /// The mods of the content index with the most recent newest release,
    /// newest first. The index reports download totals but no trend over time,
    /// so Home shows what changed instead of what is trending (#8).
    /// </summary>
    public ObservableCollection<RecentItem> RecentItems { get; } = [];

    public bool HasRecentItems => RecentItems.Count > 0;

    //library
    public ObservableCollection<InstanceItem> Instances { get; } = [];

    [ObservableProperty]
    private bool _isCreatingInstance;

    [ObservableProperty]
    private string _newInstanceName = string.Empty;

    /// <summary>
    /// The last instance operation that failed, as the repository reported it.
    /// </summary>
    [ObservableProperty]
    private string? _instanceError;

    //themes
    public IReadOnlyList<string> ThemeNames { get; } = BundledThemeNames.ToArray();

    [ObservableProperty]
    private string _currentTheme = DefaultThemeName;

    public MainViewModel()
        : this(new LocalizationService())
    {
    }

    public MainViewModel(LocalizationService localization)
        : this(
            localization,
            new RegionalFormatService(localization),
            appPreferencesRepository: null,
            AppPreferences.Empty)
    {
    }

    public MainViewModel(
        LocalizationService localization,
        RegionalFormatService regionalFormat,
        IAppPreferencesRepository? appPreferencesRepository,
        AppPreferences appPreferences)
        : this(localization, regionalFormat, appPreferencesRepository, appPreferences, services: null)
    {
    }

    /// <param name="services">
    /// The composed services, or null in tests and the XAML previewer. Without
    /// them every page shows its empty state and actions do nothing.
    /// </param>
    public MainViewModel(
        LocalizationService localization,
        RegionalFormatService regionalFormat,
        IAppPreferencesRepository? appPreferencesRepository,
        AppPreferences appPreferences,
        BoreaServices? services)
        : this(localization, regionalFormat, appPreferencesRepository, appPreferences, services, rebuildServices: null)
    {
    }

    /// <param name="rebuildServices">
    /// Builds a fresh service graph after a settings change. Null builds from
    /// Borea's default root; tests pass their own root.
    /// </param>
    internal MainViewModel(
        LocalizationService localization,
        RegionalFormatService regionalFormat,
        IAppPreferencesRepository? appPreferencesRepository,
        AppPreferences appPreferences,
        BoreaServices? services,
        Func<Task<BoreaServices>>? rebuildServices)
    {
        _rebuildServices = rebuildServices ?? (() => BoreaServices.BuildAsync());
        Localization = localization ?? throw new ArgumentNullException(nameof(localization));
        RegionalFormat = regionalFormat ?? throw new ArgumentNullException(nameof(regionalFormat));
        _appPreferencesRepository = appPreferencesRepository;
        _appPreferences = appPreferences ?? throw new ArgumentNullException(nameof(appPreferences));
        _services = services;
        _instances = services?.Instances;
        _currentTheme = appPreferences.ResolveSelectedThemeName(BundledThemeNames, DefaultThemeName);
        RegionalFormat.PropertyChanged += OnRegionalFormatChanged;
        Localization.PropertyChanged += OnLocalizationChanged;
    }

    /// <summary>
    /// Fills the Current Install card and the instance list. Safe to call
    /// without services; the views then show their empty states.
    /// </summary>
    public async Task LoadAsync()
    {
        StartUpdateCheck();
        InstalledVersionText = _services?.InstalledVersion.GetInstalledVersion()?.RawVersion;
        await ReloadInstancesAsync();
        await RefreshContentIndexAsync();
        await LoadRecentItemsAsync();
        UpdateIndexRefreshStatus();
        await RefreshGameSetupAsync();
    }

    /// <summary>
    /// Reads the installed game again, for when a new KSA release was put in
    /// place while Borea stayed open (#169). The window calls it when it is
    /// activated. An unchanged version leaves every list as it is.
    /// </summary>
    internal async Task RefreshInstalledGameAsync()
    {
        if (_services is null)
            return;

        var installed = _services.InstalledVersion.GetInstalledVersion();
        if (string.Equals(installed?.RawVersion, InstalledVersionText, StringComparison.Ordinal))
            return;

        InstalledVersionText = installed?.RawVersion;

        // the content page shows the same rows, so its chip follows too
        await RefreshCompatibilityAsync(installed?.Version);
    }

    /// <summary>
    /// Refreshes the content index once per start. A failure keeps the cached
    /// snapshot in use, and <see cref="IndexRefreshStatus"/> tells the pages.
    /// </summary>
    private async Task RefreshContentIndexAsync()
    {
        if (_services is null || _indexRefreshed)
            return;

        try
        {
            await _services.IndexRefresh.RefreshAsync();
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // without a cached file nothing can be read, and the status carries the reason
        }

        _indexRefreshed = true;
        StartContentUpdateCheck();
    }

    private bool _indexRefreshed;

    internal const int RecentItemCount = 8;

    /// <summary>
    /// Fills the Home grid from the cached content index. A failure leaves the
    /// grid empty, and Home hides the section.
    /// </summary>
    private async Task LoadRecentItemsAsync()
    {
        if (_services is null)
            return;

        var recent = new List<RecentItem>();
        try
        {
            var icons = new Dictionary<string, IconImage?>(ModIds.Comparer);
            foreach (var entry in (await _services.IndexSnapshots.GetSnapshotAsync()).Listings)
                icons.TryAdd(entry.Id, entry.Images?.Icon);

            foreach (var listing in await _services.ContentIndex.GetAvailableModsAsync())
            {
                if (listing.Type != ContentType.Mod)
                    continue;

                var release = await _services.ContentIndex.GetLatestReleaseInChannelAsync(listing.ModId, _services.Settings.ReleaseChannel);
                if (release is not null)
                    recent.Add(new RecentItem(this, listing, release.ReleaseDate, IconFor(icons.GetValueOrDefault(listing.ModId))));
            }
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            recent.Clear();
        }

        RecentItems.Clear();
        foreach (var item in recent.OrderByDescending(item => item.UpdatedAt).Take(RecentItemCount))
            RecentItems.Add(item);
        OnPropertyChanged(nameof(HasRecentItems));
    }

    /// <summary>
    /// Opens a Home card on the content page. It uses the Discover row of the
    /// same listing when Discover has one, so both pages show the same row.
    /// </summary>
    internal async Task OpenRecentAsync(RecentItem item)
    {
        await EnsureDiscoverLoadedAsync();
        var row = _listings.FirstOrDefault(listing => ModIds.Equals(listing.ModId, item.ModId) && listing.Source == item.Listing.Source)
            ?? new DiscoverItem(this, item.Listing);
        await OpenContentAsync(row);
    }

    /// <summary>The active instance as last read, for what the Discover rows can remove.</summary>
    private Instance? _activeInstanceEntity;

    private async Task ReloadInstancesAsync()
    {
        if (_instances is null)
            return;

        var activeId = await _instances.GetActiveInstanceIdAsync();
        var all = (await _instances.GetAllAsync())
            .OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(instance => instance.CreatedAt)
            .ToList();

        Instances.Clear();
        foreach (var instance in all)
            Instances.Add(new InstanceItem(this, instance, instance.InstanceId == activeId));
        _activeInstanceEntity = all.FirstOrDefault(instance => instance.InstanceId == activeId);

        ActiveInstance = Instances.FirstOrDefault(instance => instance.IsActive);
        OnPropertyChanged(nameof(CanActOnSelectedContent));
        RefreshInstalledFlags();
        OnPropertyChanged(nameof(InstalledInText));

        // the instance page shows fresh rows after a change; any other page
        // stays where the user is instead of jumping to that instance
        if (SelectedInstance is not null)
        {
            var stillThere = Instances.FirstOrDefault(instance => instance.InstanceId == SelectedInstance.InstanceId);
            if (!CurrentWindowInstance)
            {
                SelectedInstance = stillThere;
            }
            else if (stillThere is null)
            {
                SetMainWindowLibrary();
            }
            else
            {
                // opening the page checks the updates of the active instance too
                await OpenInstanceAsync(stillThere);
                return;
            }
        }

        StartContentUpdateCheck();
    }

    internal string? DescribeSource(InstanceSource? source) => source switch
    {
        InstanceSource.FromModPack pack => pack.ModPackId,
        InstanceSource.Custom => Localization.HomeInstanceSourceCustom,
        _ => null,
    };

    /// <summary>
    /// How long ago <paramref name="at"/> was, such as "3 days ago". The design
    /// in #8 shows an age wherever it shows when something was released.
    /// </summary>
    internal string AgeText(DateTimeOffset at) => Localization.FormatTimeAgo(DateTimeOffset.UtcNow - at);

    /// <summary>The date in the regional format the user chose, for the tooltip of an age.</summary>
    internal static string DateText(DateTimeOffset at) => at.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    /// <summary>
    /// Opens "modal: new instance" from #8.
    /// </summary>
    [RelayCommand]
    private void BeginCreateInstance()
    {
        NewInstanceName = string.Empty;
        IsCreatingInstance = true;
    }

    [RelayCommand]
    private void CancelCreateInstance() => IsCreatingInstance = false;

    [RelayCommand]
    private Task CreateInstanceAsync() => RunInstanceOperationAsync(async instances =>
    {
        var name = NewInstanceName.Trim();
        if (name.Length == 0)
            return;

        await instances.CreateAsync(name, InstanceSource.Custom.Value);
        NewInstanceName = string.Empty;
        IsCreatingInstance = false;
    });

    internal Task ActivateInstanceAsync(Guid instanceId)
        => RunInstanceOperationAsync(instances => instances.SetActiveInstanceAsync(instanceId));

    internal Task DeactivateInstanceAsync()
        => RunInstanceOperationAsync(instances => instances.ClearActiveInstanceAsync());

    internal Task RenameInstanceAsync(Guid instanceId, string newName)
        => RunInstanceOperationAsync(instances => instances.RenameAsync(instanceId, newName.Trim()));

    internal Task DeleteInstanceAsync(Guid instanceId)
        => RunInstanceOperationAsync(instances => instances.DeleteAsync(instanceId));

    /// <summary>
    /// Runs one repository call, then reloads the list so every row reflects
    /// the outcome. The repository's message becomes <see cref="InstanceError"/>.
    /// </summary>
    private async Task RunInstanceOperationAsync(Func<IInstanceRepository, Task> operation)
    {
        if (_instances is null)
            return;

        try
        {
            await operation(_instances);
            InstanceError = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            InstanceError = exception.Message;
        }

        await ReloadInstancesAsync();
    }

    private Task _preferenceSaves = Task.CompletedTask;

    /// <summary>
    /// Saves from a property change, which cannot await. The saves run one at a
    /// time behind <see cref="_preferenceSaveLock"/>.
    /// </summary>
    private void QueuePreferenceSave(Func<AppPreferences, AppPreferences> update)
        => _preferenceSaves = Task.WhenAll(_preferenceSaves, SavePreferencesAsync(update));

    /// <summary>
    /// Completes when every preference save queued so far has finished.
    /// </summary>
    internal Task WhenPreferencesSavedAsync() => _preferenceSaves;

    private async Task SavePreferencesAsync(Func<AppPreferences, AppPreferences> update)
    {
        if (_appPreferencesRepository is null)
            return;

        await _preferenceSaveLock.WaitAsync();
        try
        {
            var updatedPreferences = update(_appPreferences);
            if (SamePreferences(_appPreferences, updatedPreferences))
            {
                PreferenceSaveError = null;
                return;
            }

            await _appPreferencesRepository.SaveAsync(updatedPreferences, BundledThemeNames);
            _appPreferences = updatedPreferences;
            PreferenceSaveError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            PreferenceSaveError = Localization.FormatPreferenceSaveError(exception.Message);
        }
        finally
        {
            _preferenceSaveLock.Release();
        }
    }

    private static bool SamePreferences(AppPreferences left, AppPreferences right)
        => string.Equals(left.SelectedThemeName, right.SelectedThemeName, StringComparison.Ordinal)
            && string.Equals(left.RegionalCultureName, right.RegionalCultureName, StringComparison.Ordinal)
            && string.Equals(left.UiCultureName, right.UiCultureName, StringComparison.Ordinal)
            && left.CheckForUpdatesAtStart == right.CheckForUpdatesAtStart
            && left.UpdateChannel == right.UpdateChannel
            && left.ForeignFolderDeletionConfirmed == right.ForeignFolderDeletionConfirmed
            && left.LoadImagesFromAuthorHosts == right.LoadImagesFromAuthorHosts;

    private void OnRegionalFormatChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegionalFormatService.SelectedFormat))
        {
            OnPropertyChanged(nameof(SelectedRegionalFormat));
            RefreshRowText();
            RefreshGameDataItems();
        }
    }

    /// <summary>
    /// The text of the Home cards, the Discover and pack rows and the versions
    /// tables. Their ages, dates and download counts follow both the language
    /// and the regional format.
    /// </summary>
    private void RefreshRowText()
    {
        foreach (var item in RecentItems)
            item.RefreshText();
        foreach (var item in _listings)
            item.RefreshText();
        foreach (var release in _contentReleases)
            release.RefreshText();
        LatestVersion?.RefreshText();
        RefreshPackText();
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        // the service raises an empty name when the culture changes, so every
        // translated string on this model needs a refresh too.
        foreach (var instance in Instances)
            instance.RefreshText();
        RefreshRowText();
        // the reasons a mod cannot be removed are translated text
        RefreshInstalledFlags();
        foreach (var option in ReleaseChannelOptions)
            option.RefreshText();
        foreach (var option in UpdateChannelOptions)
            option.RefreshText();
        foreach (var category in CategoryOptions)
            category.RefreshText();
        RefreshContentGroups();
        foreach (var item in ManualInstallItems)
            item.RefreshText();
        RefreshGameDataItems();
        RefreshLoaderText();
        RefreshIndexStatusText();
        OnPropertyChanged(nameof(GameSetupBannerText));
        OnPropertyChanged(nameof(InstalledInText));
        OnPropertyChanged(nameof(ActiveInstanceUpdatesText));
        OnPropertyChanged(nameof(ContentVersionsEmptyText));

        QueuePreferenceSave(preferences => preferences.WithUiCultureName(Localization.SelectedCultureName));
    }

    partial void OnCurrentThemeChanged(string value)
    {
        QueuePreferenceSave(preferences => preferences.WithSelectedThemeName(value));
    }
}

/// <summary>
/// One card in the Home grid: a mod and the date of its newest release.
/// </summary>
public sealed partial class RecentItem : ObservableObject
{
    private readonly MainViewModel _owner;

    internal ModMetadata Listing { get; }

    public string ModId => Listing.ModId;

    public string Name => Listing.Name;

    public ListingImage? Icon { get; }

    public DateTimeOffset UpdatedAt { get; }

    /// <summary>How long ago the release came out.</summary>
    public string UpdatedText => _owner.AgeText(UpdatedAt);

    /// <summary>The release date in the regional format the user chose.</summary>
    public string UpdatedDateText => MainViewModel.DateText(UpdatedAt);

    public RecentItem(MainViewModel owner, ModMetadata listing, DateTimeOffset updatedAt, ListingImage? icon = null)
    {
        _owner = owner;
        Listing = listing;
        UpdatedAt = updatedAt;
        Icon = icon;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(UpdatedDateText));
    }

    [RelayCommand]
    private Task OpenAsync() => _owner.OpenRecentAsync(this);
}

/// <summary>
/// One row of the instance list. Rename and delete happen inline: the row
/// switches into an editing or confirming state instead of opening a dialog.
/// </summary>
public sealed partial class InstanceItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly InstanceSource _source;
    private readonly Dictionary<string, ModVersion> _modVersions;

    public Guid InstanceId { get; }

    public string Name { get; }

    public int ModCount { get; }

    public IReadOnlyList<string> ModIds { get; }

    internal IReadOnlyList<InstalledMod> Mods { get; }

    public bool IsActive { get; }

    public string? SourceText => _owner.DescribeSource(_source);

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    [ObservableProperty]
    private string _editName;

    public InstanceItem(MainViewModel owner, Instance instance, bool isActive)
    {
        _owner = owner;
        _source = instance.Source;
        InstanceId = instance.InstanceId;
        Name = instance.Name;
        ModCount = instance.Mods.Count;
        Mods = instance.Mods;
        ModIds = Mods.Select(mod => mod.ModId).ToList();
        _modVersions = instance.Mods.ToDictionary(mod => mod.ModId, mod => mod.Version, Borea.Core.Mods.ModIds.Comparer);
        IsActive = isActive;
        _editName = instance.Name;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(SourceText));

    internal ModVersion? InstalledVersionOf(string modId)
        => _modVersions.TryGetValue(modId, out var version) ? version : null;

    [RelayCommand]
    private Task ActivateAsync() => _owner.ActivateInstanceAsync(InstanceId);

    /// <summary>
    /// The switch on the row. It activates an inactive instance and leaves no
    /// instance active when it is switched off on the active one.
    /// </summary>
    [RelayCommand]
    private Task ToggleActiveAsync() => IsActive ? _owner.DeactivateInstanceAsync() : _owner.ActivateInstanceAsync(InstanceId);

    [RelayCommand]
    private Task OpenAsync() => _owner.OpenInstanceAsync(this);

    [RelayCommand]
    private void BeginRename()
    {
        EditName = Name;
        IsConfirmingDelete = false;
        IsRenaming = true;
    }

    [RelayCommand]
    private Task CommitRenameAsync()
    {
        IsRenaming = false;
        return string.IsNullOrWhiteSpace(EditName) || EditName.Trim() == Name
            ? Task.CompletedTask
            : _owner.RenameInstanceAsync(InstanceId, EditName);
    }

    [RelayCommand]
    private void BeginDelete()
    {
        IsRenaming = false;
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private Task ConfirmDeleteAsync() => _owner.DeleteInstanceAsync(InstanceId);

    [RelayCommand]
    private void Cancel()
    {
        IsRenaming = false;
        IsConfirmingDelete = false;
    }
}
