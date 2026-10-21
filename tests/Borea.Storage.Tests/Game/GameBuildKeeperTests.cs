using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.Files;
using Borea.Storage.Game;
using Borea.Storage.Instances;
using Borea.Storage.Paths;

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
        _paths = new GamePathProvider(SettingsGame, boreaRoot: Path.Combine(_tempRoot, "Borea"), libraryFolder: Path.Combine(_tempRoot, "Library"));
        _instances = new FileInstanceRepository(_paths);
        Directory.CreateDirectory(Path.Combine(SettingsGame, "Content", "Core"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, RealBuildFixture), Path.Combine(SettingsGame, "KSA.dll"));
        File.WriteAllText(Path.Combine(SettingsGame, "Content", "Core", "Astronomicals.xml"), "<Astronomicals />");
        File.WriteAllText(Path.Combine(SettingsGame, "KSA.exe"), "app host");
    }

    private string SettingsGame => Path.Combine(_tempRoot, "Game");

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
            () => refusal == GameBuildKeepRefusal.GameRunning && !startedByBorea,
            id => startedByBorea && id == running.InstanceId,
            wine,
            new UnknownFreeSpace());

        var result = await keeper.KeepAsync(instance.InstanceId, FixtureVersion, null, CancellationToken.None);

        Assert.Equal(refusal, result.Refusal);
        Assert.False(Directory.Exists(GameBuilds));
        Assert.Null((await _instances.GetByIdAsync(instance.InstanceId))?.GameBuild);
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

    private GameBuildKeeper Keeper() => Keeper(_paths);

    private GameBuildKeeper Keeper(GamePathProvider paths) => new(paths, _instances, () => false, _ => false, new FakeWineProbe(new WineInstall(Path.GetFullPath(Path.Combine(_tempRoot, "Elsewhere")), WineFixtures.Drives(new Dictionary<char, string>()), null)), new UnknownFreeSpace());

    private async Task<Instance> CreateInstanceAsync(string name)
    {
        var instance = new Instance(name, InstanceSource.Custom.Value);
        await _instances.CreateAsync(instance);
        return instance;
    }

    public void Dispose() => DirectoryLinks.DeleteTreeWithoutFollowingLinks(_tempRoot);

    private sealed class UnknownFreeSpace : IFreeSpaceProbe
    {
        public FreeSpace? Measure(string path) => null;
    }
}
