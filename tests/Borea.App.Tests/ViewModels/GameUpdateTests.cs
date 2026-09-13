using Borea.Core.Game;

namespace Borea.App.Tests.ViewModels;

public sealed class GameUpdateTests
{
    private const string GameBuild = "GameVersionFixture.dll";
    private const string ForeignBuild = "UnparseableVersionFixture.dll";

    /// <summary>A harness whose settings point at an empty game folder.</summary>
    private static async Task<(ViewModelHarness Harness, string Game)> CreateAsync()
    {
        string? game = null;
        var harness = await ViewModelHarness.CreateAsync(services =>
        {
            game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
            return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
        });
        return (harness, game!);
    }

    /// <summary>Puts a fixture where the game's own assembly is, as a KSA update does.</summary>
    private static void PutGameAssembly(string game, string fixture) =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, fixture), Path.Combine(game, "KSA.dll"), overwrite: true);

    [Fact]
    public async Task NewGameBuild_UpdatesTheChipAndTheCompatibility()
    {
        var (harness, game) = await CreateAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.Null(viewModel.InstalledVersionText);
        Assert.All(viewModel.DiscoverItems, item => Assert.Equal(GameCompatibility.Unknown, item.Compatibility));

        PutGameAssembly(game, GameBuild);
        await viewModel.RefreshInstalledGameAsync();

        Assert.Equal("2026.8.3.5117", viewModel.InstalledVersionText);
        Assert.Contains(viewModel.DiscoverItems, item => item.Compatibility != GameCompatibility.Unknown);
    }

    [Fact]
    public async Task UnchangedGame_LeavesTheRowsAlone()
    {
        var (harness, game) = await CreateAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        PutGameAssembly(game, GameBuild);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        afc.Compatibility = GameCompatibility.Untested;

        await viewModel.RefreshInstalledGameAsync();

        // a real refresh would have evaluated the row again
        Assert.Equal(GameCompatibility.Untested, afc.Compatibility);
    }

    [Fact]
    public async Task GameBuildTheAppCannotRead_ShowsItsTextAndUnknownCompatibility()
    {
        var (harness, game) = await CreateAsync();
        using var _ = harness;
        var viewModel = harness.ViewModel;
        PutGameAssembly(game, GameBuild);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();

        PutGameAssembly(game, ForeignBuild);
        await viewModel.RefreshInstalledGameAsync();

        Assert.NotEqual("2026.8.3.5117", viewModel.InstalledVersionText);
        Assert.All(viewModel.DiscoverItems, item => Assert.Equal(GameCompatibility.Unknown, item.Compatibility));
    }
}
