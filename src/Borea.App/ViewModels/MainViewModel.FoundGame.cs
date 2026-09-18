using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>The modal that offers the games Borea found while no usable game directory is saved.</summary>
public partial class MainViewModel
{
    private Task _gameDetection = Task.CompletedTask;

    private bool _gameTabShown;

    public ObservableCollection<FoundGameItem> FoundGames { get; } = [];

    [ObservableProperty]
    private FoundGameItem? _selectedFoundGame;

    [ObservableProperty]
    private bool _isFoundGameModalOpen;

    public bool HasSeveralFoundGames => FoundGames.Count > 1;

    public string? FoundGameText => FoundGames.Count switch
    {
        0 => null,
        1 => Localization.FormatSetupFoundGame(FoundGames[0].Version, FoundGames[0].DirectoryText),
        _ => Localization.SetupFoundGames,
    };

    /// <summary>Completes when the detection of the first load has finished.</summary>
    internal Task WhenGameDetectedAsync() => _gameDetection;

    private async Task OfferFoundGamesAsync(BoreaServices services)
    {
        var detection = await FindInstallsAsync(services);

        // a player who saved a folder, looked at the Game tab or is in another modal keeps the banner as the reminder
        if (detection is not { Games.Count: > 0 } || !NeedsGameSetup || _gameTabShown || IsSettingsOpen || IsNameModalOpen || IsReviewingModList || IsLoaderPromptOpen || IsLaunchArgumentsModalOpen || IsLaunchFailureOpen || IsToastDetailsOpen)
            return;

        FoundGames.Clear();
        foreach (var game in detection.Games)
            FoundGames.Add(new FoundGameItem(game));
        SelectedFoundGame = FoundGames.Count == 1 ? FoundGames[0] : null;
        OnPropertyChanged(nameof(HasSeveralFoundGames));
        OnPropertyChanged(nameof(FoundGameText));
        SetupError = null;
        IsFoundGameModalOpen = true;
    }

    /// <summary>Looks for KSA and the listed mod loaders on disk. Null when the detection failed.</summary>
    private static async Task<InstallDetection?> FindInstallsAsync(BoreaServices services)
    {
        IReadOnlyList<ModMetadata> loaders = [];
        try
        {
            loaders = (await services.ContentIndex.GetAvailableModsAsync()).Where(listing => listing.Type == ContentType.ModLoader).ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // a loader's configuration can name the game, but the usual folders are checked without it
        }

        try
        {
            return await Task.Run(() => services.InstallDetector.DetectAsync(loaders));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task UseFoundGameAsync()
    {
        if (SelectedFoundGame is not { } game || IsSetupBusy)
            return;

        await RunSetupAsync(services => ChangeGameDirectoryAsync(services, game.Directory));
        if (SetupError is null)
            IsFoundGameModalOpen = false;
    }

    [RelayCommand]
    private void DismissFoundGame() => IsFoundGameModalOpen = false;
}

/// <summary>One game in the found-game modal, with the folder shown the way the App shows paths.</summary>
public sealed class FoundGameItem(DetectedGame game)
{
    public string Directory => game.Directory;

    public string Version => game.Version.RawVersion;

    public string DirectoryText => MainViewModel.WithoutUserProfile(game.Directory);
}
