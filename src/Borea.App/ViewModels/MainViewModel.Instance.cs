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

    [ObservableProperty]
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
    /// What the launcher said the last time Play was pressed, success or not.
    /// </summary>
    [ObservableProperty]
    private string? _launchMessage;

    [ObservableProperty]
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

    public string? DisableBlamedModText => LaunchBlamedModId is null ? null : Localization.FormatLaunchDisableMod(BlamedModName(LaunchBlamedModId));

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
        LaunchMessage = null;
        ContentError = null;
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
            // a release from SpaceDock carries no listing, so the name comes from the catalog
            var listing = mod.Metadata.Listing is null ? await ResolveListingAsync(mod.ModId) : null;
            var page = mod.Ownership == ModInstallOwnership.Borea ? _listings.FirstOrDefault(entry => ModIds.Equals(entry.ModId, mod.ModId)) : null;
            content.Add(new ContentItem(this, item.InstanceId, mod, enabled.Contains(mod.ModId), listing, page));
        }

        _content = content.OrderBy(content => content.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        RefreshContentGroups();
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

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (_services is null || _selectedInstanceEntity is null || IsLaunching)
            return;

        IsLaunching = true;
        ClearLaunchFailure();
        try
        {
            var instance = _selectedInstanceEntity;
            var loader = await FindInstalledLoaderAsync();
            var result = _services.Launcher.Launch(instance, loader);
            if (result.Started && loader is not null)
            {
                // the loader can still stop while it loads the mods, so the start is watched before it counts
                LaunchMessage = Localization.FormatLaunchStarting(loader.Name);
                result = await _services.Launcher.WatchStartAsync(instance, result);
            }

            if (result.Outcome == LaunchOutcome.ExitedEarly)
                ShowLaunchFailure(result, instance, loader);
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

    private void ShowLaunchFailure(LaunchResult result, Instance instance, ModMetadata? loader)
    {
        var loaderName = loader?.Name ?? string.Empty;
        var blamed = result.BlamedModId is null ? null : instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, result.BlamedModId));
        LaunchMessage = blamed is null
            ? Localization.FormatLaunchExitedEarly(loaderName, result.ExitCode ?? 0)
            : Localization.FormatLaunchModBroke(BlamedModName(blamed.ModId), blamed.Version.ToString(), loaderName);
        LaunchOutputText = result.Output.Count == 0 ? Localization.LaunchNoOutput : string.Join(Environment.NewLine, result.Output);
        LaunchBlamedModId = blamed?.ModId;
        OnPropertyChanged(nameof(DisableBlamedModText));
    }

    private void ClearLaunchFailure()
    {
        LaunchOutputText = null;
        IsLaunchOutputShown = false;
        LaunchBlamedModId = null;
        OnPropertyChanged(nameof(DisableBlamedModText));
    }

    private string BlamedModName(string modId) =>
        _content.FirstOrDefault(item => ModIds.Equals(item.ModId, modId))?.Name ?? modId;

    [RelayCommand]
    private void ToggleLaunchOutput() => IsLaunchOutputShown = !IsLaunchOutputShown;

    [RelayCommand]
    private async Task DisableBlamedModAsync()
    {
        if (SelectedInstance is not { } instance || LaunchBlamedModId is not { } modId)
            return;

        var name = BlamedModName(modId);
        await SetContentEnabledAsync(instance.InstanceId, modId, enabled: false);
        if (ContentError is not null)
            return;

        await OpenInstanceAsync(instance);
        ClearLaunchFailure();
        LaunchMessage = Localization.FormatLaunchModDisabled(name);
    }

    [RelayCommand]
    private void OpenLaunchLog()
    {
        if (_services is not null && SelectedInstance is { } instance)
            ContentError = TryOpenWithSystem(_services.Paths.GetInstanceLaunchLogPath(instance.InstanceId));
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
        if (_services is null)
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

public sealed record ContentGroup(string Title, IReadOnlyList<ContentItem> Items);

/// <summary>
/// One row of the instance's content table.
/// </summary>
public sealed partial class ContentItem : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly Guid _instanceId;

    public string ModId { get; }

    public string Name { get; }

    public string? Authors { get; }

    public string Version { get; }

    public ContentType Type { get; }

    public bool IsDependency { get; }

    public string? AuthorsText => Authors is null ? null : _owner.Localization.FormatContentByAuthor(Authors);

    private readonly DiscoverItem? _page;

    private readonly bool _ownedByBorea;

    /// <summary>Whether the row links to the mod page: installed by Borea and in the content index.</summary>
    public bool CanOpen => _page is not null;

    /// <summary>Why the row has no link, for its tooltip. Null when it links.</summary>
    public string? NoPageText => CanOpen
        ? null
        : _ownedByBorea ? _owner.Localization.InstanceContentNotInIndex : _owner.Localization.InstanceContentNotOwned;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isConfirmingRemove;

    public ContentItem(MainViewModel owner, Guid instanceId, InstalledMod mod, bool enabled, ModMetadata? listing, DiscoverItem? page = null)
    {
        _owner = owner;
        _instanceId = instanceId;
        _page = page;
        _ownedByBorea = mod.Ownership == ModInstallOwnership.Borea;
        ModId = mod.ModId;
        Name = mod.Metadata.Listing?.Name ?? listing?.Name ?? mod.ModId;
        var authors = mod.Metadata.Listing?.Authors ?? listing?.Authors;
        Authors = authors is { Count: > 0 } ? string.Join(", ", authors) : null;
        Version = mod.Version.ToString();
        Type = mod.Metadata.Type;
        IsDependency = mod.Reason == InstallReason.Dependency;
        _isEnabled = enabled;
    }

    [RelayCommand]
    private Task ToggleEnabledAsync() => _owner.SetContentEnabledAsync(_instanceId, ModId, IsEnabled);

    [RelayCommand]
    private Task OpenAsync() => _page is null ? Task.CompletedTask : _owner.OpenContentFromInstanceAsync(_page);

    internal void RefreshText()
    {
        OnPropertyChanged(nameof(AuthorsText));
        OnPropertyChanged(nameof(NoPageText));
    }

    [RelayCommand]
    private void BeginRemove() => IsConfirmingRemove = true;

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    [RelayCommand]
    private Task ConfirmRemoveAsync() => _owner.RemoveContentAsync(_instanceId, ModId);
}
