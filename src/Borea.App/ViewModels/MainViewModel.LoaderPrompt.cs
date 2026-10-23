using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The modal that installs the mod loader a launch needs, or updates the
/// installed one when it does not support the game, then starts the launch again.
/// When no release fits, it only warns before the launch.
/// </summary>
public partial class MainViewModel
{
    private Guid? _loaderPromptInstanceId;

    /// <summary>The release the modal installs, or null for the newest release that fits the game.</summary>
    private ModVersionMetadata? _promptedLoaderRelease;

    /// <summary>The update a launch skipped with "Play anyway", which no later launch asks about again.</summary>
    private string? _playedAnywayLoaderUpdate;

    /// <summary>The listed loader the launch needs. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaderPromptOpen), nameof(LoaderPromptText))]
    private ModMetadata? _promptedLoader;

    /// <summary>
    /// The installed loader the modal warns about. With an offer the modal updates it,
    /// without one it only warns. Null when the loader is not installed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaderUpdatePrompt), nameof(LoaderPromptText), nameof(LoaderPromptActionText), nameof(ShowLoaderPromptAction))]
    private LoaderUpdate? _promptedLoaderUpdate;

    public bool IsLoaderPromptOpen => PromptedLoader is not null;

    public bool IsLoaderUpdatePrompt => PromptedLoaderUpdate is not null;

    /// <summary>False when the game is not set up, or when the modal only warns, because no release fits the game and the mods.</summary>
    public bool ShowLoaderPromptAction => !NeedsGameSetup && PromptedLoaderUpdate is not { Offer: null };

    public string? LoaderPromptText => PromptedLoader is not { } loader
        ? null
        : PromptedLoaderUpdate switch
        {
            { InstalledVersion: { } installed, Offer: { } offer } => Localization.FormatLaunchLoaderUpdate(loader.Name, installed.ToString(), InstalledVersionText ?? string.Empty, offer.Version.ToString()),
            { InstalledVersion: { } installed } => Localization.FormatLaunchLoaderIncompatible(loader.Name, installed.ToString(), InstalledVersionText ?? string.Empty),
            _ => NeedsGameSetup ? Localization.FormatLaunchSetUpGameFirst(loader.Name) : Localization.FormatLaunchInstallLoader(loader.Name),
        };

    public string LoaderPromptActionText => IsLoaderUpdatePrompt ? Localization.LaunchUpdateAndPlay : Localization.SetupInstallLoader;

    partial void OnPromptedLoaderChanged(ModMetadata? value)
    {
        if (value is null)
            PromptedLoaderUpdate = null;
    }

    /// <summary>
    /// The needed loader, or else the first loader by id whose listing takes an
    /// instance. Null when installing a listed loader would not let the launch start.
    /// </summary>
    private static ModMetadata? LoaderToInstall(LaunchLoaderChoice choice, IReadOnlyList<ModMetadata> listings)
    {
        var loaders = listings.Where(listing => listing.Type == ContentType.ModLoader).OrderBy(listing => listing.ModId, ModIds.Comparer);
        return choice.Failure switch
        {
            LaunchLoaderFailure.NeededLoaderNotInstalled => loaders.FirstOrDefault(listing => ModIds.Equals(listing.ModId, choice.LoaderIds[0])),
            LaunchLoaderFailure.NoLoaderTakesInstance => loaders.FirstOrDefault(listing => listing.Provides?.Instance is not null),
            _ => null,
        };
    }

    /// <summary>
    /// What a launch of <paramref name="instance"/> through <paramref name="loader"/> asks
    /// about first: an installed release that does not support the game. The offer stays
    /// only when the mods of the instance accept it, else the modal only warns.
    /// </summary>
    private LoaderUpdate? LoaderUpdateBeforeLaunch(Instance instance, ModMetadata loader)
    {
        if (LoaderUpdateFor(loader.ModId) is not { } update || !IsBroken(update))
            return null;

        var prompt = update.Offer is { } offer && !AcceptsLoader(instance, loader.ModId, offer.Version)
            ? update with { Offer = null }
            : update;
        return string.Equals(PlayAnywayKey(prompt), _playedAnywayLoaderUpdate, StringComparison.Ordinal) ? null : prompt;
    }

    private void OpenLoaderPrompt(Guid instanceId, ModMetadata loader, LoaderUpdate? update = null, ModVersionMetadata? release = null)
    {
        _loaderPromptInstanceId = instanceId;
        _promptedLoaderRelease = release;
        LaunchMessage = null;
        SetupMessage = null;
        SetupError = null;
        PromptedLoader = loader;
        PromptedLoaderUpdate = update;
    }

    [RelayCommand]
    private async Task InstallPromptedLoaderAsync()
    {
        if (PromptedLoader is not { } loader || _loaderPromptInstanceId is not { } instanceId || IsSetupBusy)
            return;

        if (PromptedLoaderUpdate is { } update)
        {
            if (update.Offer is not { } offer || !await UpdateLoaderToAsync(loader.ModId, offer))
                return;
        }
        else
        {
            var release = _promptedLoaderRelease;
            await RunSetupAsync(services => InstallLoaderReleaseAsync(services, loader.ModId, release, directory: string.Empty));
            if (SetupError is not null || _services?.Settings.LoaderInstallations.Keys.Any(id => ModIds.Equals(id, loader.ModId)) != true)
                return;
        }

        PromptedLoader = null;
        await LaunchAsync(instanceId);
    }

    /// <summary>Starts the launch with the installed loader, because the bounds in the index can be wrong.</summary>
    [RelayCommand]
    private async Task PlayAnywayAsync()
    {
        if (PromptedLoaderUpdate is not { } update || _loaderPromptInstanceId is not { } instanceId || IsSetupBusy)
            return;

        _playedAnywayLoaderUpdate = PlayAnywayKey(update);
        PromptedLoader = null;
        await LaunchAsync(instanceId);
    }

    [RelayCommand]
    private Task SetUpGameForLoaderAsync()
    {
        PromptedLoader = null;
        return OpenGameSetupAsync();
    }

    [RelayCommand]
    private void CancelLoaderPrompt() => PromptedLoader = null;
}
