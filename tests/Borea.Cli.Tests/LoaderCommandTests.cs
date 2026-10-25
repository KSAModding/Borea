using System.Text.Json;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Instances;

namespace Borea.Cli.Tests;

public sealed class LoaderCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Install_UsesTheNewestReleaseAndRequestedDirectory()
    {
        var installer = new FakeLoaderInstaller();
        var directory = Path.Combine(_host.Root, "Loader");
        _host.LoaderInstaller = installer;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.5"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.6"));

        var run = await _host.RunAsync("loader", "install", "StarMap", "--directory", directory);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("0.4.6", Assert.Single(installer.Calls).Release.Version.ToString());
        Assert.Equal(directory, installer.Calls[0].Directory);
        Assert.Contains("Installed StarMap 0.4.6", run.Output);
    }

    [Fact]
    public async Task Install_WithoutVersion_FollowsTheSavedChannel()
    {
        var installer = new FakeLoaderInstaller();
        _host.LoaderInstaller = installer;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.6"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.5.0-dev.1", releaseStatus: ReleaseStatus.Dev));

        var stable = await _host.RunAsync("loader", "install", "StarMap");
        await _host.RunAsync("settings", "set", "channel", "dev");
        var dev = await _host.RunAsync("loader", "install", "StarMap");

        Assert.Equal(0, stable.ExitCode);
        Assert.Equal(0, dev.ExitCode);
        Assert.Equal(["0.4.6", "0.5.0-dev.1"], installer.Calls.Select(call => call.Release.Version.ToString()));
    }

    [Fact]
    public async Task Install_WithoutVersion_DoesNotDowngradeAnInstalledReleaseOutsideTheChannel()
    {
        var installer = new FakeLoaderInstaller();
        _host.LoaderInstaller = installer;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.6"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.5.0-dev.1", releaseStatus: ReleaseStatus.Dev));
        Directory.CreateDirectory(_host.Root);
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), """
            [LoaderInstallations.StarMap]
            DirectoryPath = 'C:\Loaders\StarMap'
            Version = '0.5.0-dev.1'
            IsAdopted = false
            """);

        var refused = await _host.RunAsync("loader", "install", "StarMap");
        var exact = await _host.RunAsync("loader", "install", "StarMap", "--version", "0.4.6");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("StarMap 0.5.0-dev.1 is installed", refused.Error);
        Assert.Contains("Use --version", refused.Error);
        Assert.Equal(0, exact.ExitCode);
        Assert.Equal("0.4.6", Assert.Single(installer.Calls).Release.Version.ToString());
    }

    [Fact]
    public async Task Install_WithoutVersion_AnUnrecordedVersionOutsideTheChannel_DoesNotBlockTheUpdate()
    {
        var installer = new FakeLoaderInstaller();
        _host.LoaderInstaller = installer;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.7"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.8"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.5.0-dev.1", releaseStatus: ReleaseStatus.Dev));
        var recorded = CreateVersionFolder(_host, "0.4.7");
        CreateVersionFolder(_host, "0.5.0-dev.1");
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), $"[LoaderInstallations.StarMap]\nDirectoryPath = '{recorded}'\nVersion = '0.4.7'\nIsAdopted = false\n");

        var run = await _host.RunAsync("loader", "install", "StarMap");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("0.4.8", Assert.Single(installer.Calls).Release.Version.ToString());
    }

    [Fact]
    public async Task Install_WithoutVersion_SkipsANewerReleaseThatDoesNotSupportTheGame()
    {
        var installer = new FakeLoaderInstaller();
        _host.LoaderInstaller = installer;
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.10.13.5602"), "2026.10.13.5602") };
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.7", gameMax: "2026.10.13.5602"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.8", gameMin: "2026.10.19.5638"));

        var run = await _host.RunAsync("loader", "install", "StarMap");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("0.4.7", Assert.Single(installer.Calls).Release.Version.ToString());
        Assert.Contains("warning: StarMap 0.4.8 does not support KSA 2026.10.13.5602; installing 0.4.7.", run.Error);
    }

    [Fact]
    public async Task Install_WithoutVersion_NoReleaseSupportsTheGame_FailsAndLeavesTheVersionOption()
    {
        var installer = new FakeLoaderInstaller();
        _host.LoaderInstaller = installer;
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.10.13.5602"), "2026.10.13.5602") };
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.8", gameMin: "2026.10.19.5638"));

        var refused = await _host.RunAsync("loader", "install", "StarMap");
        var exact = await _host.RunAsync("loader", "install", "StarMap", "--version", "0.4.8");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("supports KSA 2026.10.13.5602", refused.Error);
        Assert.Contains("Use --version", refused.Error);
        Assert.Equal(0, exact.ExitCode);
        Assert.Equal("0.4.8", Assert.Single(installer.Calls).Release.Version.ToString());
    }

    [Fact]
    public async Task Install_WithoutVersion_NoReleaseInTheChannel_FailsNamingTheChannel()
    {
        var installer = new FakeLoaderInstaller();
        _host.LoaderInstaller = installer;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.5.0-dev.1", releaseStatus: ReleaseStatus.Dev));

        var run = await _host.RunAsync("loader", "install", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("stable channel", run.Error);
        Assert.Empty(installer.Calls);
    }

    [Fact]
    public async Task Install_RequestedReleaseThatDoesNotExist_Fails()
    {
        _host.LoaderInstaller = new FakeLoaderInstaller();
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release());

        var run = await _host.RunAsync("loader", "install", "StarMap", "--version", "0.4.5");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("has no release '0.4.5'", run.Error);
    }

    [Fact]
    public async Task Install_InvalidVersion_IsAUsageError()
    {
        var run = await _host.RunAsync("loader", "install", "StarMap", "--version", "latest");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("not a valid semantic version", run.Error);
        Assert.Equal(0, _host.Builds);
    }

    [Fact]
    public async Task Install_ServiceFailure_IsReportedAsAnOperationFailure()
    {
        _host.LoaderInstaller = new FakeLoaderInstaller { Failure = new InvalidOperationException("Destination has foreign files.") };
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release());

        var run = await _host.RunAsync("loader", "install", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Destination has foreign files", run.Error);
    }

    [Fact]
    public async Task Install_ExplicitYankedRelease_FailsBeforeTheInstallerRuns()
    {
        var installer = new FakeLoaderInstaller();
        _host.LoaderInstaller = installer;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(yanked: true));

        var run = await _host.RunAsync("loader", "install", "StarMap", "--version", "0.4.6");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("is yanked and cannot be installed", run.Error);
        Assert.Empty(installer.Calls);
    }

    [Fact]
    public async Task Adopt_MissingLaunchFile_FailsWithoutARecord()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_host.Root, "Loader")).FullName;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release());

        var run = await _host.RunAsync("loader", "adopt", "StarMap", directory);
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("does not hold the listed launch file", run.Error);
        Assert.Empty(show.Json.GetProperty("loaderDirectories").EnumerateObject());
    }

    [Fact]
    public async Task Adopt_UnreadableVersion_RecordsTheLoaderAndWarns()
    {
        var directory = CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release());

        var run = await _host.RunAsync("loader", "adopt", "StarMap", directory);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Loader version: unknown", run.Output);
        Assert.Contains("could not read a file version", run.Error);
    }

    [Fact]
    public async Task Adopt_MismatchedGamePath_RecordsTheLoaderAndWarnsWithoutChangingIt()
    {
        var managedGame = Directory.CreateDirectory(Path.Combine(_host.Root, "ManagedGame")).FullName;
        var otherGame = Directory.CreateDirectory(Path.Combine(_host.Root, "OtherGame")).FullName;
        var directory = CreateLoaderDirectory("StarMap", "not a program", _host.Root, otherGame);
        _host.Mods.Listings.Add(LoaderFixtures.Listing());

        await _host.RunAsync("settings", "set", "game", managedGame);
        var run = await _host.RunAsync("loader", "adopt", "StarMap", directory);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("not the game directory Borea manages", run.Error);
        using var configuration = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "StarMapConfig.json")));
        Assert.Equal(otherGame, configuration.RootElement.GetProperty("GameLocation").GetString());
    }

    [Fact]
    public async Task Install_ExactReleaseWhoseDownloadIsGone_WarnsWithTheDate()
    {
        var installer = new FakeLoaderInstaller();
        _host.LoaderInstaller = installer;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.6", unavailableSince: new DateTimeOffset(2026, 9, 23, 10, 24, 0, TimeSpan.Zero)));

        var run = await _host.RunAsync("loader", "install", "StarMap", "--version", "0.4.6");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("warning: The download of StarMap 0.4.6 is gone from its host since 2026-09-23", run.Error);
        Assert.Equal("0.4.6", Assert.Single(installer.Calls).Release.Version.ToString());
    }

    [Fact]
    public async Task Adopt_InstalledReleaseWhoseDownloadIsGone_StillGetsItsVersion()
    {
        var adopter = new FakeLoaderAdopter { RawVersion = "0.4.6" };
        _host.LoaderAdopter = adopter;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.5"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.6", unavailableSince: new DateTimeOffset(2026, 9, 23, 10, 24, 0, TimeSpan.Zero)));
        // the index offers no release whose download is gone
        _host.Mods.AvailableVersions = (_, _) => Task.FromResult<IReadOnlyList<ModVersion>>([ModVersion.Parse("0.4.5")]);

        var adopt = await _host.RunAsync("loader", "adopt", "StarMap", _host.Root);
        var list = await _host.RunAsync("loader", "list", "--versions");

        Assert.Equal(0, adopt.ExitCode);
        Assert.Contains("Loader version: 0.4.6", adopt.Output);
        Assert.Empty(adopt.Error);
        Assert.Equal($"StarMap{Environment.NewLine}  0.4.5{Environment.NewLine}", list.Output);
    }

    [Fact]
    public async Task Uninstall_ReportsWhetherTheDirectoryWasRemoved()
    {
        var uninstaller = new FakeLoaderUninstaller
        {
            Result = new LoaderUninstallResult("StarMap", "C:\\Loaders\\StarMap", RecordRemoved: true, DirectoryRemoved: false),
        };
        _host.LoaderUninstaller = uninstaller;

        var run = await _host.RunAsync("loader", "uninstall", "StarMap");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("StarMap", Assert.Single(uninstaller.LoaderIds));
        Assert.Contains("remains", run.Output);
    }

    [Fact]
    public async Task Uninstall_WhileTheGameRuns_FailsAndKeepsTheRecordAndFolder()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_host.Paths.GetLoadersRoot(), "StarMap")).FullName;
        await File.WriteAllTextAsync(Path.Combine(directory, "StarMap.exe"), "loader");
        var settings = $"""
            [LoaderInstallations.StarMap]
            DirectoryPath = '{directory}'
            Version = '0.4.7'
            IsAdopted = false
            """;
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), settings);
        _host.GameRunning = true;

        var run = await _host.RunAsync("loader", "uninstall", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Close the game before you remove StarMap.", run.Error);
        Assert.DoesNotContain("was not recorded", run.Output);
        Assert.True(File.Exists(Path.Combine(directory, "StarMap.exe")));
        Assert.Equal(settings, await File.ReadAllTextAsync(_host.Paths.GetBoreaSettingsPath()));
    }

    [Fact]
    public async Task Install_PrintsTheInstallStepsOfTheLoaderAfterTheResult()
    {
        _host.LoaderInstaller = new FakeLoaderInstaller();
        _host.Mods.Listings.Add(LoaderFixtures.Listing(steps: ["Allow StarMap through your firewall."]));
        _host.Mods.Releases.Add(LoaderFixtures.Release());

        var run = await _host.RunAsync("loader", "install", "StarMap");

        Assert.Equal(0, run.ExitCode);
        var output = run.Output.ReplaceLineEndings("\n");
        Assert.True(output.IndexOf("Installed StarMap 0.4.6", StringComparison.Ordinal) < output.IndexOf("Install steps for StarMap:\n  1. Allow StarMap through your firewall.\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Uninstall_PrintsTheUninstallStepsOfTheIndexListing()
    {
        var listing = LoaderFixtures.Listing(uninstall: ["Delete the StarMap directory. The game runs unmodded again with no further cleanup."]);
        _host.IndexReader.Snapshot = new Borea.Core.Index.ContentIndexSnapshot(
            1,
            [new Borea.Core.Index.ContentIndexListing(listing.ModId, listing, Array.Empty<ModVersionMetadata>(), null)],
            Array.Empty<Borea.Core.Index.ContentIndexPack>(),
            null,
            Array.Empty<Borea.Core.Index.ContentIndexDiagnostic>());
        _host.LoaderUninstaller = new FakeLoaderUninstaller
        {
            Result = new LoaderUninstallResult("StarMap", "C:\\Loaders\\StarMap", RecordRemoved: true, DirectoryRemoved: true),
        };

        var run = await _host.RunAsync("loader", "uninstall", "StarMap");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Uninstall steps for StarMap:\n  1. Delete the StarMap directory. The game runs unmodded again with no further cleanup.\n", run.Output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Uninstall_IndexWithoutReadAccess_RemovesWithoutSteps()
    {
        _host.IndexReader.Read = _ => throw new UnauthorizedAccessException("Access to the cached index is denied.");
        _host.LoaderUninstaller = new FakeLoaderUninstaller
        {
            Result = new LoaderUninstallResult("StarMap", "C:\\Loaders\\StarMap", RecordRemoved: true, DirectoryRemoved: true),
        };

        var run = await _host.RunAsync("loader", "uninstall", "StarMap");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(string.Empty, run.Error);
        Assert.Contains("Removed loader StarMap", run.Output);
        Assert.DoesNotContain("steps", run.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UninstallVersion_WhileTheGameRuns_KeepsTheFolder()
    {
        var folder = CreateVersionFolder(_host, "0.4.7");
        _host.GameRunning = true;

        var run = await _host.RunAsync("loader", "uninstall", "StarMap", "--version", "0.4.7");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Close the game before you remove StarMap.", run.Error);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task UninstallVersion_PinnedByAnInstance_IsRefusedAndNamesTheInstance()
    {
        var folder = CreateVersionFolder(_host, "0.4.7");
        await SavePinnedInstanceAsync(_host, "Flight Test", "0.4.7");

        var run = await _host.RunAsync("loader", "uninstall", "StarMap", "--version", "0.4.7");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("'Flight Test' (pinned)", run.Error);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task Uninstall_OtherVersionStays_MovesTheRecordThereAndThenRefusesItsPinnedUser()
    {
        var pinned = CreateVersionFolder(_host, "0.4.7");
        var recorded = CreateVersionFolder(_host, "0.4.8");
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), $"[LoaderInstallations.StarMap]\nDirectoryPath = '{recorded}'\nVersion = '0.4.8'\nIsAdopted = false\n");
        await SavePinnedInstanceAsync(_host, "Flight Test", "0.4.7");

        var removed = await _host.RunAsync("loader", "uninstall", "StarMap");
        var refused = await _host.RunAsync("loader", "uninstall", "StarMap");
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, removed.ExitCode);
        Assert.False(Directory.Exists(recorded));
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("Borea keeps StarMap 0.4.7, because 'Flight Test' (pinned) starts it.", refused.Error);
        Assert.True(Directory.Exists(pinned));
        var version = Assert.Single(show.Json.GetProperty("loaderVersions").EnumerateArray());
        Assert.Equal("0.4.7", version.GetProperty("version").GetString());
        Assert.True(version.GetProperty("recorded").GetBoolean());
    }

    [Fact]
    public async Task UninstallUnused_RemovesTheUnusedVersionAndKeepsThePinnedOne()
    {
        var pinned = CreateVersionFolder(_host, "0.4.7");
        var unused = CreateVersionFolder(_host, "0.4.8");
        await SavePinnedInstanceAsync(_host, "Flight Test", "0.4.7");

        var run = await _host.RunAsync("loader", "uninstall", "StarMap", "--unused");
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Removed StarMap 0.4.8", run.Output);
        Assert.Contains("Kept StarMap 0.4.7, because 'Flight Test' (pinned) starts it.", run.Output);
        Assert.False(Directory.Exists(unused));
        Assert.True(Directory.Exists(pinned));
        var version = Assert.Single(show.Json.GetProperty("loaderVersions").EnumerateArray());
        Assert.Equal("0.4.7", version.GetProperty("version").GetString());
    }

    [Fact]
    public async Task UninstallUnused_KeepsTheVersionThatAnAutomaticInstanceStarts()
    {
        _host.IndexReader.Snapshot = LaunchCommandTests.StarMapIndex();
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.10.19.5638"), "2026.10.19.5638") };
        var older = CreateVersionFolder(_host, "0.4.7");
        var started = CreateVersionFolder(_host, "0.4.8");

        // the record is on the version that goes, so the loop must read the uses again after the record moves
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), $"[LoaderInstallations.StarMap]\nDirectoryPath = '{older}'\nVersion = '0.4.7'\nIsAdopted = false\n");
        var mod = ContentCommandFixtures.Release("flight-tools", loader: new LoaderRequirement("StarMap", ModVersion.Parse("0.4.0"), null));
        var instance = Instance.FromExisting(Guid.NewGuid(), "Flight Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, [new InstalledMod(mod.ModId, mod.Version, InstallReason.Manual, DateTimeOffset.UtcNow, mod)], isFavorite: false);
        await new FileInstanceRepository(_host.Paths).SaveAsync(instance);

        var refused = await _host.RunAsync("loader", "uninstall", "StarMap", "--version", "0.4.8");
        var run = await _host.RunAsync("loader", "uninstall", "StarMap", "--unused");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("'Flight Test' (automatic)", refused.Error);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Removed StarMap 0.4.7", run.Output);
        Assert.Contains("Kept StarMap 0.4.8, because 'Flight Test' (automatic) starts it.", run.Output);
        Assert.False(Directory.Exists(older));
        Assert.True(Directory.Exists(started));
    }

    [Fact]
    public async Task Install_WhileTheGameRuns_IsRefusedBeforeTheInstallerRuns()
    {
        var installer = new FakeLoaderInstaller();
        _host.LoaderInstaller = installer;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release());
        _host.GameRunning = true;

        var run = await _host.RunAsync("loader", "install", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Close the game before you install StarMap.", run.Error);
        Assert.Empty(installer.Calls);
    }

    /// <summary>A Borea-owned version folder Loaders/StarMap/&lt;version&gt; that the store lists.</summary>
    internal static string CreateVersionFolder(CliHost host, string version)
    {
        var folder = Directory.CreateDirectory(host.Paths.GetLoaderVersionDirectoryPath("StarMap", ModVersion.Parse(version))).FullName;
        File.WriteAllText(Path.Combine(folder, "StarMap.exe"), "loader");
        return folder;
    }

    internal static async Task SavePinnedInstanceAsync(CliHost host, string name, string version)
    {
        var instance = new Instance(name, InstanceSource.Custom.Value);
        instance.SetLoader(LoaderSetting.Pinned("StarMap", ModVersion.Parse(version)));
        await new FileInstanceRepository(host.Paths).SaveAsync(instance);
    }

    internal static string CreateLoaderDirectory(string loaderId, string executableContents, string root, string? gameDirectory = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, loaderId)).FullName;
        File.WriteAllText(Path.Combine(directory, "StarMap.exe"), executableContents);
        File.WriteAllText(
            Path.Combine(directory, "StarMapConfig.json"),
            $$"""
            {
              "GameLocation": "{{(gameDirectory ?? Path.Combine(root, "Game")).Replace("\\", "\\\\")}}"
            }
            """);
        return directory;
    }

    public void Dispose() => _host.Dispose();

    private sealed class FakeLoaderInstaller : ILoaderInstaller
    {
        public List<(ModMetadata Listing, ModVersionMetadata Release, string? Directory)> Calls { get; } = new();

        public Exception? Failure { get; init; }

        public Task<LoaderInstallResult> InstallAsync(
            ModMetadata loader,
            ModVersionMetadata release,
            string? directory = null,
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                throw Failure;

            Calls.Add((loader, release, directory));
            return Task.FromResult(new LoaderInstallResult(
                loader.ModId,
                release.Version,
                directory ?? "C:\\Loaders\\StarMap",
                new DownloadResult(release.Download.Url, 100, "ABC"),
                "StarMapConfig.json",
                Replaced: false));
        }
    }

    /// <summary>Matches the file version <see cref="RawVersion"/> against the releases the command passes.</summary>
    private sealed class FakeLoaderAdopter : ILoaderAdopter
    {
        public required string RawVersion { get; init; }

        public Task<LoaderAdoptionResult> AdoptAsync(ModMetadata loader, IReadOnlyList<ModVersionMetadata> releases, string directory, CancellationToken cancellationToken = default)
            => InspectAsync(loader, releases, directory, cancellationToken);

        public Task<LoaderAdoptionResult> InspectAsync(ModMetadata loader, IReadOnlyList<ModVersionMetadata> releases, string directory, CancellationToken cancellationToken = default)
        {
            var version = releases.FirstOrDefault(release => release.Version == ModVersion.Parse(RawVersion))?.Version;
            return Task.FromResult(new LoaderAdoptionResult(loader.ModId, directory, RawVersion, version, ConfiguredGameDirectory: null, GameDirectoryMatches: null, Warnings: []));
        }
    }

    private sealed class FakeLoaderUninstaller : ILoaderUninstaller
    {
        public List<string> LoaderIds { get; } = new();

        public required LoaderUninstallResult Result { get; init; }

        public Task<LoaderUninstallResult> UninstallAsync(string loaderId, CancellationToken cancellationToken = default)
        {
            LoaderIds.Add(loaderId);
            return Task.FromResult(Result);
        }

        public Task<LoaderUninstallResult> UninstallVersionAsync(string loaderId, ModVersion version, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
