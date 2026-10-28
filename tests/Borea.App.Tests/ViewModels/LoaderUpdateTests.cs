using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Launch;

namespace Borea.App.Tests.ViewModels;

/// <summary>
/// The offer of a newer mod loader on Home, before Play, on the instance page and
/// in the settings. The game is <see cref="GameBuild"/>. StarMap 0.4.7 supports it,
/// 0.4.8 starts after it, and 0.4.6 ends before it unless a test keeps it open.
/// </summary>
public sealed class LoaderUpdateTests
{
    private const string GameBuild = "2026.8.3.5117";

    // the file version of this fixture reads as a game build of revision 0, which only StarMap 0.4.6 fits with keptBuild
    private static readonly GameVersion KeptBuild = GameVersion.Parse("0.4.6.0");

    private static readonly byte[] StarMapArchive = CreateStarMapArchive();

    private static string DownloadUrl(string version) => $"https://github.com/StarMapLoader/StarMap/releases/download/{version}/StarMap-{version}.zip";

    /// <param name="installed">The recorded StarMap release, or null to record none.</param>
    /// <param name="installedFitsTheGame">False gives 0.4.6 a game_max below the game.</param>
    /// <param name="knownVersion">False records the loader without a version, as "Use existing" did.</param>
    /// <param name="noFittingRelease">True leaves out 0.4.7, so no release supports the game.</param>
    /// <param name="loaderMax">The highest StarMap version that KSArmory accepts, or null for no limit.</param>
    /// <param name="adoptedOutsideTheLoaders">True records the loader as a folder the player brought, outside the Loaders root.</param>
    /// <param name="keptBuild">True keeps <see cref="KeptBuild"/> in the library and lets StarMap 0.4.6 fit it, and only it.</param>
    /// <param name="keptBuildFits">False lets no release fit <see cref="KeptBuild"/>.</param>
    private static async Task<ViewModelHarness> CreateAsync(string? installed = "0.4.6", bool installedFitsTheGame = false, bool knownVersion = true, bool withGame = true, IProcessStarter? starter = null, bool noFittingRelease = false, string? loaderMax = null, bool adoptedOutsideTheLoaders = false, bool keptBuild = false, bool keptBuildFits = true)
    {
        var harness = await ViewModelHarness.CreateAsync(
            async services =>
            {
                var root = Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!;
                var game = Directory.CreateDirectory(Path.Combine(root, "Game")).FullName;
                if (withGame)
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
                var settings = services.Settings.WithGameDirectory(game);
                if (installed is not null)
                {
                    var loader = Directory.CreateDirectory(adoptedOutsideTheLoaders ? Path.Combine(root, "MyStarMap") : Path.Combine(root, "Loaders", "StarMap")).FullName;
                    // the launch file carries the version that "Use existing" reads
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "LoaderVersionFixture.dll"), Path.Combine(loader, "StarMap.exe"));
                    File.WriteAllText(Path.Combine(loader, "StarMap.dll"), string.Empty);
                    settings = settings.WithLoaderInstallation("StarMap", new LoaderInstallation(loader, knownVersion ? ModVersion.Parse(installed) : null, rawVersion: null, isAdopted: !knownVersion || adoptedOutsideTheLoaders));
                }

                if (keptBuild)
                {
                    var kept = Directory.CreateDirectory(services.Paths.GetGameBuildFolder(KeptBuild)).FullName;
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "LoaderVersionFixture.dll"), Path.Combine(kept, "KSA.dll"));
                }

                await services.SettingsRepository.SaveAsync(settings);
            },
            respond: ServeStarMap,
            editSnapshot: json => StarMapReleases(json, installedFitsTheGame, noFittingRelease, loaderMax, keptBuild && keptBuildFits),
            processStarter: starter ?? new RunningStarter());
        await harness.ViewModel.WhenLoaderUpdatesCheckedAsync();
        return harness;
    }

    [Fact]
    public async Task Load_InstalledLoaderDoesNotSupportTheGame_ShowsTheUrgentBannerForTheReleaseThatDoes()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.False(viewModel.ShowLoaderUpdateBanner);
        // 0.4.8 is newer but starts after the game, so 0.4.7 is offered
        Assert.Equal(harness.Localization.FormatLoaderUpdateUrgent("StarMap", "0.4.6", GameBuild, "0.4.7"), viewModel.LoaderUpdateBannerText);
        Assert.Equal(harness.Localization.FormatLoaderUpdateAction("StarMap", "0.4.7"), viewModel.LoaderUpdateActionText);
    }

    [Fact]
    public async Task Load_LoaderFitsTheGameAndIsTheNewestThatDoes_ShowsNoBanner()
    {
        using var harness = await CreateAsync(installed: "0.4.7");

        Assert.False(harness.ViewModel.ShowUrgentLoaderUpdateBanner);
        Assert.False(harness.ViewModel.ShowLoaderUpdateBanner);
    }

    [Fact]
    public async Task Load_VersionUnknown_ReadsItFromTheLoaderFiles()
    {
        using var harness = await CreateAsync(knownVersion: false);

        Assert.True(harness.ViewModel.ShowUrgentLoaderUpdateBanner);
        Assert.Equal(harness.Localization.FormatLoaderUpdateUrgent("StarMap", "0.4.6", GameBuild, "0.4.7"), harness.ViewModel.LoaderUpdateBannerText);
        // the read changes nothing on disk
        Assert.Null(harness.Services.Settings.LoaderInstallations["StarMap"].Version);
    }

    [Fact]
    public async Task RefreshInstalledGame_NewBuild_RatesTheLoaderAgain()
    {
        using var harness = await CreateAsync(withGame: false);
        var viewModel = harness.ViewModel;
        Assert.False(viewModel.ShowUrgentLoaderUpdateBanner);

        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(harness.Services.Settings.GameDirectoryPath!, "KSA.dll"));
        await viewModel.RefreshInstalledGameAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);
    }

    [Fact]
    public async Task UpdateLoader_InstallsTheOfferedReleaseBesideTheOldOne_RemovesTheUnusedOldOne_AndTheBannerGoes()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var directory = harness.Services.Settings.LoaderInstallations["StarMap"].DirectoryPath;

        await viewModel.UpdateLoaderCommand.ExecuteAsync(null);
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        // The flat folder of the old version moves into its own version folder, and no instance starts it after the update.
        var installation = harness.Services.Settings.LoaderInstallations["StarMap"];
        Assert.Equal(ModVersion.Parse("0.4.7"), installation.Version);
        Assert.Equal(Path.Combine(directory, "0.4.7"), installation.DirectoryPath);
        Assert.False(Directory.Exists(Path.Combine(directory, "0.4.6")));
        Assert.Null(viewModel.SetupError);
        Assert.False(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.Single(harness.Requests, uri => uri.AbsoluteUri == DownloadUrl("0.4.7"));
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == DownloadUrl("0.4.8"));
    }

    [Fact]
    public async Task UpdateLoader_WhileTheGameRuns_SaysSoAndKeepsTheLoader()
    {
        using var harness = await CreateAsync();
        harness.IsGameProcessRunning = () => true;

        await harness.ViewModel.UpdateLoaderCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.LoaderUpdateGameRunning, harness.ViewModel.SetupError);
        Assert.Equal(ModVersion.Parse("0.4.6"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == DownloadUrl("0.4.7"));
    }

    [Fact]
    public async Task NewerReleaseWhileTheInstalledOneFits_ShowsABannerThatStaysClosed()
    {
        using var harness = await CreateAsync(installedFitsTheGame: true);
        var viewModel = harness.ViewModel;

        Assert.False(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.True(viewModel.ShowLoaderUpdateBanner);
        Assert.Equal(harness.Localization.FormatLoaderUpdateAvailable("StarMap", "0.4.7"), viewModel.LoaderUpdateBannerText);

        viewModel.DismissLoaderUpdateBannerCommand.Execute(null);
        await harness.WhenIdleAsync();

        Assert.False(viewModel.ShowLoaderUpdateBanner);
        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal("StarMap 0.4.7", saved.Preferences.DismissedLoaderUpdate);
        var restarted = harness.NewViewModel(harness.Services.AppPreferences, saved.Preferences, harness.Services);
        await restarted.LoadAsync();
        await restarted.WhenLoaderUpdatesCheckedAsync();
        Assert.Equal("0.4.7", restarted.LoaderUpdate?.Offer?.Version.ToString());
        Assert.False(restarted.ShowLoaderUpdateBanner);
    }

    [Fact]
    public async Task Play_UrgentUpdate_AsksBeforeTheLaunch()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter: starter);
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.True(viewModel.IsLoaderUpdatePrompt);
        Assert.Equal(harness.Localization.FormatLaunchLoaderUpdate("StarMap", "0.4.6", GameBuild, "0.4.7"), viewModel.LoaderPromptText);
        Assert.Equal(harness.Localization.LaunchUpdateAndPlay, viewModel.LoaderPromptActionText);
        Assert.True(viewModel.ShowLoaderPromptAction);
        Assert.False(starter.Started);

        viewModel.CancelLoaderPromptCommand.Execute(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.False(viewModel.IsLoaderUpdatePrompt);
        Assert.False(starter.Started);
        Assert.Equal(ModVersion.Parse("0.4.6"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
    }

    [Fact]
    public async Task UpdateAndPlay_InstallsTheOfferThenLaunches()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter: starter);
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        await viewModel.InstallPromptedLoaderCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.Equal(ModVersion.Parse("0.4.7"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
    }

    [Fact]
    public async Task PlayAnyway_LaunchesWithTheInstalledLoader_AndTheNextPlayDoesNotAskAgain()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter: starter);
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        await viewModel.PlayAnywayCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.Equal(ModVersion.Parse("0.4.6"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == DownloadUrl("0.4.7"));

        // the instance still runs, so this launch stops there and not at the prompt
        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.NotNull(viewModel.LaunchMessage);
    }

    [Fact]
    public async Task Play_PinnedToTheRecordedVersionThatDoesNotSupportTheGame_OnlyWarnsAndOffersToChangeTheLoader()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter: starter);
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await PinAsync(harness, instance, "0.4.6");
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        // an update cannot change a pinned version, so Home only says that the pin does not fit
        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.Equal(harness.Localization.FormatLoaderPinnedIncompatible("StarMap", "0.4.6", "Main", GameBuild), viewModel.LoaderUpdateBannerText);
        Assert.Equal(harness.Localization.LoaderChangeAction, viewModel.LoaderUpdateActionText);

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.Equal(harness.Localization.FormatLaunchLoaderIncompatible("StarMap", "0.4.6", GameBuild), viewModel.LoaderPromptText);
        Assert.False(viewModel.ShowLoaderPromptAction);
        Assert.True(viewModel.ShowChangeLoaderPromptAction);
        Assert.False(starter.Started);

        await viewModel.ChangeLoaderFromPromptCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.True(viewModel.IsLoaderSettingModalOpen);

        viewModel.CancelLoaderSettingModalCommand.Execute(null);
        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);
        await viewModel.PlayAnywayCommand.ExecuteAsync(null);

        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.Equal(ModVersion.Parse("0.4.6"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
    }

    [Fact]
    public async Task InstancePage_LoaderDoesNotSupportTheGame_ShowsItAndUpdatesInPlace()
    {
        using var harness = await CreateAsync();
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.InstanceLoader!.Rows);
        Assert.Equal(InstanceLoaderState.GameIncompatible, row.State);
        Assert.Equal(harness.Localization.FormatInstanceLoaderIncompatible("0.4.6"), row.StatusText);
        Assert.True(row.IsDanger);
        Assert.Equal(harness.Localization.FormatLoaderUpdateUrgent("StarMap", "0.4.6", GameBuild, "0.4.7"), row.StatusToolTip);
        Assert.True(row.CanUpdate);
        Assert.False(row.CanInstall);
        Assert.Equal(harness.Localization.FormatSetupUpdateLoader("0.4.7"), row.UpdateText);

        await row.UpdateCommand.ExecuteAsync(null);
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        Assert.Equal(ModVersion.Parse("0.4.7"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        var updated = Assert.Single(viewModel.InstanceLoader!.Rows);
        Assert.Equal(InstanceLoaderState.Installed, updated.State);
        Assert.False(updated.CanUpdate);
    }

    [Fact]
    public async Task GameSettings_LoaderDoesNotSupportTheGame_MarksTheRowAndOffersTheReleaseThatDoes()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.LoaderVersionItems);
        Assert.Equal("StarMap 0.4.6", row.Title);
        Assert.True(row.IsIncompatible);
        Assert.Equal("0.4.7", viewModel.SelectedLoaderInstallRelease?.VersionText);
        Assert.True(viewModel.CanInstallLoader);

        await viewModel.InstallLoaderCommand.ExecuteAsync(null);

        Assert.Equal(ModVersion.Parse("0.4.7"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        Assert.Equal(["StarMap 0.4.7", "StarMap 0.4.6"], viewModel.LoaderVersionItems.Select(item => item.Title));
        Assert.True(viewModel.LoaderVersionItems[0].IsCompatible);
    }

    [Fact]
    public async Task UseExistingLoader_RecordsTheVersionItsFilesMatch()
    {
        using var harness = await CreateAsync(installed: null);
        var viewModel = harness.ViewModel;
        var loader = Directory.CreateDirectory(Path.Combine(harness.Root, "StarMap")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "LoaderVersionFixture.dll"), Path.Combine(loader, "StarMap.exe"));
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        viewModel.LoaderDirectoryInput = loader;
        await viewModel.AdoptLoaderCommand.ExecuteAsync(null);
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        var installation = harness.Services.Settings.LoaderInstallations["StarMap"];
        Assert.Equal(ModVersion.Parse("0.4.6"), installation.Version);
        Assert.True(installation.IsAdopted);
        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);
    }

    [Fact]
    public async Task Load_NoReleaseSupportsTheGame_ShowsTheErrorBannerWithoutAnAction()
    {
        using var harness = await CreateAsync(noFittingRelease: true);
        var viewModel = harness.ViewModel;

        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.False(viewModel.ShowLoaderUpdateBanner);
        Assert.Equal(harness.Localization.FormatLoaderUpdateNone("StarMap", "0.4.6", GameBuild), viewModel.LoaderUpdateBannerText);
        Assert.Null(viewModel.LoaderUpdateActionText);
    }

    [Fact]
    public async Task Load_InstalledReleaseStartsAfterTheGame_SaysToUpdateKsa()
    {
        using var harness = await CreateAsync(installed: "0.4.8");

        Assert.True(harness.ViewModel.ShowUrgentLoaderUpdateBanner);
        Assert.Equal(harness.Localization.FormatLoaderNeedsNewerGame("StarMap", "0.4.8", GameBuild), harness.ViewModel.LoaderUpdateBannerText);
        Assert.Null(harness.ViewModel.LoaderUpdateActionText);
    }

    [Fact]
    public async Task Banners_HideWhileASetupTaskRuns()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.IsSetupBusy = true;

        Assert.False(viewModel.ShowUrgentLoaderUpdateBanner);
        viewModel.IsSetupBusy = false;
        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);
    }

    [Fact]
    public async Task Load_TheActiveInstanceRejectsThePlainUpdate_ShowsNoBannerUntilAnotherInstanceIsActive()
    {
        using var harness = await CreateAsync(installedFitsTheGame: true, loaderMax: "0.4.6");
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        var viewModel = harness.ViewModel;

        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        Assert.Equal("0.4.7", viewModel.LoaderUpdateFor("StarMap")?.Offer?.Version.ToString());
        Assert.False(viewModel.ShowLoaderUpdateBanner);

        // an update goes beside the installed version, so the banner rates only the instance that Home starts
        await viewModel.ActivateInstanceAsync(other.InstanceId);
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        Assert.True(viewModel.ShowLoaderUpdateBanner);
        Assert.Equal(harness.Localization.FormatLoaderUpdateAvailable("StarMap", "0.4.7"), viewModel.LoaderUpdateBannerText);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "0.4.6")]
    public async Task Play_NoReleaseFitsTheGameAndTheMods_OnlyWarns(bool noFittingRelease, string? loaderMax)
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter: starter, noFittingRelease: noFittingRelease, loaderMax: loaderMax);
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.False(viewModel.ShowLoaderPromptAction);
        Assert.Equal(harness.Localization.FormatLaunchLoaderIncompatible("StarMap", "0.4.6", GameBuild), viewModel.LoaderPromptText);
        Assert.False(starter.Started);

        await viewModel.InstallPromptedLoaderCommand.ExecuteAsync(null);
        Assert.Equal(ModVersion.Parse("0.4.6"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);

        await viewModel.PlayAnywayCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.Equal(ModVersion.Parse("0.4.6"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == DownloadUrl("0.4.7"));
    }

    [Fact]
    public async Task InstancePage_NoReleaseSupportsTheGame_NamesTheStateAndLinksNowhere()
    {
        using var harness = await CreateAsync(noFittingRelease: true);
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.InstanceLoader!.Rows);
        Assert.Equal(InstanceLoaderState.GameIncompatible, row.State);
        Assert.False(row.CanUpdate);
        Assert.False(row.CanInstall);
        Assert.Equal(harness.Localization.FormatLoaderUpdateNone("StarMap", "0.4.6", GameBuild), row.StatusToolTip);
    }

    [Fact]
    public async Task GameSettings_InstallWhileTheGameRuns_SaysSoAndKeepsTheLoader()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        harness.IsGameProcessRunning = () => true;

        await viewModel.InstallLoaderCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.LoaderUpdateGameRunning, viewModel.SetupError);
        Assert.Equal(ModVersion.Parse("0.4.6"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsoluteUri == DownloadUrl("0.4.7"));
    }

    [Fact]
    public async Task GameSettings_RemoveAVersion_OnlyWhenNoInstanceStartsItAndOnlyThatFolder()
    {
        var (harness, viewModel, pinned) = await CreateWithTwoVersionsAsync();
        using var _ = harness;
        var unused = viewModel.LoaderVersionItems.Single(item => item.Title == "StarMap 0.4.7");
        var used = viewModel.LoaderVersionItems.Single(item => item.Title == "StarMap 0.4.6");
        var loaders = Path.Combine(harness.Services.Paths.GetLoadersRoot(), "StarMap");

        Assert.False(used.CanRemove);
        Assert.Equal(harness.Localization.FormatSettingsLoaderInUse(pinned.Name), used.RemoveToolTip);
        Assert.Equal(harness.Localization.FormatSettingsLoaderUsedBy(pinned.Name), used.UsedByText);
        Assert.True(unused.CanRemove);
        Assert.Equal(harness.Localization.SettingsLoaderUnused, unused.UsedByText);

        await unused.RemoveCommand.ExecuteAsync(null);
        Assert.True(unused.IsConfirmingRemove);
        await unused.RemoveCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SetupError);
        Assert.Equal(harness.Localization.FormatSettingsLoaderRemoved("StarMap", "0.4.7"), viewModel.SetupMessage);
        Assert.False(Directory.Exists(Path.Combine(loaders, "0.4.7")));
        Assert.True(File.Exists(Path.Combine(loaders, "0.4.6", "StarMap.exe")));
        Assert.Equal("StarMap 0.4.6", Assert.Single(viewModel.LoaderVersionItems).Title);
        Assert.Equal(ModVersion.Parse("0.4.6"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
    }

    [Fact]
    public async Task GameSettings_BoreaFolderRecordedAsAdopted_OffersRemoveWithAConfirmNotForget()
    {
        // Borea 0.5.1 recorded its own Loaders/StarMap as adopted after "Use existing", and the uninstaller deletes that folder
        using var harness = await CreateAsync(knownVersion: false);
        var viewModel = harness.ViewModel;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        var row = Assert.Single(viewModel.LoaderVersionItems);

        Assert.False(row.IsAdopted);
        Assert.Equal(harness.Localization.ContentRemove, row.RemoveText);
        Assert.Equal(string.Empty, viewModel.LoaderDirectoryInput);

        await row.RemoveCommand.ExecuteAsync(null);

        Assert.True(row.IsConfirmingRemove);
        Assert.True(Directory.Exists(row.Version.DirectoryPath));
    }

    [Fact]
    public async Task GameSettings_RemoveWhileTheGameRuns_SaysSoAndKeepsTheFolder()
    {
        var (harness, viewModel, _) = await CreateWithTwoVersionsAsync();
        using var _ = harness;
        var unused = viewModel.LoaderVersionItems.Single(item => item.Title == "StarMap 0.4.7");
        harness.IsGameProcessRunning = () => true;

        await unused.RemoveCommand.ExecuteAsync(null);
        await unused.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.LoaderUpdateGameRunning, viewModel.SetupError);
        Assert.True(Directory.Exists(unused.Version.DirectoryPath));
        Assert.Equal(2, (await harness.Services.LoaderVersions.ListAsync()).Count);
    }

    /// <summary>StarMap 0.4.6, which an instance pins, and 0.4.7 beside it from Settings, which no instance starts.</summary>
    private static async Task<(ViewModelHarness Harness, MainViewModel ViewModel, Instance Pinned)> CreateWithTwoVersionsAsync()
    {
        var harness = await CreateAsync();
        var pinned = (await harness.Services.Instances.CreateAsync("Pinned", InstanceSource.Custom.Value)).Instance;
        await PinAsync(harness, pinned, "0.4.6");
        var viewModel = harness.ViewModel;
        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);
        await viewModel.InstallLoaderCommand.ExecuteAsync(null);
        Assert.Null(viewModel.SetupError);
        Assert.Equal(2, viewModel.LoaderVersionItems.Count);
        return (harness, viewModel, pinned);
    }

    [Fact]
    public async Task UpdateLoader_OneInstallAndNoSetting_ShowsTheSameBannerAndLeavesOnlyTheNewVersion()
    {
        using var harness = await CreateAsync();
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        var directory = harness.Services.Settings.LoaderInstallations["StarMap"].DirectoryPath;

        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.Equal(harness.Localization.FormatLoaderUpdateUrgent("StarMap", "0.4.6", GameBuild, "0.4.7"), viewModel.LoaderUpdateBannerText);
        Assert.Equal(harness.Localization.FormatLoaderUpdateAction("StarMap", "0.4.7"), viewModel.LoaderUpdateActionText);

        await viewModel.UpdateLoaderCommand.ExecuteAsync(null);
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        Assert.Equal([Path.Combine(directory, "0.4.7")], Directory.GetFileSystemEntries(directory));
        Assert.Equal(LoaderSetting.Automatic, (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Loader);
        Assert.False(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.False(viewModel.ShowLoaderUpdateBanner);
    }

    [Fact]
    public async Task UpdateLoader_AnotherInstancePinsTheOldVersion_KeepsItAndThatInstanceStillStartsIt()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter: starter);
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        var pinned = (await harness.Services.Instances.CreateAsync("Pinned", InstanceSource.Custom.Value)).Instance;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: false, into: pinned);
        await PinAsync(harness, pinned, "0.4.6");
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(pinned.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        var directory = harness.Services.Settings.LoaderInstallations["StarMap"].DirectoryPath;

        await viewModel.UpdateLoaderCommand.ExecuteAsync(null);
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        // the pinned instance still starts 0.4.6, so the update keeps that version folder
        var old = Path.Combine(directory, "0.4.6");
        Assert.True(Directory.Exists(old));
        var installed = await harness.Services.LoaderVersions.ListAsync();
        Assert.NotNull(InstalledLoaderVersion.Find(installed, "StarMap", ModVersion.Parse("0.4.6")));
        Assert.NotNull(InstalledLoaderVersion.Find(installed, "StarMap", ModVersion.Parse("0.4.7")));
        Assert.DoesNotContain(harness.Services.Log.ReadRecentLines(200), line => line.Contains("Removed StarMap 0.4.6", StringComparison.Ordinal));

        await viewModel.Instances.Single(item => item.InstanceId == pinned.InstanceId).OpenCommand.ExecuteAsync(null);
        await viewModel.PlayCommand.ExecuteAsync(null);
        await viewModel.PlayAnywayCommand.ExecuteAsync(null);

        Assert.True(harness.Services.Launcher.IsRunning(pinned.InstanceId));
        Assert.Equal(old, starter.Plan?.WorkingDirectory);
    }

    [Fact]
    public async Task ActiveInstancePinsAMissingVersion_ShowsTheErrorBannerAndPlayOffersToInstallThePin()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(installed: "0.4.7", starter: starter);
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await PinAsync(harness, instance, "0.4.8");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.False(viewModel.ShowLoaderUpdateBanner);
        Assert.Equal(harness.Localization.FormatLoaderPinnedMissing("StarMap", "0.4.8", "Main"), viewModel.LoaderUpdateBannerText);
        Assert.Equal(harness.Localization.FormatLoaderInstallAction("StarMap", "0.4.8"), viewModel.LoaderUpdateActionText);

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.False(viewModel.IsLoaderUpdatePrompt);
        Assert.Equal(harness.Localization.FormatLaunchInstallLoader("StarMap 0.4.8"), viewModel.LoaderPromptText);
        Assert.True(viewModel.ShowLoaderPromptAction);

        await viewModel.InstallPromptedLoaderCommand.ExecuteAsync(null);

        // the pin goes beside 0.4.7, and because 0.4.8 starts after the game the launch then only warns
        var installed = await harness.Services.LoaderVersions.ListAsync();
        Assert.NotNull(InstalledLoaderVersion.Find(installed, "StarMap", ModVersion.Parse("0.4.7")));
        Assert.NotNull(InstalledLoaderVersion.Find(installed, "StarMap", ModVersion.Parse("0.4.8")));
        Assert.Equal(harness.Localization.FormatLaunchLoaderIncompatible("StarMap", "0.4.8", GameBuild), viewModel.LoaderPromptText);
        Assert.True(viewModel.ShowChangeLoaderPromptAction);
        Assert.False(starter.Started);
    }

    [Fact]
    public async Task PlayAnyway_OnOneInstance_StillAsksOnAnother()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter: starter);
        var first = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        var second = (await harness.Services.Instances.CreateAsync("Second", InstanceSource.Custom.Value)).Instance;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: false, into: second);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(first.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);
        await viewModel.PlayAnywayCommand.ExecuteAsync(null);
        Assert.True(harness.Services.Launcher.IsRunning(first.InstanceId));

        await viewModel.Instances.Single(item => item.InstanceId == second.InstanceId).OpenCommand.ExecuteAsync(null);
        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.Equal(harness.Localization.FormatLaunchLoaderUpdate("StarMap", "0.4.6", GameBuild, "0.4.7"), viewModel.LoaderPromptText);
        Assert.False(harness.Services.Launcher.IsRunning(second.InstanceId));
    }

    [Fact]
    public async Task Play_PinnedToAReleaseTheIndexDoesNotList_OffersAutomaticWhichSavesAndPlays()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(installed: "0.4.7", starter: starter);
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await PinAsync(harness, instance, "0.4.5");
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        // Borea cannot install the pin, so the banner leads to the setting
        Assert.Equal(harness.Localization.FormatLoaderPinnedMissing("StarMap", "0.4.5", "Main"), viewModel.LoaderUpdateBannerText);
        Assert.Equal(harness.Localization.LoaderChangeAction, viewModel.LoaderUpdateActionText);

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.Equal(harness.Localization.FormatLaunchPinnedLoaderNotInstallable("StarMap", "0.4.5"), viewModel.LoaderPromptText);
        Assert.True(viewModel.ShowUseAutomaticLoaderAction);
        Assert.False(viewModel.ShowLoaderPromptAction);
        Assert.False(viewModel.IsLoaderUpdatePrompt);

        await viewModel.UseAutomaticLoaderCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.Equal(LoaderSetting.Automatic, (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Loader);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
    }

    [Fact]
    public async Task UpdateLoader_AdoptedFolderOutsideTheLoadersRoot_InstallsBesideItAndPlayStartsTheNewVersion()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter: starter, adoptedOutsideTheLoaders: true);
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        var adopted = harness.Services.Settings.LoaderInstallations["StarMap"].DirectoryPath;
        var adoptedFiles = Directory.GetFileSystemEntries(adopted).Order(StringComparer.Ordinal).ToList();
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);

        await viewModel.UpdateLoaderCommand.ExecuteAsync(null);
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        Assert.False(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.False(viewModel.ShowLoaderUpdateBanner);
        var updated = harness.Services.Paths.GetLoaderVersionDirectoryPath("StarMap", ModVersion.Parse("0.4.7"));
        Assert.True(File.Exists(Path.Combine(updated, "StarMap.exe")));
        Assert.Equal(adopted, harness.Services.Settings.LoaderInstallations["StarMap"].DirectoryPath);
        Assert.Equal(adoptedFiles, Directory.GetFileSystemEntries(adopted).Order(StringComparer.Ordinal).ToList());

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.Equal(updated, starter.Plan?.WorkingDirectory);
    }

    [Fact]
    public async Task Play_OnAKeptBuildWithOnlyANewerLoader_OffersTheOlderReleaseThatFitsAndInstallsItBeside()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(installed: "0.4.7", starter: starter, keptBuild: true);
        var instance = await KeepBuildActiveAsync(harness);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.Equal(harness.Localization.FormatLoaderUpdateUrgentKept("Kept", "0.4.6.0", "StarMap", "0.4.7", "0.4.6"), viewModel.LoaderUpdateBannerText);
        Assert.Equal(harness.Localization.FormatLoaderInstallAction("StarMap", "0.4.6"), viewModel.LoaderUpdateActionText);

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatLaunchLoaderInstallOlder("StarMap", "0.4.7", "0.4.6.0", "0.4.6"), viewModel.LoaderPromptText);
        Assert.Equal(harness.Localization.LaunchInstallAndPlay, viewModel.LoaderPromptActionText);

        await viewModel.InstallPromptedLoaderCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SetupError);
        var versions = (await harness.Services.LoaderVersions.ListAsync()).Select(version => version.Version?.ToString()).Order(StringComparer.Ordinal);
        Assert.Equal(["0.4.6", "0.4.7"], versions);
        Assert.Equal(ModVersion.Parse("0.4.7"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.Equal(harness.Services.Paths.GetLoaderVersionDirectoryPath("StarMap", ModVersion.Parse("0.4.6")), starter.Plan?.WorkingDirectory);
    }

    [Fact]
    public async Task Play_OnAKeptBuildWithARecordedLoaderOfUnknownVersion_StartsWithoutTheUpdateOfTheSettingsBuild()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(knownVersion: false, starter: starter, keptBuild: true);
        var instance = await KeepBuildActiveAsync(harness);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        // the update check rates the recorded folder against the build of the settings, which 0.4.6 does not fit
        Assert.True(viewModel.LoaderUpdateFor("StarMap")?.IsUrgent);
        // the index offers 0.4.6 for the kept build, which the recorded folder already holds
        Assert.Null(viewModel.LoaderUpdate);

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
    }

    [Fact]
    public async Task Play_OnAKeptBuildThatTheRecordedLoaderOfUnknownVersionDoesNotFit_AsksAndNamesTheKeptBuild()
    {
        using var harness = await CreateAsync(knownVersion: false, keptBuild: true, keptBuildFits: false);
        var instance = await KeepBuildActiveAsync(harness);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.Equal(harness.Localization.FormatLaunchLoaderIncompatible("StarMap", "0.4.6", "0.4.6.0"), viewModel.LoaderPromptText);
        Assert.False(harness.Services.Launcher.IsRunning(instance.InstanceId));
    }

    [Fact]
    public async Task InstancePage_OnAKeptBuildWithOnlyANewerLoader_ExplainsItAgainstTheKeptBuild()
    {
        using var harness = await CreateAsync(installed: "0.4.7", keptBuild: true);
        await KeepBuildActiveAsync(harness);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.InstanceLoader!.Rows);
        Assert.Equal(InstanceLoaderState.GameIncompatible, row.State);
        Assert.Equal(harness.Localization.FormatLoaderUpdateUrgent("StarMap", "0.4.7", "0.4.6.0", "0.4.6"), row.StatusToolTip);
        Assert.Equal(harness.Localization.FormatInstanceLoaderInstall("0.4.6"), row.UpdateText);
    }

    [Fact]
    public async Task BuildChange_OfTheActiveInstance_RatesTheLoaderAgainForTheBuildItStarts()
    {
        using var harness = await CreateAsync(installed: "0.4.7", keptBuild: true);
        var instance = (await harness.Services.Instances.CreateAsync("Kept", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();
        Assert.False(viewModel.ShowUrgentLoaderUpdateBanner);

        // A keep and a use-current write the build into the record and reload the instances, as this activation does.
        // The commands share the static gate of the keeper with a keep test that runs at the same time: a keep waits
        // for it, and a use-current is refused as busy.
        await SetGameBuildAsync(KeptBuild);
        Assert.True(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.Equal(harness.Localization.FormatLoaderUpdateUrgentKept("Kept", "0.4.6.0", "StarMap", "0.4.7", "0.4.6"), viewModel.LoaderUpdateBannerText);

        // a rename rates nothing again, and the banner names the instance as it is called now
        await viewModel.RenameInstanceAsync(viewModel.ActiveInstance!, "Career");
        Assert.Equal(harness.Localization.FormatLoaderUpdateUrgentKept("Career", "0.4.6.0", "StarMap", "0.4.7", "0.4.6"), viewModel.LoaderUpdateBannerText);

        await SetGameBuildAsync(null);
        Assert.False(viewModel.ShowUrgentLoaderUpdateBanner);
        Assert.Null(viewModel.LoaderUpdateBannerText);

        async Task SetGameBuildAsync(GameVersion? build)
        {
            await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved =>
            {
                saved.SetGameBuild(build);
                return true;
            });
            await viewModel.ActivateInstanceAsync(instance.InstanceId);
            await viewModel.WhenLoaderUpdatesCheckedAsync();
        }
    }

    /// <summary>A new instance on <see cref="KeptBuild"/>, which Home starts.</summary>
    private static async Task<Instance> KeepBuildActiveAsync(ViewModelHarness harness)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Kept", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.UpdateAsync(instance.InstanceId, saved =>
        {
            saved.SetGameBuild(KeptBuild);
            return true;
        });
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        return instance;
    }

    private static Task PinAsync(ViewModelHarness harness, Instance instance, string version) =>
        harness.Services.Instances.UpdateAsync(instance.InstanceId, saved =>
        {
            saved.SetLoader(LoaderSetting.Pinned("StarMap", ModVersion.Parse(version)));
            return true;
        });

    /// <summary>
    /// Gives 0.4.6 a game_max below the game unless it fits, and adds 0.4.7 for the game and 0.4.8 for a later build.
    /// With <paramref name="keptBuild"/>, 0.4.6 also starts at <see cref="KeptBuild"/> and downloads like the others.
    /// </summary>
    private static string StarMapReleases(string json, bool installedFitsTheGame, bool noFittingRelease, string? loaderMax, bool keptBuild = false)
    {
        var root = JsonNode.Parse(json)!;
        var listings = root["listings"]!.AsArray();
        var listing = listings.Single(node => (string?)node!["id"] == "StarMap")!;
        var releases = listing["releases"]!.AsArray();
        var first = releases.Single()!;
        if (!installedFitsTheGame)
        {
            listing["authored"]!["compatibility"]!["game_min"] = "2026.7.20.5000";
            first["game_min"] = "2026.7.20.5000";
            first["game_min_revision"] = 5000;
            first["game_max"] = "2026.8.1.5100";
            first["game_max_revision"] = 5100;
        }

        if (keptBuild)
        {
            first["game_min"] = KeptBuild.ToString();
            first["game_min_revision"] = KeptBuild.Revision;
            first["download"]!["url"] = DownloadUrl("0.4.6");
            first["download"]!["sha256"] = Convert.ToHexString(SHA256.HashData(StarMapArchive));
            first["download"]!["size"] = StarMapArchive.Length;
        }

        if (!noFittingRelease)
            releases.Add(Release(first, "0.4.7", "2026.8.3.5117"));
        releases.Add(Release(first, "0.4.8", "2026.8.10.5200"));

        if (loaderMax is not null)
        {
            foreach (var release in listings.Single(node => (string?)node!["id"] == "KSArmory")!["releases"]!.AsArray())
                release!["loader"]!["max"] = loaderMax;
        }

        return root.ToJsonString();

        static JsonNode Release(JsonNode from, string version, string gameMin)
        {
            var release = from.DeepClone();
            release["version"] = version;
            release["game_min"] = gameMin;
            release["game_min_revision"] = int.Parse(gameMin[(gameMin.LastIndexOf('.') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            release.AsObject().Remove("game_max");
            release.AsObject().Remove("game_max_revision");
            release["changelog"] = $"https://github.com/StarMapLoader/StarMap/releases/tag/{version}";
            release["download"]!["url"] = DownloadUrl(version);
            release["download"]!["sha256"] = Convert.ToHexString(SHA256.HashData(StarMapArchive));
            release["download"]!["size"] = StarMapArchive.Length;
            return release;
        }
    }

    private static HttpResponseMessage? ServeStarMap(HttpRequestMessage request) =>
        request.RequestUri?.AbsoluteUri is { } uri && (uri == DownloadUrl("0.4.6") || uri == DownloadUrl("0.4.7") || uri == DownloadUrl("0.4.8"))
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(StarMapArchive) }
            : null;

    /// <summary>The app host and the assembly of a StarMap release.</summary>
    private static byte[] CreateStarMapArchive()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("StarMap.exe");
            archive.CreateEntry("StarMap.dll");
        }

        return buffer.ToArray();
    }

    /// <summary>Hands out a loader process that keeps running and writes <see cref="GameLog"/> once the start is watched, like a game that started.</summary>
    private sealed class RunningStarter : IProcessStarter
    {
        public string? GameLog { get; set; }

        public bool Started { get; private set; }

        /// <summary>The last plan that was started, or null.</summary>
        public LaunchPlan? Plan { get; private set; }

        public IStartedProcess Start(LaunchPlan plan)
        {
            Started = true;
            Plan = plan;
            return new RunningProcess(this);
        }

        private sealed class RunningProcess(RunningStarter owner) : IStartedProcess
        {
            public int Id => 4245;

            public bool HasExited => false;

            public int? ExitCode => null;

            public IReadOnlyList<string> RecentOutput => [];

            public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                if (owner.GameLog is { } log)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                    File.WriteAllText(log, "KSA started.");
                }

                return Task.FromResult(false);
            }

            public void Dispose()
            {
            }
        }
    }
}
