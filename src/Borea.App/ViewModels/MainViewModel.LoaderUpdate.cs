using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// Rates the loader version that starts the active instance against its game and offers a newer
/// release on Home, before Play, on the instance page and in the settings. Each recorded loader is
/// rated too, for the instance page and the settings. Borea never updates a loader on its own, and
/// an update goes beside the installed versions, so it changes no other instance.
/// </summary>
public partial class MainViewModel
{
    private Task _loaderUpdateCheck = Task.CompletedTask;

    private int _loaderUpdateCheckGeneration;

    private IReadOnlyDictionary<string, LoaderUpdate> _loaderUpdates = new Dictionary<string, LoaderUpdate>(ModIds.Comparer);

    private string _loaderUpdateName = string.Empty;

    private string? _dismissedLoaderUpdate;

    /// <summary>The pinned version of the active instance that is missing or does not support the game, or null.</summary>
    private PinnedLoaderProblem? _pinnedLoaderProblem;

    /// <summary>The active instance and its loader setting when the last check started, so a change rates again.</summary>
    private (Guid? InstanceId, LoaderSetting? Setting) _loaderUpdateRated;

    /// <summary>
    /// The update the Home banner shows for an active instance that chooses its version automatically: the version
    /// that starts it when it does not support the game, else an offer that its mods accept. Null shows no such banner.
    /// </summary>
    [ObservableProperty]
    private LoaderUpdate? _loaderUpdate;

    /// <summary>The banners hide while a setup task runs, because the task already shows its progress.</summary>
    public bool ShowUrgentLoaderUpdateBanner => !IsSetupBusy && (_pinnedLoaderProblem is not null || IsBroken(LoaderUpdate));

    public bool ShowLoaderUpdateBanner => !IsSetupBusy
        && _pinnedLoaderProblem is null
        && !IsBroken(LoaderUpdate)
        && LoaderUpdate is { InstalledVersion: not null, Offer: not null } update
        && !string.Equals(DismissKey(update), _dismissedLoaderUpdate ?? _appPreferences.DismissedLoaderUpdate, StringComparison.Ordinal);

    public string? LoaderUpdateBannerText => (_pinnedLoaderProblem, LoaderUpdate) switch
    {
        ({ IsIncompatible: true } pin, _) => Localization.FormatLoaderPinnedIncompatible(_loaderUpdateName, pin.Version.ToString(), pin.InstanceName, InstalledVersionText ?? string.Empty),
        ({ } pin, _) => Localization.FormatLoaderPinnedMissing(_loaderUpdateName, pin.Version.ToString(), pin.InstanceName),
        (null, { } update) when IsBroken(update) => LoaderIncompatibleText(_loaderUpdateName, update),
        (null, { InstalledVersion: not null, Offer: { } offer }) => Localization.FormatLoaderUpdateAvailable(_loaderUpdateName, offer.Version.ToString()),
        _ => null,
    };

    public string? LoaderUpdateActionText => _pinnedLoaderProblem is { } pinned
        ? pinned.Install is { } install
            ? Localization.FormatLoaderInstallAction(_loaderUpdateName, install.Version.ToString())
            : Localization.LoaderChangeAction
        : LoaderUpdate?.Offer is { } release
            ? Localization.FormatLoaderUpdateAction(_loaderUpdateName, release.Version.ToString())
            : null;

    partial void OnLoaderUpdateChanged(LoaderUpdate? value) => RefreshLoaderUpdateText();

    partial void OnIsSetupBusyChanged(bool value) => RefreshLoaderUpdateText();

    /// <summary>True when the index says the installed release does not support the game, with or without a release to update to.</summary>
    private static bool IsBroken(LoaderUpdate? update) =>
        update is { InstalledCompatibility: GameCompatibility.Incompatible, InstalledVersion: not null };

    /// <summary>
    /// Why the installed release does not fit the game, and the next step: the
    /// release to update to, a KSA update, or no step yet.
    /// </summary>
    internal string LoaderIncompatibleText(string loaderName, LoaderUpdate update)
    {
        var installed = update.InstalledVersion?.ToString() ?? string.Empty;
        var game = InstalledVersionText ?? string.Empty;
        return update.Offer is { } offer
            ? Localization.FormatLoaderUpdateUrgent(loaderName, installed, game, offer.Version.ToString())
            : update.InstalledNeedsNewerGame
                ? Localization.FormatLoaderNeedsNewerGame(loaderName, installed, game)
                : Localization.FormatLoaderUpdateNone(loaderName, installed, game);
    }

    /// <summary>True when the mods of <paramref name="instance"/> accept <paramref name="version"/> of the loader, or do not ask for that loader.</summary>
    private static bool AcceptsLoader(Instance instance, string loaderId, ModVersion version) =>
        LoaderNeed.For(instance).Loaders.FirstOrDefault(loader => ModIds.Equals(loader.LoaderId, loaderId)) is not { } needed
        || needed.HasConflict
        || needed.Accepts(version);

    /// <summary>True while the game or an instance runs, when the loader folder is in use and a replace would fail halfway.</summary>
    private bool IsGameRunning(BoreaServices services) =>
        services.IsGameProcessRunning() || Instances.Any(instance => services.Launcher.IsRunning(instance.InstanceId));

    private void RefreshLoaderUpdateText()
    {
        OnPropertyChanged(nameof(ShowUrgentLoaderUpdateBanner));
        OnPropertyChanged(nameof(ShowLoaderUpdateBanner));
        OnPropertyChanged(nameof(LoaderUpdateBannerText));
        OnPropertyChanged(nameof(LoaderUpdateActionText));
    }

    private static string DismissKey(LoaderUpdate update) => $"{update.LoaderId} {update.Offer?.Version}";

    /// <summary>What a launch of one instance skips the prompt for after "Play anyway", until the installed or the offered release changes.</summary>
    private static string PlayAnywayKey(Guid instanceId, LoaderUpdate update) => $"{instanceId} {update.LoaderId} {update.InstalledVersion} {update.Offer?.Version}";

    /// <summary>The last check's answer for one loader, or null before a check answered.</summary>
    internal LoaderUpdate? LoaderUpdateFor(string loaderId) => _loaderUpdates.GetValueOrDefault(loaderId);

    /// <summary>Runs in the background after the index or the game changed. A later check stops the earlier one.</summary>
    private void StartLoaderUpdateCheck()
    {
        var previous = _loaderUpdateCheck;
        var check = RefreshLoaderUpdatesAsync(++_loaderUpdateCheckGeneration);
        _loaderUpdateCheck = previous.IsCompleted ? check : Task.WhenAll(previous, check);
    }

    /// <summary>Completes when the loader update checks have finished.</summary>
    internal Task WhenLoaderUpdatesCheckedAsync() => _loaderUpdateCheck;

    private async Task RefreshLoaderUpdatesAsync(int generation)
    {
        if (_services is not { } services)
            return;

        // also a check that fails counts, so a reload without the index does not start the next one
        _loaderUpdateRated = (_activeInstanceEntity?.InstanceId, _activeInstanceEntity?.Loader);
        var found = new Dictionary<string, LoaderUpdate>(ModIds.Comparer);
        LoaderUpdate? shown = null;
        PinnedLoaderProblem? pinned = null;
        var name = string.Empty;
        try
        {
            foreach (var (loaderId, installation) in services.Settings.LoaderInstallations.OrderBy(entry => entry.Key, ModIds.Comparer))
                found[loaderId] = await FindLoaderUpdateAsync(services, loaderId, installation);

            // Home's Play starts the active instance, so the banners rate it. Without one they rate an instance
            // with no mods and the Automatic setting, which every new instance starts with.
            var active = await services.Instances.GetActiveInstanceIdAsync() is { } activeId ? await services.Instances.GetByIdAsync(activeId) : null;
            var instance = active ?? new Instance(nameof(Instance), InstanceSource.Custom.Value);
            var resolution = await ResolveLaunchLoaderAsync(services, instance);
            if (resolution.Version is { IsPinned: true } pin)
                pinned = PinnedLoaderProblemOf(instance, pin, resolution.Start);
            else if (resolution.Start is { } start)
                shown = await RateLoaderStartAsync(services, instance, start, found);

            if ((pinned?.LoaderId ?? shown?.LoaderId) is { } shownId)
                name = (await services.Mods.GetListingAsync(shownId))?.Name ?? shownId;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // without the index there is nothing to rate or to offer
        }

        if (generation != _loaderUpdateCheckGeneration || !ReferenceEquals(services, _services))
            return;

        _loaderUpdates = found;
        _loaderUpdateName = name;
        _pinnedLoaderProblem = pinned;
        LoaderUpdate = shown;
        RefreshLoaderUpdateText();
        RefreshInstanceLoader();
    }

    /// <summary>The banners follow the active instance, so another active instance or a new loader setting rates again.</summary>
    partial void OnActiveInstanceChanged(InstanceItem? value)
    {
        // the first check waits until the start has refreshed the index
        if (_loaderUpdateCheckGeneration > 0 && _loaderUpdateRated != (_activeInstanceEntity?.InstanceId, _activeInstanceEntity?.Loader))
            StartLoaderUpdateCheck();
    }

    /// <summary>The Error banner for a pinned version that is missing or that the index rates incompatible with the game, else null.</summary>
    private static PinnedLoaderProblem? PinnedLoaderProblemOf(Instance instance, LoaderVersionResolution version, InstalledLoaderVersion? start)
    {
        if (instance.Loader.Version is not { } pin)
            return null;

        if (start is null)
            return new PinnedLoaderProblem(instance.InstanceId, instance.Name, version.LoaderId, pin, version.Missing, IsIncompatible: false);

        return version.StartCompatibility == GameCompatibility.Incompatible
            ? new PinnedLoaderProblem(instance.InstanceId, instance.Name, version.LoaderId, pin, Install: null, IsIncompatible: true)
            : null;
    }

    /// <summary>
    /// The update for the version that starts an Automatic instance, rated the way the update check rates a recorded
    /// loader, with an offer that the mods of the instance accept. Null when there is nothing to show.
    /// </summary>
    private static async Task<LoaderUpdate?> RateLoaderStartAsync(BoreaServices services, Instance instance, InstalledLoaderVersion start, IReadOnlyDictionary<string, LoaderUpdate> recorded)
    {
        var loaderId = start.LoaderId;

        // the check of the recorded folder reads a version that the settings do not know from its files
        var version = start.Version ?? (start.IsRecorded ? recorded.GetValueOrDefault(loaderId)?.InstalledVersion : null);
        var need = LoaderNeed.For(instance).Loaders.FirstOrDefault(loader => ModIds.Equals(loader.LoaderId, loaderId));
        var update = await LoaderUpdate.FindAsync(
            services.Mods,
            services.Settings.ReleaseChannel,
            loaderId,
            new LoaderInstallation(start.DirectoryPath, version, start.RawVersion, start.IsAdopted),
            LoaderGameOf(services, instance).Version,
            need: need);
        return IsBroken(update) || update is { InstalledVersion: not null, Offer: not null } ? update : null;
    }

    /// <summary>
    /// The update for one loader. A recorded loader whose version is unknown is
    /// read from its files first, and the settings stay as they are.
    /// </summary>
    private static async Task<LoaderUpdate> FindLoaderUpdateAsync(BoreaServices services, string loaderId, LoaderInstallation? installation)
    {
        if (installation is { Version: null } unknown && await InspectLoaderAsync(services, loaderId, unknown.DirectoryPath) is { Version: { } read })
            installation = new LoaderInstallation(unknown.DirectoryPath, read, unknown.RawVersion, unknown.IsAdopted);

        return await LoaderUpdate.FindAsync(services.Mods, services.Settings.ReleaseChannel, loaderId, installation, services.InstalledVersion.GetInstalledVersion()?.Version);
    }

    /// <summary>The release that the files of a loader folder match, read the way <c>borea loader adopt</c> reads it. Null when the folder does not hold the loader.</summary>
    private static async Task<LoaderAdoptionResult?> InspectLoaderAsync(BoreaServices services, string loaderId, string directory)
    {
        try
        {
            if (await services.Mods.GetListingAsync(loaderId) is not { } listing)
                return null;

            // the history keeps a release whose download is gone, so an installed copy of it still gets its version
            var releases = await services.Mods.GetReleaseHistoryAsync(loaderId);
            return await services.LoaderAdopter.InspectAsync(listing, releases, directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>The action of the banner: install a missing pin, change a pin that does not fit, or update the version that starts.</summary>
    [RelayCommand]
    private Task UpdateLoaderAsync()
    {
        if (_pinnedLoaderProblem is { } pinned)
        {
            return pinned.Install is { } install
                ? UpdateLoaderToAsync(pinned.LoaderId, install)
                : ChangeLoaderOfAsync(pinned.InstanceId);
        }

        return LoaderUpdate is { Offer: { } offer } update
            ? UpdateLoaderToAsync(update.LoaderId, offer, update.InstalledVersion)
            : Task.CompletedTask;
    }

    [RelayCommand]
    private void DismissLoaderUpdateBanner()
    {
        if (LoaderUpdate is not { Offer: not null } update)
            return;

        var key = _dismissedLoaderUpdate = DismissKey(update);
        OnPropertyChanged(nameof(ShowLoaderUpdateBanner));
        QueuePreferenceSave(preferences => preferences.WithDismissedLoaderUpdate(key));
    }

    /// <summary>
    /// Installs <paramref name="release"/> beside the installed versions of the loader,
    /// as the usual loader install task. False when it did not install.
    /// </summary>
    /// <param name="replaced">
    /// The version that the update replaces, or null for an install. Borea removes it after the install when Borea
    /// created it and no instance starts it any more, so a player who only updates keeps one folder, as before.
    /// </param>
    internal async Task<bool> UpdateLoaderToAsync(string loaderId, ModVersionMetadata release, ModVersion? replaced = null)
    {
        if (_services is not { } services || IsSetupBusy)
            return false;

        if (IsGameRunning(services))
        {
            SetupError = Localization.LoaderUpdateGameRunning;

            // an open modal shows the error itself
            if (!IsLoaderPromptOpen && !IsLoaderSettingModalOpen)
                ShowErrorToast(() => Localization.LoaderUpdateGameRunning);
            return false;
        }

        await RunSetupAsync(async current =>
        {
            var message = await InstallLoaderReleaseAsync(current, loaderId, release);

            // the removal runs inside the task, so the services that the task builds again already see it
            if (replaced is { } old && old != release.Version)
                await RemoveReplacedLoaderVersionAsync(current, loaderId, old, release.Version);
            return message;
        });

        // the version goes beside the others, and the record moves to it only by the record rule, so the store says whether it is there
        return SetupError is null
            && _services is { } installed
            && InstalledLoaderVersion.Find(await installed.LoaderVersions.ListAsync(), loaderId, release.Version) is not null;
    }

    /// <summary>
    /// Removes the version that an update replaced, when the new version is in place, Borea created the old folder,
    /// no instance starts it, and no game runs. Any failure keeps the folder, and the settings can remove it later.
    /// </summary>
    private async Task RemoveReplacedLoaderVersionAsync(BoreaServices services, string loaderId, ModVersion replaced, ModVersion updated)
    {
        try
        {
            var installed = await services.LoaderVersions.ListAsync();
            if (InstalledLoaderVersion.Find(installed, loaderId, updated) is null
                || InstalledLoaderVersion.Find(installed, loaderId, replaced) is not { IsAdopted: false } old
                || IsGameRunning(services))
            {
                return;
            }

            // the same rule as the launch decides which version each instance starts
            var uses = await LoaderVersionUse.UsedByAsync(
                await services.Instances.GetAllAsync(),
                installed,
                services.OfflineContentIndex,
                services.ContentIndex,
                await HeldLoaderReleasesAsync(services),
                services.Settings.ReleaseChannel,
                instance => LoaderGameOf(services, instance).Version);
            if (uses.Any(use => ReferenceEquals(use.Version, old) && use.IsUsed))
                return;

            var result = await services.LoaderUninstaller.UninstallVersionAsync(loaderId, replaced);
            services.Log.Write(result.DirectoryRemoved
                ? $"Removed {loaderId} {replaced} after the update to {updated}, because no instance starts it."
                : $"Kept {loaderId} {replaced} after the update to {updated}: {result.Failure}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            services.Log.Write($"Kept {loaderId} {replaced} after the update to {updated}, because it could not be removed.", exception);
        }
    }

    /// <summary>
    /// Drops the answer for a loader that was just installed, until the next check
    /// rates the new release. A check that is still running rated the old one, so its answer is dropped too.
    /// </summary>
    private void ForgetLoaderUpdate(string loaderId)
    {
        ++_loaderUpdateCheckGeneration;
        _loaderUpdates = _loaderUpdates.Where(entry => !ModIds.Equals(entry.Key, loaderId)).ToDictionary(entry => entry.Key, entry => entry.Value, ModIds.Comparer);
        if (_pinnedLoaderProblem is { } pinned && ModIds.Equals(pinned.LoaderId, loaderId))
        {
            _pinnedLoaderProblem = null;
            RefreshLoaderUpdateText();
        }

        if (LoaderUpdate is { } shown && ModIds.Equals(shown.LoaderId, loaderId))
            LoaderUpdate = null;
    }

    /// <summary>A pinned version of the active instance that the Home banner names.</summary>
    /// <param name="Install">The release that the banner installs, or null when Borea cannot install the pin or it is installed.</param>
    /// <param name="IsIncompatible">True when the pin is installed and the index rates it incompatible with the game.</param>
    private sealed record PinnedLoaderProblem(Guid InstanceId, string InstanceName, string LoaderId, ModVersion Version, ModVersionMetadata? Install, bool IsIncompatible);
}
