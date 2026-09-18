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
using Borea.Core.History;
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
    /// <summary>The task drawer opens over the current page, which stays as it is.</summary>
    [ObservableProperty]
    private bool _isTasksOpen;
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
        IsTasksOpen = false;
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
        IsTasksOpen = false;
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
        IsTasksOpen = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
    }
    [RelayCommand]
    private void ToggleTasks() => IsTasksOpen = !IsTasksOpen;

    [RelayCommand]
    private void CloseTasks() => IsTasksOpen = false;
    /// <summary>
    /// Settings open as a modal over the current page (modal: settings in #8).
    /// </summary>
    [RelayCommand]
    public void SetMainWindowSettings()
    {
        IsTasksOpen = false;
        IsSettingsOpen = true;
    }

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
    [NotifyPropertyChangedFor(nameof(HasActiveInstance), nameof(EnableActiveInstance), nameof(EnableHomeLaunch))]
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
    [NotifyPropertyChangedFor(nameof(IsNameModalOpen))]
    private bool _isCreatingInstance;

    /// <summary>The row the name modal renames. Null while the modal creates an instance or is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameModalOpen), nameof(NameModalTitle), nameof(NameModalConfirmText))]
    private InstanceItem? _renamingInstance;

    /// <summary>The name the modal creates or renames an instance with.</summary>
    [ObservableProperty]
    private string _modalInstanceName = string.Empty;

    public bool IsNameModalOpen => IsCreatingInstance || RenamingInstance is not null;

    public string NameModalTitle => RenamingInstance is not null ? Localization.ModalRenameInstanceTitle : IsImportingSharedProfile ? Localization.SharedProfileModalTitle : Localization.ModalCreateInstanceTitle;

    public string NameModalConfirmText => RenamingInstance is null ? Localization.LibraryCreate : Localization.LibrarySave;

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
        Tasks = new TaskRegistry(Localization, () => _services?.TaskHistory, () => _services?.Log, RetryTaskAsync);
        Toasts = new ToastService(this);
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
        _ = Tasks.LoadAsync();
        StartUpdateCheck();
        InstalledVersionText = _services?.InstalledVersion.GetInstalledVersion()?.RawVersion;
        await ReloadInstancesAsync();
        await RefreshContentIndexAsync();
        await LoadRecentItemsAsync();
        UpdateIndexRefreshStatus();
        RefreshGameSetup();
        await RefreshSharedProfileAsync();
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
        if (_services is not { } services || _indexRefreshed)
            return;

        var task = StartTask(TaskKind.IndexRefresh);
        var completed = false;
        string? error = null;
        try
        {
            await services.IndexRefresh.RefreshAsync();
            completed = true;
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            error = exception.Message;
        }
        finally
        {
            if (services.IndexRefresh.Status is { Outcome: ContentIndexRefreshOutcome.Failed } status)
                error = status.FailureReason ?? error ?? string.Empty;
            EndTask(task, completed, stopped: false, error);
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
        var all = await _instances.GetAllAsync();
        var rows = new List<InstanceItem>();
        foreach (var instance in all)
            rows.Add(new InstanceItem(this, instance, instance.InstanceId == activeId, await LastPlayedAsync(instance)));

        Instances.Clear();
        foreach (var row in Sorted(rows))
            Instances.Add(row);
        RefreshOtherInstances();
        _activeInstanceEntity = all.FirstOrDefault(instance => instance.InstanceId == activeId);

        ActiveInstance = Instances.FirstOrDefault(instance => instance.IsActive);
        RefreshInstanceHint();
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

    internal static string DateTimeText(DateTimeOffset at) => at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>
    /// Opens "modal: new instance" from #8.
    /// </summary>
    [RelayCommand]
    private void BeginCreateInstance()
    {
        InstanceError = null;
        ModalInstanceName = string.Empty;
        RenamingInstance = null;
        IsCreatingInstance = true;
    }

    /// <summary>Opens the name modal to rename <paramref name="item"/>.</summary>
    internal void BeginRenameInstance(InstanceItem item)
    {
        item.IsConfirmingDelete = false;
        InstanceError = null;
        ModalInstanceName = item.Name;
        IsCreatingInstance = false;
        RenamingInstance = item;
    }

    [RelayCommand]
    private void CancelNameModal()
    {
        ClearNameRequired();
        IsCreatingInstance = false;
        RenamingInstance = null;
    }

    private string? _nameRequiredError;

    partial void OnModalInstanceNameChanged(string value) => ClearNameRequired();

    private void ShowNameRequired() => InstanceError = _nameRequiredError = Localization.ModalNameRequired;

    private void ClearNameRequired()
    {
        if (_nameRequiredError is not null && InstanceError == _nameRequiredError)
            InstanceError = null;

        _nameRequiredError = null;
    }

    [RelayCommand]
    private Task ConfirmNameModalAsync() => RenamingInstance is { } item ? RenameFromModalAsync(item) : CreateInstanceAsync();

    [RelayCommand]
    private Task CreateInstanceAsync()
    {
        var name = ModalInstanceName.Trim();
        if (name.Length == 0)
        {
            ShowNameRequired();
            return Task.CompletedTask;
        }

        return IsImportingSharedProfile ? ImportSharedProfileAsync(name) : RunInstanceOperationAsync(async instances =>
        {
            await instances.CreateAsync(name, InstanceSource.Custom.Value);
            ModalInstanceName = string.Empty;
            IsCreatingInstance = false;
        });
    }

    /// <summary>Keeps the modal open with the error when the name is empty or the repository refuses it.</summary>
    private async Task RenameFromModalAsync(InstanceItem item)
    {
        var name = ModalInstanceName.Trim();
        if (name.Length == 0)
        {
            ShowNameRequired();
            return;
        }

        if (name != item.Name)
        {
            await RenameInstanceAsync(item.InstanceId, name);
            if (InstanceError is not null)
                return;
        }

        RenamingInstance = null;
    }

    internal Task ActivateInstanceAsync(Guid instanceId)
        => RunInstanceOperationAsync(instances => instances.SetActiveInstanceAsync(instanceId));

    internal Task DeactivateInstanceAsync()
        => RunInstanceOperationAsync(instances => instances.ClearActiveInstanceAsync());

    internal Task RenameInstanceAsync(Guid instanceId, string newName)
        => RunInstanceOperationAsync(instances => instances.RenameAsync(instanceId, newName.Trim()));

    internal Task DeleteInstanceAsync(Guid instanceId)
        => RunInstanceOperationAsync(instances => instances.DeleteAsync(instanceId));

    /// <summary>Opens the folder of the instance, and creates it when it does not exist yet.</summary>
    internal void OpenInstanceFolder(Guid instanceId)
    {
        if (_services is not { } services)
            return;

        var root = services.Paths.GetInstanceRoot(instanceId);
        string? error;
        try
        {
            Directory.CreateDirectory(root);
            error = TryOpenWithSystem(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
        }

        if (CurrentWindowLibrary)
            InstanceError = error;
        else
            ContentError = error;
    }

    /// <summary>
    /// Runs one repository call, then reloads the list so every row reflects
    /// the outcome. The repository's message becomes <see cref="InstanceError"/>.
    /// </summary>
    private async Task RunInstanceOperationAsync(Func<IInstanceRepository, Task> operation)
    {
        if (_instances is null)
            return;

        InstanceNotice = null;
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
            && left.LoadImagesFromAuthorHosts == right.LoadImagesFromAuthorHosts
            && left.HomeLaunch == right.HomeLaunch
            && left.DiscoverSortOrder == right.DiscoverSortOrder
            && left.SharedProfileBannerDismissed == right.SharedProfileBannerDismissed;

    private void OnRegionalFormatChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegionalFormatService.SelectedFormat))
        {
            OnPropertyChanged(nameof(SelectedRegionalFormat));
            RefreshRowText();
            RefreshGameDataItems();
            RefreshGameSaveText();
        }
    }

    /// <summary>
    /// The text of the Home cards, the instance, Discover and pack rows and the
    /// versions tables. Their ages, dates and download counts follow both the
    /// language and the regional format.
    /// </summary>
    private void RefreshRowText()
    {
        foreach (var instance in Instances)
            instance.RefreshText();
        foreach (var item in RecentItems)
            item.RefreshText();
        foreach (var item in _listings)
            item.RefreshText();
        foreach (var release in _contentReleases)
            release.RefreshText();
        LatestVersion?.RefreshText();
        RefreshPackText();
        Tasks.RefreshText();
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        // the service raises an empty name when the culture changes, so every
        // translated string on this model needs a refresh too.
        RefreshRowText();
        RefreshLibraryText();
        RefreshPlaytimeText();
        // the reasons a mod cannot be removed are translated text
        RefreshInstalledFlags();
        foreach (var option in ReleaseChannelOptions)
            option.RefreshText();
        foreach (var option in UpdateChannelOptions)
            option.RefreshText();
        foreach (var category in CategoryOptions)
            category.RefreshText();
        RefreshContentGroups();
        RefreshModListText();
        foreach (var item in ManualInstallItems)
            item.RefreshText();
        foreach (var run in _installRuns)
            run.RefreshText();
        Toasts.RefreshText();
        RefreshGameDataItems();
        RefreshGameSaveText();
        RefreshLoaderText();
        RefreshIndexStatusText();
        OnPropertyChanged(nameof(GameSetupBannerText));
        OnPropertyChanged(nameof(FoundGameText));
        OnPropertyChanged(nameof(LoaderPromptText));
        OnPropertyChanged(nameof(SharedProfileBannerText));
        OnPropertyChanged(nameof(SharedProfileImportNotice));
        OnPropertyChanged(nameof(InstalledInText));
        OnPropertyChanged(nameof(ActiveInstanceUpdatesText));
        OnPropertyChanged(nameof(HomeLaunchText));
        RefreshInstanceHint();
        OnPropertyChanged(nameof(NameModalTitle));
        OnPropertyChanged(nameof(NameModalConfirmText));
        OnPropertyChanged(nameof(ContentVersionsEmptyText));
        OnPropertyChanged(nameof(DiscoverSortText));

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
/// One row of the instance list. Delete asks for a confirmation in the row, and
/// rename opens the name modal.
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

    internal IReadOnlyList<string> LaunchArguments { get; }

    public bool IsActive { get; }

    public string? SourceText => _owner.DescribeSource(_source);

    internal DateTimeOffset CreatedAt { get; }

    /// <summary>The newer of the launch Borea recorded and the last write of the game's log.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastPlayedText))]
    [NotifyPropertyChangedFor(nameof(LastPlayedToolTip))]
    private DateTimeOffset? _lastPlayedAt;

    public string LastPlayedText => LastPlayedAt is { } at ? _owner.AgeText(at) : _owner.Localization.LibraryNeverPlayed;

    public string? LastPlayedToolTip => LastPlayedAt is { } at ? _owner.Localization.FormatLibraryLastPlayed(MainViewModel.DateTimeText(at)) : null;

    [ObservableProperty]
    private bool _isConfirmingDelete;

    public InstanceItem(MainViewModel owner, Instance instance, bool isActive, DateTimeOffset? lastPlayedAt = null)
    {
        _owner = owner;
        _source = instance.Source;
        InstanceId = instance.InstanceId;
        Name = instance.Name;
        CreatedAt = instance.CreatedAt;
        _lastPlayedAt = lastPlayedAt;
        ModCount = instance.Mods.Count;
        Mods = instance.Mods;
        ModIds = Mods.Select(mod => mod.ModId).ToList();
        _modVersions = instance.Mods.ToDictionary(mod => mod.ModId, mod => mod.Version, Borea.Core.Mods.ModIds.Comparer);
        LaunchArguments = instance.LaunchArguments;
        IsActive = isActive;
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(LastPlayedText));
        OnPropertyChanged(nameof(LastPlayedToolTip));
    }

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

    /// <summary>Play on the active row of the Library, which starts the same watched launch as Home.</summary>
    [RelayCommand]
    private Task PlayAsync() => IsActive ? _owner.LaunchActiveInstanceAsync() : Task.CompletedTask;

    [RelayCommand]
    private void BeginRename() => _owner.BeginRenameInstance(this);

    [RelayCommand]
    private void BeginEditLaunchArguments() => _owner.BeginEditLaunchArguments(this);

    [RelayCommand]
    private void OpenFolder() => _owner.OpenInstanceFolder(InstanceId);

    [RelayCommand]
    private Task DuplicateAsync() => _owner.BeginDuplicateAsync(InstanceId);

    [RelayCommand]
    private Task ExportModListAsync() => _owner.ExportModListAsync(InstanceId);

    [RelayCommand]
    private Task CopyModListAsync() => _owner.CopyModListAsync(InstanceId);

    [RelayCommand]
    private void BeginDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private Task ConfirmDeleteAsync() => _owner.DeleteInstanceAsync(InstanceId);

    [RelayCommand]
    private void Cancel() => IsConfirmingDelete = false;
}
