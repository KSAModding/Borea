using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// Rates each recorded mod loader against the installed game and offers a newer
/// release on Home, before Play, on the instance page and in the settings. Borea
/// never updates a loader on its own, because one loader starts every instance.
/// </summary>
public partial class MainViewModel
{
    private Task _loaderUpdateCheck = Task.CompletedTask;

    private int _loaderUpdateCheckGeneration;

    private IReadOnlyDictionary<string, LoaderUpdate> _loaderUpdates = new Dictionary<string, LoaderUpdate>(ModIds.Comparer);

    private string _loaderUpdateName = string.Empty;

    private string? _dismissedLoaderUpdate;

    /// <summary>The update the Home banner shows: the first urgent one by loader id, else the first offer. Null shows no banner.</summary>
    [ObservableProperty]
    private LoaderUpdate? _loaderUpdate;

    public bool ShowUrgentLoaderUpdateBanner => LoaderUpdate is { IsUrgent: true };

    public bool ShowLoaderUpdateBanner => LoaderUpdate is { IsUrgent: false, InstalledVersion: not null, Offer: not null } update
        && !string.Equals(DismissKey(update), _dismissedLoaderUpdate ?? _appPreferences.DismissedLoaderUpdate, StringComparison.Ordinal);

    public string? LoaderUpdateBannerText => LoaderUpdate is not { InstalledVersion: { } installed, Offer: { } offer } update
        ? null
        : update.IsUrgent
            ? Localization.FormatLoaderUpdateUrgent(_loaderUpdateName, installed.ToString(), InstalledVersionText ?? string.Empty, offer.Version.ToString())
            : Localization.FormatLoaderUpdateAvailable(_loaderUpdateName, offer.Version.ToString());

    public string? LoaderUpdateActionText => LoaderUpdate?.Offer is { } release
        ? Localization.FormatLoaderUpdateAction(_loaderUpdateName, release.Version.ToString())
        : null;

    partial void OnLoaderUpdateChanged(LoaderUpdate? value) => RefreshLoaderUpdateText();

    private void RefreshLoaderUpdateText()
    {
        OnPropertyChanged(nameof(ShowUrgentLoaderUpdateBanner));
        OnPropertyChanged(nameof(ShowLoaderUpdateBanner));
        OnPropertyChanged(nameof(LoaderUpdateBannerText));
        OnPropertyChanged(nameof(LoaderUpdateActionText));
    }

    private static string DismissKey(LoaderUpdate update) => $"{update.LoaderId} {update.Offer?.Version}";

    /// <summary>What a launch skips the update prompt for after "Play anyway", until the installed or the offered release changes.</summary>
    private static string PlayAnywayKey(LoaderUpdate update) => $"{update.LoaderId} {update.InstalledVersion} {update.Offer?.Version}";

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

        var found = new Dictionary<string, LoaderUpdate>(ModIds.Comparer);
        LoaderUpdate? shown = null;
        var name = string.Empty;
        try
        {
            foreach (var (loaderId, installation) in services.Settings.LoaderInstallations.OrderBy(entry => entry.Key, ModIds.Comparer))
                found[loaderId] = await FindLoaderUpdateAsync(services, loaderId, installation);

            shown = found.Values.FirstOrDefault(update => update.IsUrgent)
                ?? found.Values.FirstOrDefault(update => update is { InstalledVersion: not null, Offer: not null });
            if (shown is not null)
                name = (await services.Mods.GetListingAsync(shown.LoaderId))?.Name ?? shown.LoaderId;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // without the index there is nothing to rate or to offer
        }

        if (generation != _loaderUpdateCheckGeneration || !ReferenceEquals(services, _services))
            return;

        _loaderUpdates = found;
        _loaderUpdateName = name;
        LoaderUpdate = shown;
        RefreshLoaderUpdateText();
        RefreshInstanceLoader();
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

    [RelayCommand]
    private Task UpdateLoaderAsync() => LoaderUpdate is { Offer: { } offer } update
        ? UpdateLoaderToAsync(update.LoaderId, offer)
        : Task.CompletedTask;

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
    /// Installs <paramref name="release"/> over the recorded loader, as the usual
    /// loader install task. False when it did not install.
    /// </summary>
    internal async Task<bool> UpdateLoaderToAsync(string loaderId, ModVersionMetadata release)
    {
        if (_services is not { } services || IsSetupBusy)
            return false;

        // the loader folder is in use while the game runs, so the replace would fail halfway
        if (services.IsGameProcessRunning() || Instances.Any(instance => services.Launcher.IsRunning(instance.InstanceId)))
        {
            SetupError = Localization.LoaderUpdateGameRunning;
            ShowErrorToast(() => Localization.LoaderUpdateGameRunning);
            return false;
        }

        await RunSetupAsync(current => InstallLoaderReleaseAsync(current, loaderId, release, directory: string.Empty));
        return SetupError is null
            && _services?.Settings.LoaderInstallations.FirstOrDefault(entry => ModIds.Equals(entry.Key, loaderId)).Value?.Version == release.Version;
    }

    /// <summary>Drops the answer for a loader that was just installed, until the next check rates the new release.</summary>
    private void ForgetLoaderUpdate(string loaderId)
    {
        _loaderUpdates = _loaderUpdates.Where(entry => !ModIds.Equals(entry.Key, loaderId)).ToDictionary(entry => entry.Key, entry => entry.Value, ModIds.Comparer);
        if (LoaderUpdate is { } shown && ModIds.Equals(shown.LoaderId, loaderId))
            LoaderUpdate = null;
    }
}
