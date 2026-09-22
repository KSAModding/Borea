using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Dependencies;
using Borea.Core.History;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Preferences;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

public enum InstanceTab
{
    Content,
    ManualInstalls,
    GameData,
    Log,
}

/// <summary>
/// The instance page (library-instance in #8): header with Play, and the
/// installed content grouped the way the design does.
/// </summary>
public partial class MainViewModel
{
    private Instance? _selectedInstanceEntity;
    private IReadOnlyList<ContentItem> _content = [];
    private ModPackMetadata? _contentPack;
    private readonly Dictionary<Guid, IInstallRow> _runningUpdates = [];
    private Task _contentUpdateCheck = Task.CompletedTask;
    private int _contentUpdateCheckGeneration;
    private Guid? _launchInstanceId;
    private string? _launchBlamedModName;
    private string? _launchLoaderName;
    private HomeLaunchOption? _homeLaunch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeContent))]
    private InstanceItem? _selectedInstance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContentTab))]
    [NotifyPropertyChangedFor(nameof(IsManualInstallsTab))]
    [NotifyPropertyChangedFor(nameof(IsGameDataTab))]
    [NotifyPropertyChangedFor(nameof(IsLogTab))]
    private InstanceTab _instanceTab;

    public bool IsContentTab => InstanceTab == InstanceTab.Content;

    public bool IsManualInstallsTab => InstanceTab == InstanceTab.ManualInstalls;

    public bool IsGameDataTab => InstanceTab == InstanceTab.GameData;

    public bool IsLogTab => InstanceTab == InstanceTab.Log;

    public ObservableCollection<ContentGroup> ContentGroups { get; } = [];

    public bool HasContent => _content.Count > 0;

    /// <summary>
    /// The "Update all" action of the page header, for the shown instance.
    /// </summary>
    [ObservableProperty]
    private UpdateAllItem? _updateAll;

    public bool HasUpdates => _content.Any(content => content.UpdateVersion is not null);

    /// <summary>The number of mods Borea owns in the active instance that have a newer release.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveInstanceUpdates))]
    [NotifyPropertyChangedFor(nameof(ActiveInstanceUpdatesText))]
    private int _activeInstanceUpdateCount;

    private Guid? _updateCountInstanceId;

    public bool HasActiveInstanceUpdates => ActiveInstanceUpdateCount > 0;

    public string ActiveInstanceUpdatesText => Localization.FormatHomeUpdates(ActiveInstanceUpdateCount);

    /// <summary>
    /// False while an update of the shown instance plans or runs.
    /// </summary>
    public bool CanChangeContent => SelectedInstance is null || !_runningUpdates.ContainsKey(SelectedInstance.InstanceId);

    /// <summary>
    /// What the launcher said the last time Play was pressed, success or not.
    /// </summary>
    [ObservableProperty]
    private string? _launchMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnableActiveInstance), nameof(EnableHomeLaunch))]
    private bool _isLaunching;

    /// <summary>What the loader wrote before it stopped, when the last Play failed that way.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLaunchOutput))]
    private string? _launchOutputText;

    public bool HasLaunchOutput => LaunchOutputText is not null;

    [ObservableProperty]
    private bool _isLaunchOutputShown;

    /// <summary>The mod the loader likely stopped on, offered to disable. Null when Borea found none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDisableBlamedMod))]
    private string? _launchBlamedModId;

    public bool CanDisableBlamedMod => LaunchBlamedModId is not null;

    public string? DisableBlamedModText => _launchBlamedModName is null ? null : Localization.FormatLaunchDisableMod(_launchBlamedModName);

    /// <summary>The modal that shows why the loader stopped, with the way out.</summary>
    [ObservableProperty]
    private bool _isLaunchFailureOpen;

    public string? LaunchFailureTitle => _launchLoaderName is null ? null : Localization.FormatLaunchStoppedTitle(_launchLoaderName);

    /// <summary>Why an action of the launch modal failed, shown in the modal because it covers the toasts.</summary>
    [ObservableProperty]
    private string? _launchFailureError;

    [RelayCommand]
    internal async Task OpenInstanceAsync(InstanceItem item)
    {
        if (_services is null || item is null)
            return;

        // a rename or a removal reloads the same instance and keeps its tab
        if (SelectedInstance?.InstanceId != item.InstanceId)
            InstanceTab = InstanceTab.Content;

        SelectedInstance = item;
        // a running launch, and the page of the launched instance, keep what the launch said
        if (!IsLaunching && item.InstanceId != _launchInstanceId)
        {
            LaunchMessage = null;
            ClearLaunchFailure();
        }

        _runningUpdates.TryGetValue(item.InstanceId, out var running);
        UpdateAll = running as UpdateAllItem ?? new UpdateAllItem(this, item.InstanceId);
        if (running is PackUpdateItem || PackUpdate?.InstanceId != item.InstanceId)
            PackUpdate = running as PackUpdateItem;
        _selectedInstanceEntity = await _services.Instances.GetByIdAsync(item.InstanceId);

        var enabled = new HashSet<string>(ModIds.Comparer);
        if (_selectedInstanceEntity is not null)
        {
            var entries = await _services.ModState.GetEntriesAsync(item.InstanceId);
            foreach (var entry in entries.Where(entry => entry.Enabled))
                enabled.Add(entry.ModId);
        }

        // the rows link to the same pages Discover opens, so its listings are needed
        await EnsureDiscoverLoadedAsync();
        var content = new List<ContentItem>();
        foreach (var mod in _selectedInstanceEntity?.Mods ?? [])
        {
            // a running update reports its progress to the row it started on
            if (running is ContentItem updating && ModIds.Equals(updating.ModId, mod.ModId))
            {
                content.Add(updating);
                continue;
            }

            // a release from SpaceDock carries no listing, so the name comes from the catalog
            var listing = mod.Metadata.Listing is null ? await ResolveListingAsync(mod.ModId) : null;
            var indexed = _listings.FirstOrDefault(entry => ModIds.Equals(entry.ModId, mod.ModId));
            content.Add(new ContentItem(this, _selectedInstanceEntity!, mod, enabled.Contains(mod.ModId), listing, indexed, indexed?.Icon));
        }

        _content = content.OrderBy(content => content.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        await ShowMissingContentAsync(item.InstanceId);
        _contentPack = await ResolveSourcePackAsync(_selectedInstanceEntity?.Source);
        ShowPackUpdate(item.InstanceId, await FindPackUpdateAsync(_selectedInstanceEntity));
        RefreshContentGroups();
        RefreshInstanceLoader();
        OnPropertyChanged(nameof(HasUpdates));
        await LoadGameSavesAsync();
        if (IsManualInstallsTab)
            await LoadManualInstallsAsync();
        else if (IsGameDataTab)
            await LoadGameDataAsync();
        else if (IsLogTab)
            await LoadGameLogAsync();

        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        IsTasksOpen = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
        CurrentWindowInstance = true;

        StartContentUpdateCheck();
        StartPlaytimeLoad(item.InstanceId);
        StartInstanceSizeLoad(item.InstanceId);
    }

    [RelayCommand]
    private void ShowInstanceContent() => InstanceTab = InstanceTab.Content;

    /// <summary>
    /// A pack member goes under its pack only when the instance was created from
    /// that pack and the index still lists the pack version, because an installed
    /// mod does not record its pack. Titles are translated, so a language change
    /// rebuilds them.
    /// </summary>
    private void RefreshContentGroups()
    {
        foreach (var item in _content)
            item.RefreshText();

        MissingContent?.RefreshText();

        ContentGroups.Clear();
        var fromPacks = _content.Where(content => content.Reason == InstallReason.ModPack).ToList();
        if (_contentPack is { } pack)
        {
            var pinned = fromPacks.Where(content => pack.Mods.Any(pin => ModIds.Equals(pin.ContentId, content.ModId))).ToList();
            Add(Localization.FormatInstanceGroupModpack(pack.Name, pack.Version.ToString()), pinned);
            fromPacks = fromPacks.Except(pinned).ToList();
        }

        Add(Localization.InstanceGroupModpacks, fromPacks);
        var chosen = _content.Where(content => content.Reason is not InstallReason.ModPack and not InstallReason.Dependency).ToList();
        Add(Localization.InstanceGroupMods, chosen.Where(content => content.Type == ContentType.Mod));
        Add(Localization.InstanceGroupModLoaders, chosen.Where(content => content.Type == ContentType.ModLoader));
        Add(Localization.InstanceGroupOther, chosen.Where(content => content.Type is not ContentType.Mod and not ContentType.ModLoader));
        Add(Localization.InstanceGroupDependencies, _content.Where(content => content.IsDependency), isDependencies: true);
        OnPropertyChanged(nameof(HasContent));

        void Add(string title, IEnumerable<ContentItem> items, bool isDependencies = false)
        {
            var list = items.ToList();
            if (list.Count > 0)
                ContentGroups.Add(new ContentGroup(title, list, isDependencies));
        }
    }

    private async Task<ModPackMetadata?> ResolveSourcePackAsync(InstanceSource? source)
    {
        if (_services is null || source is not InstanceSource.FromModPack pack)
            return null;

        try
        {
            return (await _services.ModPacks.GetVersionAsync(pack.ModPackId, pack.Version))?.Metadata;
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs in the background, so the page and its messages do not wait for
    /// one planner search per mod. A later check stops the earlier one.
    /// </summary>
    private void StartContentUpdateCheck()
    {
        var previous = _contentUpdateCheck;
        var check = RefreshContentUpdatesAsync(++_contentUpdateCheckGeneration);
        _contentUpdateCheck = previous.IsCompleted ? check : Task.WhenAll(previous, check);
    }

    /// <summary>Completes when the update checks have finished.</summary>
    internal Task WhenContentUpdatesCheckedAsync() => _contentUpdateCheck;

    /// <summary>
    /// Plans an update of each mod Borea owns on its own. The rows of the shown
    /// instance get the newer release, and the active instance gets its count.
    /// </summary>
    private async Task RefreshContentUpdatesAsync(int generation)
    {
        var services = _services;
        var shown = CurrentWindowInstance ? _selectedInstanceEntity : null;
        var content = _content;
        var active = _activeInstanceEntity;
        // a check of the same instance keeps the last count until it ends, so the card does not flicker
        if (active?.InstanceId != _updateCountInstanceId)
        {
            ActiveInstanceUpdateCount = 0;
            _updateCountInstanceId = active?.InstanceId;
        }

        if (services is null)
            return;

        // the shown instance is often the active one, so each mod is planned once
        var found = new Dictionary<(Guid InstanceId, string ModId), ModVersion?>();
        async Task<ModVersion?> FindAsync(Instance instance, InstalledMod installed)
        {
            if (!found.TryGetValue((instance.InstanceId, installed.ModId), out var newer))
                found[(instance.InstanceId, installed.ModId)] = newer = await FindUpdateAsync(services, instance, installed);
            return newer;
        }

        if (shown is not null)
        {
            var packUpdate = await FindPackUpdateAsync(shown);
            if (generation != _contentUpdateCheckGeneration)
                return;

            ShowPackUpdate(shown.InstanceId, packUpdate, keepSameVersion: true);
            foreach (var item in content.Where(item => item.IsOwned && !item.IsMissing))
            {
                var installed = shown.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, item.ModId));
                if (installed is null)
                    continue;

                var newer = await FindAsync(shown, installed);
                if (generation != _contentUpdateCheckGeneration)
                    return;

                item.UpdateVersion = newer?.ToString();
            }

            OnPropertyChanged(nameof(HasUpdates));
        }

        // the Home card counts what the instance page offers, and no update
        // reaches a mod whose folder is gone
        var missing = await MissingModIdsAsync(services, active);
        var count = 0;
        foreach (var installed in active?.Mods.Where(mod => mod.Ownership == ModInstallOwnership.Borea && !missing.Contains(mod.ModId, ModIds.Comparer)) ?? [])
        {
            var newer = await FindAsync(active!, installed);
            if (generation != _contentUpdateCheckGeneration)
                return;

            if (newer is not null)
                count++;
        }

        ActiveInstanceUpdateCount = count;
    }

    /// <summary>
    /// The recorded mods of the instance that have no folder. Empty when there
    /// is no instance or the folders cannot be read, so a count is never lost
    /// over a read that failed.
    /// </summary>
    private static async Task<IReadOnlyList<string>> MissingModIdsAsync(BoreaServices services, Instance? instance)
    {
        if (instance is null)
            return [];

        try
        {
            return await services.MissingMods.ScanAsync(instance.InstanceId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return [];
        }
    }

    private static async Task<ModVersion?> FindUpdateAsync(BoreaServices services, Instance instance, InstalledMod installed)
    {
        try
        {
            var plan = await services.InstallPlanner.PlanAsync(PlanningRequest(services, instance, UpdateRequests([installed])));
            return plan.Operations
                .Select(operation => operation.Release)
                .FirstOrDefault(release => ModIds.Equals(release.ModId, installed.ModId) && release.Version > installed.Version)?.Version;
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Updates one mod Borea owns, with what its new release requires.
    /// </summary>
    internal Task UpdateContentAsync(ContentItem item)
        => RunUpdateAsync(item, item.InstanceId, () => PlanUpdateAsync(item, item.InstanceId, mod => ModIds.Equals(mod.ModId, item.ModId)));

    /// <summary>
    /// Plans every mod Borea owns in the instance together, so a shared
    /// dependency is resolved once.
    /// </summary>
    internal Task UpdateAllContentAsync(UpdateAllItem item)
        => RunUpdateAsync(item, item.InstanceId, () => PlanUpdateAsync(item, item.InstanceId, _ => true));

    internal Task ConfirmUpdateAsync(IUpdateRow row, Guid instanceId)
        => RunUpdateAsync(row, instanceId, () => ExecutePendingPlanAsync(row, () => row.Changelogs = []));

    internal static void CancelUpdate(IUpdateRow row)
    {
        CancelInstall(row);
        row.Changelogs = [];
    }

    /// <summary>Waits for a confirmation on planner warnings, choices or passed changelogs.</summary>
    private Task<bool> PlanUpdateAsync(IUpdateRow row, Guid instanceId, Func<InstalledMod, bool> select)
    {
        row.Changelogs = [];
        return PlanAndExecuteAsync(
            row,
            instanceId,
            instance => Task.FromResult(UpdateRequests(instance.Mods.Where(mod => mod.Ownership == ModInstallOwnership.Borea && select(mod)))),
            async (instance, plan) =>
            {
                row.Changelogs = await PassedChangelogsAsync(instance, plan);
                return row.Changelogs.Count > 0;
            });
    }

    /// <summary>Newest first, from the installed release up to the planned one.</summary>
    private async Task<IReadOnlyList<ReleaseChangelog>> PassedChangelogsAsync(Instance instance, InstallPlan plan)
    {
        var changelogs = new List<ReleaseChangelog>();
        if (_services is not { } services)
            return changelogs;

        foreach (var target in plan.Operations.Select(operation => operation.Release))
        {
            var installed = instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, target.ModId));
            if (installed is null || target.Version <= installed.Version)
                continue;

            var name = _content.FirstOrDefault(content => ModIds.Equals(content.ModId, target.ModId))?.Name ?? target.Listing?.Name ?? target.ModId;
            try
            {
                var versions = await services.Mods.GetAvailableVersionsAsync(target.ModId);
                foreach (var version in versions.Where(version => version > installed.Version && version <= target.Version).OrderByDescending(version => version))
                {
                    var release = version == target.Version ? target : await services.Mods.GetReleaseAsync(target.ModId, version);
                    if (release is not null && ReleaseChangelog.From(release, $"{name} {version}", Localization.ContentChangelog) is { } changelog)
                        changelogs.Add(changelog);
                }
            }
            catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
            {
                // the update does not depend on its changelog
            }
        }

        return changelogs;
    }

    private static IReadOnlyList<RequestedMod> UpdateRequests(IEnumerable<InstalledMod> mods)
        => mods.Select(mod => new RequestedMod(mod.Metadata, mod.Reason, Exact: false)).ToList();

    /// <summary>
    /// Runs one update per instance at a time. The reload builds new rows, so
    /// an error or a stop is shown again afterwards, on the row of the same mod.
    /// </summary>
    private async Task RunUpdateAsync(IInstallRow row, Guid instanceId, Func<Task<bool>> run)
    {
        if (!_runningUpdates.TryAdd(instanceId, row))
            return;

        OnPropertyChanged(nameof(CanChangeContent));
        bool executed;
        try
        {
            executed = await run();
        }
        finally
        {
            _runningUpdates.Remove(instanceId);
            OnPropertyChanged(nameof(CanChangeContent));
        }

        if (!executed)
            return;

        var error = row.InstallError;
        var stopped = row.ProgressStatus;
        await ReloadInstancesAsync();
        if ((error is null && stopped is null) || SelectedInstance?.InstanceId != instanceId)
            return;

        IInstallRow? target = row switch
        {
            ContentItem item => _content.FirstOrDefault(content => ModIds.Equals(content.ModId, item.ModId)),
            PackUpdateItem => PackUpdate,
            MissingContentItem => MissingContent,
            _ => UpdateAll,
        };
        if (target is not null)
        {
            target.InstallError = error;
            target.ProgressStatus = stopped;
        }
    }

    /// <summary>What the Home launch button starts, which is the option last chosen in its menu.</summary>
    public HomeLaunchOption HomeLaunch
    {
        get => _homeLaunch ?? _appPreferences.HomeLaunch;
        private set
        {
            if (value == HomeLaunch)
                return;

            _homeLaunch = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHomeLaunchActiveInstance));
            OnPropertyChanged(nameof(HomeLaunchText));
            OnPropertyChanged(nameof(EnableHomeLaunch));
            QueuePreferenceSave(preferences => preferences.WithHomeLaunch(value));
        }
    }

    public bool IsHomeLaunchActiveInstance => HomeLaunch == HomeLaunchOption.ActiveInstance;

    public string HomeLaunchText => IsHomeLaunchActiveInstance ? Localization.LaunchActiveInstance : Localization.LaunchWithoutModLoader;

    public bool EnableHomeLaunch => IsHomeLaunchActiveInstance ? EnableActiveInstance : !IsLaunching;

    [RelayCommand]
    private Task PlayAsync() => SelectedInstance is { } instance ? LaunchAsync(instance.InstanceId) : Task.CompletedTask;

    /// <summary>Launches the active instance, and makes it what the Home launch button starts.</summary>
    [RelayCommand]
    private Task PlayActiveInstanceAsync()
    {
        HomeLaunch = HomeLaunchOption.ActiveInstance;
        return LaunchActiveInstanceAsync();
    }

    internal Task LaunchActiveInstanceAsync() => ActiveInstance is { } instance ? LaunchAsync(instance.InstanceId) : Task.CompletedTask;

    [RelayCommand]
    private Task PlayHomeAsync() => IsHomeLaunchActiveInstance ? PlayActiveInstanceAsync() : PlayWithoutModLoader();

    /// <summary>Starts the instance through the loader that <see cref="LaunchLoaderChoice"/> picks and watches the start.</summary>
    private async Task LaunchAsync(Guid instanceId)
    {
        if (_services is not { } services || IsLaunching)
            return;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            LaunchMessage = Localization.LibraryFolderBusy;
            return;
        }

        IsLaunching = true;
        _launchInstanceId = instanceId;
        ClearLaunchFailure();
        try
        {
            var instance = await services.Instances.GetByIdAsync(instanceId)
                ?? throw new InvalidOperationException(Localization.LaunchInstanceMissing);
            var listings = await services.Mods.GetAvailableModsAsync();
            var choice = LaunchLoaderChoice.Choose(instance, services.Settings.LoaderInstallations, listings);
            if (!choice.Succeeded)
            {
                var loaderIds = choice.LoaderIds.Count == 0 ? "" : ": " + string.Join(", ", choice.LoaderIds);
                services.Log.Write($"Launch of instance {instance.InstanceId} did not start, {choice.Failure}{loaderIds}.");
                if (LoaderToInstall(choice, listings) is { } missing)
                    OpenLoaderPrompt(instanceId, missing);
                else
                    LaunchMessage = LaunchLoaderFailureText(choice);
                return;
            }

            var loader = choice.Loader;
            if (services.Settings.GameDirectoryPath is { } gameDirectory
                && await services.ModState.PutGameContentFirstAsync(instance.InstanceId, gameDirectory))
            {
                services.Log.Write($"Instance {instance.InstanceId}: the game's own content now loads before the mods.");
            }

            var result = services.Launcher.Launch(instance, loader);
            if (result.Started)
            {
                // the loader can still stop while it loads the mods, so the start is watched before it counts
                LaunchMessage = Localization.FormatLaunchStarting(loader.Name);
                result = await services.Launcher.WatchStartAsync(instance, result);
            }

            if (result.Outcome == LaunchOutcome.ExitedEarly)
                await ShowLaunchFailureAsync(result, instance, loader);
            else
                LaunchMessage = LaunchResultText(result, loader);

            if (result.Started)
                await RefreshLastPlayedAsync(instance.InstanceId);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Net.Http.HttpRequestException)
        {
            LaunchMessage = exception.Message;
        }
        finally
        {
            IsLaunching = false;
        }
    }

    /// <summary>Launches the game without a mod loader, and makes that what the Home launch button starts.</summary>
    [RelayCommand]
    private async Task PlayWithoutModLoader()
    {
        HomeLaunch = HomeLaunchOption.WithoutModLoader;
        if (_services is null || IsLaunching)
            return;

        IsLaunching = true;
        _launchInstanceId = null;
        ClearLaunchFailure();
        try
        {
            var result = _services.SharedProfileLauncher.Launch();
            LaunchMessage = result.Message;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Net.Http.HttpRequestException)
        {
            LaunchMessage = exception.Message;
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private async Task ShowLaunchFailureAsync(LaunchResult result, Instance instance, ModMetadata? loader)
    {
        var loaderName = _launchLoaderName = loader?.Name ?? string.Empty;
        var blamed = result.BlamedModId is null ? null : instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, result.BlamedModId));
        var loadingMods = result.CrashCause == LoaderCrashCause.ModLoading;
        if (blamed is null)
        {
            LaunchMessage = loadingMods
                ? Localization.FormatLaunchStoppedLoadingMods(loaderName, result.ExitCode ?? 0)
                : Localization.FormatLaunchExitedEarly(loaderName, result.ExitCode ?? 0);
        }
        else
        {
            // the same name the content row shows, also when the instance page was never opened
            _launchBlamedModName = blamed.Metadata.Listing?.Name ?? (await ResolveListingAsync(blamed.ModId))?.Name ?? blamed.ModId;
            LaunchMessage = loadingMods
                ? Localization.FormatLaunchModLikelyBroke(_launchBlamedModName, blamed.Version.ToString(), loaderName)
                : Localization.FormatLaunchModBroke(_launchBlamedModName, blamed.Version.ToString(), loaderName);
        }

        IReadOnlyList<string> output = result.Output.Count == 0 ? [Localization.LaunchNoOutput] : result.Output;
        LaunchOutputText = result.ExitCode is { } exitCode
            ? string.Join(Environment.NewLine, [Localization.FormatLaunchExitCode(LoaderExitCode.Describe(exitCode, OperatingSystem.IsWindows())), string.Empty, .. output])
            : string.Join(Environment.NewLine, output);
        LaunchBlamedModId = blamed?.ModId;
        OnPropertyChanged(nameof(DisableBlamedModText));
        OnPropertyChanged(nameof(LaunchFailureTitle));
        LaunchFailureError = null;
        IsLaunchFailureOpen = true;
    }

    private void ClearLaunchFailure()
    {
        IsLaunchFailureOpen = false;
        LaunchFailureError = null;
        LaunchOutputText = null;
        IsLaunchOutputShown = false;
        LaunchBlamedModId = null;
        _launchBlamedModName = null;
        _launchLoaderName = null;
        OnPropertyChanged(nameof(DisableBlamedModText));
        OnPropertyChanged(nameof(LaunchFailureTitle));
    }

    [RelayCommand]
    private void ShowLaunchFailure()
    {
        if (!HasLaunchOutput)
            return;

        LaunchFailureError = null;
        IsLaunchFailureOpen = true;
    }

    [RelayCommand]
    private void CloseLaunchFailure() => IsLaunchFailureOpen = false;

    [RelayCommand]
    private void ToggleLaunchOutput() => IsLaunchOutputShown = !IsLaunchOutputShown;

    [RelayCommand]
    private async Task DisableBlamedModAsync()
    {
        if (_launchInstanceId is not { } instanceId || LaunchBlamedModId is not { } modId || _launchBlamedModName is not { } name)
            return;

        LaunchFailureError = await TrySetContentEnabledAsync(instanceId, modId, enabled: false);
        if (LaunchFailureError is not null)
            return;

        if (CurrentWindowInstance && SelectedInstance is { } shown && shown.InstanceId == instanceId)
            await OpenInstanceAsync(shown);
        ClearLaunchFailure();
        LaunchMessage = Localization.FormatLaunchModDisabled(name);
    }

    [RelayCommand]
    private void OpenLaunchLog()
    {
        if (_services is not null && _launchInstanceId is { } instanceId)
            LaunchFailureError = TryOpenWithSystem(_services.Paths.GetInstanceLaunchLogPath(instanceId));
    }

    private string LaunchResultText(LaunchResult result, ModMetadata loader) => result.Outcome switch
    {
        LaunchOutcome.UnknownPlatformKey => Localization.FormatLaunchUnknownPlatformKey(loader.Name, result.UnknownName!),
        LaunchOutcome.UnknownRuntime => Localization.FormatLaunchUnknownRuntime(loader.Name, result.UnknownName!),
        LaunchOutcome.DotnetMissing => Localization.FormatLaunchDotnetMissing(loader.Name),
        LaunchOutcome.LaunchTargetMissing when result.Plan is { } plan => Localization.FormatLaunchTargetMissing(plan.Executable, loader.Name),
        _ => result.Message,
    };

    private string LaunchLoaderFailureText(LaunchLoaderChoice choice) => choice.Failure switch
    {
        LaunchLoaderFailure.GivenLoaderNotInstalled => Localization.FormatLaunchLoaderNotInstalled(choice.LoaderIds[0]),
        LaunchLoaderFailure.NeededLoaderNotInstalled => Localization.FormatLaunchNeededLoaderNotInstalled(choice.LoaderIds[0]),
        LaunchLoaderFailure.DifferentLoadersNeeded => Localization.FormatLaunchDifferentLoadersNeeded(string.Join(", ", choice.LoaderIds)),
        LaunchLoaderFailure.LoaderNotListed => Localization.FormatLaunchLoaderNotListed(string.Join(", ", choice.LoaderIds)),
        LaunchLoaderFailure.NoLoaderTakesInstance => Localization.LaunchNoLoaderTakesInstance,
        _ => throw new ArgumentOutOfRangeException(nameof(choice), choice.Failure, null),
    };

    internal async Task SetContentEnabledAsync(Guid instanceId, string modId, string name, bool enabled)
    {
        if (await TrySetContentEnabledAsync(instanceId, modId, enabled) is { } error)
            ShowErrorToast(() => enabled ? Localization.FormatToastEnableFailed(name) : Localization.FormatToastDisableFailed(name), error);
    }

    /// <summary>Enables or disables the mod, or returns why it could not.</summary>
    private async Task<string?> TrySetContentEnabledAsync(Guid instanceId, string modId, bool enabled)
    {
        if (_services is null)
            return null;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
            return Localization.LibraryFolderBusy;

        if (enabled && IsMissingContent(instanceId, modId))
            return Localization.ContentNotOnDiskEnable;

        try
        {
            if (enabled)
                await _services.ModState.SetActiveAsync(instanceId, modId);
            else
                await _services.ModState.SetInactiveAsync(instanceId, modId);
            return null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// The listing for a mod id, from the loaded catalog when possible, else
    /// from the repository. Null when no source knows it.
    /// </summary>
    private async Task<ModMetadata?> ResolveListingAsync(string modId)
    {
        if (_services is null)
            return null;

        if (_listingCache.TryGetValue(modId, out var cached))
            return cached;

        ModMetadata? listing = null;
        try
        {
            listing = await _services.Mods.GetListingAsync(modId);
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // the row falls back to the id
        }

        _listingCache[modId] = listing;
        return listing;
    }

    private readonly Dictionary<string, ModMetadata?> _listingCache = new(ModIds.Comparer);

    /// <summary>
    /// Removes a mod Borea installed, with its folder and its record. A mod
    /// that another installed mod requires stays, and so does a mod Borea did
    /// not install, because its files are not Borea's to delete. The toast of
    /// the task says why a mod stays.
    /// </summary>
    internal async Task RemoveContentAsync(Guid instanceId, string modId)
    {
        if (_services is null || _runningUpdates.ContainsKey(instanceId))
            return;

        var name = _content.FirstOrDefault(content => content.InstanceId == instanceId && ModIds.Equals(content.ModId, modId))?.Name ?? modId;
        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(() => Localization.FormatToastRemoveFailed(name), Localization.LibraryFolderBusy);
            return;
        }

        var task = StartTask(TaskKind.ModRemoval, name, instanceId, modId);
        var completed = false;
        string? error = null;
        try
        {
            error = await TryRemoveContentAsync(_services, instanceId, modId);
            completed = true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = exception.Message;
        }
        finally
        {
            EndTask(task, completed, stopped: false, error);
        }

        await ReloadInstancesAsync();
    }

    /// <summary>
    /// Why the button cannot remove <paramref name="mod"/> from
    /// <paramref name="instance"/>, or null when it can: files Borea did not
    /// install, or another mod that needs it. The same rules
    /// <see cref="TryRemoveContentAsync"/> applies when it runs.
    /// </summary>
    internal string? RemoveBlockedReason(Instance? instance, InstalledMod? mod)
    {
        if (instance is null || mod is null)
            return null;

        if (mod.Ownership != ModInstallOwnership.Borea)
            return Localization.FormatContentRemoveNotOwned(mod.ModId);

        var check = new ModDependencyResolver().CheckUninstall(instance, mod.ModId, mod.Version, isActive: false);
        return check.CanUninstall ? null : Localization.FormatContentRemoveRequired(mod.ModId, string.Join(", ", check.DependentModIds));
    }

    /// <summary>
    /// Installs the release a mod Borea did not install is recorded as over its
    /// folder, so Borea owns it from then on. The mod keeps its place in the
    /// instance and its enabled state, and a handover that fails leaves it
    /// foreign. It takes the run slot of the instance, because it writes the
    /// same manifest an update writes and nothing below serializes the two.
    /// </summary>
    internal Task TakeOwnershipOfContentAsync(ContentItem item)
        => RunUpdateAsync(item, item.InstanceId, () => RunHandoverAsync(item));

    /// <summary>
    /// Returns whether the handover ran, so the caller reloads the instances.
    /// </summary>
    private async Task<bool> RunHandoverAsync(ContentItem item)
    {
        if (_services is not { } services || item.IsInstalling)
            return false;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(() => Localization.FormatToastManageFailed(item.Name), Localization.LibraryFolderBusy);
            return false;
        }

        item.IsConfirmingManage = false;
        item.InstallError = null;
        item.IsInstalling = true;
        var run = item.Run = StartInstallRun(StartTask(TaskKind.ModHandover, item.Name, item.InstanceId, item.ModId));
        var completed = false;
        string? stopped = null;
        string? error = null;
        try
        {
            // the stop reaches the download only, so a stopped handover never touched the folder
            await run.InstallStop.RunAsync(
                (progress, cancellationToken) => services.ForeignModHandover.TakeOwnershipAsync(item.InstanceId, item.ModId, progress, cancellationToken),
                ProgressOf(item));
            completed = true;
        }
        catch (InstallStoppedException)
        {
            stopped = Localization.InstallStopped;
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            error = item.InstallError = InstallFailureText(exception);
        }
        finally
        {
            EndInstallRun(run, completed, stopped is not null, error);
            item.IsInstalling = false;
            item.Run = null;
            item.Progress = 0;
            item.ProgressStatus = stopped;
            item.ProgressDetail = null;
        }

        return true;
    }

    /// <summary>
    /// Removes the mod, or returns why it stays.
    /// </summary>
    private async Task<string?> TryRemoveContentAsync(BoreaServices services, Guid instanceId, string modId)
    {
        var instance = await services.Instances.GetByIdAsync(instanceId);
        var installed = instance?.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, modId));
        if (instance is null || installed is null)
            return null;

        if (installed.Ownership != ModInstallOwnership.Borea)
            return Localization.FormatContentRemoveNotOwned(installed.ModId);

        var active = await services.ModState.IsActiveAsync(instanceId, installed.ModId);
        var check = new ModDependencyResolver().CheckUninstall(instance, installed.ModId, installed.Version, active);
        if (!check.CanUninstall)
            return Localization.FormatContentRemoveRequired(installed.ModId, string.Join(", ", check.DependentModIds));

        await services.Uninstaller.UninstallAsync(instanceId, installed.ModId);
        return null;
    }
}

/// <summary>An update on the instance page.</summary>
internal interface IUpdateRow : IInstallRow
{
    IReadOnlyList<ReleaseChangelog> Changelogs { get; set; }
}

/// <param name="IsDependencies">The design shows this group last, after the saves and vehicles.</param>
public sealed record ContentGroup(string Title, IReadOnlyList<ContentItem> Items, bool IsDependencies = false);

/// <summary>
/// One row of the instance's content table.
/// </summary>
public sealed partial class ContentItem : ObservableObject, IUpdateRow
{
    private readonly MainViewModel _owner;

    internal Guid InstanceId { get; }

    public string ModId { get; }

    public string Name { get; }

    public string? Authors { get; }

    public string Version { get; }

    public ContentType Type { get; }

    public InstallReason Reason { get; }

    public bool IsDependency { get; }

    /// <summary>Borea installed the files, so it may update them.</summary>
    public bool IsOwned { get; }

    public string? AuthorsText => Authors is null ? null : _owner.Localization.FormatContentByAuthor(Authors);

    private readonly DiscoverItem? _page;

    private readonly Instance _instance;

    private readonly InstalledMod _mod;

    /// <summary>
    /// Why the remove button is disabled, for its tooltip. Null when the mod can
    /// be removed, which a mod without a folder always can, because the button
    /// then only drops its record.
    /// </summary>
    public string? RemoveBlockedText => IsMissing ? null : _owner.RemoveBlockedReason(_instance, _mod);

    public bool CanRemove => RemoveBlockedText is null;

    public string RemoveToolTip => IsMissing
        ? _owner.Localization.ContentRemoveFromList
        : RemoveBlockedText ?? _owner.Localization.ContentRemove;

    public string RemoveConfirmText => IsMissing
        ? _owner.Localization.ContentRemoveFromListConfirm
        : _owner.Localization.ContentRemoveConfirm;

    public string RemoveActionText => IsMissing ? _owner.Localization.ContentRemoveFromList : _owner.Localization.ContentRemove;

    /// <summary>Whether the row links to the mod page, which every mod in the content index does.</summary>
    public bool CanOpen => _page is not null;

    /// <summary>The row itself while it links to the mod page, so that only the linked body is built.</summary>
    public ContentItem? PageRow => CanOpen ? this : null;

    /// <summary>The row itself while it has no page, so that only the plain body is built.</summary>
    public ContentItem? PlainRow => CanOpen ? null : this;

    /// <summary>The icon of the index listing with the mod's id, also when the row does not link to it.</summary>
    public ListingImage? Icon { get; }

    /// <summary>Why the row has no link, for its tooltip. Null when it links.</summary>
    public string? NoPageText => CanOpen ? null : _owner.Localization.InstanceContentNotInIndex;

    /// <summary>Whether the row offers to let Borea install the recorded release over the folder and own it.</summary>
    public bool CanManage => !IsOwned && !IsInstalling;

    public string ManageConfirmText => _owner.Localization.FormatContentManageConfirm(Name, Version);

    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>The mod has no folder with a mod.toml, so the game would not load it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(RemoveBlockedText))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(RemoveToolTip))]
    [NotifyPropertyChangedFor(nameof(RemoveConfirmText))]
    [NotifyPropertyChangedFor(nameof(RemoveActionText))]
    private bool _isMissing;

    private bool _isOpening;

    /// <summary>What the mod's folder takes on disk, or null until it is measured or when the folder is gone.</summary>
    [ObservableProperty]
    private string? _sizeText;

    [ObservableProperty]
    private bool _isConfirmingRemove;

    [ObservableProperty]
    private bool _isConfirmingManage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    private string? _updateVersion;

    /// <summary>A dependency shows no update of its own, "Update all" updates it.</summary>
    public bool HasUpdate => UpdateVersion is not null && !IsInstalling && !IsDependency && !IsMissing;

    public string? UpdateText => UpdateVersion is null ? null : _owner.Localization.FormatContentUpdateTo(UpdateVersion);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
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

    /// <summary>
    /// The planner's warnings while <see cref="PendingPlan"/> waits for a confirmation.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingUpdate))]
    [NotifyPropertyChangedFor(nameof(ConfirmUpdateText))]
    private string? _installWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingUpdate))]
    [NotifyPropertyChangedFor(nameof(HasChangelogs))]
    private IReadOnlyList<ReleaseChangelog> _changelogs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingUpdate))]
    private InstallChoices? _choices;

    public bool HasChangelogs => Changelogs.Count > 0;

    public bool IsConfirmingUpdate => InstallWarning is not null || HasChangelogs || Choices is not null;

    public string ConfirmUpdateText => InstallWarning is null ? _owner.Localization.ContentUpdate : _owner.Localization.UpdateAnyway;

    public InstallPlan? PendingPlan { get; set; }

    public ContentItem(MainViewModel owner, Instance instance, InstalledMod mod, bool enabled, ModMetadata? listing, DiscoverItem? page = null, ListingImage? icon = null)
    {
        _owner = owner;
        _instance = instance;
        _mod = mod;
        InstanceId = instance.InstanceId;
        _page = page;
        Icon = icon;
        ModId = mod.ModId;
        Name = mod.Metadata.Listing?.Name ?? listing?.Name ?? mod.ModId;
        var authors = mod.Metadata.Listing?.Authors ?? listing?.Authors;
        Authors = authors is { Count: > 0 } ? string.Join(", ", authors) : null;
        Version = mod.Version.ToString();
        Type = mod.Metadata.Type;
        Reason = mod.Reason;
        IsDependency = mod.Reason == InstallReason.Dependency;
        IsOwned = mod.Ownership == ModInstallOwnership.Borea;
        _isEnabled = enabled;
    }

    [RelayCommand]
    private Task ToggleEnabledAsync() => _owner.SetContentEnabledAsync(InstanceId, ModId, Name, IsEnabled);

    /// <summary>
    /// The whole row is this command, so it stays executable while the page
    /// loads, because a command that cannot execute greys out every control on
    /// the row. The flag takes over the job of dropping a second click.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OpenAsync()
    {
        if (_page is null || _isOpening)
            return;

        _isOpening = true;
        try
        {
            await _owner.OpenContentFromInstanceAsync(_page);
        }
        finally
        {
            _isOpening = false;
        }
    }

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(AuthorsText));
        OnPropertyChanged(nameof(NoPageText));
        OnPropertyChanged(nameof(RemoveBlockedText));
        OnPropertyChanged(nameof(RemoveToolTip));
        OnPropertyChanged(nameof(RemoveConfirmText));
        OnPropertyChanged(nameof(RemoveActionText));
        OnPropertyChanged(nameof(UpdateText));
        OnPropertyChanged(nameof(ManageConfirmText));
    }

    [RelayCommand]
    private void BeginRemove()
    {
        MainViewModel.CancelUpdate(this);
        IsConfirmingRemove = true;
    }

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    [RelayCommand]
    private Task ConfirmRemoveAsync() => IsMissing
        ? _owner.DropMissingContentAsync(InstanceId, [ModId])
        : _owner.RemoveContentAsync(InstanceId, ModId);

    [RelayCommand]
    private Task InstallAgainAsync() => _owner.InstallMissingAgainAsync(this, InstanceId, [ModId]);

    [RelayCommand]
    private void BeginManage()
    {
        MainViewModel.CancelUpdate(this);
        IsConfirmingManage = true;
    }

    [RelayCommand]
    private void CancelManage() => IsConfirmingManage = false;

    [RelayCommand]
    private Task ConfirmManageAsync() => _owner.TakeOwnershipOfContentAsync(this);

    [RelayCommand]
    private Task UpdateAsync() => _owner.UpdateContentAsync(this);

    [RelayCommand]
    private Task ConfirmUpdateAsync() => IsMissing
        ? _owner.ConfirmMissingInstallAsync(this, InstanceId, [ModId])
        : _owner.ConfirmUpdateAsync(this, InstanceId);

    [RelayCommand]
    private void CancelUpdate() => MainViewModel.CancelUpdate(this);
}

/// <summary>
/// "Update all" on the instance page. It holds its plan and its outcome the
/// way a row does.
/// </summary>
public sealed partial class UpdateAllItem : ObservableObject, IUpdateRow
{
    private readonly MainViewModel _owner;

    internal Guid InstanceId { get; }

    [ObservableProperty]
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingUpdate))]
    [NotifyPropertyChangedFor(nameof(ConfirmUpdateText))]
    private string? _installWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingUpdate))]
    [NotifyPropertyChangedFor(nameof(HasChangelogs))]
    private IReadOnlyList<ReleaseChangelog> _changelogs = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingUpdate))]
    private InstallChoices? _choices;

    public bool HasChangelogs => Changelogs.Count > 0;

    public bool IsConfirmingUpdate => InstallWarning is not null || HasChangelogs || Choices is not null;

    public string ConfirmUpdateText => InstallWarning is null ? _owner.Localization.ContentUpdate : _owner.Localization.UpdateAnyway;

    public InstallPlan? PendingPlan { get; set; }

    public UpdateAllItem(MainViewModel owner, Guid instanceId)
    {
        _owner = owner;
        InstanceId = instanceId;
    }

    [RelayCommand]
    private Task UpdateAsync() => _owner.UpdateAllContentAsync(this);

    [RelayCommand]
    private Task ConfirmUpdateAsync() => _owner.ConfirmUpdateAsync(this, InstanceId);

    [RelayCommand]
    private void CancelUpdate() => MainViewModel.CancelUpdate(this);
}
