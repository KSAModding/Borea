using System.Text.Json;

namespace Borea.App.Tests.ViewModels;

public sealed class GameDetectionTests
{
    [Fact]
    public async Task GameTab_NothingFound_LeavesTheFieldsEmpty()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.DetectedGames);
        Assert.Equal(string.Empty, viewModel.GameDirectoryInput);
        Assert.False(viewModel.IsGameDirectorySuggested);
        Assert.Equal(string.Empty, viewModel.LoaderDirectoryInput);
    }

    [Fact]
    public async Task FirstStart_OneGameFound_FillsTheFieldAndSavesNothing()
    {
        string game = null!;
        using var harness = await ViewModelHarness.CreateAsync(candidates: h => h.Candidates.Games.Add(game = PlaceGame(h, "KSA")));
        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsGameTab);
        Assert.Equal(game, viewModel.GameDirectoryInput);
        Assert.True(viewModel.IsGameDirectorySuggested);
        Assert.False(viewModel.HasSeveralDetectedGames);
        Assert.Null(harness.Services.Settings.GameDirectoryPath);
        Assert.Null(await harness.Services.SettingsRepository.GetAsync());
    }

    [Fact]
    public async Task GameTab_TwoGamesFound_ListsBothAndFillsTheFieldOnlyOnSelection()
    {
        string first = null!, second = null!;
        using var harness = await ViewModelHarness.CreateAsync(candidates: h =>
        {
            h.Candidates.Games.Add(first = PlaceGame(h, "KSA"));
            h.Candidates.Games.Add(second = PlaceGame(h, "KSA Copy"));
        });
        var viewModel = harness.ViewModel;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSeveralDetectedGames);
        Assert.Equal([first, second], viewModel.DetectedGames.Select(game => game.Directory));
        Assert.All(viewModel.DetectedGames, game => Assert.Equal("2026.8.3.5117", game.Version.RawVersion));
        Assert.Equal(string.Empty, viewModel.GameDirectoryInput);

        viewModel.SelectedDetectedGame = viewModel.DetectedGames[1];

        Assert.Equal(second, viewModel.GameDirectoryInput);
        Assert.True(viewModel.IsGameDirectorySuggested);
        Assert.Null(harness.Services.Settings.GameDirectoryPath);
    }

    [Fact]
    public async Task UseThisFolder_SavesTheFoundGame()
    {
        string game = null!;
        using var harness = await ViewModelHarness.CreateAsync(candidates: h => h.Candidates.Games.Add(game = PlaceGame(h, "KSA")));
        var viewModel = harness.ViewModel;

        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(game, harness.Services.Settings.GameDirectoryPath);
        Assert.False(viewModel.IsGameDirectorySuggested);
    }

    [Fact]
    public async Task UseThisFolder_OneOfTwoGames_HidesTheList()
    {
        using var harness = await ViewModelHarness.CreateAsync(candidates: h =>
        {
            h.Candidates.Games.Add(PlaceGame(h, "KSA"));
            h.Candidates.Games.Add(PlaceGame(h, "KSA Copy"));
        });
        var viewModel = harness.ViewModel;
        viewModel.SelectedDetectedGame = viewModel.DetectedGames[1];

        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.DetectedGames);
        Assert.False(viewModel.HasSeveralDetectedGames);
    }

    [Fact]
    public async Task GameTab_FolderTypedWhileDetecting_KeepsTheTypedFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync(candidates: h => h.Candidates.Games.Add(PlaceGame(h, "KSA")));
        var viewModel = harness.ViewModel;
        var typed = Path.Combine(harness.Root, "typed");
        harness.Candidates.Reading = () => viewModel.GameDirectoryInput = typed;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        Assert.Equal(typed, viewModel.GameDirectoryInput);
        Assert.Single(viewModel.DetectedGames);
        Assert.False(viewModel.IsGameDirectorySuggested);
    }

    [Fact]
    public async Task GameTab_SavedGame_IsNotReplacedByAFoundOne()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var saved = Directory.CreateDirectory(Path.Combine(harness.Root, "saved")).FullName;
        viewModel.GameDirectoryInput = saved;
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        harness.Candidates.Games.Add(PlaceGame(harness, "KSA"));

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        Assert.Equal(saved, viewModel.GameDirectoryInput);
        Assert.Empty(viewModel.DetectedGames);
        Assert.False(viewModel.IsGameDirectorySuggested);
    }

    [Fact]
    public async Task GameTab_StarMapFound_OffersItsFolderWithoutRecordingIt()
    {
        string loader = null!;
        using var harness = await ViewModelHarness.CreateAsync(candidates: h => h.Candidates.Loaders.Add(loader = PlaceStarMap(h)));
        var viewModel = harness.ViewModel;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        Assert.Equal(loader, viewModel.LoaderDirectoryInput);
        Assert.True(viewModel.IsLoaderDirectorySuggested);
        Assert.Empty(harness.Services.Settings.LoaderInstallations);

        await viewModel.AdoptLoaderCommand.ExecuteAsync(null);

        Assert.Equal(loader, harness.Services.Settings.LoaderInstallations["StarMap"].DirectoryPath);
        Assert.False(viewModel.IsLoaderDirectorySuggested);
    }

    [Fact]
    public async Task OtherLoaderSelected_RemovesTheFoundFolder()
    {
        using var harness = await ViewModelHarness.CreateAsync(candidates: h => h.Candidates.Loaders.Add(PlaceStarMap(h)));
        var viewModel = harness.ViewModel;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        viewModel.SelectedLoader = null;

        Assert.Equal(string.Empty, viewModel.LoaderDirectoryInput);
        Assert.False(viewModel.IsLoaderDirectorySuggested);
    }

    private static string PlaceGame(ViewModelHarness harness, string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(harness.Root, name)).FullName;
        File.WriteAllText(Path.Combine(directory, "KSA.exe"), "game");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(directory, "KSA.dll"));
        return directory;
    }

    private static string PlaceStarMap(ViewModelHarness harness)
    {
        var directory = Directory.CreateDirectory(Path.Combine(harness.Root, "StarMap")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "LoaderVersionFixture.dll"), Path.Combine(directory, "StarMap.exe"));
        File.WriteAllText(Path.Combine(directory, "StarMapConfig.json"), JsonSerializer.Serialize(new { GameLocation = "" }));
        return directory;
    }
}
