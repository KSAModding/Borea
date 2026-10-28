using System.Text.Json.Nodes;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.Files;
using Borea.Storage.Game;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.Paths;
using Borea.Storage.Tests.Launch;
using Borea.Storage.Tests.ModPacks;

namespace Borea.Storage.Tests.Game;

public sealed class GameBuildKeeperTests : IDisposable
{
    private const string RealBuildFixture = "GameVersionFixture.dll";

    private const string OtherBuildFixture = "LoaderVersionFixture.dll";

    private static readonly GameVersion FixtureVersion = GameVersion.Parse("2026.8.3.5117");

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly GamePathProvider _paths;
    private readonly FileInstanceRepository _instances;

    public GameBuildKeeperTests()
    {
        _paths = new GamePathProvider(SettingsGame, new Dictionary<string, string> { ["StarMap"] = LoaderDirectory }, Path.Combine(_tempRoot, "Borea"), libraryFolder: Path.Combine(_tempRoot, "Library"));
        _instances = new FileInstanceRepository(_paths);
        WriteGame(SettingsGame);
    }

    private string SettingsGame => Path.Combine(_tempRoot, "Game");

    private string PlayerFolder => Path.Combine(_tempRoot, "Old");

    private string LoaderDirectory => Path.Combine(_tempRoot, "StarMap");

    private string LoaderConfig => Path.Combine(LoaderDirectory, "StarMapConfig.json");

    private string GameBuilds => _paths.GetGameBuildsRoot();

    [Fact]
    public async Task KeepAsync_CopiesTheBuildAndSetsTheBuildOfTheInstance()
    {
        var instance = await CreateInstanceAsync("Career");

        var result = await Keeper().KeepAsync(instance.InstanceId, FixtureVersion, null, CancellationToken.None);

        var kept = Path.Combine(GameBuilds, "2026.8.3.5117");
        Assert.Equal(GameBuildKeepRefusal.None, result.Refusal);
        Assert.Equal("<Astronomicals />", File.ReadAllText(Path.Combine(kept, "Content", "Core", "Astronomicals.xml")));
        Assert.Equal("app host", File.ReadAllText(Path.Combine(kept, "KSA.exe")));
        Assert.Equal(FixtureVersion, InstalledGameVersionProvider.Read(kept)?.Version);
        Assert.Equal(FixtureVersion, (await _instances.GetByIdAsync(instance.InstanceId))?.GameBuild);
        Assert.Equal([kept], Directory.GetFileSystemEntries(GameBuilds));
    }

    [Fact]
    public async Task KeepAsync_SecondInstanceOnTheSameBuild_UsesTheKeptCopy()
    {
        var first = await CreateInstanceAsync("Career");
        var second = await CreateInstanceAsync("Sandbox");
        var keeper = Keeper();
        await keeper.KeepAsync(first.InstanceId, FixtureVersion, null, CancellationToken.None);
        File.WriteAllText(Path.Combine(SettingsGame, "KSA.exe"), "patched app host");

        var check = keeper.Check(second);
        var result = await keeper.KeepAsync(second.InstanceId, FixtureVersion, null, CancellationToken.None);

        Assert.Equal(GameBuildKeepRefusal.None, result.Refusal);
        Assert.Equal(0, check.BytesToCopy);
        Assert.Equal("app host", File.ReadAllText(Path.Combine(GameBuilds, "2026.8.3.5117", "KSA.exe")));
        Assert.Equal(FixtureVersion, (await _instances.GetByIdAsync(second.InstanceId))?.GameBuild);
    }

    [Fact]
    public async Task KeepAsync_Cancelled_LeavesNoFolder()
    {
        var instance = await CreateInstanceAsync("Career");
        using var cancellation = new CancellationTokenSource();
        var progress = new SynchronousProgress<LibraryMoveProgress>(value =>
        {
            if (value.Files == 1)
                cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Keeper().KeepAsync(instance.InstanceId, FixtureVersion, progress, cancellation.Token));

        Assert.Empty(Directory.GetFileSystemEntries(GameBuilds));
        Assert.Null((await _instances.GetByIdAsync(instance.InstanceId))?.GameBuild);
    }

    [Fact]
    public async Task KeepAsync_LeftoverOfAnInterruptedCopy_IsDeleted()
    {
        var instance = await CreateInstanceAsync("Career");
        var leftover = Path.Combine(GameBuilds, ".2026.8.2.5100.copying");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "KSA.dll"), "partial");

        await Keeper().KeepAsync(instance.InstanceId, FixtureVersion, null, CancellationToken.None);

        Assert.Equal([Path.Combine(GameBuilds, "2026.8.3.5117")], Directory.GetFileSystemEntries(GameBuilds));
    }

    [Fact]
    public async Task KeepAsync_BuildChangedAfterTheCheck_IsRefused()
    {
        var instance = await CreateInstanceAsync("Career");
        var keeper = Keeper();
        var check = keeper.Check(instance);
        File.Copy(Path.Combine(AppContext.BaseDirectory, OtherBuildFixture), Path.Combine(SettingsGame, "KSA.dll"), overwrite: true);

        var result = await keeper.KeepAsync(instance.InstanceId, check.Version!.Value, null, CancellationToken.None);

        Assert.Equal(FixtureVersion, check.Version);
        Assert.Equal(GameBuildKeepRefusal.BuildChanged, result.Refusal);
        Assert.False(Directory.Exists(GameBuilds));
        Assert.Null((await _instances.GetByIdAsync(instance.InstanceId))?.GameBuild);
    }

    [Theory]
    [InlineData(GameBuildKeepRefusal.GameRunning, false)]
    [InlineData(GameBuildKeepRefusal.GameRunning, true)]
    [InlineData(GameBuildKeepRefusal.WineWrapper, false)]
    public async Task KeepAsync_GameRunningOrInAWineWrapper_IsRefused(GameBuildKeepRefusal refusal, bool startedByBorea)
    {
        var instance = await CreateInstanceAsync("Career");
        var running = await CreateInstanceAsync("Sandbox");
        var bundle = Path.Combine(_tempRoot, "Kitten Space Agency.app");
        var wine = new FakeWineProbe(new WineInstall(
            Path.GetFullPath(refusal == GameBuildKeepRefusal.WineWrapper ? _tempRoot : Path.Combine(_tempRoot, "Elsewhere")),
            WineFixtures.Drives(new Dictionary<char, string>()),
            new WineWrapper(bundle, Path.Combine(bundle, "Contents", "MacOS", "launcher"))));
        var keeper = new GameBuildKeeper(
            _paths,
            _instances,
            Loaders(),
            new FixedVersions(),
            () => refusal == GameBuildKeepRefusal.GameRunning && !startedByBorea,
            id => startedByBorea && id == running.InstanceId,
            wine,
            new UnknownFreeSpace(),
            OsPlatform.Windows);

        var result = await keeper.KeepAsync(instance.InstanceId, FixtureVersion, null, CancellationToken.None);

        Assert.Equal(refusal, result.Refusal);
        Assert.False(Directory.Exists(GameBuilds));
        Assert.Null((await _instances.GetByIdAsync(instance.InstanceId))?.GameBuild);
    }

    [Theory]
    [InlineData(-1L, GameBuildKeepRefusal.NotEnoughSpace)]
    [InlineData(0L, GameBuildKeepRefusal.None)]
    public async Task Check_FreeSpaceAroundTheMargin_RefusesOnlyBelowIt(long offset, GameBuildKeepRefusal refusal)
    {
        var instance = await CreateInstanceAsync("Career");
        var bytes = Keeper().Check(instance).BytesToCopy;

        var check = Keeper(freeSpace: new FixedFreeSpace(bytes + DriveInstallSpaceCheck.MarginFor(bytes) + offset)).Check(instance);

        Assert.Equal(refusal, check.Refusal);
        Assert.Equal(@"D:\", check.VolumeName);
    }

    // the change comes with the report after the first file, and whether it hits a copied file or one still to copy,
    // the keep is refused
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeepAsync_SourceChangesDuringTheCopy_IsRefusedAndLeavesNoFolder(bool delete)
    {
        var instance = await CreateInstanceAsync("Career");
        var changed = Path.Combine(SettingsGame, "Content", "Core", "Astronomicals.xml");
        var progress = new SynchronousProgress<LibraryMoveProgress>(value =>
        {
            if (value.Files != 1)
                return;

            if (delete)
                File.Delete(changed);
            else
                File.WriteAllText(changed, "<Astronomicals patched=\"true\" />");
        });

        var result = await Keeper().KeepAsync(instance.InstanceId, FixtureVersion, progress, CancellationToken.None);

        Assert.Equal(GameBuildKeepRefusal.ChangedDuringCopy, result.Refusal);
        Assert.Empty(Directory.GetFileSystemEntries(GameBuilds));
        Assert.Null((await _instances.GetByIdAsync(instance.InstanceId))?.GameBuild);
    }

    [Fact]
    public async Task Check_FolderLinkInTheSource_IsRefusedWithTheLink()
    {
        var instance = await CreateInstanceAsync("Career");
        WriteGame(PlayerFolder);
        var shared = Path.Combine(_tempRoot, "Shared");
        Directory.CreateDirectory(shared);
        var linker = new DirectoryLinker();
        Assert.True(linker.TryCreate(Path.Combine(SettingsGame, "Mods"), shared).Linked);
        Assert.True(linker.TryCreate(Path.Combine(PlayerFolder, "Mods"), shared).Linked);
        var keeper = Keeper();

        var game = keeper.Check(instance);
        var folder = keeper.CheckFolder(PlayerFolder);

        Assert.Equal(GameBuildKeepRefusal.HoldsFolderLink, game.Refusal);
        Assert.Equal(Path.GetFullPath(Path.Combine(SettingsGame, "Mods")), game.LinkPath);
        Assert.Equal(GameBuildKeepRefusal.HoldsFolderLink, folder.Refusal);
        Assert.Equal(Path.GetFullPath(Path.Combine(PlayerFolder, "Mods")), folder.LinkPath);
    }

    // WriteGame writes KSA.exe and no Linux app host, so the macOS row also shows that the wrapper comes first
    [Theory]
    [InlineData(OsPlatform.Windows, false, GameBuildKeepRefusal.None)]
    [InlineData(OsPlatform.MacOs, true, GameBuildKeepRefusal.WineWrapper)]
    [InlineData(OsPlatform.Linux, false, GameBuildKeepRefusal.NotAGame)]
    public async Task CheckGameFolder_WrapperOrWindowsBuildOnLinux_RefusesTheKeepOfAnInstance(OsPlatform host, bool inWrapper, GameBuildKeepRefusal refusal)
    {
        var instance = await CreateInstanceAsync("Career");
        var bundle = Path.Combine(_tempRoot, "Kitten Space Agency.app");
        var wine = new FakeWineProbe(new WineInstall(
            Path.GetFullPath(inWrapper ? _tempRoot : Path.Combine(_tempRoot, "Elsewhere")),
            WineFixtures.Drives(new Dictionary<char, string>()),
            inWrapper ? new WineWrapper(bundle, Path.Combine(bundle, "Contents", "MacOS", "launcher")) : null));
        var keeper = Keeper(host: host, wine: wine);

        Assert.Equal(refusal, keeper.CheckGameFolder());
        Assert.Equal(refusal, keeper.Check(instance).Refusal);
    }

    [Fact]
    public async Task UseCurrentBuildAsync_ClearsTheBuildAndKeepsTheSaves()
    {
        var instance = await CreateInstanceAsync("Career");
        var keeper = Keeper();
        await keeper.KeepAsync(instance.InstanceId, FixtureVersion, null, CancellationToken.None);
        var save = Path.Combine(_paths.GetInstanceSavesFolder(instance.InstanceId), "Orbit", "meta.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(save)!);
        File.WriteAllText(save, "Version = \"2026.8.3.5117\"");

        var result = await keeper.UseCurrentBuildAsync(instance.InstanceId, CancellationToken.None);

        Assert.Equal(GameBuildChangeRefusal.None, result);
        Assert.Null((await _instances.GetByIdAsync(instance.InstanceId))?.GameBuild);
        Assert.Equal("Version = \"2026.8.3.5117\"", File.ReadAllText(save));
        Assert.Equal(FixtureVersion, InstalledGameVersionProvider.Read(Path.Combine(GameBuilds, "2026.8.3.5117"))?.Version);
    }

    [Fact]
    public async Task RemoveAsync_RemovesOnlyABuildNoInstanceUses()
    {
        var instance = await CreateInstanceAsync("Career");
        var keeper = Keeper();
        await keeper.KeepAsync(instance.InstanceId, FixtureVersion, null, CancellationToken.None);
        var folder = Path.Combine(GameBuilds, "2026.8.3.5117");
        var settingsBytes = new DirectoryInfo(SettingsGame).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);

        var listed = Assert.Single(await keeper.ListKeptAsync(CancellationToken.None));
        var inUse = await keeper.RemoveAsync(FixtureVersion, CancellationToken.None);

        Assert.Equal(FixtureVersion, listed.Version);
        Assert.Equal(folder, listed.Folder);
        Assert.Equal(settingsBytes, listed.Bytes);
        Assert.Equal(["Career"], listed.InstanceNames);
        Assert.False(listed.IsGameFolderOfSettings);
        Assert.Equal(GameBuildChangeRefusal.InUse, inUse);
        Assert.Equal(FixtureVersion, InstalledGameVersionProvider.Read(folder)?.Version);

        await keeper.UseCurrentBuildAsync(instance.InstanceId, CancellationToken.None);
        var unused = Assert.Single(await keeper.ListKeptAsync(CancellationToken.None));
        var removed = await keeper.RemoveAsync(FixtureVersion, CancellationToken.None);

        Assert.Empty(unused.InstanceNames);
        Assert.Equal(GameBuildChangeRefusal.None, removed);
        Assert.Empty(Directory.GetFileSystemEntries(GameBuilds));
        Assert.Empty(await keeper.ListKeptAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_GameFolderOfTheSettings_KeepsTheBuild()
    {
        var instance = await CreateInstanceAsync("Career");
        await Keeper().KeepAsync(instance.InstanceId, FixtureVersion, null, CancellationToken.None);
        await Keeper().UseCurrentBuildAsync(instance.InstanceId, CancellationToken.None);
        var folder = Path.Combine(GameBuilds, "2026.8.3.5117");
        var keeper = Keeper(new GamePathProvider(folder, boreaRoot: Path.Combine(_tempRoot, "Borea"), libraryFolder: Path.Combine(_tempRoot, "Library")));

        var listed = Assert.Single(await keeper.ListKeptAsync(CancellationToken.None));
        var result = await keeper.RemoveAsync(FixtureVersion, CancellationToken.None);

        Assert.Empty(listed.InstanceNames);
        Assert.True(listed.IsGameFolderOfSettings);
        Assert.Equal(GameBuildChangeRefusal.GameFolderOfSettings, result);
        Assert.Equal(FixtureVersion, InstalledGameVersionProvider.Read(folder)?.Version);
    }

    [Fact]
    public async Task RemoveAsync_LoaderOnTheBuild_GetsTheGameFolderOfTheSettings()
    {
        var folder = await KeepUnusedBuildAsync();
        WriteLoaderConfig(folder);

        var result = await Keeper().RemoveAsync(FixtureVersion, CancellationToken.None);

        var json = JsonNode.Parse(File.ReadAllText(LoaderConfig))!;
        Assert.Equal(GameBuildChangeRefusal.None, result);
        Assert.Equal(SettingsGame, json["GameLocation"]!.GetValue<string>());
        Assert.True(json["Keep"]!.GetValue<bool>());
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task RemoveAsync_OtherVersionFolderOnTheBuild_GetsTheGameFolderOfTheSettings()
    {
        var folder = await KeepUnusedBuildAsync();
        var original = WriteLoaderConfig(Path.Combine(_tempRoot, "Other Game"));
        var version = Path.Combine(_tempRoot, "Borea", "Loaders", "StarMap", "0.4.7");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "StarMapConfig.json"), new JsonObject { ["GameLocation"] = folder }.ToJsonString());
        var versions = new FixedVersions(new InstalledLoaderVersion("StarMap", ModVersion.Parse("0.4.7"), RawVersion: null, version, IsAdopted: false, IsRecorded: false));

        var result = await Keeper(versions: versions).RemoveAsync(FixtureVersion, CancellationToken.None);

        Assert.Equal(GameBuildChangeRefusal.None, result);
        Assert.Equal(SettingsGame, JsonNode.Parse(File.ReadAllText(Path.Combine(version, "StarMapConfig.json")))!["GameLocation"]!.GetValue<string>());
        Assert.Equal(original, File.ReadAllText(LoaderConfig));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task RemoveAsync_LoaderOnAnotherFolder_LeavesItsFile()
    {
        var folder = await KeepUnusedBuildAsync();
        var original = WriteLoaderConfig(Path.Combine(_tempRoot, "Other Game"));

        var result = await Keeper().RemoveAsync(FixtureVersion, CancellationToken.None);

        Assert.Equal(GameBuildChangeRefusal.None, result);
        Assert.Equal(original, File.ReadAllText(LoaderConfig));
        Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData(GameBuildChangeRefusal.InUse)]
    [InlineData(GameBuildChangeRefusal.GameRunning)]
    public async Task RemoveAsync_Refused_LeavesTheLoaderAndTheBuild(GameBuildChangeRefusal refusal)
    {
        var instance = await CreateInstanceAsync("Career");
        await Keeper().KeepAsync(instance.InstanceId, FixtureVersion, null, CancellationToken.None);
        if (refusal == GameBuildChangeRefusal.GameRunning)
            await Keeper().UseCurrentBuildAsync(instance.InstanceId, CancellationToken.None);

        var folder = Path.Combine(GameBuilds, "2026.8.3.5117");
        var original = WriteLoaderConfig(folder);

        var result = await Keeper(gameRunning: refusal == GameBuildChangeRefusal.GameRunning).RemoveAsync(FixtureVersion, CancellationToken.None);

        Assert.Equal(refusal, result);
        Assert.Equal(original, File.ReadAllText(LoaderConfig));
        Assert.Equal(FixtureVersion, InstalledGameVersionProvider.Read(folder)?.Version);
    }

    [Fact]
    public async Task RemoveAsync_ListingsUnreadable_RemovesTheBuild()
    {
        var folder = await KeepUnusedBuildAsync();

        var result = await Keeper(listings: new UnreadableListings()).RemoveAsync(FixtureVersion, CancellationToken.None);

        Assert.Equal(GameBuildChangeRefusal.None, result);
        Assert.False(Directory.Exists(folder));
    }

    [WindowsFact("A read-only file refuses a replacement only on Windows.")]
    public async Task RemoveAsync_LoaderFileReadOnly_NamesTheRemovalAndKeepsTheBuild()
    {
        var folder = await KeepUnusedBuildAsync();
        var original = WriteLoaderConfig(folder);
        File.SetAttributes(LoaderConfig, FileAttributes.ReadOnly);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Keeper().RemoveAsync(FixtureVersion, CancellationToken.None));

            Assert.Contains("so Borea removed nothing", exception.Message);
            Assert.Equal(original, File.ReadAllText(LoaderConfig));
            Assert.Equal(FixtureVersion, InstalledGameVersionProvider.Read(folder)?.Version);
        }
        finally
        {
            File.SetAttributes(LoaderConfig, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task UseCurrentBuildAsyncAndRemoveAsync_WhileACopyRuns_AreBusy()
    {
        var copying = await CreateInstanceAsync("Career");
        var other = await CreateInstanceAsync("Sandbox");
        var keeper = Keeper();
        using var copyStarted = new ManualResetEventSlim();
        using var copyMayGoOn = new ManualResetEventSlim();
        var progress = new SynchronousProgress<LibraryMoveProgress>(value =>
        {
            if (value.Files == 1)
            {
                copyStarted.Set();
                copyMayGoOn.Wait();
            }
        });

        var keep = Task.Run(() => keeper.KeepAsync(copying.InstanceId, FixtureVersion, progress, CancellationToken.None));
        GameBuildChangeRefusal cleared;
        GameBuildChangeRefusal removed;
        try
        {
            Assert.True(copyStarted.Wait(TimeSpan.FromSeconds(30)));
            cleared = await keeper.UseCurrentBuildAsync(other.InstanceId, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            removed = await keeper.RemoveAsync(FixtureVersion, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            copyMayGoOn.Set();
        }

        Assert.Equal(GameBuildChangeRefusal.Busy, cleared);
        Assert.Equal(GameBuildChangeRefusal.Busy, removed);
        Assert.Equal(GameBuildKeepRefusal.None, (await keep).Refusal);
    }

    // Linux cannot start the Windows build that WriteGame writes, and macOS starts no copy
    [Theory]
    [InlineData(OsPlatform.Windows, "KSA.exe")]
    [InlineData(OsPlatform.Windows, "KSA.dll")]
    [InlineData(OsPlatform.Linux, null)]
    [InlineData(OsPlatform.MacOs, null)]
    public async Task KeepFolderAsync_FolderWithoutTheHostExecutableOrVersion_IsRefused(OsPlatform host, string? brokenFile)
    {
        WriteGame(PlayerFolder);
        if (brokenFile == "KSA.exe")
            File.Delete(Path.Combine(PlayerFolder, "KSA.exe"));
        else if (brokenFile == "KSA.dll")
            File.WriteAllText(Path.Combine(PlayerFolder, "KSA.dll"), "not an assembly");
        var keeper = Keeper(host: host);

        var check = keeper.CheckFolder(PlayerFolder);
        var result = await keeper.KeepFolderAsync(PlayerFolder, FixtureVersion, null, CancellationToken.None);

        Assert.Equal(GameBuildKeepRefusal.NotAGame, check.Refusal);
        Assert.Equal(GameBuildKeepRefusal.NotAGame, result.Refusal);
        Assert.False(Directory.Exists(GameBuilds));
    }

    [Fact]
    public async Task KeepFolderAsync_BuildAlreadyKept_DoesNotCopyAgain()
    {
        WriteGame(PlayerFolder);
        var keeper = Keeper();
        await keeper.KeepFolderAsync(PlayerFolder, FixtureVersion, null, CancellationToken.None);
        File.WriteAllText(Path.Combine(PlayerFolder, "KSA.exe"), "patched app host");

        var check = keeper.CheckFolder(PlayerFolder);
        var result = await keeper.KeepFolderAsync(PlayerFolder, FixtureVersion, null, CancellationToken.None);

        var kept = Path.Combine(GameBuilds, "2026.8.3.5117");
        Assert.Equal(GameBuildKeepRefusal.None, result.Refusal);
        Assert.Equal(0, check.BytesToCopy);
        Assert.Equal("app host", File.ReadAllText(Path.Combine(kept, "KSA.exe")));
        Assert.Equal([kept], Directory.GetFileSystemEntries(GameBuilds));
        Assert.Equal([FixtureVersion], keeper.ListKept());
    }

    [Fact]
    public void CheckFolder_LinuxBuildOnLinux_CanBeKept()
    {
        WriteGame(PlayerFolder);
        File.Move(Path.Combine(PlayerFolder, "KSA.exe"), Path.Combine(PlayerFolder, "KSA"));

        var check = Keeper(host: OsPlatform.Linux).CheckFolder(PlayerFolder);

        Assert.Equal(GameBuildKeepRefusal.None, check.Refusal);
        Assert.Equal(FixtureVersion, check.Version);
        Assert.True(check.BytesToCopy > 0);
    }

    private static void WriteGame(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "Content", "Core"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, RealBuildFixture), Path.Combine(folder, "KSA.dll"));
        File.WriteAllText(Path.Combine(folder, "Content", "Core", "Astronomicals.xml"), "<Astronomicals />");
        File.WriteAllText(Path.Combine(folder, "KSA.exe"), "app host");
    }

    private GameBuildKeeper Keeper(GamePathProvider? paths = null, bool gameRunning = false, IModRepository? listings = null, OsPlatform host = OsPlatform.Windows, ILoaderVersionStore? versions = null, IFreeSpaceProbe? freeSpace = null, IWinePrefixProbe? wine = null) => new(paths ?? _paths, _instances, listings ?? Loaders(), versions ?? new FixedVersions(), () => gameRunning, _ => false, wine ?? new FakeWineProbe(new WineInstall(Path.GetFullPath(Path.Combine(_tempRoot, "Elsewhere")), WineFixtures.Drives(new Dictionary<char, string>()), null)), freeSpace ?? new UnknownFreeSpace(), host);

    private static ModPackInstallerTests.FakeModRepository Loaders() => new([], [LoaderLauncherTests.LoaderListing(provides: LoaderLauncherTests.StarMapProvides())]);

    private async Task<string> KeepUnusedBuildAsync()
    {
        var instance = await CreateInstanceAsync("Career");
        await Keeper().KeepAsync(instance.InstanceId, FixtureVersion, null, CancellationToken.None);
        await Keeper().UseCurrentBuildAsync(instance.InstanceId, CancellationToken.None);
        return Path.Combine(GameBuilds, "2026.8.3.5117");
    }

    private string WriteLoaderConfig(string gameLocation)
    {
        var text = new JsonObject { ["GameLocation"] = gameLocation, ["Keep"] = true }.ToJsonString();
        Directory.CreateDirectory(LoaderDirectory);
        File.WriteAllText(LoaderConfig, text);
        return text;
    }

    private async Task<Instance> CreateInstanceAsync(string name)
    {
        var instance = new Instance(name, InstanceSource.Custom.Value);
        await _instances.CreateAsync(instance);
        return instance;
    }

    public void Dispose() => DirectoryLinks.DeleteTreeWithoutFollowingLinks(_tempRoot);

    private sealed class FixedVersions(params InstalledLoaderVersion[] versions) : ILoaderVersionStore
    {
        public Task<IReadOnlyList<InstalledLoaderVersion>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InstalledLoaderVersion>>(versions);
    }

    private sealed class UnknownFreeSpace : IFreeSpaceProbe
    {
        public FreeSpace? Measure(string path) => null;
    }

    private sealed class FixedFreeSpace(long available) : IFreeSpaceProbe
    {
        public FreeSpace? Measure(string path) => new(@"D:\", available);
    }

    private sealed class UnreadableListings : IModRepository
    {
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("The index does not exist.");
        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
