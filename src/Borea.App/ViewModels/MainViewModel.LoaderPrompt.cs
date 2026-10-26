using System;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The modal that installs the loader version a launch needs, or updates the version that starts
/// when it does not support the game, then starts the launch again. When no release fits, or the
/// instance pins that version, it only warns before the launch.
/// </summary>
public partial class MainViewModel
{
    private Guid? _loaderPromptInstanceId;

    /// <summary>The release the modal installs when the version is missing, as the setting and the mods of the instance ask for it.</summary>
    private ModVersionMetadata? _promptedLoaderRelease;

    /// <summary>The kept build of the prompted instance, which the modal names in place of the build of the settings. Null on the settings build.</summary>
    private string? _promptedKeptBuild;

    /// <summary>The update a launch skipped with "Play anyway", which no later launch of that instance asks about again.</summary>
    private string? _playedAnywayLoaderUpdate;

    /// <summary>The listed loader the launch needs. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaderPromptOpen), nameof(LoaderPromptText))]
    private ModMetadata? _promptedLoader;

    /// <summary>
    /// The installed version the modal warns about. With an offer the modal updates it,
    /// without one it only warns. Null when the version is not installed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaderUpdatePrompt), nameof(LoaderPromptText), nameof(LoaderPromptActionText), nameof(ShowLoaderPromptAction), nameof(ShowChangeLoaderPromptAction), nameof(ShowUseAutomaticLoaderAction))]
    private LoaderUpdate? _promptedLoaderUpdate;

    /// <summary>
    /// The version that the instance pins, when the modal warns about it or when Borea cannot install it.
    /// Null for an instance that chooses its version automatically, and for a pin that the modal installs.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoaderPromptText), nameof(ShowLoaderPromptAction), nameof(ShowChangeLoaderPromptAction), nameof(ShowUseAutomaticLoaderAction))]
    private ModVersion? _promptedLoaderPin;

    public bool IsLoaderPromptOpen => PromptedLoader is not null;

    public bool IsLoaderUpdatePrompt => PromptedLoaderUpdate is not null;

    /// <summary>False when the game is not set up, when the modal only warns because no release fits the game and the mods, and for a pin that Borea cannot install.</summary>
    public bool ShowLoaderPromptAction => !NeedsGameSetup && PromptedLoaderUpdate is not { Offer: null } && !ShowUseAutomaticLoaderAction;

    /// <summary>True when the modal warns about a pinned version, which only the player can change.</summary>
    public bool ShowChangeLoaderPromptAction => PromptedLoaderPin is not null && PromptedLoaderUpdate is not null;

    /// <summary>True when the pinned version is not installed and the index no longer offers it.</summary>
    public bool ShowUseAutomaticLoaderAction => PromptedLoaderPin is not null && PromptedLoaderUpdate is null;

    public string? LoaderPromptText => PromptedLoader is not { } loader
        ? null
        : PromptedLoaderUpdate switch
        {
            { InstalledVersion: { } installed, Offer: { } offer } => Localization.FormatLaunchLoaderUpdate(loader.Name, installed.ToString(), _promptedKeptBuild ?? InstalledVersionText ?? string.Empty, offer.Version.ToString()),
            { InstalledVersion: { } installed } => Localization.FormatLaunchLoaderIncompatible(loader.Name, installed.ToString(), _promptedKeptBuild ?? InstalledVersionText ?? string.Empty),
            _ when PromptedLoaderPin is { } pin => Localization.FormatLaunchPinnedLoaderNotInstallable(loader.Name, pin.ToString()),
            _ => NeedsGameSetup
                ? Localization.FormatLaunchSetUpGameFirst(loader.Name)
                : Localization.FormatLaunchInstallLoader(_promptedLoaderRelease is { } release ? $"{loader.Name} {release.Version}" : loader.Name),
        };

    public string LoaderPromptActionText => IsLoaderUpdatePrompt ? Localization.LaunchUpdateAndPlay : Localization.SetupInstallLoader;

    partial void OnPromptedLoaderChanged(ModMetadata? value)
    {
        if (value is not null)
            return;

        PromptedLoaderUpdate = null;
        PromptedLoaderPin = null;
    }

    /// <summary>
    /// What a launch of <paramref name="instance"/> asks about first: a version that starts and that the index rates
    /// incompatible with the game. An Automatic instance gets the newest release that fits the game and its mods, or
    /// only a warning when none does. A pinned instance gets only a warning, because an update cannot change its version.
    /// Null when the launch starts without a question, also after "Play anyway" for the same instance and update.
    /// </summary>
    /// <param name="recorded">The recorded folder rated against the game of the instance, or null.</param>
    private LoaderUpdate? LoaderUpdateBeforeLaunch(Instance instance, ModMetadata loader, LoaderVersionResolution version, InstalledLoaderVersion start, LoaderUpdate? recorded)
    {
        LoaderUpdate update;
        if (version.StartCompatibility == GameCompatibility.Incompatible && start.Version is { } known)
        {
            update = new LoaderUpdate(loader.ModId, known, GameCompatibility.Incompatible, version.IsPinned ? null : version.Offer);
        }
        else if (!version.IsPinned && start.IsRecorded && version.StartCompatibility == GameCompatibility.Unknown
            && recorded is { } rated && IsBroken(rated))
        {
            // Without held releases, or for a version that only its files name, the rating of the recorded folder still
            // knows it. Its offer stays only when the mods of the instance accept it.
            update = rated.Offer is { } offer && !AcceptsLoader(instance, loader.ModId, offer.Version)
                ? rated with { Offer = null }
                : rated;
        }
        else
        {
            return null;
        }

        return string.Equals(PlayAnywayKey(instance.InstanceId, update), _playedAnywayLoaderUpdate, StringComparison.Ordinal) ? null : update;
    }

    private void OpenLoaderPrompt(Instance instance, ModMetadata loader, LoaderUpdate? update = null, ModVersionMetadata? release = null, ModVersion? pin = null)
    {
        _loaderPromptInstanceId = instance.InstanceId;
        _promptedKeptBuild = KeptBuildTextOf(instance);
        _promptedLoaderRelease = release;
        LaunchMessage = null;
        SetupMessage = null;
        SetupError = null;
        PromptedLoaderUpdate = update;
        PromptedLoaderPin = pin;
        PromptedLoader = loader;
    }

    [RelayCommand]
    private async Task InstallPromptedLoaderAsync()
    {
        if (PromptedLoader is not { } loader || _loaderPromptInstanceId is not { } instanceId || IsSetupBusy)
            return;

        if (PromptedLoaderUpdate is { } update)
        {
            if (update.Offer is not { } offer || !await UpdateLoaderToAsync(loader.ModId, offer, update.InstalledVersion))
                return;
        }
        else if (_promptedLoaderRelease is not { } release || !await UpdateLoaderToAsync(loader.ModId, release))
        {
            return;
        }

        PromptedLoader = null;
        await LaunchAsync(instanceId);
    }

    /// <summary>Starts the launch with the installed version, because the bounds in the index can be wrong.</summary>
    [RelayCommand]
    private async Task PlayAnywayAsync()
    {
        if (PromptedLoaderUpdate is not { } update || _loaderPromptInstanceId is not { } instanceId || IsSetupBusy)
            return;

        _playedAnywayLoaderUpdate = PlayAnywayKey(instanceId, update);
        PromptedLoader = null;
        await LaunchAsync(instanceId);
    }

    /// <summary>Lets the instance choose its version again, because Borea cannot install the version it pins, then starts it.</summary>
    [RelayCommand]
    private async Task UseAutomaticLoaderAsync()
    {
        if (PromptedLoaderPin is null || _loaderPromptInstanceId is not { } instanceId || IsSetupBusy)
            return;

        SetupError = await TryInstanceOperationAsync(instances => instances.UpdateAsync(instanceId, instance =>
        {
            instance.SetLoader(LoaderSetting.Automatic);
            return true;
        }));
        if (SetupError is not null)
            return;

        PromptedLoader = null;
        await LaunchAsync(instanceId);
    }

    /// <summary>Opens the modal that changes the loader version of the prompted instance.</summary>
    [RelayCommand]
    private Task ChangeLoaderFromPromptAsync()
    {
        if (_loaderPromptInstanceId is not { } instanceId || IsSetupBusy)
            return Task.CompletedTask;

        PromptedLoader = null;
        return ChangeLoaderOfAsync(instanceId);
    }

    /// <summary>Opens the modal that changes the loader version of one instance, when that instance is still in the list.</summary>
    private Task ChangeLoaderOfAsync(Guid instanceId) =>
        Instances.FirstOrDefault(instance => instance.InstanceId == instanceId) is { } item
            ? BeginEditLoaderSettingAsync(item)
            : Task.CompletedTask;

    [RelayCommand]
    private Task SetUpGameForLoaderAsync()
    {
        PromptedLoader = null;
        return OpenGameSetupAsync();
    }

    [RelayCommand]
    private void CancelLoaderPrompt() => PromptedLoader = null;
}
