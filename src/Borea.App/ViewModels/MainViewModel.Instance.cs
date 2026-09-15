using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Launch;
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
    private readonly Dictionary<Guid, IInstallRow> _runningUpdates = [];
    private Task _contentUpdateCheck = Task.CompletedTask;
    private int _contentUpdateCheckGeneration;
    private Guid? _launchInstanceId;
    private string? _launchBlamedModName;
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

    /// <summary>The mod the loader's error names, offered to disable. Null when none was named.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDisableBlamedMod))]
    private string? _launchBlamedModId;

    public bool CanDisableBlamedMod => LaunchBlamedModId is not null;

    public string? DisableBlamedModText => _launchBlamedModName is null ? null : Localization.FormatLaunchDisableMod(_launchBlamedModName);

    [ObservableProperty]
    private string? _contentError;

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

        ContentError = null;
        _runningUpdates.TryGetValue(item.InstanceId, out var running);
        UpdateAll = running as UpdateAllItem ?? new UpdateAllItem(this, item.InstanceId);
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
            var page = mod.Ownership == ModInstallOwnership.Borea ? indexed : null;
            content.Add(new ContentItem(this, _selectedInstanceEntity!, mod, enabled.Contains(mod.ModId), listing, page, indexed?.Icon));
        }

        _content = content.OrderBy(content => content.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        RefreshContentGroups();
        OnPropertyChanged(nameof(HasUpdates));
        if (IsManualInstallsTab)
            await LoadManualInstallsAsync();
        else if (IsGameDataTab)
            await LoadGameDataAsync();
        else if (IsLogTab)
            await LoadGameLogAsync();

        CurrentWindowHome = false;
        CurrentWindowDiscover = false;
        CurrentWindowLibrary = false;
        CurrentWindowTasks = false;
        CurrentWindowContent = false;
        CurrentWindowPack = false;
        CurrentWindowInstance = true;

        StartContentUpdateCheck();
    }

    [RelayCommand]
    private void ShowInstanceContent() => InstanceTab = InstanceTab.Content;

    /// <summary>
    /// Groups follow the design: content the user chose, then what came along
    /// as a dependency. Titles are translated, so a language change rebuilds them.
    /// </summary>
    private void RefreshContentGroups()
    {
        foreach (var item in _content)
            item.RefreshText();

        ContentGroups.Clear();
        Add(Localization.InstanceGroupMods, _content.Where(content => content.Type == ContentType.Mod && !content.IsDependency));
        Add(Localization.InstanceGroupModLoaders, _content.Where(content => content.Type == ContentType.ModLoader && !content.IsDependency));
        Add(Localization.InstanceGroupOther, _content.Where(content => content.Type is not ContentType.Mod and not ContentType.ModLoader && !content.IsDependency));
        Add(Localization.InstanceGroupDependencies, _content.Where(content => content.IsDependency));
        OnPropertyChanged(nameof(HasContent));

        void Add(string title, IEnumerable<ContentItem> items)
        {
            var list = items.ToList();
            if (list.Count > 0)
                ContentGroups.Add(new ContentGroup(title, list));
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
            foreach (var item in content.Where(item => item.IsOwned))
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

        var count = 0;
        foreach (var installed in active?.Mods.Where(mod => mod.Ownership == ModInstallOwnership.Borea) ?? [])
        {
            var newer = await FindAsync(active!, installed);
            if (generation != _contentUpdateCheckGeneration)
                return;

            if (newer is not null)
                count++;
        }

        ActiveInstanceUpdateCount = count;
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
        => RunUpdateAsync(row, instanceId, () =>
        {
            row.Changelogs = [];
            return ExecutePendingPlanAsync(row);
        });

    internal static void CancelUpdate(IUpdateRow row)
    {
        CancelInstall(row);
        row.Changelogs = [];
    }

    /// <summary>Waits for a confirmation on planner warnings or passed changelogs.</summary>
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
    /// an error is set again afterwards, on the row of the same mod.
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
        await ReloadInstancesAsync();
        if (error is null || SelectedInstance?.InstanceId != instanceId)
            return;

        IInstallRow? target = row is ContentItem item
            ? _content.FirstOrDefault(content => ModIds.Equals(content.ModId, item.ModId))
            : UpdateAll;
        if (target is not null)
            target.InstallError = error;
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
        return ActiveInstance is { } instance ? LaunchAsync(instance.InstanceId) : Task.CompletedTask;
    }

    [RelayCommand]
    private Task PlayHomeAsync() => IsHomeLaunchActiveInstance ? PlayActiveInstanceAsync() : PlayWithoutModLoader();

    /// <summary>Starts the instance through the loader Borea has recorded and watches the start.</summary>
    private async Task LaunchAsync(Guid instanceId)
    {
        if (_services is not { } services || IsLaunching)
            return;

        IsLaunching = true;
        _launchInstanceId = instanceId;
        ClearLaunchFailure();
        try
        {
            var instance = await services.Instances.GetByIdAsync(instanceId)
                ?? throw new InvalidOperationException(Localization.LaunchInstanceMissing);
            var loader = await FindInstalledLoaderAsync();
            var result = services.Launcher.Launch(instance, loader);
            if (result.Started && loader is not null)
            {
                // the loader can still stop while it loads the mods, so the start is watched before it counts
                LaunchMessage = Localization.FormatLaunchStarting(loader.Name);
                result = await services.Launcher.WatchStartAsync(instance, result);
            }

            if (result.Outcome == LaunchOutcome.ExitedEarly)
                await ShowLaunchFailureAsync(result, instance, loader);
            else
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
        var loaderName = loader?.Name ?? string.Empty;
        var blamed = result.BlamedModId is null ? null : instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, result.BlamedModId));
        if (blamed is null)
        {
            LaunchMessage = Localization.FormatLaunchExitedEarly(loaderName, result.ExitCode ?? 0);
        }
        else
        {
            // the same name the content row shows, also when the instance page was never opened
            _launchBlamedModName = blamed.Metadata.Listing?.Name ?? (await ResolveListingAsync(blamed.ModId))?.Name ?? blamed.ModId;
            LaunchMessage = Localization.FormatLaunchModBroke(_launchBlamedModName, blamed.Version.ToString(), loaderName);
        }

        LaunchOutputText = result.Output.Count == 0 ? Localization.LaunchNoOutput : string.Join(Environment.NewLine, result.Output);
        LaunchBlamedModId = blamed?.ModId;
        OnPropertyChanged(nameof(DisableBlamedModText));
    }

    private void ClearLaunchFailure()
    {
        LaunchOutputText = null;
        IsLaunchOutputShown = false;
        LaunchBlamedModId = null;
        _launchBlamedModName = null;
        OnPropertyChanged(nameof(DisableBlamedModText));
    }

    [RelayCommand]
    private void ToggleLaunchOutput() => IsLaunchOutputShown = !IsLaunchOutputShown;

    [RelayCommand]
    private async Task DisableBlamedModAsync()
    {
        if (_launchInstanceId is not { } instanceId || LaunchBlamedModId is not { } modId || _launchBlamedModName is not { } name)
            return;

        await SetContentEnabledAsync(instanceId, modId, enabled: false);
        if (ContentError is not null)
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
            ContentError = TryOpenWithSystem(_services.Paths.GetInstanceLaunchLogPath(instanceId));
    }

    /// <summary>
    /// The listing of the mod loader the settings point at. The launcher needs
    /// its metadata to know what to run; null lets it explain that no loader is set.
    /// </summary>
    private async Task<ModMetadata?> FindInstalledLoaderAsync()
    {
        if (_services is null)
            return null;

        var loaderId = _services.Settings.LoaderInstallations.Keys.FirstOrDefault();
        if (loaderId is null)
            return null;

        var listings = await _services.Mods.GetAvailableModsAsync();
        return listings.FirstOrDefault(listing => listing.Type == ContentType.ModLoader && ModIds.Equals(listing.ModId, loaderId));
    }

    internal async Task SetContentEnabledAsync(Guid instanceId, string modId, bool enabled)
    {
        if (_services is null)
            return;

        try
        {
            if (enabled)
                await _services.ModState.SetActiveAsync(instanceId, modId);
            else
                await _services.ModState.SetInactiveAsync(instanceId, modId);
            ContentError = null;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ContentError = exception.Message;
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
    /// not install, because its files are not Borea's to delete. The error is
    /// set after the page reloads, because opening the instance clears it.
    /// </summary>
    internal async Task RemoveContentAsync(Guid instanceId, string modId)
    {
        if (_services is null || _runningUpdates.ContainsKey(instanceId))
            return;

        string? error;
        try
        {
            error = await TryRemoveContentAsync(_services, instanceId, modId);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = exception.Message;
        }

        await ReloadInstancesAsync();
        ContentError = error;
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

public sealed record ContentGroup(string Title, IReadOnlyList<ContentItem> Items);

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

    public bool IsDependency { get; }

    /// <summary>Borea installed the files, so it may update them.</summary>
    public bool IsOwned { get; }

    public string? AuthorsText => Authors is null ? null : _owner.Localization.FormatContentByAuthor(Authors);

    private readonly DiscoverItem? _page;

    private readonly Instance _instance;

    private readonly InstalledMod _mod;

    /// <summary>Why the remove button is disabled, for its tooltip. Null when the mod can be removed.</summary>
    public string? RemoveBlockedText => _owner.RemoveBlockedReason(_instance, _mod);

    public bool CanRemove => RemoveBlockedText is null;

    public string RemoveToolTip => RemoveBlockedText ?? _owner.Localization.ContentRemove;

    /// <summary>Whether the row links to the mod page: installed by Borea and in the content index.</summary>
    public bool CanOpen => _page is not null;

    /// <summary>The icon of the index listing with the mod's id, also when the row does not link to it.</summary>
    public ListingImage? Icon { get; }

    /// <summary>Why the row has no link, for its tooltip. Null when it links.</summary>
    public string? NoPageText => CanOpen
        ? null
        : IsOwned ? _owner.Localization.InstanceContentNotInIndex : _owner.Localization.InstanceContentNotOwned;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isConfirmingRemove;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    private string? _updateVersion;

    /// <summary>A dependency shows no update of its own, "Update all" updates it.</summary>
    public bool HasUpdate => UpdateVersion is not null && !IsInstalling && !IsDependency;

    public string? UpdateText => UpdateVersion is null ? null : _owner.Localization.FormatContentUpdateTo(UpdateVersion);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _progressStatus;

    [ObservableProperty]
    private string? _progressDetail;

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

    public bool HasChangelogs => Changelogs.Count > 0;

    public bool IsConfirmingUpdate => InstallWarning is not null || HasChangelogs;

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
        IsDependency = mod.Reason == InstallReason.Dependency;
        IsOwned = mod.Ownership == ModInstallOwnership.Borea;
        _isEnabled = enabled;
    }

    [RelayCommand]
    private Task ToggleEnabledAsync() => _owner.SetContentEnabledAsync(InstanceId, ModId, IsEnabled);

    [RelayCommand]
    private Task OpenAsync() => _page is null ? Task.CompletedTask : _owner.OpenContentFromInstanceAsync(_page);

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(AuthorsText));
        OnPropertyChanged(nameof(NoPageText));
        OnPropertyChanged(nameof(RemoveBlockedText));
        OnPropertyChanged(nameof(RemoveToolTip));
        OnPropertyChanged(nameof(UpdateText));
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
    private Task ConfirmRemoveAsync() => _owner.RemoveContentAsync(InstanceId, ModId);

    [RelayCommand]
    private Task UpdateAsync() => _owner.UpdateContentAsync(this);

    [RelayCommand]
    private Task ConfirmUpdateAsync() => _owner.ConfirmUpdateAsync(this, InstanceId);

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
    private string? _installError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingUpdate))]
    [NotifyPropertyChangedFor(nameof(ConfirmUpdateText))]
    private string? _installWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingUpdate))]
    [NotifyPropertyChangedFor(nameof(HasChangelogs))]
    private IReadOnlyList<ReleaseChangelog> _changelogs = [];

    public bool HasChangelogs => Changelogs.Count > 0;

    public bool IsConfirmingUpdate => InstallWarning is not null || HasChangelogs;

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
