using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.State;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class InstallStopTests : IAsyncLifetime
{
    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileInstanceRepository _instances;
    private readonly FileModStateRepository _modState;
    private readonly FakeModDownloader _downloader = new();
    private readonly InstallPlanExecutor _executor;
    private readonly InstallStop _stop = new();
    private Guid _instanceId;

    public InstallStopTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _instances = new FileInstanceRepository(_pathProvider);
        _modState = new FileModStateRepository(_pathProvider);
        _executor = new InstallPlanExecutor(
            _instances,
            new FileModInstaller(_pathProvider, _downloader, _instances, _modState),
            new FileModReplacer(_pathProvider, _downloader, _instances, _modState));
        _downloader.Bytes = TestArchives.Build(("content/mod.toml", "name = \"mod\""));
    }

    public async Task InitializeAsync()
    {
        var instance = (await _instances.CreateAsync("Test", InstanceSource.Custom.Value)).Instance;
        _instanceId = instance.InstanceId;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Execute_StopDuringTheDownload_LeavesNoArchiveAndNoChange()
    {
        _downloader.Downloading = async (_, token) =>
        {
            _stop.Request();
            await Task.Delay(Timeout.Infinite, token);
        };

        var stopped = await Assert.ThrowsAsync<InstallStoppedException>(async () => await _executor.ExecuteAsync(await PlanAsync("first-mod"), enable: true, stop: _stop));

        Assert.Equal(0, stopped.Completed);
        Assert.Equal(1, stopped.Total);
        Assert.All(_downloader.ArchivePaths, path => Assert.False(File.Exists(path)));
        Assert.Empty((await _instances.GetByIdAsync(_instanceId))!.Mods);
        Assert.Empty(await _modState.GetEntriesAsync(_instanceId));
        Assert.False(Directory.Exists(_pathProvider.GetInstanceModsFolder(_instanceId)));
    }

    [Fact]
    public async Task Execute_StopDuringTheDownloadOfAnUpdate_KeepsTheOldVersion()
    {
        await _executor.ExecuteAsync(await PlanAsync("first-mod"), enable: true);
        _downloader.Downloading = async (_, token) =>
        {
            _stop.Request();
            await Task.Delay(Timeout.Infinite, token);
        };

        var stopped = await Assert.ThrowsAsync<InstallStoppedException>(async () => await _executor.ExecuteAsync(await PlanAsync(Release("first-mod", "2.0.0")), enable: true, stop: _stop));

        Assert.Equal(0, stopped.Completed);
        Assert.All(_downloader.ArchivePaths, path => Assert.False(File.Exists(path)));
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods).Version);
        Assert.True(File.Exists(Path.Combine(_pathProvider.GetInstanceModsFolder(_instanceId), "first-mod", "mod.toml")));
    }

    [Fact]
    public async Task Execute_StopDuringTheSecondMod_KeepsTheFirstAndReportsHowFarItGot()
    {
        _downloader.Downloading = async (release, token) =>
        {
            if (release.ModId != "second-mod")
                return;

            _stop.Request();
            await Task.Delay(Timeout.Infinite, token);
        };

        var stopped = await Assert.ThrowsAsync<InstallStoppedException>(async () => await _executor.ExecuteAsync(await PlanAsync("first-mod", "second-mod"), enable: true, stop: _stop));

        Assert.Equal(1, stopped.Completed);
        Assert.Equal(2, stopped.Total);
        Assert.Equal("first-mod", Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods).ModId);
        Assert.True(File.Exists(Path.Combine(_pathProvider.GetInstanceModsFolder(_instanceId), "first-mod", "mod.toml")));
        Assert.False(Directory.Exists(Path.Combine(_pathProvider.GetInstanceModsFolder(_instanceId), "second-mod")));
    }

    [Fact]
    public async Task Execute_StopDuringTheExtraction_FinishesThatModThenStops()
    {
        var progress = new SynchronousProgress<InstallProgress>(report =>
        {
            if (report.Phase == InstallPhase.Extracting)
                _stop.Request();
        });

        var stopped = await Assert.ThrowsAsync<InstallStoppedException>(async () => await _executor.ExecuteAsync(await PlanAsync("first-mod", "second-mod"), enable: true, progress, _stop));

        Assert.Equal(1, stopped.Completed);
        Assert.Single(_downloader.ArchivePaths);
        var installed = Assert.Single((await _instances.GetByIdAsync(_instanceId))!.Mods);
        Assert.Equal("first-mod", installed.ModId);
        Assert.Equal("first-mod", Assert.Single(await _modState.GetEntriesAsync(_instanceId)).ModId);
    }

    private Task<InstallPlan> PlanAsync(params string[] modIds) => PlanAsync(modIds.Select(modId => Release(modId)).ToArray());

    private async Task<InstallPlan> PlanAsync(params ModVersionMetadata[] releases)
    {
        var instance = await _instances.GetByIdAsync(_instanceId);
        var operations = releases.Select(release => new PlannedInstall(release, InstallReason.Manual, GameCompatibility.Compatible, null)).ToList();
        return new InstallPlan(_instanceId, InstallPlanningState.Capture(instance!), [], operations, [], [], [], []);
    }

    private static ModVersionMetadata Release(string modId, string version = "1.0.0") => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: new DownloadInfo($"https://example.com/{modId}.zip", null, null, "application/zip"),
        installSizeBytes: null,
        dependencies: Array.Empty<ModDependency>());
}
