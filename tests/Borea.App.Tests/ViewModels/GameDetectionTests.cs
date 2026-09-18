using System.Text.Json;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.ModLoaders;

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
    public async Task FirstStart_OneGameFound_AsksToUseItAndSavesNothing()
    {
        string game = null!;
        using var harness = await ViewModelHarness.CreateAsync(candidates: h => h.Candidates.Games.Add(game = PlaceGame(h, "KSA")));
        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsFoundGameModalOpen);
        Assert.False(viewModel.HasSeveralFoundGames);
        Assert.Equal(harness.Localization.FormatSetupFoundGame("2026.8.3.5117", MainViewModel.WithoutUserProfile(game)), viewModel.FoundGameText);
        Assert.False(viewModel.IsSettingsOpen);
        Assert.Null(await harness.Services.SettingsRepository.GetAsync());
    }

    [Fact]
    public async Task FirstStart_DetectionRunsInTheBackground()
    {
        using var release = new ManualResetEventSlim();
        using var harness = await CreateWithHeldDetectionAsync(release);
        var viewModel = harness.ViewModel;

        var openBeforeDetection = viewModel.IsFoundGameModalOpen;
        release.Set();
        await viewModel.WhenGameDetectedAsync();

        Assert.False(openBeforeDetection);
        Assert.True(viewModel.IsFoundGameModalOpen);
    }

    [Fact]
    public async Task FirstStart_FolderSavedWhileDetecting_OpensNothing()
    {
        using var release = new ManualResetEventSlim();
        using var harness = await CreateWithHeldDetectionAsync(release);
        var viewModel = harness.ViewModel;
        viewModel.GameDirectoryInput = Directory.CreateDirectory(Path.Combine(harness.Root, "saved")).FullName;

        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        release.Set();
        await viewModel.WhenGameDetectedAsync();

        Assert.False(viewModel.IsFoundGameModalOpen);
    }

    [Fact]
    public async Task FirstStart_SettingsOpenWhenDetectionEnds_OpensNothing()
    {
        using var release = new ManualResetEventSlim();
        using var harness = await CreateWithHeldDetectionAsync(release);
        var viewModel = harness.ViewModel;

        viewModel.SetMainWindowSettings();
        release.Set();
        await viewModel.WhenGameDetectedAsync();

        Assert.False(viewModel.IsFoundGameModalOpen);
    }

    [Fact]
    public async Task FirstStart_GameTabShownWhileDetecting_OpensNothing()
    {
        using var release = new ManualResetEventSlim();
        using var harness = await CreateWithHeldDetectionAsync(release);
        var viewModel = harness.ViewModel;

        await viewModel.OpenGameSetupCommand.ExecuteAsync(null);
        viewModel.CloseSettingsCommand.Execute(null);
        release.Set();
        await viewModel.WhenGameDetectedAsync();

        Assert.False(viewModel.IsFoundGameModalOpen);
    }

    [Fact]
    public async Task FirstStart_InstanceNameModalOpenWhenDetectionEnds_OpensNothing()
    {
        using var release = new ManualResetEventSlim();
        using var harness = await CreateWithHeldDetectionAsync(release);
        var viewModel = harness.ViewModel;

        viewModel.BeginCreateInstanceCommand.Execute(null);
        release.Set();
        await viewModel.WhenGameDetectedAsync();

        Assert.False(viewModel.IsFoundGameModalOpen);
    }

    [Fact]
    public async Task FirstStart_LaunchArgumentsModalOpenWhenDetectionEnds_OpensNothing()
    {
        using var release = new ManualResetEventSlim();
        using var harness = await CreateWithHeldDetectionAsync(release);
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Main", Borea.Core.Instances.InstanceSource.Custom.Value);
        await viewModel.LoadAsync();

        viewModel.Instances.Single().BeginEditLaunchArgumentsCommand.Execute(null);
        Assert.True(viewModel.IsLaunchArgumentsModalOpen);
        release.Set();
        await viewModel.WhenGameDetectedAsync();

        Assert.False(viewModel.IsFoundGameModalOpen);
    }

    [Fact]
    public async Task FirstStart_SavedFolderMissing_AsksToUseTheFoundGame()
    {
        string root = null!;
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(Path.Combine(root, "removed"))),
            candidates: h =>
            {
                root = h.Root;
                h.Candidates.Games.Add(PlaceGame(h, "KSA"));
            });
        var viewModel = harness.ViewModel;

        Assert.Equal(GameSetupState.FolderMissing, viewModel.GameSetupState);
        Assert.True(viewModel.IsFoundGameModalOpen);
        Assert.False(viewModel.IsSettingsOpen);
    }

    [Fact]
    public async Task FolderClearedLaterInTheSession_OpensNothing()
    {
        string game = null!;
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game)),
            candidates: h => h.Candidates.Games.Add(game = PlaceGame(h, "KSA")));
        var viewModel = harness.ViewModel;
        viewModel.GameDirectoryInput = string.Empty;

        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        await viewModel.WhenGameDetectedAsync();

        Assert.True(viewModel.NeedsGameSetup);
        Assert.False(viewModel.IsFoundGameModalOpen);
    }

    [Fact]
    public async Task UseIt_SavesTheFoundGameAndClosesTheModal()
    {
        string game = null!;
        using var harness = await ViewModelHarness.CreateAsync(candidates: h => h.Candidates.Games.Add(game = PlaceGame(h, "KSA")));
        var viewModel = harness.ViewModel;

        await viewModel.UseFoundGameCommand.ExecuteAsync(null);

        Assert.Equal(game, harness.Services.Settings.GameDirectoryPath);
        Assert.False(viewModel.IsFoundGameModalOpen);
        Assert.False(viewModel.NeedsGameSetup);
        Assert.Equal("2026.8.3.5117", viewModel.InstalledVersionText);
    }

    [Fact]
    public async Task UseIt_WritesTheGameIntoTheRecordedLoader()
    {
        string game = null!, loader = null!;
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation("StarMap", new LoaderInstallation(loader, version: null, rawVersion: null, isAdopted: true))),
            candidates: h =>
            {
                h.Candidates.Games.Add(game = PlaceGame(h, "KSA"));
                loader = PlaceStarMap(h);
            });
        var viewModel = harness.ViewModel;

        await viewModel.UseFoundGameCommand.ExecuteAsync(null);

        var configuration = JsonNode.Parse(File.ReadAllText(Path.Combine(loader, "StarMapConfig.json")))!;
        Assert.Equal(game, (string?)configuration["GameLocation"]);
        Assert.Equal(game, harness.Services.Settings.GameDirectoryPath);
    }

    [Fact]
    public async Task UseIt_Fails_KeepsTheModalOpenWithTheReason()
    {
        string loader = null!;
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation("StarMap", new LoaderInstallation(loader, version: null, rawVersion: null, isAdopted: true))),
            candidates: h =>
            {
                h.Candidates.Games.Add(PlaceGame(h, "KSA"));
                loader = PlaceStarMap(h);
                File.WriteAllText(Path.Combine(loader, "StarMapConfig.json"), "{ not json");
            });
        var viewModel = harness.ViewModel;

        await viewModel.UseFoundGameCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsFoundGameModalOpen);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.SetupError));
        Assert.False(viewModel.IsSetupBusy);
        Assert.Null(harness.Services.Settings.GameDirectoryPath);
    }

    [Fact]
    public async Task GameTab_Save_WritesTheGameIntoTheRecordedLoader()
    {
        string loader = null!;
        using var harness = await ViewModelHarness.CreateAsync(
            services => services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation("StarMap", new LoaderInstallation(loader, version: null, rawVersion: null, isAdopted: true))),
            candidates: h => loader = PlaceStarMap(h));
        var viewModel = harness.ViewModel;
        var game = Directory.CreateDirectory(Path.Combine(harness.Root, "game")).FullName;
        viewModel.GameDirectoryInput = game;

        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);

        var configuration = JsonNode.Parse(File.ReadAllText(Path.Combine(loader, "StarMapConfig.json")))!;
        Assert.Equal(game, (string?)configuration["GameLocation"]);
        Assert.Equal(game, harness.Services.Settings.GameDirectoryPath);
    }

    [Fact]
    public async Task FirstStart_TwoGamesFound_LetsThePlayerPickOne()
    {
        string second = null!;
        using var harness = await ViewModelHarness.CreateAsync(candidates: h =>
        {
            h.Candidates.Games.Add(PlaceGame(h, "KSA"));
            h.Candidates.Games.Add(second = PlaceGame(h, "KSA Copy"));
        });
        var viewModel = harness.ViewModel;

        Assert.True(viewModel.IsFoundGameModalOpen);
        Assert.True(viewModel.HasSeveralFoundGames);
        Assert.Equal(harness.Localization.SetupFoundGames, viewModel.FoundGameText);
        Assert.Equal(2, viewModel.FoundGames.Count);
        Assert.Null(viewModel.SelectedFoundGame);

        await viewModel.UseFoundGameCommand.ExecuteAsync(null);
        Assert.Null(harness.Services.Settings.GameDirectoryPath);

        viewModel.SelectedFoundGame = viewModel.FoundGames[1];
        await viewModel.UseFoundGameCommand.ExecuteAsync(null);

        Assert.Equal(second, harness.Services.Settings.GameDirectoryPath);
        Assert.False(viewModel.IsFoundGameModalOpen);
    }

    [Fact]
    public async Task Later_KeepsTheBannerAndDoesNotAskAgainInTheSession()
    {
        using var harness = await ViewModelHarness.CreateAsync(candidates: h => h.Candidates.Games.Add(PlaceGame(h, "KSA")));
        var viewModel = harness.ViewModel;

        viewModel.DismissFoundGameCommand.Execute(null);
        await viewModel.LoadAsync();
        await viewModel.WhenGameDetectedAsync();

        Assert.False(viewModel.IsFoundGameModalOpen);
        Assert.True(viewModel.NeedsGameSetup);
        Assert.Equal(harness.Localization.SetupBannerNotSaved, viewModel.GameSetupBannerText);
        Assert.False(viewModel.IsSettingsOpen);
        Assert.Null(harness.Services.Settings.GameDirectoryPath);
    }

    [Fact]
    public async Task GameTab_OneGameFound_FillsTheFieldAndSavesNothing()
    {
        string game = null!;
        using var harness = await ViewModelHarness.CreateAsync(candidates: h => h.Candidates.Games.Add(game = PlaceGame(h, "KSA")));
        var viewModel = harness.ViewModel;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

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
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

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
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
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

    /// <summary>A first start that finds one game, with the detection held in its first read of the folders until <paramref name="release"/> is set.</summary>
    private static Task<ViewModelHarness> CreateWithHeldDetectionAsync(ManualResetEventSlim release)
    {
        var reads = 0;
        return ViewModelHarness.CreateAsync(
            candidates: h =>
            {
                h.Candidates.Games.Add(PlaceGame(h, "KSA"));
                h.Candidates.Reading = () =>
                {
                    if (Interlocked.Increment(ref reads) == 1)
                        release.Wait(TimeSpan.FromSeconds(5));
                };
            },
            waitForDetection: false);
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
