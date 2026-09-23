using Borea.App.ViewModels;

namespace Borea.App.Tests.ViewModels;

public sealed class GameSetupPromptTests
{
    [Fact]
    public async Task FirstLoad_WithoutAGameDirectoryAndNoGameFound_ShowsTheBannerAndOpensNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        Assert.Equal(GameSetupState.NotSaved, viewModel.GameSetupState);
        Assert.True(viewModel.NeedsGameSetup);
        Assert.Equal(harness.Localization.SetupBannerNotSaved, viewModel.GameSetupBannerText);
        Assert.Equal(harness.Localization.HomeSetupNotSaved, viewModel.HomeSetupText);
        Assert.False(viewModel.IsSettingsOpen);
        Assert.False(viewModel.IsFoundGameModalOpen);
    }

    [Fact]
    public async Task LaterLoads_DoNotOpenTheSettings()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.LoadAsync();

        Assert.False(viewModel.IsSettingsOpen);
        Assert.True(viewModel.NeedsGameSetup);
    }

    [Fact]
    public async Task SavingTheGameDirectory_ClearsTheBanner()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var game = Directory.CreateDirectory(Path.Combine(harness.Root, "game")).FullName;

        viewModel.GameDirectoryInput = game;
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(GameSetupState.Ready, viewModel.GameSetupState);
        Assert.False(viewModel.NeedsGameSetup);
        Assert.Null(viewModel.GameSetupBannerText);
        Assert.Null(viewModel.HomeSetupText);
    }

    [Fact]
    public async Task SavedFolderThatIsGone_ShowsTheMissingBannerWithoutOpeningTheSettings()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.GameDirectoryInput = Path.Combine(harness.Root, "not-there");
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        await viewModel.LoadAsync();

        Assert.Equal(GameSetupState.FolderMissing, viewModel.GameSetupState);
        Assert.Equal(harness.Localization.SetupBannerFolderMissing, viewModel.GameSetupBannerText);
        Assert.Equal(harness.Localization.HomeSetupFolderMissing, viewModel.HomeSetupText);
        Assert.False(viewModel.IsSettingsOpen);
    }

    [Fact]
    public async Task BannerAction_OpensTheGameTab()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.OpenGameSetupCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSettingsOpen);
        Assert.True(viewModel.IsGameTab);
    }

    [Fact]
    public async Task LanguageChange_RetranslatesTheBanner()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.Equal(harness.Localization.SetupBannerNotSaved, viewModel.GameSetupBannerText);
        Assert.Equal(harness.Localization.HomeSetupNotSaved, viewModel.HomeSetupText);
        Assert.Contains(nameof(MainViewModel.HomeSetupText), changed);
        Assert.Contains("Kitten Space Agency", viewModel.GameSetupBannerText);
    }
}
