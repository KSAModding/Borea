using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.History;
using Borea.Core.Index;
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
    [NotifyPropertyChangedFor(nameof(IsGameDataTab))]
    [NotifyPropertyChangedFor(nameof(IsLogTab))]
    private InstanceTab _instanceTab;

    public bool IsContentTab => InstanceTab == InstanceTab.Content;

    public bool IsGameDataTab => InstanceTab == InstanceTab.GameData;

    public bool IsLogTab => InstanceTab == InstanceTab.Log;

    public ObservableCollection<ContentGroup> ContentGroups { get; } = [];

    public bool HasContent => _content.Count > 0;

    /// <summary>"2 mods differ from Flight Planning Essentials 1.0.1", or null when the instance has its pack as the pack pins it.</summary>
    [ObservableProperty]
    private string? _packDifferenceText;

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
            var goneSince = indexed is null ? null : await UnavailableSinceAsync(mod);
            content.Add(new ContentItem(this, _selectedInstanceEntity!, mod, enabled.Contains(mod.ModId), listing, indexed, indexed?.Icon, goneSince));
        }

        _content = content.OrderBy(content => content.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        await ShowMissingContentAsync(item.InstanceId);
        _contentPack = await ResolveSourcePackAsync(_selectedInstanceEntity?.Source);
        ShowPackUpdate(item.InstanceId, await FindPackUpdateAsync(_selectedInstanceEntity));
        RefreshContentGroups();
        RefreshInstanceLoader();
        OnPropertyChanged(nameof(HasUpdates));
        await LoadGameSavesAsync();
        await LoadManualInstallsAsync();
        if (IsGameDataTab)
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
        StartModStoreCheck(item.InstanceId);
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
        {
            item.RefreshText();
            // a pack version that no longer pins the mod would remove it once it follows the pack again
            item.CanAttach = item.IsDetached && (_contentPack is null || _contentPack.Mods.Any(pin => ModIds.Equals(pin.ContentId, item.ModId)));
            item.DetachedText = DetachedText(item);
        }

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
        PackDifferenceText = DescribePackDifference(_selectedInstanceEntity, _contentPack);

        void Add(string title, IEnumerable<ContentItem> items, bool isDependencies = false)
        {
            var list = items.ToList();
            if (list.Count > 0)
                ContentGroups.Add(new ContentGroup(title, list, isDependencies));
        }
    }

    /// <summary>The tooltip of the Detached chip, which names the pack and the version it pins, or null for a row that cannot attach.</summary>
    private string? DetachedText(ContentItem item)
    {
        if (!item.CanAttach || _selectedInstanceEntity?.Source is not InstanceSource.FromModPack source)
            return null;

        return _contentPack?.Mods.FirstOrDefault(pin => ModIds.Equals(pin.ContentId, item.ModId)) is { } pinned
            ? Localization.FormatContentDetachedFrom(_contentPack.Name, pinned.Version.ToString())
            : Localization.FormatContentDetachedFromPack(source.ModPackId);
    }

    /// <summary>
    /// A detached mod that the pack pins always differs, and so does a mod that is gone or at another version than
    /// the pack pins. Without the pack version from the index, every detached mod counts.
    /// </summary>
    private string? DescribePackDifference(Instance? instance, ModPackMetadata? pack)
    {
        if (instance?.Source is not InstanceSource.FromModPack source)
            return null;

        if (pack is null)
            return source.Detached.Count == 0 ? null : Localization.FormatInstancePackDifference(source.Detached.Count, source.ModPackId, source.Version.ToString());

        var differ = new HashSet<string>(ModIds.Comparer);
        foreach (var pin in pack.Mods)
        {
            if (source.Detached.Contains(pin.ContentId) || instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, pin.ContentId))?.Version != pin.Version)
                differ.Add(pin.ContentId);
        }

        return differ.Count == 0 ? null : Localization.FormatInstancePackDifference(differ.Count, pack.Name, source.Version.ToString());
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

        // the shown instance is often the active one, so each mod is planned once,
        // and only its rows look for a pin that holds an update back
        var found = new Dictionary<(Guid InstanceId, string ModId), UpdateCheck>();
        async Task<UpdateCheck> FindAsync(Instance instance, InstalledMod installed, bool findHeld)
        {
            if (!found.TryGetValue((instance.InstanceId, installed.ModId), out var check))
                found[(instance.InstanceId, installed.ModId)] = check = await FindUpdateAsync(services, instance, installed, findHeld);
            return check;
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

                var check = await FindAsync(shown, installed, findHeld: true);
                if (generation != _contentUpdateCheckGeneration)
                    return;

                item.UpdateVersion = check.Newer?.ToString();
                item.HeldUpdate = check.Held;
            }

            OnPropertyChanged(nameof(HasUpdates));
        }

        // the Home card counts what the instance page offers, and no update
        // reaches a mod whose folder is gone
        var missing = await MissingModIdsAsync(services, active);
        var count = 0;
        foreach (var installed in active?.Mods.Where(mod => mod.Ownership == ModInstallOwnership.Borea && !missing.Contains(mod.ModId, ModIds.Comparer)) ?? [])
        {
            var check = await FindAsync(active!, installed, findHeld: false);
            if (generation != _contentUpdateCheckGeneration)
                return;

            if (check.Newer is not null)
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

    /// <summary>
    /// The planner ranks a conflict above a newer release, so a pin can keep the update at the installed release
    /// without a word. Only then, when <paramref name="findHeld"/> is set and the newest release needs another
    /// version of a pinned mod, the newest release is planned once more on its own to find the pin that holds it back.
    /// </summary>
    private static async Task<UpdateCheck> FindUpdateAsync(BoreaServices services, Instance instance, InstalledMod installed, bool findHeld)
    {
        try
        {
            var plan = await services.InstallPlanner.PlanAsync(PlanningRequest(services, instance, UpdateRequests([installed])));
            var newer = plan.Operations
                .Select(operation => operation.Release)
                .FirstOrDefault(release => ModIds.Equals(release.ModId, installed.ModId) && release.Version > installed.Version)?.Version;
            if (newer is not null || !findHeld || installed.IsPinned || !instance.Mods.Any(mod => mod.IsPinned))
                return new UpdateCheck(newer, null);

            if (await services.Mods.GetLatestReleaseAsync(installed.ModId) is not { } latest || latest.Version <= installed.Version || !NeedsOtherPinnedVersion(instance, latest))
                return UpdateCheck.None;

            var exact = await services.InstallPlanner.PlanAsync(PlanningRequest(services, instance, [new RequestedMod(latest, installed.Reason)]));
            bool IsOwnPin(PlanningMessage conflict) => conflict.Kind == PlanningMessageKind.PinnedDependency && ModIds.Equals(conflict.ModId, installed.ModId);
            // unpinning must bring the update, so a conflict the update plan does not already have hides the note
            var known = plan.Conflicts.Select(ConflictKey).ToHashSet();
            if (exact.Conflicts.FirstOrDefault(IsOwnPin) is not { } pin || exact.Conflicts.Any(conflict => !IsOwnPin(conflict) && !known.Contains(ConflictKey(conflict))))
                return UpdateCheck.None;

            return new UpdateCheck(null, new HeldUpdate(latest.Version, pin));
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            return UpdateCheck.None;
        }
    }

    /// <summary>
    /// An update check asks for no alternative and no recommendation, so only a required dependency of the release
    /// itself gives it a pinned dependency conflict of its own.
    /// </summary>
    private static bool NeedsOtherPinnedVersion(Instance instance, ModVersionMetadata release)
        => release.Dependencies.Any(dependency => dependency.Kind == ModDependencyKind.Required && !dependency.IsAnyOf
            && instance.Mods.FirstOrDefault(mod => mod.IsPinned && ModIds.Equals(mod.ModId, dependency.ModId)) is { } pinned
            && !dependency.BoundsContain(pinned.Version));

    private static (string ModId, string Code, string Message) ConflictKey(PlanningMessage conflict) => (conflict.ModId, conflict.Code, conflict.Message);

    private sealed record UpdateCheck(ModVersion? Newer, HeldUpdate? Held)
    {
        public static UpdateCheck None { get; } = new(null, null);
    }

    /// <summary>"2.0.0 needs Library >= 1.1.0, which is pinned at 1.0.0", with the name of the pinned mod when the instance shows it.</summary>
    internal string HeldUpdateText(HeldUpdate held)
    {
        var pinnedId = held.Pin.Value ?? string.Empty;
        var name = _content.FirstOrDefault(content => ModIds.Equals(content.ModId, pinnedId))?.Name ?? pinnedId;
        var dependency = held.Pin.Dependency;
        return Localization.FormatContentHeldByPin(held.Version.ToString(), PlanningText.Bounds(name, dependency?.MinVersion, dependency?.MaxVersion), held.Pin.Version?.ToString() ?? string.Empty);
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
                // the history keeps a passed release whose download is gone, because the update still holds its changes
                var passed = (await services.Mods.GetReleaseHistoryAsync(target.ModId))
                    .Where(release => release.Version > installed.Version && release.Version < target.Version)
                    .OrderByDescending(release => release.Version);
                foreach (var release in passed.Prepend(target))
                {
                    if (ReleaseChangelog.From(release, $"{name} {release.Version}", Localization.ContentChangelog) is { } changelog)
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
            LaunchLoaderChoice choice;
            IReadOnlyList<ModMetadata> listings;
            try
            {
                // only the index lists mod loaders, so SpaceDock cannot change the choice and is not asked
                (choice, listings) = await LaunchLoaderChoice.ChooseAsync(instance, services.Settings.LoaderInstallations, services.OfflineContentIndex, services.ContentIndex);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                // a failed fetch leaves no index to read, and its reason tells the user more than the missing file does
                var reason = services.IndexRefresh.Status is { Outcome: ContentIndexRefreshOutcome.Failed, FailureReason: { } fetchFailure } ? fetchFailure : exception.Message;
                services.Log.Write($"Launch of instance {instance.InstanceId} did not start, the listings did not load: {reason}", exception);
                LaunchMessage = Localization.FormatLaunchListingsFailed(reason);
                return;
            }

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

            // The load order is a write, so it waits for a shape Borea trusts.
            // This is the shape of the instance that starts, which is not the
            // shape Home reports, so it stays a local answer.
            var shape = await services.GameShape.GetForInstanceAsync(instance.InstanceId);
            if (!shape.AllowsWrites)
                services.Log.Write($"Instance {instance.InstanceId}: the load order was left as it is. {shape.BrokenText}");

            if (shape.AllowsWrites
                && services.Settings.GameDirectoryPath is { } gameDirectory
                && await services.ModState.PutGameContentFirstAsync(instance.InstanceId, gameDirectory))
            {
                services.Log.Write($"Instance {instance.InstanceId}: the game's own content now loads before the mods.");
            }

            // an older Borea wrote the host path of the game, which a loader in a Wine prefix cannot open
            if (services.Paths.GetGameDirectoryPath() is { } game
                && services.Paths.GetLoaderDirectoryPath(loader.ModId) is { } loaderDirectory
                && await services.LoaderConfiguration.RefreshForWineAsync(loader, loaderDirectory, game) is { } refreshed)
            {
                services.Log.Write($"Wrote the game path of the Wine prefix to '{refreshed}' before the launch.");
            }

            var result = services.Launcher.Launch(instance, loader);
            if (result.Outcome == LaunchOutcome.WrapperRuntimeMissing)
            {
                OpenRuntimePrompt(instanceId, result, loader);
                return;
            }

            PromptedRuntime = null;
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
            {
                StartCrashWatch(instance, result);
                await RefreshLastPlayedAsync(instance.InstanceId);
                StartModStoreCheckAfterGame(instance.InstanceId);
            }
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
            LaunchMessage = result.Outcome switch
            {
                SharedProfileLaunchOutcome.WindowsBuild => WindowsBuildText(result.Wine),
                SharedProfileLaunchOutcome.PathOutsidePrefix => Localization.FormatLaunchPathOutsidePrefix(result.UnmappedPath!, result.Wine!.PrefixRoot),
                SharedProfileLaunchOutcome.WrapperBusy => Localization.FormatLaunchWrapperBusy(result.Wine!.Wrapper!.BundlePath),
                _ => result.Message,
            };
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
        if (result.Wine?.Wrapper is { } wrapper)
        {
            LaunchMessage = Localization.FormatLaunchWrapperStopped(wrapper.BundlePath, loaderName);
        }
        else if (blamed is null)
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
        LaunchOutcome.WindowsBuild => WindowsBuildText(result.Wine),
        LaunchOutcome.PathOutsidePrefix => Localization.FormatLaunchPathOutsidePrefix(result.UnmappedPath!, result.Wine!.PrefixRoot),
        LaunchOutcome.WrapperBusy => Localization.FormatLaunchWrapperBusy(result.Wine!.Wrapper!.BundlePath),
        LaunchOutcome.WrapperNeedsVariable => Localization.FormatLaunchWrapperNeedsVariable(loader.Name, loader.Provides!.Instance!.Flag!, result.Wine!.Wrapper!.BundlePath),
        LaunchOutcome.WrapperArguments => Localization.FormatLaunchWrapperArguments(result.Wine!.Wrapper!.BundlePath, loader.Name),
        LaunchOutcome.WrapperRuntime => Localization.FormatLaunchWrapperRuntime(loader.Name, result.Wine!.Wrapper!.BundlePath),
        _ => result.Message,
    };

    private string WindowsBuildText(WineInstall? wine) => wine switch
    {
        { } prefix => Localization.FormatLaunchWindowsBuildInPrefix(prefix.PrefixRoot),
        null => Localization.LaunchWindowsBuildWithoutWine,
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

    /// <summary>Pins or unpins a mod Borea owns.</summary>
    internal Task SetContentPinnedAsync(ContentItem item, bool pinned)
        => ChangeContentRecordAsync(
            item,
            instance => instance.SetPinned(item.ModId, pinned),
            () => pinned ? Localization.FormatToastPinFailed(item.Name) : Localization.FormatToastUnpinFailed(item.Name));

    /// <summary>Makes a mod of the pack a mod the player chose, so pack updates leave it alone.</summary>
    internal Task DetachContentFromPackAsync(ContentItem item)
        => ChangeContentRecordAsync(item, instance => instance.DetachFromModPack(item.ModId), () => Localization.FormatToastDetachFailed(item.Name));

    /// <summary>
    /// Makes a detached mod follow the pack again. When the pack pins another version than the installed one, the row
    /// then plans that version and asks first, because the files of the installed version go away with it.
    /// </summary>
    internal async Task AttachContentToPackAsync(ContentItem item)
    {
        var pack = _contentPack;
        if (!await ChangeContentRecordAsync(item, instance => instance.AttachToModPack(item.ModId), () => Localization.FormatToastAttachFailed(item.Name)))
            return;

        var pinned = pack?.Mods.Where(entry => ModIds.Equals(entry.ContentId, item.ModId)).Select(entry => (ModPackEntry?)entry).FirstOrDefault();
        var row = _content.FirstOrDefault(content => content.InstanceId == item.InstanceId && ModIds.Equals(content.ModId, item.ModId));
        if (pack is null || pinned is not { } pin || row is null || row.Version == pin.Version.ToString() || _services is not { } services)
            return;

        if (await services.Mods.GetReleaseAsync(pin.ContentId, pin.Version) is not { } release)
            return;

        await RunUpdateAsync(row, row.InstanceId, () => PlanBackToPackPinAsync(
            row,
            row.InstanceId,
            release,
            _ => { },
            () => row.PackVersionText = Localization.FormatContentAttachVersion(pin.Version.ToString(), pack.Name, pack.Version.ToString())));
    }

    /// <summary>
    /// Plans the release that the pack pins for a mod that follows the pack again, and always waits for a
    /// confirmation, because the files of the installed version go away. <paramref name="prepare"/> changes the
    /// in-memory copy of the instance that the plan starts from.
    /// </summary>
    private Task<bool> PlanBackToPackPinAsync(IInstallRow row, Guid instanceId, ModVersionMetadata pinned, Action<Instance> prepare, Action waiting)
        => PlanAndExecuteAsync(
            row,
            instanceId,
            instance =>
            {
                prepare(instance);
                return Task.FromResult<IReadOnlyList<RequestedMod>>([new RequestedMod(pinned, InstallReason.ModPack)]);
            },
            (_, _) =>
            {
                waiting();
                return Task.FromResult(true);
            });

    /// <summary>
    /// The change alters what an update plans, so it does nothing while an update of the instance runs,
    /// and the reload drops the plans that wait for a confirmation. Returns whether the record changed.
    /// </summary>
    private async Task<bool> ChangeContentRecordAsync(ContentItem item, Func<Instance, bool> change, Func<string> failed)
    {
        if (_services is not { } services || _runningUpdates.ContainsKey(item.InstanceId))
            return false;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            ShowErrorToast(failed, Localization.LibraryFolderBusy);
            return false;
        }

        bool changed;
        try
        {
            changed = await services.Instances.UpdateAsync(item.InstanceId, change);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ShowErrorToast(failed, exception.Message);
            return false;
        }

        await ReloadInstancesAsync();
        return changed;
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

    /// <summary>Since when the index marks the download of the installed release as gone, or null.</summary>
    private async Task<DateTimeOffset?> UnavailableSinceAsync(InstalledMod mod)
    {
        if (_services is null)
            return null;

        try
        {
            return (await _services.ContentIndex.GetReleaseAsync(mod.ModId, mod.Version))?.Download.UnavailableSince;
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // the row only leaves out the note
            return null;
        }
    }

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
    /// Checks what the recorded release needs before the row asks, so that the
    /// confirmation names a required dependency the instance does not have.
    /// </summary>
    internal async Task BeginManageContentAsync(ContentItem item)
    {
        if (_services is not { } services || item.IsInstalling)
            return;

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            item.InstallError = Localization.LibraryFolderBusy;
            return;
        }

        CancelUpdate(item);
        item.InstallError = null;
        try
        {
            var instance = await services.Instances.GetByIdAsync(item.InstanceId)
                ?? throw new InvalidOperationException(Localization.InstallInstanceMissing);
            var recorded = instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, item.ModId) && mod.Ownership == ModInstallOwnership.Foreign)
                ?? throw new InvalidOperationException(Localization.ManualInstallsInstanceChanged);
            item.ManageDependencies = await HandoverDependencies.Check(instance, recorded).PlanAsync(
                services.InstallPlanner,
                instance,
                recorded,
                services.Mods,
                services.InstalledVersion.GetInstalledVersion()?.Version,
                services.GamePlatform.Current);
            item.InstallManageDependencies = true;
            item.IsConfirmingManage = true;
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            item.InstallError = exception.Message;
        }
    }

    /// <summary>
    /// "Needs library >= 1.0.0, which this instance does not have.", followed
    /// by why Borea cannot install it or by the warnings of the plan that would.
    /// </summary>
    internal string? HandoverMissingText(HandoverDependencies? dependencies)
    {
        if (dependencies is not { Missing.Count: > 0 })
            return null;

        var text = Localization.FormatContentManageMissing(DependencyList(dependencies.Missing));
        if (dependencies.Plan is not { } plan)
            return text;

        if (!plan.IsReady)
            return $"{text} {Localization.FormatContentManageCannotInstall(Describe(plan.Conflicts.Concat(plan.UnresolvedChoices)))}";

        if (dependencies.NotOwned.Count > 0)
            return $"{text} {Localization.FormatContentManageNotOwned(string.Join(", ", dependencies.NotOwned.Select(ContentName)))}";

        return plan.Warnings.Count > 0 ? $"{text} {Describe(plan.Warnings)}" : text;
    }

    internal string? HandoverInstallMissingText(HandoverDependencies? dependencies)
        => dependencies is { CanInstallMissing: true, Plan: { } plan }
            ? Localization.FormatContentManageInstallMissing(string.Join(", ", plan.Operations.Select(operation => $"{ContentName(operation.Release.ModId)} {operation.Release.Version}")))
            : null;

    internal string? HandoverNotInstalledText(HandoverDependencies? dependencies)
        => dependencies is { NotInstalled.Count: > 0 }
            ? Localization.FormatContentManageNotInstalled(DependencyList(dependencies.NotInstalled))
            : null;

    private static string DependencyList(IEnumerable<ModDependency> dependencies)
        => string.Join(", ", dependencies.Select(PlanningText.Dependency));

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

        var dependencies = item.InstallManageDependencies && item.ManageDependencies is { CanInstallMissing: true } check ? check.Plan : null;
        item.IsConfirmingManage = false;
        item.ManageDependencies = null;
        item.InstallError = null;
        item.IsInstalling = true;
        var run = item.Run = StartInstallRun(StartTask(TaskKind.ModHandover, item.Name, item.InstanceId, item.ModId));
        var completed = false;
        string? stopped = null;
        string? error = null;
        try
        {
            if (dependencies is not null)
                await services.PlanExecutor.ExecuteAsync(dependencies, enable: true, ProgressOf(item), run.InstallStop);

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

/// <summary>The newest release of a mod and the planner conflict that says which pin it needs to move.</summary>
public sealed record HeldUpdate(ModVersion Version, PlanningMessage Pin);

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

    /// <summary>Updates leave the mod at its version.</summary>
    public bool IsPinned { get; }

    public bool CanPin => IsOwned && !IsPinned;

    /// <summary>The pack of the instance installed the mod, so pack updates move it until it is detached.</summary>
    public bool CanDetach { get; }

    /// <summary>The player detached the mod from the pack of the instance.</summary>
    public bool IsDetached { get; }

    /// <summary>Set with the content groups, because it needs the pack version from the index.</summary>
    [ObservableProperty]
    private bool _canAttach;

    /// <summary>The tooltip of the Detached chip while <see cref="CanAttach"/>, which names the pack and the version it pins.</summary>
    [ObservableProperty]
    private string? _detachedText;

    public string? PinnedText => IsPinned ? _owner.Localization.FormatContentPinned(Version) : null;

    public string? AuthorsText => Authors is null ? null : _owner.Localization.FormatContentByAuthor(Authors);

    private readonly DateTimeOffset? _unavailableSince;

    /// <summary>Says that the download of the installed release is gone from its host, or null while it downloads.</summary>
    public string? GoneText => _unavailableSince is { } since ? _owner.Localization.FormatContentGone(MainViewModel.DateText(since)) : null;

    private readonly DiscoverItem? _page;

    private readonly Instance _instance;

    private readonly InstalledMod _mod;

    /// <summary>
    /// Why Remove in the row menu is off, which the menu shows below it. Null when
    /// the mod can be removed, which a mod without a folder always can, because
    /// Remove then only drops its record.
    /// </summary>
    public string? RemoveBlockedText => IsMissing ? null : _owner.RemoveBlockedReason(_instance, _mod);

    public bool CanRemove => RemoveBlockedText is null;

    public string RemoveConfirmText => IsMissing
        ? _owner.Localization.ContentRemoveFromListConfirm
        : _owner.Localization.ContentRemoveConfirm;

    public string RemoveActionText => IsMissing ? _owner.Localization.ContentRemoveFromList : _owner.Localization.ContentRemove;

    /// <summary>The uninstall steps of the listing for the remove confirmation. A mod without a folder has no files for Borea to remove.</summary>
    public StepList? RemoveSteps => IsMissing ? null : _page?.RemoveSteps;

    /// <summary>Whether the row links to the mod page, which every mod in the content index does.</summary>
    public bool CanOpen => _page is not null;

    /// <summary>The Discover row of the mod page, which the row menu marks as a favorite. Null without a page.</summary>
    public DiscoverItem? Page => _page;

    /// <summary>The row itself while it links to the mod page, so that only the linked body is built.</summary>
    public ContentItem? PageRow => CanOpen ? this : null;

    /// <summary>The row itself while it has no page, so that only the plain body is built.</summary>
    public ContentItem? PlainRow => CanOpen ? null : this;

    /// <summary>The icon of the index listing with the mod's id, also when the row does not link to it.</summary>
    public ListingImage? Icon { get; }

    /// <summary>Why the row has no link, for its tooltip. Null when it links.</summary>
    public string? NoPageText => CanOpen ? null : _owner.Localization.InstanceContentNotInIndex;

    /// <summary>Whether the row offers to let Borea install the recorded release over the folder and own it.</summary>
    public bool CanManage => !IsOwned && !IsInstalling && !IsMissing;

    public string ManageConfirmText => _owner.Localization.FormatContentManageConfirm(Name, Version);

    public string? ManageMissingText => _owner.HandoverMissingText(ManageDependencies);

    /// <summary>The label of the choice to install the missing dependencies first, or null when Borea cannot install them.</summary>
    public string? ManageInstallMissingText => _owner.HandoverInstallMissingText(ManageDependencies);

    public string? ManageNotInstalledText => _owner.HandoverNotInstalledText(ManageDependencies);

    /// <summary>What the recorded release needs, checked when the row starts to ask for the handover.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManageMissingText))]
    [NotifyPropertyChangedFor(nameof(ManageInstallMissingText))]
    [NotifyPropertyChangedFor(nameof(ManageNotInstalledText))]
    private HandoverDependencies? _manageDependencies;

    [ObservableProperty]
    private bool _installManageDependencies = true;

    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>The mod has no folder with a mod.toml, so the game would not load it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    [NotifyPropertyChangedFor(nameof(RemoveBlockedText))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(RemoveConfirmText))]
    [NotifyPropertyChangedFor(nameof(RemoveActionText))]
    [NotifyPropertyChangedFor(nameof(RemoveSteps))]
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

    /// <summary>The newest release that a pin of another mod holds back, while the row has no update.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeldText))]
    private HeldUpdate? _heldUpdate;

    public string? HeldText => HeldUpdate is { } held ? _owner.HeldUpdateText(held) : null;

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

    /// <summary>"Changes to 0.8.44, the version that Tools Pack 1.0.0 pins", while that change waits for a confirmation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingUpdate))]
    [NotifyPropertyChangedFor(nameof(ConfirmUpdateText))]
    private string? _packVersionText;

    public bool IsConfirmingUpdate => InstallWarning is not null || HasChangelogs || Choices is not null || PackVersionText is not null;

    public string ConfirmUpdateText => PackVersionText is not null ? _owner.Localization.ContentChangeVersion
        : InstallWarning is null ? _owner.Localization.ContentUpdate : _owner.Localization.UpdateAnyway;

    public InstallPlan? PendingPlan { get; set; }

    public ContentItem(MainViewModel owner, Instance instance, InstalledMod mod, bool enabled, ModMetadata? listing, DiscoverItem? page = null, ListingImage? icon = null, DateTimeOffset? unavailableSince = null)
    {
        _owner = owner;
        _unavailableSince = unavailableSince;
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
        IsPinned = mod.IsPinned;
        CanDetach = instance.Source is InstanceSource.FromModPack && mod.Reason == InstallReason.ModPack;
        IsDetached = instance.Source is InstanceSource.FromModPack pack && pack.Detached.Contains(mod.ModId);
        _isEnabled = enabled;
    }

    [RelayCommand]
    private Task ToggleEnabledAsync() => _owner.SetContentEnabledAsync(InstanceId, ModId, Name, IsEnabled);

    [RelayCommand]
    private Task PinAsync() => _owner.SetContentPinnedAsync(this, pinned: true);

    [RelayCommand]
    private Task UnpinAsync() => _owner.SetContentPinnedAsync(this, pinned: false);

    [RelayCommand]
    private Task DetachAsync() => _owner.DetachContentFromPackAsync(this);

    [RelayCommand]
    private Task AttachAsync() => _owner.AttachContentToPackAsync(this);

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
        OnPropertyChanged(nameof(GoneText));
        OnPropertyChanged(nameof(NoPageText));
        OnPropertyChanged(nameof(RemoveBlockedText));
        OnPropertyChanged(nameof(RemoveConfirmText));
        OnPropertyChanged(nameof(RemoveActionText));
        OnPropertyChanged(nameof(RemoveSteps));
        OnPropertyChanged(nameof(UpdateText));
        OnPropertyChanged(nameof(HeldText));
        OnPropertyChanged(nameof(PinnedText));
        OnPropertyChanged(nameof(ManageConfirmText));
        OnPropertyChanged(nameof(ManageMissingText));
        OnPropertyChanged(nameof(ManageInstallMissingText));
        OnPropertyChanged(nameof(ManageNotInstalledText));
    }

    [RelayCommand]
    private void BeginRemove()
    {
        CancelUpdate();
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
    private Task BeginManageAsync() => _owner.BeginManageContentAsync(this);

    [RelayCommand]
    private void CancelManage()
    {
        IsConfirmingManage = false;
        ManageDependencies = null;
    }

    [RelayCommand]
    private Task ConfirmManageAsync() => _owner.TakeOwnershipOfContentAsync(this);

    [RelayCommand]
    private Task UpdateAsync() => _owner.UpdateContentAsync(this);

    [RelayCommand]
    private Task ConfirmUpdateAsync() => IsMissing
        ? _owner.ConfirmMissingInstallAsync(this, InstanceId, [ModId])
        : _owner.ConfirmUpdateAsync(this, InstanceId);

    [RelayCommand]
    private void CancelUpdate()
    {
        PackVersionText = null;
        MainViewModel.CancelUpdate(this);
    }
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
