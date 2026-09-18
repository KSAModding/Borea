using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Borea.Core.Launch;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The modal that installs the mod loader a launch needs, then starts the launch again.</summary>
public partial class MainViewModel
{
    private Guid? _loaderPromptInstanceId;

    /// <summary>The listed loader the launch needs. Null while the modal is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaderPromptOpen), nameof(LoaderPromptText))]
    private ModMetadata? _promptedLoader;

    public bool IsLoaderPromptOpen => PromptedLoader is not null;

    public string? LoaderPromptText => PromptedLoader is not { } loader
        ? null
        : NeedsGameSetup ? Localization.FormatLaunchSetUpGameFirst(loader.Name) : Localization.FormatLaunchInstallLoader(loader.Name);

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

    private void OpenLoaderPrompt(Guid instanceId, ModMetadata loader)
    {
        _loaderPromptInstanceId = instanceId;
        LaunchMessage = null;
        SetupMessage = null;
        SetupError = null;
        PromptedLoader = loader;
    }

    [RelayCommand]
    private async Task InstallPromptedLoaderAsync()
    {
        if (PromptedLoader is not { } loader || _loaderPromptInstanceId is not { } instanceId || IsSetupBusy)
            return;

        await RunSetupAsync(services => InstallNewestLoaderAsync(services, loader.ModId, directory: string.Empty));
        if (SetupError is not null || _services?.Settings.LoaderInstallations.Keys.Any(id => ModIds.Equals(id, loader.ModId)) != true)
            return;

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
