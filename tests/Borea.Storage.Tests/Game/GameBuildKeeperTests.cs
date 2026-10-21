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

    private GameBuildKeeper Keeper() => new(_paths, _instances, () => false, _ => false, new FakeWineProbe(new WineInstall(Path.GetFullPath(Path.Combine(_tempRoot, "Elsewhere")), WineFixtures.Drives(new Dictionary<char, string>()), null)), new UnknownFreeSpace());

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
