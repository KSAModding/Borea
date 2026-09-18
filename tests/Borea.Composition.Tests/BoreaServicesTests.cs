using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Logging;
using Borea.Core.ModLoaders;
using Borea.Storage.Launch;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Network.GitHub;
using Borea.Network.Index;
using Borea.Network.Planning;
using Borea.Network.Sources;
using Borea.Storage.Game;
using Borea.Storage.Index;
using Borea.Storage.Instances;
using Borea.Storage.ModLoaders;
using Borea.Storage.Mods;
using Borea.Storage.Paths;
using Borea.Storage.Preferences;
using Borea.Storage.Settings;

namespace Borea.Composition.Tests;

public sealed class BoreaServicesTests : IDisposable
{
    private const string GamePath = @"C:\Games\KSA";
    private const string StarMapPath = @"C:\Games\StarMap";
    private const string FixtureArchiveSha256 = "AD14E4FE8111F4DAE8406D50459B7E5C42D58F1E01F549636946922CF72AE9E6";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    [Fact]
    public async Task BuildAsync_NoSettingsFile_KnowsNoGameAndNoLoader()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.Null(services.Settings.GameDirectoryPath);
        Assert.Empty(services.Settings.LoaderInstallations);
        Assert.Null(services.Paths.GetGameDirectoryPath());
        Assert.Null(services.Paths.GetLoaderDirectoryPath("StarMap"));
    }

    [Fact]
    public async Task BuildAsync_NoSettingsFile_WritesNothing()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.False(Directory.Exists(_tempRoot));
    }

    [Fact]
    public async Task BuildAsync_SettingsNamingNoGame_KnowsTheLoaderOnly()
    {
        await SaveAsync(new BoreaSettings(null, LoaderAt(StarMapPath)));

        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.Null(services.Paths.GetGameDirectoryPath());
        Assert.Equal(StarMapPath, services.Paths.GetLoaderDirectoryPath("StarMap"));
    }

    [Fact]
    public async Task BuildAsync_FullSettings_KnowsTheGameAndTheLoader()
    {
        await SaveAsync(new BoreaSettings(GamePath, LoaderAt(StarMapPath)));

        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.Equal(GamePath, services.Settings.GameDirectoryPath);
        Assert.Equal(GamePath, services.Paths.GetGameDirectoryPath());
        Assert.Equal(StarMapPath, services.Paths.GetLoaderDirectoryPath("StarMap"));
    }

    [Fact]
    public async Task BuildAsync_RootsBoreaPathsAtTheGivenRoot()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.StartsWith(_tempRoot, services.Paths.GetBoreaSettingsPath());
        Assert.StartsWith(_tempRoot, services.Paths.GetAppPreferencesPath());
        Assert.StartsWith(_tempRoot, services.Paths.GetInstancesRoot());
        Assert.StartsWith(_tempRoot, services.Paths.GetImageCacheFolder());
    }

    [Fact]
    public async Task BuildAsync_SavedLibraryFolder_RootsOnlyInstancesAndBackupsThere()
    {
        var library = Path.Combine(_tempRoot, "Library");
        await SaveAsync(new BoreaSettings(gameDirectoryPath: null, libraryFolderPath: library));

        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.Equal(Path.Combine(library, "Instances"), services.Paths.GetInstancesRoot());
        Assert.Equal(Path.Combine(library, "Backups"), services.Paths.GetBackupsRoot());
        Assert.Equal(Path.Combine(_tempRoot, "borea-settings.toml"), services.Paths.GetBoreaSettingsPath());
        Assert.Equal(Path.Combine(_tempRoot, "active-instance.toml"), services.Paths.GetActiveInstancePointerPath());
        Assert.StartsWith(_tempRoot + Path.DirectorySeparatorChar + "Loaders", services.Paths.GetLoadersRoot());
    }

    [Fact]
    public async Task Images_LoadingFromAuthorHostsOff_ServesNoUncachedImageAndWritesNothing()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);
        var icon = new IconImage("https://images.example/icon.png", new string('A', 64), 512, 512, 1000);

        var result = await services.Images.GetAsync(icon, loadFromAuthorHosts: false);

        Assert.Equal(ContentImageFailure.DisabledByPreference, result.Failure);
        Assert.False(Directory.Exists(_tempRoot));
    }

    [Fact]
    public async Task BuildAsync_SettingsFileThatDoesNotLoad_Throws()
    {
        // Loader ids that collide by case are rejected by BoreaSettings, and a
        // build must surface that instead of starting with empty settings.
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(SettingsPath, """
            [LoaderInstallations.StarMap]
            DirectoryPath = 'C:\Games\StarMap'
            IsAdopted = true

            [LoaderInstallations.starmap]
            DirectoryPath = 'C:\Games\Other'
            IsAdopted = true
            """);

        await Assert.ThrowsAsync<ArgumentException>(() => BoreaServices.BuildAsync(_tempRoot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildAsync_WhitespaceRoot_ThrowsArgumentException(string boreaRoot)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => BoreaServices.BuildAsync(boreaRoot));
    }

    [Fact]
    public async Task SettingsRepository_WritesWhereThePathsPoint_AndTheNextBuildReadsIt()
    {
        using (var services = await BoreaServices.BuildAsync(_tempRoot))
        {
            await services.SettingsRepository.SaveAsync(new BoreaSettings(GamePath));

            Assert.True(File.Exists(services.Paths.GetBoreaSettingsPath()));
        }

        using var rebuilt = await BoreaServices.BuildAsync(_tempRoot);

        Assert.Equal(GamePath, rebuilt.Paths.GetGameDirectoryPath());
    }

    [Fact]
    public async Task Mods_IsTheCompositeRepository()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        var channelled = Assert.IsType<ReleaseChannelModRepository>(services.Mods);
        Assert.IsType<CompositeModRepository>(channelled.Inner);
        Assert.Equal(ReleaseChannel.Stable, channelled.Channel);
        Assert.IsType<FileLoaderInstaller>(services.LoaderInstaller);
        Assert.IsType<FileLoaderAdopter>(services.LoaderAdopter);
        Assert.IsType<FileLoaderUninstaller>(services.LoaderUninstaller);
        Assert.IsType<GameDirectoryChanger>(services.GameDirectoryChanger);
        Assert.IsType<LibraryFolderChanger>(services.LibraryFolderChanger);
        Assert.IsType<LoaderLauncher>(Assert.IsType<LastPlayedLauncher>(Assert.IsType<LoggingLauncher>(services.Launcher).Inner).Inner);
        Assert.IsType<FileModUninstaller>(Assert.IsType<LoggingModUninstaller>(services.Uninstaller).Inner);
        Assert.IsType<FileModInstaller>(Assert.IsType<LoggingModInstaller>(services.Installer).Inner);
        Assert.IsType<FileModReplacer>(Assert.IsType<LoggingModReplacer>(services.Replacer).Inner);
        Assert.IsType<RepositoryInstallPlanner>(Assert.IsType<LoggingInstallPlanner>(services.InstallPlanner).Inner);
        Assert.IsType<FileGameLogReader>(services.GameLog);
        Assert.IsType<FilePlaytimeService>(services.Playtime);
        Assert.IsType<SharedProfileLauncher>(Assert.IsType<LoggingSharedProfileLauncher>(services.SharedProfileLauncher).Inner);
        Assert.IsType<FileSharedProfileImporter>(services.SharedProfileImporter);
        Assert.IsType<FileGameDataReader>(services.GameData);
        Assert.IsType<FileGameSaveStore>(services.GameSaves);
    }

    [Fact]
    public async Task SavedChannel_ReachesTheRepositoriesAndThePlanner()
    {
        using (var services = await BoreaServices.BuildAsync(_tempRoot))
            await services.SettingsRepository.SaveAsync(new BoreaSettings(null, releaseChannel: ReleaseChannel.Testing));

        using var rebuilt = await BoreaServices.BuildAsync(_tempRoot);
        var available = new[] { ChannelRelease("1.0.0", ReleaseStatus.Stable), ChannelRelease("1.1.0-beta.1", ReleaseStatus.Testing), ChannelRelease("1.2.0-dev.1", ReleaseStatus.Dev) };
        var request = new Borea.Core.Planning.InstallPlanningRequest(
            new Instance("Test", InstanceSource.Custom.Value),
            [new Borea.Core.Planning.RequestedMod(available[2], InstallReason.Manual, Exact: false)],
            new ReleaseListRepository(available));
        var plan = await rebuilt.InstallPlanner.PlanAsync(request);

        Assert.Equal(ReleaseChannel.Testing, rebuilt.Settings.ReleaseChannel);
        Assert.Equal(ReleaseChannel.Testing, Assert.IsType<ReleaseChannelModRepository>(rebuilt.Mods).Channel);
        Assert.Equal(ReleaseChannel.Testing, Assert.IsType<ReleaseChannelModRepository>(rebuilt.ReadOnlyMods).Channel);
        Assert.Equal(ModVersion.Parse("1.1.0-beta.1"), Assert.Single(plan.Operations).Release.Version);
    }

    [Fact]
    public async Task Mods_LatestReleaseOfAFallbackSource_FollowsTheSavedChannel()
    {
        using (var services = await BoreaServices.BuildAsync(_tempRoot))
            await services.SettingsRepository.SaveAsync(new BoreaSettings(null, releaseChannel: ReleaseChannel.Testing));
        var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath);
        var available = new[] { ChannelRelease("1.0.0", ReleaseStatus.Stable), ChannelRelease("1.1.0-beta.1", ReleaseStatus.Testing), ChannelRelease("1.2.0-dev.1", ReleaseStatus.Dev) };

        using var rebuilt = await BoreaServices.BuildAsync(_tempRoot, new ControlledHttpMessageHandler(snapshot), new ReleaseListRepository(available));
        var latest = await rebuilt.Mods.GetLatestReleaseAsync("A");
        var versions = await rebuilt.Mods.GetAvailableVersionsAsync("A");

        Assert.Equal(ModVersion.Parse("1.1.0-beta.1"), latest!.Version);
        Assert.Equal(3, versions.Count);
    }

    [Fact]
    public async Task ModPackInstaller_DevPinOnTheSavedStableChannel_InstallsWithTheChannelWarning()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);
        var pin = ChannelRelease("1.2.0-dev.1", ReleaseStatus.Dev);
        var repository = new ReleaseListRepository([ChannelRelease("1.0.0", ReleaseStatus.Stable), pin]);
        var instance = await services.Instances.CreateAsync("Pack target", InstanceSource.Custom.Value);
        var installer = new RecordingModInstaller(services.Instances);
        var packs = new Borea.Storage.ModPacks.ModPackInstaller(services.Instances, services.InstallPlanner, installer, new UnusedModReplacer());
        var metadata = new Borea.Core.ModPacks.ModPackMetadata(1, "Pack", "test", "Pack", ["Author"], "Pack.", "CC0-1.0", new Dictionary<string, string> { ["forums"] = "https://example.com/pack" }, "2026.7", ModVersion.Parse("1.0.0"), DateTimeOffset.UnixEpoch, [new Borea.Core.ModPacks.ModPackEntry("A", pin.Version)]);
        var pack = new Borea.Core.ModPacks.ModPackResult("Pack", "1.0.0", metadata, null, null, []);

        var result = await packs.InstallAsync(new Borea.Core.ModPacks.ModPackInstallRequest(instance.InstanceId, pack, repository));

        Assert.True(result.IsComplete);
        Assert.Equal(pin.Version, Assert.Single(installer.Installed).Version);
        Assert.Contains(result.Warnings, value => value.ModId == "A" && value.Code == "release-channel");
    }

    private sealed class RecordingModInstaller(IInstanceRepository instances) : IModInstaller
    {
        public List<ModVersionMetadata> Installed { get; } = [];

        public Task<InstallResult> InstallAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task<GuardedInstallResult> InstallGuardedAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, Borea.Core.Planning.InstallPlanningState expectedState, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Installed.Add(release);
            var installed = new InstalledMod(release.ModId, release.Version, reason, DateTimeOffset.UnixEpoch, release);
            var state = await instances.UpdateAsync(instanceId, value =>
            {
                value.AddMod(installed);
                return Borea.Core.Planning.InstallPlanningState.Capture(value);
            }, cancellationToken);
            return new GuardedInstallResult(new InstallResult(installed, new DownloadResult(release.Download.Url, 1, release.Download.Sha256!), Borea.Core.State.ModEntryAddResult.Added), state);
        }
    }

    private sealed class UnusedModReplacer : IModReplacer
    {
        public Task<ModReplacementResult> ReplaceAsync(Guid instanceId, InstalledMod expectedCurrent, ModVersionMetadata replacement, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<GuardedModReplacementResult> ReplaceGuardedAsync(Guid instanceId, InstalledMod expectedCurrent, ModVersionMetadata replacement, Borea.Core.Planning.InstallPlanningState expectedState, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static ModVersionMetadata ChannelRelease(string version, ReleaseStatus status) => new(
        1,
        "A",
        ModVersion.Parse(version),
        status,
        DateTimeOffset.UnixEpoch,
        "2026.7.4.2131",
        2131,
        new DownloadInfo("https://example.com/mod.zip", new string('A', 64), 1, "application/zip"),
        1,
        Array.Empty<ModDependency>());

    private sealed class ReleaseListRepository(IReadOnlyList<ModVersionMetadata> releases) : IModRepository
    {
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModMetadata>>([]);
        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) => Task.FromResult(releases.OrderByDescending(value => value.Version).FirstOrDefault());
        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) => Task.FromResult(releases.FirstOrDefault(value => value.Version == version));
        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModVersion>>(releases.Select(value => value.Version).OrderByDescending(value => value).ToList());
        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }

    [Fact]
    public async Task Log_WritesTheDayFileUnderTheRootWithTheSource()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot, BoreaLogSource.Cli);
        Assert.False(Directory.Exists(_tempRoot));

        services.Log.Write("Install of Example 1.0.0 started.");

        Assert.Equal(Path.Combine(_tempRoot, "Logs"), Path.GetDirectoryName(services.Log.CurrentFilePath));
        Assert.Matches(@"^borea-\d{4}-\d{2}-\d{2}\.log$", Path.GetFileName(services.Log.CurrentFilePath));
        Assert.Contains("[cli] Install of Example 1.0.0 started.", File.ReadAllText(services.Log.CurrentFilePath));
    }

    [Fact]
    public async Task IndexFetcher_IsTheNetworkFetcher()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.IsType<ContentIndexFetcher>(Assert.IsType<LoggingContentIndexFetcher>(services.IndexFetcher).Inner);
    }

    [Fact]
    public async Task IndexReader_IsTheStorageReader()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.IsType<ContentIndexReader>(services.IndexReader);
        Assert.IsType<ContentIndexSnapshotProvider>(services.IndexSnapshots);
        Assert.Same(services.IndexSnapshots, services.IndexRefresh);
        Assert.IsType<ContentIndexModRepository>(services.ContentIndex);
        Assert.IsAssignableFrom<IContentIndexRepository>(services.ContentIndex);
        Assert.IsType<ContentIndexModPackRepository>(services.ModPacks);
        Assert.IsType<ContentIndexModPackRepository>(services.ReadOnlyModPacks);
        Assert.NotSame(services.ModPacks, services.ReadOnlyModPacks);
    }

    [Fact]
    public async Task ContentIndex_CurrentSnapshotFixture_IsUsableThroughTheRepository()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);
        var indexPath = services.Paths.GetIndexPath();
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json"),
            indexPath);
        var repository = new ContentIndexModRepository(
            new CachedIndexFetcher(),
            services.IndexReader,
            services.Paths);

        var available = await repository.GetAvailableModsAsync();
        var latest = await repository.GetLatestReleaseAsync("AdvancedFlightComputer");
        var diagnostics = await repository.GetDiagnosticsAsync();

        Assert.Equal(4, available.Count);
        Assert.Contains(available, mod => mod.ModId == "StarMap" && mod.Type == ContentType.ModLoader);
        Assert.Equal(ModVersion.Parse("0.7.5"), latest!.Version);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Mods_ControlledSnapshot_ResolvesStarMapThroughTheProductionComposite()
    {
        var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath);
        var handler = new ControlledHttpMessageHandler(snapshot);
        using var services = await BoreaServices.BuildAsync(_tempRoot, handler, new ConflictingStarMapRepository());

        var available = await services.Mods.GetAvailableModsAsync();
        var packs = await services.ModPacks.GetAvailableModPacksAsync();
        var retainedSnapshot = await services.IndexSnapshots.GetSnapshotAsync();
        var starMap = Assert.Single(available, mod => mod.ModId == "StarMap");
        var versions = await services.Mods.GetAvailableVersionsAsync("StarMap");
        var latest = await services.Mods.GetLatestReleaseAsync("StarMap");

        Assert.Equal(ContentIndexModRepository.SourceName, starMap.Source);
        Assert.Empty(packs);
        Assert.Equal(4, retainedSnapshot.Listings.Count);
        Assert.Equal(ContentType.ModLoader, starMap.Type);
        Assert.Equal(InstallAnchor.Standalone, starMap.Install!.Target);
        Assert.Equal("StarMap.exe", starMap.Provides!.Launch);
        Assert.Equal(InstallAnchor.Mods, starMap.Provides.ContentDir);
        Assert.Equal("StarMapConfig.json", starMap.Provides.Configure!.File);
        Assert.Equal(ConfigureFormat.Json, starMap.Provides.Configure.Format);
        Assert.Equal("GameLocation", starMap.Provides.Configure.GamePath);
        Assert.Equal("-InstancePath", starMap.Provides.Instance!.Flag);
        Assert.Equal("STARMAP_INSTANCE_PATH", starMap.Provides.Instance.Variable);
        Assert.Equal([ModVersion.Parse("0.4.6")], versions);
        Assert.NotNull(latest);
        Assert.Equal(ModVersion.Parse("0.4.6"), latest.Version);
        Assert.Equal(ContentIndexModRepository.SourceName, latest.Source);
        Assert.Equal(InstallAnchor.Standalone, latest.Install!.Target);
        Assert.Equal(
            ["https://ksamodding.github.io/content-index-releases/v1/index.json"],
            handler.RequestUris.Select(uri => uri.AbsoluteUri));
    }

    [Fact]
    public async Task ForeignModAdopter_MatchingIdAndArchive_PreservesForeignFolderOwnership()
    {
        byte[] archiveBytes = [1, 2, 3, 4];
        var snapshot = await SnapshotWithArchiveHashAsync(archiveBytes);
        using var services = await BoreaServices.BuildAsync(
            _tempRoot,
            new ControlledHttpMessageHandler(snapshot),
            new ConflictingStarMapRepository());
        Assert.IsType<FileForeignModAdopter>(services.ForeignModAdopter);
        Assert.IsType<FileForeignModReleaseMatcher>(services.ForeignModReleaseMatcher);
        var instance = await services.Instances.CreateAsync("Test", InstanceSource.Custom.Value);
        var folder = WriteForeignMod(services, instance.InstanceId, "AdvancedFlightComputer");
        var payload = Path.Combine(folder, "keep.txt");
        await File.WriteAllTextAsync(payload, "Keep these bytes.");
        var archive = await WriteArchiveAsync(archiveBytes);
        await services.ForeignModAdopter.ScanAsync(instance.InstanceId);

        var result = await services.ForeignModAdopter.AdoptArchiveAsync(
            instance.InstanceId,
            "advancedflightcomputer",
            archive);
        await services.Uninstaller.UninstallAsync(instance.InstanceId, "AdvancedFlightComputer");

        Assert.True(result.Matched);
        Assert.Equal(ModInstallOwnership.Foreign, result.InstalledMod!.Ownership);
        Assert.False(result.InstalledMod.CanDeleteFiles);
        Assert.True(Directory.Exists(folder));
        Assert.Equal("Keep these bytes.", await File.ReadAllTextAsync(payload));
        var saved = await services.Instances.GetByIdAsync(instance.InstanceId);
        Assert.Empty(saved!.ForeignMods);
        Assert.Equal(ModInstallOwnership.Foreign, Assert.Single(saved.Mods).Ownership);
    }

    [Theory]
    [InlineData("OtherMod", true)]
    [InlineData("AdvancedFlightComputer", false)]
    public async Task ForeignModAdopter_WrongIdOrUnknownArchive_KeepsUnknownForeignMod(
        string folderName,
        bool publishArchiveHash)
    {
        byte[] archiveBytes = [5, 6, 7, 8];
        var publishedBytes = publishArchiveHash ? archiveBytes : new byte[] { 9, 10, 11, 12 };
        var snapshot = await SnapshotWithArchiveHashAsync(publishedBytes);
        using var services = await BoreaServices.BuildAsync(
            _tempRoot,
            new ControlledHttpMessageHandler(snapshot),
            new ConflictingStarMapRepository());
        var instance = await services.Instances.CreateAsync("Test", InstanceSource.Custom.Value);
        var folder = WriteForeignMod(services, instance.InstanceId, folderName);
        var archive = await WriteArchiveAsync(archiveBytes);
        await services.ForeignModAdopter.ScanAsync(instance.InstanceId);

        var result = await services.ForeignModAdopter.AdoptArchiveAsync(instance.InstanceId, folderName, archive);

        Assert.False(result.Matched);
        Assert.Null(result.InstalledMod);
        Assert.Equal(folderName, result.ForeignMod!.ModId);
        Assert.True(Directory.Exists(folder));
        var saved = await services.Instances.GetByIdAsync(instance.InstanceId);
        Assert.Empty(saved!.Mods);
        Assert.Equal(folderName, Assert.Single(saved.ForeignMods).ModId);
    }

    [Fact]
    public async Task GameDirectoryChanger_ControlledSnapshot_UpdatesStarMapAndSettings()
    {
        var oldGame = Path.Combine(_tempRoot, "OldGame");
        var newGame = Path.Combine(_tempRoot, "NewGame");
        var loaderDirectory = Path.Combine(_tempRoot, "StarMap");
        var configurationPath = Path.Combine(loaderDirectory, "StarMapConfig.json");
        Directory.CreateDirectory(loaderDirectory);
        await File.WriteAllTextAsync(configurationPath, """
            {
              "GameLocation": "old",
              "Keep": true
            }
            """);
        await SaveAsync(new BoreaSettings(oldGame, LoaderAt(loaderDirectory)));
        var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath);

        using var services = await BoreaServices.BuildAsync(
            _tempRoot,
            new ControlledHttpMessageHandler(snapshot),
            new ConflictingStarMapRepository());
        await services.GameDirectoryChanger.ChangeAsync(newGame);

        using var configuration = JsonDocument.Parse(await File.ReadAllTextAsync(configurationPath));
        Assert.Equal(newGame, configuration.RootElement.GetProperty("GameLocation").GetString());
        Assert.True(configuration.RootElement.GetProperty("Keep").GetBoolean());
        Assert.Equal(newGame, (await services.SettingsRepository.GetAsync())!.GameDirectoryPath);
    }

    [Fact]
    public async Task GameDirectoryChanger_InvalidLoaderConfiguration_RestoresFileAndSettings()
    {
        var oldGame = Path.Combine(_tempRoot, "OldGame");
        var newGame = Path.Combine(_tempRoot, "NewGame");
        var loaderDirectory = Path.Combine(_tempRoot, "StarMap");
        var configurationPath = Path.Combine(loaderDirectory, "StarMapConfig.json");
        const string originalConfiguration = "[]";
        Directory.CreateDirectory(loaderDirectory);
        await File.WriteAllTextAsync(configurationPath, originalConfiguration);
        await SaveAsync(new BoreaSettings(oldGame, LoaderAt(loaderDirectory)));
        var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath);

        using var services = await BoreaServices.BuildAsync(
            _tempRoot,
            new ControlledHttpMessageHandler(snapshot),
            new ConflictingStarMapRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => services.GameDirectoryChanger.ChangeAsync(newGame));
        Assert.Equal(originalConfiguration, await File.ReadAllTextAsync(configurationPath));
        Assert.Equal(oldGame, (await services.SettingsRepository.GetAsync())!.GameDirectoryPath);
    }

    [Fact]
    public async Task AppPreferences_IsTheFileRepository()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.IsType<FileAppPreferencesRepository>(services.AppPreferences);
    }

    [Fact]
    public async Task ReleaseCheck_IsTheGitHubCheck()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.IsType<BoreaReleaseCheck>(services.ReleaseCheck);
    }

    [Fact]
    public async Task InstalledVersion_ReadsTheGameDirectoryTheSettingsName()
    {
        using var services = await BoreaServices.BuildAsync(_tempRoot);

        Assert.IsType<InstalledGameVersionProvider>(services.InstalledVersion);
        // No game directory is set, so there is nothing to read.
        Assert.Null(services.InstalledVersion.GetInstalledVersion());
    }

    [Fact]
    public async Task Dispose_ClosesTheOneClientEveryNetworkServiceUses()
    {
        var services = await BoreaServices.BuildAsync(_tempRoot);

        services.Dispose();

        // A disposed client refuses a request before it reaches any host, so each
        // probe proves that the service holds the shared client and sends nothing.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.LatestVersion.PingAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.ReleaseCheck.GetLatestReleaseAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.Mods.GetAvailableModsAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.Downloader.DownloadAsync(Release(), Path.Combine(_tempRoot, "probe.zip")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => services.IndexFetcher.FetchAsync(services.Paths.GetIndexPath()));
    }

    /// <summary>The least a release needs to reach the client, which is all the
    /// disposal probe above asks of it.</summary>
    private static ModVersionMetadata Release() => new(
        specVersion: 1,
        modId: "ModA",
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: new DownloadInfo("https://example.invalid/ModA.zip", null, null, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>());

    private string SettingsPath => new GamePathProvider(gameDirectory: null, boreaRoot: _tempRoot).GetBoreaSettingsPath();

    private async Task<string> SnapshotWithArchiveHashAsync(byte[] archiveBytes)
    {
        var snapshot = await File.ReadAllTextAsync(SnapshotFixturePath);
        var sha256 = Convert.ToHexString(SHA256.HashData(archiveBytes));
        Assert.Contains(FixtureArchiveSha256, snapshot, StringComparison.Ordinal);
        return snapshot.Replace(FixtureArchiveSha256, sha256, StringComparison.Ordinal);
    }

    private static string WriteForeignMod(BoreaServices services, Guid instanceId, string folderName)
    {
        var folder = Path.Combine(services.Paths.GetInstanceModsFolder(instanceId), folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.toml"), "name = \"Local\"");
        return folder;
    }

    private async Task<string> WriteArchiveAsync(byte[] bytes)
    {
        var path = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N") + ".zip");
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    private static string SnapshotFixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Index", "Fixtures", "current-snapshot.json");

    private static Dictionary<string, LoaderInstallation> LoaderAt(string path) => new()
    {
        ["StarMap"] = new LoaderInstallation(path, ModVersion.Parse("0.4.6"), "0.4.6.0", isAdopted: true),
    };

    private Task SaveAsync(BoreaSettings settings)
        => new FileBoreaSettingsRepository(new GamePathProvider(gameDirectory: null, boreaRoot: _tempRoot)).SaveAsync(settings);

    private sealed class CachedIndexFetcher : IContentIndexFetcher
    {
        public Task<ContentIndexFetchResult> FetchAsync(
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ContentIndexFetchResult.NotModified);
    }

    private sealed class ControlledHttpMessageHandler(string snapshot) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request.RequestUri);
            RequestUris.Add(request.RequestUri);

            if (!request.RequestUri.AbsoluteUri.StartsWith(
                "https://ksamodding.github.io/content-index-releases/",
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected request to {request.RequestUri}.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(snapshot, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ConflictingStarMapRepository : IModRepository
    {
        private static readonly ModVersion Version = ModVersion.Parse("9.9.9");

        private static readonly ModMetadata Listing = new(
            specVersion: 1,
            modId: "StarMap",
            source: "fallback",
            name: "Fallback StarMap",
            authors: ["Fallback"],
            abstractText: "Fallback listing.",
            license: "MIT",
            links: new Dictionary<string, string>
            {
                ["forums"] = "https://forums.ahwoo.com/threads/fallback.1/",
            },
            gameMin: "2026.1",
            type: ContentType.ModLoader);

        private static readonly ModVersionMetadata Release = new(
            specVersion: 1,
            modId: "StarMap",
            version: Version,
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            gameMin: "2026.1.1.1",
            gameMinRevision: 1,
            download: new DownloadInfo("https://example.invalid/fallback.zip", null, null, "application/zip"),
            installSizeBytes: null,
            dependencies: [],
            type: ContentType.ModLoader,
            source: "fallback");

        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>([Listing]);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(
            string modId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(ModIds.Equals(modId, Listing.ModId) ? Release : null);

        public Task<ModVersionMetadata?> GetReleaseAsync(
            string modId,
            ModVersion version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(
                ModIds.Equals(modId, Listing.ModId) && version.Equals(Version) ? Release : null);

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(
            string modId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModVersion>>(
                ModIds.Equals(modId, Listing.ModId) ? [Version] : []);

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>(
                Listing.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? [Listing] : []);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
