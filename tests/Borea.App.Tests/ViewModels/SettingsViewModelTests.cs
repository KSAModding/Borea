using Borea.App.ViewModels;

namespace Borea.App.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task GameTab_ListsTheLoadersFromTheListings()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsGameTab);
        Assert.False(viewModel.IsGeneralTab);
        Assert.Equal("StarMap", Assert.Single(viewModel.Loaders).ModId);
        Assert.Equal("StarMap", viewModel.SelectedLoader?.ModId);
        Assert.Equal(string.Empty, viewModel.GameDirectoryInput);

        viewModel.ShowGeneralSettingsCommand.Execute(null);
        Assert.True(viewModel.IsGeneralTab);
    }

    [Fact]
    public async Task SaveGameDirectory_PersistsAndRebuildsTheServices()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var game = Directory.CreateDirectory(Path.Combine(harness.Root, "game")).FullName;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        viewModel.GameDirectoryInput = game;
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.SetupSaved, viewModel.SetupMessage);
        Assert.Null(viewModel.SetupError);
        Assert.False(viewModel.IsSetupBusy);
        Assert.Equal(game, harness.Services.Settings.GameDirectoryPath);
    }

    [Fact]
    public async Task SaveGameDirectory_MissingFolder_SavesAndWarns()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        viewModel.GameDirectoryInput = Path.Combine(harness.Root, "not-there");
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.SetupDirectoryMissing, viewModel.SetupMessage);
    }

    [Fact]
    public async Task UseExistingLoader_RecordsTheLoaderDirectory()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var loader = Directory.CreateDirectory(Path.Combine(harness.Root, "StarMap")).FullName;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        viewModel.LoaderDirectoryInput = loader;
        await viewModel.AdoptLoaderCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.SetupSaved, viewModel.SetupMessage);
        Assert.Equal(loader, harness.Services.Settings.LoaderInstallations["StarMap"].DirectoryPath);

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        Assert.Equal(loader, viewModel.LoaderDirectoryInput);
    }

    [Fact]
    public async Task LoaderActions_WithoutASelection_AskForOne()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.SelectedLoader = null;
        await viewModel.AdoptLoaderCommand.ExecuteAsync(null);
        Assert.Equal(harness.Localization.SetupNoLoaderSelected, viewModel.SetupMessage);

        await viewModel.InstallLoaderCommand.ExecuteAsync(null);
        Assert.Equal(harness.Localization.SetupNoLoaderSelected, viewModel.SetupMessage);
    }

    [Fact]
    public async Task InstallLoader_DownloadFails_ShowsTheError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        await viewModel.InstallLoaderCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SetupError);
        Assert.False(viewModel.IsSetupBusy);
        Assert.Equal(0, viewModel.SetupProgress);
        Assert.Empty(harness.Services.Settings.LoaderInstallations);
    }

    [Fact]
    public async Task Theme_IsSaved()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        harness.ViewModel.CurrentTheme = "Light";

        var saved = await WaitForSavedAsync(harness, preferences => preferences.SelectedThemeName == "Light");
        Assert.Equal("Light", saved.SelectedThemeName);
    }

    [Fact]
    public async Task Language_IsSaved()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        harness.Localization.TrySetCulture("de");

        var saved = await WaitForSavedAsync(harness, preferences => preferences.UiCultureName == "de");
        Assert.Equal("de", saved.UiCultureName);
    }

    /// <summary>
    /// Theme and language are saved without awaiting the property change, so
    /// the test polls the file for a short while.
    /// </summary>
    private static async Task<Borea.Core.Preferences.AppPreferences> WaitForSavedAsync(
        ViewModelHarness harness,
        Func<Borea.Core.Preferences.AppPreferences, bool> done)
    {
        for (var attempt = 0; ; attempt++)
        {
            var saved = (await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames)).Preferences;
            if (done(saved) || attempt == 100)
                return saved;
            await Task.Delay(20);
        }
    }
}
