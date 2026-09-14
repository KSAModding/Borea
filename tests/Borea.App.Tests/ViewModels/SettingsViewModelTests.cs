using Borea.App.Formatting;
using Borea.App.ViewModels;
using Borea.Composition;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Preferences;
using Borea.Core.Settings;

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
    public async Task GameTab_NoLoaderRecorded_OffersInstall()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        Assert.Null(viewModel.InstalledLoaderText);
        Assert.Equal(harness.Localization.SetupInstallLoader, viewModel.LoaderInstallActionText);
        Assert.True(viewModel.CanInstallLoader);
    }

    [Theory]
    [InlineData("0.4.6", false)]
    [InlineData("0.4.5", true)]
    public async Task GameTab_RecordedLoader_ShowsItsVersionAndOffersOnlyANewerRelease(string recorded, bool canUpdate)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var installation = new LoaderInstallation(Path.Combine(harness.Root, "StarMap"), ModVersion.Parse(recorded), recorded, isAdopted: false);
        using var services = await ServicesWithLoaderAsync(harness, installation);
        var viewModel = new MainViewModel(harness.Localization, new RegionalFormatService(harness.Localization), null, AppPreferences.Empty, services);

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatSetupLoaderInstalledVersion(recorded), viewModel.InstalledLoaderText);
        Assert.Equal(harness.Localization.FormatSetupUpdateLoader("0.4.6"), viewModel.LoaderInstallActionText);
        Assert.Equal(canUpdate, viewModel.CanInstallLoader);
    }

    [Fact]
    public async Task GameTab_RecordedLoaderNewerThanTheChannel_OffersNoOlderRelease()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var installation = new LoaderInstallation(Path.Combine(harness.Root, "StarMap"), ModVersion.Parse("0.5.0-dev.1"), "0.5.0-dev.1", isAdopted: false);
        using var services = await ServicesWithLoaderAsync(harness, installation);
        var viewModel = new MainViewModel(harness.Localization, new RegionalFormatService(harness.Localization), null, AppPreferences.Empty, services);

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatSetupLoaderInstalledVersion("0.5.0-dev.1"), viewModel.InstalledLoaderText);
        Assert.Equal(harness.Localization.SetupInstallLoader, viewModel.LoaderInstallActionText);
        Assert.False(viewModel.CanInstallLoader);
    }

    [Fact]
    public async Task SaveGameDirectory_KeepsAChannelSavedWhileTheAppIsOpen()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var game = Directory.CreateDirectory(Path.Combine(harness.Root, "game")).FullName;
        await harness.Services.SettingsRepository.SaveAsync(new BoreaSettings(null, releaseChannel: ReleaseChannel.Testing));
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        viewModel.GameDirectoryInput = game;
        await viewModel.SaveGameDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(game, harness.Services.Settings.GameDirectoryPath);
        Assert.Equal(ReleaseChannel.Testing, harness.Services.Settings.ReleaseChannel);
        Assert.Equal(ReleaseChannel.Testing, viewModel.SelectedReleaseChannel.Channel);
    }

    [Fact]
    public async Task UseExistingLoader_KeepsAChannelSavedWhileTheAppIsOpen()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var loader = Directory.CreateDirectory(Path.Combine(harness.Root, "StarMap")).FullName;
        await harness.Services.SettingsRepository.SaveAsync(new BoreaSettings(null, releaseChannel: ReleaseChannel.Testing));
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        viewModel.LoaderDirectoryInput = loader;
        await viewModel.AdoptLoaderCommand.ExecuteAsync(null);

        Assert.Equal(loader, harness.Services.Settings.LoaderInstallations["StarMap"].DirectoryPath);
        Assert.Equal(ReleaseChannel.Testing, harness.Services.Settings.ReleaseChannel);
    }

    [Fact]
    public async Task UseExistingLoader_WithoutAVersion_OffersReinstall()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var loader = Directory.CreateDirectory(Path.Combine(harness.Root, "StarMap")).FullName;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        viewModel.LoaderDirectoryInput = loader;
        await viewModel.AdoptLoaderCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.SetupLoaderInstalledUnknownVersion, viewModel.InstalledLoaderText);
        Assert.Equal(harness.Localization.SetupReinstallLoader, viewModel.LoaderInstallActionText);
        Assert.True(viewModel.CanInstallLoader);
    }

    private static async Task<BoreaServices> ServicesWithLoaderAsync(ViewModelHarness harness, LoaderInstallation installation)
    {
        var settings = harness.Services.Settings.WithLoaderInstallation("StarMap", installation);
        await harness.Services.SettingsRepository.SaveAsync(settings);
        return await harness.BuildServicesAsync();
    }

    [Fact]
    public async Task Theme_IsSaved()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        harness.ViewModel.CurrentTheme = "Light";
        await harness.ViewModel.WhenPreferencesSavedAsync();

        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal("Light", saved.Preferences.SelectedThemeName);
    }

    [Fact]
    public async Task Language_IsSaved()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        harness.Localization.TrySetCulture("de");
        await harness.ViewModel.WhenPreferencesSavedAsync();

        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal("de", saved.Preferences.UiCultureName);
    }
}
