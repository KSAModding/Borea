using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Borea.Core.Dependencies;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.ModLoaders;
using Borea.Storage.Settings;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.ModLoaders;

public sealed class FileLoaderInstallerTests : IDisposable
{
    private const string LoaderId = "StarMap";

    private static readonly IReadOnlyDictionary<string, string> Links = new Dictionary<string, string>
    {
        ["forums"] = "https://forums.ahwoo.com/threads/starmap-mod-loader.384/",
        ["repository"] = "https://github.com/StarMapLoader/StarMap",
        ["bugtracker"] = "https://github.com/StarMapLoader/StarMap/issues",
    };

    private static readonly LoaderConfigure StarMapConfigure = new("StarMapConfig.json", ConfigureFormat.Json, "GameLocation");

    private static readonly InstallInfo StandaloneStamp = new(null, derived: true, target: InstallAnchor.Standalone);

    private readonly string _tempRoot;
    private readonly string _gameDirectory;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileBoreaSettingsRepository _settings;
    private readonly FakeModDownloader _downloader = new();
    private readonly FileLoaderInstaller _installer;

    public FileLoaderInstallerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _gameDirectory = Path.Combine(_tempRoot, "Game");
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _settings = new FileBoreaSettingsRepository(_pathProvider);
        _installer = new FileLoaderInstaller(_pathProvider, _downloader, _settings, new LoaderConfigurator());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string DefaultDirectory => Path.Combine(_pathProvider.GetLoadersRoot(), LoaderId);

    private static string ConfigPath(string directory) => Path.Combine(directory, "StarMapConfig.json");

    /// <summary>The live StarMap listing of the index, as of 2026-09-04.</summary>
    private static ModMetadata StarMap() => Listing(Standalone(), Provides(StarMapConfigure));

    private static ModMetadata Listing(
        InstallDescriptor? install,
        LoaderProvides? provides,
        ContentType type = ContentType.ModLoader,
        string id = LoaderId) => new(
        specVersion: 1,
        modId: id,
        source: "index",
        name: "StarMap",
        authors: new[] { "KlaasWhite" },
        abstractText: "Mod loader that runs code mods for Kitten Space Agency.",
        license: "MIT",
        links: Links,
        gameMin: "2026.8.3.5117",
        type: type,
        tags: new[] { "library" },
        install: install,
        provides: provides);

    private static InstallDescriptor Standalone() => new(
        target: InstallAnchor.Standalone,
        uninstall: new[] { "Delete the StarMap directory. The game runs unmodded again with no further cleanup." });

    private static LoaderProvides Provides(LoaderConfigure? configure, string? launch = "StarMap.exe") =>
        new(launch, InstallAnchor.Mods, configure: configure);

    /// <summary>StarMap 0.4.6 as stamped in the index.</summary>
    private static ModVersionMetadata Release(
        InstallInfo? install,
        string version = "0.4.6",
        ContentType type = ContentType.ModLoader,
        string contentType = "application/zip",
        string id = LoaderId) => new(
        specVersion: 1,
        modId: id,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 8, 2, 16, 47, 50, TimeSpan.Zero),
        gameMin: "2026.8.3.5117",
        gameMinRevision: 5117,
        download: new DownloadInfo(
            $"https://github.com/StarMapLoader/StarMap/releases/download/{version}/StarMap-{version}.zip",
            "BC9510994DAF56FD826B734EF0F2704C8E4D1B91BCF010C76733E168CC23604A",
            891020,
            contentType),
        installSizeBytes: 2549644,
        dependencies: Array.Empty<ModDependency>(),
        type: type,
        install: install,
        changelog: $"https://github.com/StarMapLoader/StarMap/releases/tag/{version}");

    private static ModVersionMetadata StarMapRelease(string version = "0.4.6") => Release(StandaloneStamp, version);

    /// <summary>The flat layout of the StarMap release archive.</summary>
    private static byte[] StarMapZip(string exe = "exe", params (string Path, string Content)[] extra) =>
        TestArchives.Build(new[]
        {
            ("StarMap.exe", exe),
            ("StarMap.dll", "dll"),
            ("0Harmony.dll", "harmony"),
            ("StarMap.runtimeconfig.json", "{}"),
        }.Concat(extra).ToArray());

    private Task SaveSettingsAsync(string? gameDirectory, IReadOnlyDictionary<string, string>? loaders = null) =>
        _settings.SaveAsync(new BoreaSettings(gameDirectory, loaders));

    private Task SaveGameDirectoryAsync() => SaveSettingsAsync(_gameDirectory);

    private Task<LoaderInstallResult> InstallAsync(ModMetadata? loader = null, ModVersionMetadata? release = null, string? directory = null) =>
        _installer.InstallAsync(loader ?? StarMap(), release ?? StarMapRelease(), directory);

    private async Task<string?> RecordedDirectoryAsync()
    {
        var settings = await _settings.GetAsync();
        return settings is not null && settings.LoaderDirectoryPaths.TryGetValue(LoaderId, out var path) ? path : null;
    }

    private static async Task<JsonObject> ReadConfigAsync(string directory) =>
        (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath(directory)))!;

    private async Task AssertNothingInstalledAsync()
    {
        Assert.False(Directory.Exists(DefaultDirectory));
        Assert.Null(await RecordedDirectoryAsync());
        Assert.All(_downloader.ArchivePaths, path => Assert.False(File.Exists(path)));
    }

    #region The real listing

    [Fact]
    public async Task InstallAsync_StarMap_UnpacksBelowTheLoadersRootAndRecordsIt()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip();

        var result = await InstallAsync();

        Assert.Equal(DefaultDirectory, result.Directory);
        Assert.Equal(
            new[] { "0Harmony.dll", "StarMap.dll", "StarMap.exe", "StarMap.runtimeconfig.json", "StarMapConfig.json" },
            Directory.EnumerateFileSystemEntries(DefaultDirectory).Select(entry => Path.GetFileName(entry)).Order(StringComparer.Ordinal));
        Assert.Equal(DefaultDirectory, await RecordedDirectoryAsync());
        Assert.Equal(LoaderId, result.LoaderId);
        Assert.Equal(ModVersion.Parse("0.4.6"), result.Version);
        Assert.False(result.Replaced);
        Assert.Equal(StarMapRelease().Download.Url, result.Download.Url);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(_downloader.Bytes)), result.Download.Sha256);
        Assert.Single(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_StarMap_WritesTheGameLocation()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip();

        var result = await InstallAsync();

        Assert.Equal(ConfigPath(DefaultDirectory), result.ConfigurationFile);
        var config = await ReadConfigAsync(DefaultDirectory);
        Assert.Equal(_gameDirectory, (string?)config["GameLocation"]);
    }

    [Fact]
    public async Task InstallAsync_ArchiveShipsAConfiguration_ItsOtherKeysSurvive()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip(extra: ("StarMapConfig.json", """
            {
              "GameLocation": "",
              "RepositoryLocation": "",
              "GameArguments": []
            }
            """));

        await InstallAsync();

        var config = await ReadConfigAsync(DefaultDirectory);
        Assert.Equal(_gameDirectory, (string?)config["GameLocation"]);
        Assert.Equal(string.Empty, (string?)config["RepositoryLocation"]);
        Assert.Empty(config["GameArguments"]!.AsArray());
    }

    [Fact]
    public async Task InstallAsync_RecordedLoader_ReplacesInPlaceAndKeepsTheConfiguration()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip(exe: "old");
        await InstallAsync();

        // The user pointed StarMap at a repository and added an argument by hand.
        await File.WriteAllTextAsync(ConfigPath(DefaultDirectory), $$"""
            {
              "GameLocation": "{{_gameDirectory.Replace(@"\", @"\\")}}",
              "RepositoryLocation": "C:\\Repos",
              "GameArguments": ["-Verbose"]
            }
            """);
        _downloader.Bytes = StarMapZip(exe: "new");

        var result = await InstallAsync(release: StarMapRelease("0.4.7"));

        Assert.True(result.Replaced);
        Assert.Equal(DefaultDirectory, result.Directory);
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(DefaultDirectory, "StarMap.exe")));
        var config = await ReadConfigAsync(DefaultDirectory);
        Assert.Equal(_gameDirectory, (string?)config["GameLocation"]);
        Assert.Equal(@"C:\Repos", (string?)config["RepositoryLocation"]);
        Assert.Equal("-Verbose", (string?)config["GameArguments"]![0]);
        Assert.Single((await _settings.GetAsync())!.LoaderDirectoryPaths);
    }

    [Fact]
    public async Task InstallAsync_ReplacementArchiveShipsAConfiguration_TheUsersFileIsKept()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip();
        await InstallAsync();
        await File.WriteAllTextAsync(ConfigPath(DefaultDirectory), $$"""
            { "GameLocation": "{{_gameDirectory.Replace(@"\", @"\\")}}", "RepositoryLocation": "C:\\Repos" }
            """);
        _downloader.Bytes = StarMapZip(extra: ("StarMapConfig.json", """{ "GameLocation": "", "Fresh": true }"""));

        await InstallAsync(release: StarMapRelease("0.4.7"));

        var config = await ReadConfigAsync(DefaultDirectory);
        Assert.Equal(_gameDirectory, (string?)config["GameLocation"]);
        Assert.Equal(@"C:\Repos", (string?)config["RepositoryLocation"]);
        Assert.False(config.ContainsKey("Fresh"));
    }

    [Fact]
    public async Task InstallAsync_RecordedUnderTheIdInOtherCase_ReplacesThatRecord()
    {
        var elsewhere = Path.Combine(_tempRoot, "Elsewhere");
        await SaveSettingsAsync(_gameDirectory, new Dictionary<string, string> { ["starmap"] = elsewhere });
        _downloader.Bytes = StarMapZip();

        var result = await InstallAsync();

        Assert.Equal(elsewhere, result.Directory);
        Assert.True(result.Replaced);
        Assert.True(File.Exists(Path.Combine(elsewhere, "StarMap.exe")));
        Assert.Single((await _settings.GetAsync())!.LoaderDirectoryPaths);
    }

    [Fact]
    public async Task InstallAsync_KeepsTheOtherLoadersInTheSettings()
    {
        await SaveSettingsAsync(_gameDirectory, new Dictionary<string, string> { ["Cheese-Loader"] = @"C:\Games\Cheese" });
        _downloader.Bytes = StarMapZip();

        await InstallAsync();

        var settings = await _settings.GetAsync();
        Assert.Equal(_gameDirectory, settings!.GameDirectoryPath);
        Assert.Equal(@"C:\Games\Cheese", settings.LoaderDirectoryPaths["Cheese-Loader"]);
        Assert.Equal(DefaultDirectory, settings.LoaderDirectoryPaths[LoaderId]);
    }

    #endregion

    #region Where it goes

    [Fact]
    public async Task InstallAsync_GivenDirectory_InstallsThereAndRecordsIt()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip();
        var chosen = Path.Combine(_tempRoot, "Tools", "My StarMap");

        var result = await InstallAsync(directory: chosen);

        Assert.Equal(chosen, result.Directory);
        Assert.True(File.Exists(Path.Combine(chosen, "StarMap.exe")));
        Assert.Equal(chosen, await RecordedDirectoryAsync());
    }

    [Fact]
    public async Task InstallAsync_RelativeDirectory_ThrowsArgumentException()
    {
        await SaveGameDirectoryAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => InstallAsync(directory: "Tools/StarMap"));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_RecordedElsewhere_GivenAnotherDirectory_Refuses()
    {
        var elsewhere = Path.Combine(_tempRoot, "Elsewhere");
        await SaveSettingsAsync(_gameDirectory, new Dictionary<string, string> { [LoaderId] = elsewhere });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync(directory: Path.Combine(_tempRoot, "Other")));

        Assert.Contains(elsewhere, ex.Message);
        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_RecordedLoader_GivenItsOwnDirectory_Replaces()
    {
        var elsewhere = Path.Combine(_tempRoot, "Elsewhere");
        await SaveSettingsAsync(_gameDirectory, new Dictionary<string, string> { [LoaderId] = elsewhere });
        _downloader.Bytes = StarMapZip();

        // The same directory, written with a trailing separator.
        var result = await InstallAsync(directory: elsewhere + Path.DirectorySeparatorChar);

        Assert.True(result.Replaced);
        Assert.Equal(elsewhere, result.Directory);
        Assert.True(File.Exists(Path.Combine(elsewhere, "StarMap.exe")));
        Assert.Equal(elsewhere, await RecordedDirectoryAsync());
    }

    [Fact]
    public async Task InstallAsync_DirectoryHoldsFilesBoreaDidNotInstall_Refuses()
    {
        await SaveGameDirectoryAsync();
        Directory.CreateDirectory(DefaultDirectory);
        var foreign = Path.Combine(DefaultDirectory, "StarMap.exe");
        await File.WriteAllTextAsync(foreign, "installed by hand");

        await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.Empty(_downloader.ArchivePaths);
        Assert.Equal("installed by hand", await File.ReadAllTextAsync(foreign));
        Assert.Null(await RecordedDirectoryAsync());
    }

    [Fact]
    public async Task InstallAsync_EmptyExistingDirectory_Installs()
    {
        await SaveGameDirectoryAsync();
        Directory.CreateDirectory(DefaultDirectory);
        _downloader.Bytes = StarMapZip();

        await InstallAsync();

        Assert.True(File.Exists(Path.Combine(DefaultDirectory, "StarMap.exe")));
    }

    [Fact]
    public async Task InstallAsync_StatedRoot_UnpacksOnlyThatDirectory()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = TestArchives.Build(
            ("StarMap-0.4.6/StarMap.exe", "exe"),
            ("StarMap-0.4.6/StarMap.dll", "dll"),
            ("README.md", "beside the root"));

        await InstallAsync(release: Release(new InstallInfo("StarMap-0.4.6", derived: false, target: InstallAnchor.Standalone)));

        Assert.True(File.Exists(Path.Combine(DefaultDirectory, "StarMap.exe")));
        Assert.False(File.Exists(Path.Combine(DefaultDirectory, "README.md")));
        Assert.False(Directory.Exists(Path.Combine(DefaultDirectory, "StarMap-0.4.6")));
    }

    [Fact]
    public async Task InstallAsync_StatedRootNotInTheArchive_FailsAndLeavesNothing()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InstallAsync(release: Release(new InstallInfo("elsewhere", derived: false, target: InstallAnchor.Standalone))));

        Assert.Contains("elsewhere", ex.Message);
        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_LaunchTargetInASubdirectory_IsFoundWithTheSeparatorTranslated()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = TestArchives.Build(("bin/StarMap.exe", "exe"), ("bin/StarMap.dll", "dll"));
        var provides = new LoaderProvides("bin/StarMap.exe", InstallAnchor.Mods, configure: StarMapConfigure);

        await InstallAsync(loader: Listing(Standalone(), provides));

        Assert.True(File.Exists(Path.Combine(DefaultDirectory, "bin", "StarMap.exe")));
    }

    [Fact]
    public async Task InstallAsync_NoLaunchTarget_NeedsNoExecutable()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = TestArchives.Build(("StarMap.dll", "a library the game loads itself"));
        var stamp = new InstallInfo(null, derived: false, target: InstallAnchor.GameRoot, path: "StarMap");
        var loader = Listing(new InstallDescriptor(target: InstallAnchor.GameRoot, path: "StarMap"), Provides(StarMapConfigure, launch: null));

        var result = await InstallAsync(loader: loader, release: Release(stamp));

        Assert.True(File.Exists(Path.Combine(result.Directory, "StarMap.dll")));
    }

    [Fact]
    public async Task InstallAsync_WrappingDirectoryWithoutAStatedRoot_IsNotDerivedForALoader()
    {
        // RFC 0035 rule 9: only a mod derives its wrapping directory as the root.
        await SaveGameDirectoryAsync();
        _downloader.Bytes = TestArchives.Build(("StarMap/StarMap.exe", "exe"), ("StarMap/StarMap.dll", "dll"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.Contains("StarMap.exe", ex.Message);
        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_ReleaseWithoutAStampedInstall_FollowsTheListing()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip();

        var result = await InstallAsync(release: Release(install: null));

        Assert.Equal(DefaultDirectory, result.Directory);
        Assert.True(File.Exists(Path.Combine(DefaultDirectory, "StarMap.exe")));
    }

    [Fact]
    public async Task InstallAsync_GameRootWithAPath_InstallsBelowTheGameDirectory()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip();
        var expected = Path.Combine(_gameDirectory, "Loaders", "StarMap");

        var result = await InstallAsync(release: Release(new InstallInfo(null, derived: false, target: InstallAnchor.GameRoot, path: "Loaders/StarMap")));

        Assert.Equal(expected, result.Directory);
        Assert.True(File.Exists(Path.Combine(expected, "StarMap.exe")));
        Assert.Equal(expected, await RecordedDirectoryAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(".")]
    [InlineData("./")]
    public async Task InstallAsync_GameRootWithoutAPathBelowIt_IsNotSupported(string? path)
    {
        await SaveGameDirectoryAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => InstallAsync(release: Release(new InstallInfo(null, derived: false, target: InstallAnchor.GameRoot, path: path))));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_GameRoot_GivenDirectory_ThrowsArgumentException()
    {
        await SaveGameDirectoryAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => InstallAsync(
            release: Release(new InstallInfo(null, derived: false, target: InstallAnchor.GameRoot, path: "StarMap")),
            directory: Path.Combine(_tempRoot, "Other")));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_GameRoot_GameDirectoryUnknown_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InstallAsync(release: Release(new InstallInfo(null, derived: false, target: InstallAnchor.GameRoot, path: "StarMap"))));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Theory]
    [InlineData(InstallAnchor.Mods)]
    [InlineData(InstallAnchor.UserData)]
    public async Task InstallAsync_PerInstanceAnchor_IsNotSupported(InstallAnchor anchor)
    {
        await SaveGameDirectoryAsync();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => InstallAsync(release: Release(new InstallInfo(null, derived: false, target: anchor))));

        Assert.Contains("instance", ex.Message);
        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_UnknownAnchor_IsNotSupported()
    {
        await SaveGameDirectoryAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => InstallAsync(release: Release(new InstallInfo(null, derived: false, target: InstallAnchor.Unknown))));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_UndescribedLoader_IsNotSupported()
    {
        await SaveGameDirectoryAsync();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => InstallAsync(loader: Listing(null, Provides(StarMapConfigure)), release: Release(install: null)));

        Assert.Contains("links", ex.Message);
        Assert.Empty(_downloader.ArchivePaths);
    }

    #endregion

    #region Configuration

    [Fact]
    public async Task InstallAsync_GameDirectoryUnknown_RefusesBeforeDownloading()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.Contains("game directory", ex.Message);
        Assert.Empty(_downloader.ArchivePaths);
        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_NoConfigureTable_NeedsNoGameDirectoryAndWritesNoFile()
    {
        _downloader.Bytes = StarMapZip();

        var result = await InstallAsync(loader: Listing(Standalone(), Provides(configure: null)));

        Assert.Null(result.ConfigurationFile);
        Assert.False(File.Exists(ConfigPath(DefaultDirectory)));
        Assert.Equal(DefaultDirectory, await RecordedDirectoryAsync());
    }

    [Fact]
    public async Task InstallAsync_UnknownConfigureFormat_IsNotSupportedBeforeDownloading()
    {
        await SaveGameDirectoryAsync();
        var configure = new LoaderConfigure("StarMap.cfg", ConfigureFormat.Unknown, "GameLocation");

        await Assert.ThrowsAsync<NotSupportedException>(() => InstallAsync(loader: Listing(Standalone(), Provides(configure))));

        Assert.Empty(_downloader.ArchivePaths);
    }

    #endregion

    #region Rollback

    [Fact]
    public async Task InstallAsync_LaunchTargetMissing_FailsAndLeavesNothing()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = TestArchives.Build(("StarMap.dll", "dll"), ("readme.txt", "no executable"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.Contains("StarMap.exe", ex.Message);
        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_FailsInAnEmptyExistingDirectory_ClearsItAndKeepsIt()
    {
        await SaveGameDirectoryAsync();
        Directory.CreateDirectory(DefaultDirectory);
        _downloader.Bytes = TestArchives.Build(("StarMap.dll", "dll"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.True(Directory.Exists(DefaultDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(DefaultDirectory));
        Assert.Null(await RecordedDirectoryAsync());
    }

    [Fact]
    public async Task InstallAsync_EmptyArchive_FailsAndLeavesNothing()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = TestArchives.Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.Contains("no files", ex.Message);
        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_EntryEscapingTheDirectory_FailsAndLeavesNothing()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = TestArchives.Build(("StarMap.exe", "exe"), ("../escaped.txt", "outside"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync());

        Assert.False(File.Exists(Path.Combine(_pathProvider.GetLoadersRoot(), "escaped.txt")));
        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_DownloadFails_LeavesNothing()
    {
        await SaveGameDirectoryAsync();
        _downloader.Failure = new DownloadFailedException("no source served the archive");

        await Assert.ThrowsAsync<DownloadFailedException>(() => InstallAsync());

        await AssertNothingInstalledAsync();
    }

    [Fact]
    public async Task InstallAsync_SettingsCannotBeSaved_RollsBack()
    {
        var installer = new FileLoaderInstaller(
            _pathProvider,
            _downloader,
            new FailingSettingsRepository(new BoreaSettings(_gameDirectory)),
            new LoaderConfigurator());
        _downloader.Bytes = StarMapZip();

        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(StarMap(), StarMapRelease()));

        Assert.False(Directory.Exists(DefaultDirectory));
    }

    [Fact]
    public async Task InstallAsync_ReplacementFails_LeavesTheDirectory()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip();
        await InstallAsync();
        _downloader.Bytes = TestArchives.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync(release: StarMapRelease("0.4.7")));

        Assert.True(File.Exists(Path.Combine(DefaultDirectory, "StarMap.exe")));
        Assert.Equal(DefaultDirectory, await RecordedDirectoryAsync());
    }

    #endregion

    #region Refusals and plumbing

    [Fact]
    public async Task InstallAsync_ModRelease_IsNotSupported()
    {
        await SaveGameDirectoryAsync();

        await Assert.ThrowsAsync<NotSupportedException>(() => InstallAsync(release: Release(install: null, type: ContentType.Mod)));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_ModListing_ThrowsArgumentException()
    {
        await SaveGameDirectoryAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => InstallAsync(loader: Listing(null, null, ContentType.Mod)));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_ReleaseOfAnotherLoader_ThrowsArgumentException()
    {
        await SaveGameDirectoryAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => InstallAsync(release: Release(StandaloneStamp, id: "Cheese-Loader")));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_NotAZip_IsNotSupported()
    {
        await SaveGameDirectoryAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => InstallAsync(release: Release(StandaloneStamp, contentType: "application/x-7z-compressed")));

        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task InstallAsync_HandsProgressAndTheTokenToTheDownloader()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip();
        var progress = new Progress<DownloadProgress>();
        using var cancellation = new CancellationTokenSource();

        await _installer.InstallAsync(StarMap(), StarMapRelease(), null, progress, cancellation.Token);

        Assert.Same(progress, _downloader.LastProgress);
        Assert.Equal(cancellation.Token, _downloader.LastToken);
    }

    [Fact]
    public async Task InstallAsync_DeletesTheTemporaryArchive()
    {
        await SaveGameDirectoryAsync();
        _downloader.Bytes = StarMapZip();

        await InstallAsync();

        Assert.False(File.Exists(Assert.Single(_downloader.ArchivePaths)));
    }

    [Fact]
    public async Task InstallAsync_NullArguments_ThrowArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _installer.InstallAsync(null!, StarMapRelease()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _installer.InstallAsync(StarMap(), null!));
    }

    [Fact]
    public void Constructor_NullDependency_ThrowsArgumentNullException()
    {
        var configurator = new LoaderConfigurator();

        Assert.Throws<ArgumentNullException>(() => new FileLoaderInstaller(null!, _downloader, _settings, configurator));
        Assert.Throws<ArgumentNullException>(() => new FileLoaderInstaller(_pathProvider, null!, _settings, configurator));
        Assert.Throws<ArgumentNullException>(() => new FileLoaderInstaller(_pathProvider, _downloader, null!, configurator));
        Assert.Throws<ArgumentNullException>(() => new FileLoaderInstaller(_pathProvider, _downloader, _settings, null!));
    }

    #endregion

    /// <summary>Settings that can be read but not written, to exercise the rollback.</summary>
    private sealed class FailingSettingsRepository(BoreaSettings? current) : IBoreaSettingsRepository
    {
        public Task<BoreaSettings?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(current);

        public Task SaveAsync(BoreaSettings settings, CancellationToken cancellationToken = default) =>
            throw new IOException("The disk is full.");
    }
}
