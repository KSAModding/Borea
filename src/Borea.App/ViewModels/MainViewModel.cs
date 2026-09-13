using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Borea.App.Formatting;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.Instances;
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
    /// The library rail item stays lit on the instance page too.
    /// </summary>
    public bool IsLibrarySection => CurrentWindowLibrary || CurrentWindowInstance;

    /// <summary>
    /// The discover rail item stays lit on a content page too.
    /// </summary>
    public bool IsDiscoverSection => CurrentWindowDiscover || CurrentWindowContent;
    [RelayCommand]
    public void SetMainWindowHome() // used to set whatever is on the main window (discover, library, etc.)
    {
        CurrentWindowHome = true;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        CurrentWindowTasks = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
    }
    [RelayCommand]
    public void SetMainWindowDiscover() // used to set whatever is on the main window (discover, library, etc.)
    {
        _ = EnsureDiscoverLoadedAsync();
        CurrentWindowHome = false;
        CurrentWindowDiscover = true;
        CurrentWindowLibrary = false;
        CurrentWindowTasks = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
    }
    [RelayCommand]
    public void SetMainWindowLibrary() // used to set whatever is on the main window (discover, library, etc.)
    {
        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = true;
        CurrentWindowTasks = false;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
    }
    [RelayCommand]
    public void SetMainWindowTasks()
    {
        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        CurrentWindowTasks = true;
        CurrentWindowInstance = false;
        CurrentWindowContent = false;
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
    [NotifyPropertyChangedFor(nameof(HasActiveInstance))]
    private InstanceItem? _activeInstance;

    public bool HasActiveInstance => ActiveInstance is not null;

    /// <summary>
    /// Placeholder cards for the trending grid until the content index carries
    /// download counts and thumbnails (see #8).
    /// </summary>
    public IReadOnlyList<TrendingItem> TrendingItems { get; } =
    [
        new("Advanced Flight Computer", 123),
        new("DeltaVMap", 123),
        new("StageInfo", 123),
        new("Auto Remove Finished Burns", 123),
        new("Auto Remove Finished Burns", 123),
        new("StageInfo", 123),
        new("DeltaVMap", 123),
        new("Advanced Flight Computer", 123),
    ];

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
        InstalledVersionText = _services?.InstalledVersion.GetInstalledVersion()?.RawVersion;
        await ReloadInstancesAsync();
        await RefreshContentIndexAsync();
    }

    /// <summary>
    /// Downloads the content index when it changed, like <c>borea index refresh</c>.
    /// A failure keeps the cached snapshot, so it is not shown as an error.
    /// </summary>
    private async Task RefreshContentIndexAsync()
    {
        if (_services is null || _indexRefreshed)
            return;

        try
        {
            await _services.IndexFetcher.FetchAsync(_services.Paths.GetIndexPath());
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // offline or a broken snapshot: the cached file stays in use
        }

        _indexRefreshed = true;
    }

    private bool _indexRefreshed;

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

        ActiveInstance = Instances.FirstOrDefault(instance => instance.IsActive);
        RefreshInstalledFlags();

        if (SelectedInstance is not null)
        {
            var stillThere = Instances.FirstOrDefault(instance => instance.InstanceId == SelectedInstance.InstanceId);
            if (stillThere is null)
                SetMainWindowLibrary();
            else
                await OpenInstanceAsync(stillThere);
        }
    }

    internal string? DescribeSource(InstanceSource? source) => source switch
    {
        InstanceSource.FromModPack pack => pack.ModPackId,
        InstanceSource.Custom => Localization.HomeInstanceSourceCustom,
        _ => null,
    };

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
            && string.Equals(left.UiCultureName, right.UiCultureName, StringComparison.Ordinal);

    private void OnRegionalFormatChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegionalFormatService.SelectedFormat))
            OnPropertyChanged(nameof(SelectedRegionalFormat));
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        // the service raises an empty name when the culture changes, so every
        // translated string on this model needs a refresh too.
        foreach (var instance in Instances)
            instance.RefreshText();
        foreach (var item in DiscoverItems)
            item.RefreshText();
        RefreshContentGroups();
        RefreshLoaderText();

        QueuePreferenceSave(preferences => preferences.WithUiCultureName(Localization.SelectedCultureName));
    }

    partial void OnCurrentThemeChanged(string value)
    {
        QueuePreferenceSave(preferences => preferences.WithSelectedThemeName(value));
    }
}

/// <summary>
/// One card in the trending grid.
/// </summary>
public sealed record TrendingItem(string Name, int Downloads);

/// <summary>
/// One row of the instance list. Rename and delete happen inline: the row
/// switches into an editing or confirming state instead of opening a dialog.
/// </summary>
public sealed partial class InstanceItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly InstanceSource _source;

    public Guid InstanceId { get; }

    public string Name { get; }

    public int ModCount { get; }

    public IReadOnlyList<string> ModIds { get; }

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
        ModIds = instance.Mods.Select(mod => mod.ModId).ToList();
        IsActive = isActive;
        _editName = instance.Name;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(SourceText));

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
