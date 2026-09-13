using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The Game section of the settings modal: where KSA is, and which mod
/// loader starts it. Saving rebuilds the services, because their paths are
/// fixed when the graph is built (see <see cref="BoreaServices"/>).
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralTab))]
    private bool _isGameTab;

    public bool IsGeneralTab => !IsGameTab;

    [ObservableProperty]
    private string _gameDirectoryInput = string.Empty;

    [ObservableProperty]
    private string _loaderDirectoryInput = string.Empty;

    public ObservableCollection<DiscoverItem> Loaders { get; } = [];

    [ObservableProperty]
    private DiscoverItem? _selectedLoader;

    [ObservableProperty]
    private string? _setupMessage;

    [ObservableProperty]
    private string? _setupError;

    [ObservableProperty]
    private bool _isSetupBusy;

    [ObservableProperty]
    private double _setupProgress;

    [RelayCommand]
    private void ShowGeneralSettings() => IsGameTab = false;

    [RelayCommand]
    private async Task ShowGameSettingsAsync()
    {
        IsGameTab = true;
        SetupMessage = null;
        SetupError = null;
        if (_services is null)
            return;

        GameDirectoryInput = _services.Settings.GameDirectoryPath ?? string.Empty;
        var loaderId = _services.Settings.LoaderInstallations.Keys.FirstOrDefault();
        LoaderDirectoryInput = loaderId is null ? string.Empty : _services.Settings.LoaderInstallations[loaderId].DirectoryPath;

        await EnsureDiscoverLoadedAsync();
        Loaders.Clear();
        foreach (var loader in _listings.Where(item => item.Type == ContentType.ModLoader))
            Loaders.Add(loader);
        SelectedLoader = Loaders.FirstOrDefault(loader => loaderId is not null && ModIds.Equals(loader.ModId, loaderId)) ?? Loaders.FirstOrDefault();
    }

    [RelayCommand]
    private Task SaveGameDirectoryAsync() => RunSetupAsync(async services =>
    {
        var path = GameDirectoryInput.Trim();
        var directory = path.Length == 0 ? null : Path.GetFullPath(path);
        await services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(directory));
        return directory is not null && !Directory.Exists(directory) ? Localization.SetupDirectoryMissing : Localization.SetupSaved;
    });

    /// <summary>
    /// Points Borea at a loader that is already on disk, the way
    /// <c>borea settings set loader</c> does.
    /// </summary>
    [RelayCommand]
    private Task AdoptLoaderAsync() => RunSetupAsync(async services =>
    {
        if (SelectedLoader is null)
            return Localization.SetupNoLoaderSelected;

        var directory = Path.GetFullPath(LoaderDirectoryInput.Trim());
        var installation = new LoaderInstallation(directory, version: null, rawVersion: null, isAdopted: true);
        await services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation(SelectedLoader.ModId, installation));
        return Directory.Exists(directory) ? Localization.SetupSaved : Localization.SetupDirectoryMissing;
    });

    /// <summary>
    /// Downloads the loader's latest release and installs it. An empty
    /// directory lets the installer pick its default.
    /// </summary>
    [RelayCommand]
    private Task InstallLoaderAsync() => RunSetupAsync(async services =>
    {
        if (SelectedLoader is null)
            return Localization.SetupNoLoaderSelected;

        var listing = await services.Mods.GetListingAsync(SelectedLoader.ModId)
            ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
        var release = await services.Mods.GetLatestReleaseAsync(SelectedLoader.ModId)
            ?? throw new InvalidOperationException(Localization.DiscoverNoRelease);
        var directory = LoaderDirectoryInput.Trim();
        var progress = new Progress<DownloadProgress>(value => SetupProgress = value.PercentComplete);
        var result = await services.LoaderInstaller.InstallAsync(listing, release, directory.Length == 0 ? null : Path.GetFullPath(directory), progress);
        LoaderDirectoryInput = result.Directory;
        return Localization.FormatSetupLoaderInstalled(listing.Name, result.Version.ToString(), result.Directory);
    });

    private async Task RunSetupAsync(Func<BoreaServices, Task<string>> operation)
    {
        if (_services is null || IsSetupBusy)
            return;

        IsSetupBusy = true;
        SetupError = null;
        SetupMessage = null;
        try
        {
            SetupMessage = await operation(_services);
            await RebuildServicesAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or HttpRequestException or DownloadFailedException or TaskCanceledException)
        {
            SetupError = exception.Message;
        }
        finally
        {
            IsSetupBusy = false;
            SetupProgress = 0;
        }
    }

    /// <summary>
    /// Builds a fresh graph from the saved settings and reloads every page
    /// that shows something path-dependent.
    /// </summary>
    private async Task RebuildServicesAsync()
    {
        if (_services is null)
            return;

        var previous = _services;
        _services = await _rebuildServices();
        _instances = _services.Instances;
        previous.Dispose();
        await LoadAsync();
    }
}
