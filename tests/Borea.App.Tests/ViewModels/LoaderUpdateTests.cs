using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Composition;
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

    private static readonly byte[] StarMapArchive = CreateStarMapArchive();

    private static string DownloadUrl(string version) => $"https://github.com/StarMapLoader/StarMap/releases/download/{version}/StarMap-{version}.zip";

    /// <param name="installed">The recorded StarMap release, or null to record none.</param>
    /// <param name="installedFitsTheGame">False gives 0.4.6 a game_max below the game.</param>
    /// <param name="knownVersion">False records the loader without a version, as "Use existing" did.</param>
    /// <param name="noFittingRelease">True leaves out 0.4.7, so no release supports the game.</param>
    /// <param name="loaderMax">The highest StarMap version that KSArmory accepts, or null for no limit.</param>
    private static async Task<ViewModelHarness> CreateAsync(string? installed = "0.4.6", bool installedFitsTheGame = false, bool knownVersion = true, bool withGame = true, IProcessStarter? starter = null, bool noFittingRelease = false, string? loaderMax = null)
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
                    var loader = Directory.CreateDirectory(Path.Combine(root, "Loaders", "StarMap")).FullName;
                    // the launch file carries the version that "Use existing" reads
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "LoaderVersionFixture.dll"), Path.Combine(loader, "StarMap.exe"));
                    File.WriteAllText(Path.Combine(loader, "StarMap.dll"), string.Empty);
                    settings = settings.WithLoaderInstallation("StarMap", new LoaderInstallation(loader, knownVersion ? ModVersion.Parse(installed) : null, rawVersion: null, isAdopted: !knownVersion));
                }

                await services.SettingsRepository.SaveAsync(settings);
            },
            respond: ServeStarMap,
            editSnapshot: json => StarMapReleases(json, installedFitsTheGame, noFittingRelease, loaderMax),
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
    public async Task UpdateLoader_InstallsTheOfferedReleaseInPlace_AndTheBannerGoes()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var directory = harness.Services.Settings.LoaderInstallations["StarMap"].DirectoryPath;

        await viewModel.UpdateLoaderCommand.ExecuteAsync(null);
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        var installation = harness.Services.Settings.LoaderInstallations["StarMap"];
        Assert.Equal(ModVersion.Parse("0.4.7"), installation.Version);
        Assert.Equal(directory, installation.DirectoryPath);
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
        Assert.False(row.CanFix);
        Assert.Equal(harness.Localization.FormatSetupUpdateLoader("0.4.7"), row.UpdateText);

        await row.UpdateCommand.ExecuteAsync(null);
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        Assert.Equal(ModVersion.Parse("0.4.7"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        var updated = Assert.Single(viewModel.InstanceLoader!.Rows);
        Assert.Equal(InstanceLoaderState.Installed, updated.State);
        Assert.False(updated.CanUpdate);
    }

    [Fact]
    public async Task GameSettings_LoaderDoesNotSupportTheGame_MarksTheChipAndOffersTheReleaseThatDoes()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        await viewModel.ShowGameSettingsCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsInstalledLoaderIncompatible);
        Assert.Equal(harness.Localization.FormatSetupLoaderIncompatible("0.4.6", GameBuild), viewModel.InstalledLoaderText);
        Assert.Equal(harness.Localization.FormatSetupUpdateLoader("0.4.7"), viewModel.LoaderInstallActionText);
        Assert.True(viewModel.CanInstallLoader);

        await viewModel.InstallLoaderCommand.ExecuteAsync(null);

        Assert.Equal(ModVersion.Parse("0.4.7"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        Assert.False(viewModel.IsInstalledLoaderIncompatible);
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
    public async Task Load_AnInstanceRejectsThePlainUpdate_ShowsNoBanner()
    {
        using var harness = await CreateAsync(installedFitsTheGame: true, loaderMax: "0.4.6");
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        var viewModel = harness.ViewModel;

        await viewModel.LoadAsync();
        await viewModel.WhenLoaderUpdatesCheckedAsync();

        Assert.Equal("0.4.7", viewModel.LoaderUpdateFor("StarMap")?.Offer?.Version.ToString());
        Assert.False(viewModel.ShowLoaderUpdateBanner);
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
        Assert.False(row.CanFix);
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

    /// <summary>Gives 0.4.6 a game_max below the game unless it fits, and adds 0.4.7 for the game and 0.4.8 for a later build.</summary>
    private static string StarMapReleases(string json, bool installedFitsTheGame, bool noFittingRelease, string? loaderMax)
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
        request.RequestUri?.AbsoluteUri is { } uri && (uri == DownloadUrl("0.4.7") || uri == DownloadUrl("0.4.8"))
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

        public IStartedProcess Start(LaunchPlan plan)
        {
            Started = true;
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
