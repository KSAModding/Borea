using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Storage.Files;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.State;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class DriveInstallSpaceCheckTests : IAsyncLifetime
{
    private const long Gigabyte = 1000L * 1000 * 1000;

    private readonly string _tempRoot;
    private readonly string _downloadFolder;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileInstanceRepository _instances;
    private readonly FileModStateRepository _modState;
    private readonly FakeModDownloader _downloader = new();
    private Guid _instanceId;

    public DriveInstallSpaceCheckTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _downloadFolder = Path.Combine(_tempRoot, "downloads");
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _instances = new FileInstanceRepository(_pathProvider);
        _modState = new FileModStateRepository(_pathProvider);
        _downloader.Bytes = TestArchives.Build(("content/mod.toml", "name = \"mod\""));
    }

    public async Task InitializeAsync()
    {
        _instanceId = (await _instances.CreateAsync("Test", InstanceSource.Custom.Value)).Instance.InstanceId;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task EnsureFits_UnpackedSizeAboveTheFreeSpace_Refuses()
    {
        var check = Check(("library", 2 * Gigabyte), ("downloads", 50 * Gigabyte));
        var plan = await PlanAsync(Release("first-mod", downloadBytes: Gigabyte, installBytes: 4 * Gigabyte));

        var refusal = Assert.Throws<InsufficientDiskSpaceException>(() => check.EnsureFits(plan));

        Assert.Equal("library", refusal.VolumeName);
        Assert.Equal(2 * Gigabyte, refusal.AvailableBytes);
        Assert.True(refusal.RequiredBytes > 4 * Gigabyte);
        Assert.Contains("free", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureFits_ArchiveAboveTheFreeSpaceOfTheDownloadFolder_Refuses()
    {
        var check = Check(("library", 50 * Gigabyte), ("downloads", Gigabyte));
        var plan = await PlanAsync(Release("first-mod", downloadBytes: 3 * Gigabyte, installBytes: Gigabyte));

        var refusal = Assert.Throws<InsufficientDiskSpaceException>(() => check.EnsureFits(plan));

        Assert.Equal("downloads", refusal.VolumeName);
    }

    [Fact]
    public async Task EnsureFits_OneVolumeForBothFolders_CountsTheArchiveAndTheUnpackedFiles()
    {
        var check = Check(("one", 5 * Gigabyte), ("one", 5 * Gigabyte));
        var plan = await PlanAsync(Release("first-mod", downloadBytes: 2 * Gigabyte, installBytes: 4 * Gigabyte));

        var refusal = Assert.Throws<InsufficientDiskSpaceException>(() => check.EnsureFits(plan));

        Assert.Equal("one", refusal.VolumeName);
        Assert.True(refusal.RequiredBytes > 6 * Gigabyte);
    }

    [Fact]
    public async Task EnsureFits_PlanThatFits_Passes()
    {
        var check = Check(("library", 50 * Gigabyte), ("downloads", 50 * Gigabyte));
        var plan = await PlanAsync(Release("first-mod", downloadBytes: Gigabyte, installBytes: 4 * Gigabyte));

        check.EnsureFits(plan);
    }

    [Fact]
    public async Task EnsureFits_ReleasesWithoutSizes_Passes()
    {
        var check = Check(("library", 1000), ("downloads", 1000));
        var plan = await PlanAsync(Release("first-mod"), Release("second-mod"));

        check.EnsureFits(plan);
    }

    [Fact]
    public async Task EnsureFits_VolumeThatCannotBeRead_Passes()
    {
        var check = new DriveInstallSpaceCheck(_pathProvider, new FakeFreeSpaceProbe(_ => null), _downloadFolder);
        var plan = await PlanAsync(Release("first-mod", downloadBytes: 9 * Gigabyte, installBytes: 40 * Gigabyte));

        check.EnsureFits(plan);
    }

    private DriveInstallSpaceCheck Check((string Volume, long Free) library, (string Volume, long Free) downloads)
    {
        var probe = new FakeFreeSpaceProbe(path => path == _downloadFolder
            ? new FreeSpace(downloads.Volume, downloads.Free)
            : new FreeSpace(library.Volume, library.Free));
        return new DriveInstallSpaceCheck(_pathProvider, probe, _downloadFolder);
    }

    private async Task<InstallPlan> PlanAsync(params ModVersionMetadata[] releases)
    {
        var instance = await _instances.GetByIdAsync(_instanceId);
        var operations = releases.Select(release => new PlannedInstall(release, InstallReason.Manual, GameCompatibility.Compatible, null)).ToList();
        return new InstallPlan(_instanceId, InstallPlanningState.Capture(instance!), [], operations, [], [], [], []);
    }

    private static ModVersionMetadata Release(string modId, long? downloadBytes = null, long? installBytes = null) => new(
        specVersion: 1,
        modId: modId,
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        gameMin: "2026.7.4.2131",
        gameMinRevision: 2131,
        download: new DownloadInfo($"https://example.com/{modId}.zip", null, downloadBytes, "application/zip"),
        installSizeBytes: installBytes,
        dependencies: Array.Empty<ModDependency>());

    private sealed class FakeFreeSpaceProbe : IFreeSpaceProbe
    {
        private readonly Func<string, FreeSpace?> _measure;

        public FakeFreeSpaceProbe(Func<string, FreeSpace?> measure) => _measure = measure;

        public FreeSpace? Measure(string path) => _measure(path);
    }
}
