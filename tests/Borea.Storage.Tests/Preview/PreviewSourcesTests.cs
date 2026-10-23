using System.Security.Cryptography;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Preview;
using Borea.Core.State;
using Borea.Storage.Files;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.Preview;
using Borea.Storage.State;
using Borea.Storage.Tests.Logging;
using Borea.Storage.Tests.Paths;
using static Borea.Storage.Tests.Instances.FileUserContentInstallerTests;
using static Borea.Storage.Tests.ModPacks.ModPackInstallerTests;

namespace Borea.Storage.Tests.Preview;

public sealed class PreviewSourcesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly ModStore _store;
    private readonly FileInstalledContentRecords _records;
    private readonly PreviewArchiveCache _archives;
    private readonly Guid _targetId = Guid.NewGuid();

    public PreviewSourcesTests()
    {
        _paths = new TestGamePathProvider(_root);
        _store = new ModStore(_paths, new DirectoryLinker(), linksReleases: true);
        _records = new FileInstalledContentRecords(_paths);
        _archives = new PreviewArchiveCache(_paths, new RecordingLog());
        Write(Path.Combine(_paths.GetGameDirectoryPath()!, "Content", "Core", "mod.toml"), "name = \"Core\"");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task PlanMods_ReadsEachModFromItsCheapestSource_CoreFirst_InLoadOrder_AndWritesNothing()
    {
        var a = Mod("A", Sha('1'));
        var b = Mod("B", Sha('2'));
        var cZip = Zip(("C/mod.toml", "name = \"C\""));
        var c = Mod("C", Hash(cZip), dependencies: "F");
        var d = Mod("D", Sha('4'), size: 5000);
        var eZip = Zip(("mod.toml", "name = \"E\""));
        var e = Mod("E", null, size: 77);
        var f = Mod("F", Sha('6'));
        var target = Target(Installed(a, "token-a"));
        var other = Instance.FromExisting(Guid.NewGuid(), "Other", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [Installed(f, "token-f")], false);
        var instances = new MemoryInstanceRepository(target);
        await instances.CreateAsync(other);

        OwnedFolder(target.InstanceId, "A", "token-a");
        OwnedFolder(other.InstanceId, "F", "token-f");
        Write(Path.Combine(_store.EntryPath("B", b.Version, b.Download.Sha256!), "mod.toml"), "name = \"B\"");
        CacheArchive(cZip);
        CacheArchive(eZip);
        Write(_paths.GetInstanceManifestPath(target.InstanceId), "[[mods]]\nid = \"B\"\nenabled = true\n\n[[mods]]\nid = \"A\"\nenabled = true\n");
        var offline = new FakeModRepository([a, b, c, d, e, f]);
        var planner = new RecordingPlanner(new FakePlanner());
        var before = Snapshot();

        using (var result = await Sources(planner, offline, instances).PlanModsAsync(Vehicle(Sha('9'), ["A", "B", "C", "D", "E"]), target))
        {
            Assert.Same(offline, planner.Repository);
            Assert.Equal(["Core", "B", "A", "F", "C"], result.Mods.Select(mod => mod.ModId));
            Assert.Equal(
                [PreviewModLocation.Game, PreviewModLocation.StoreEntry, PreviewModLocation.InstanceFolder, PreviewModLocation.OtherInstance, PreviewModLocation.ArchiveCache],
                result.Sources.Select(source => source.Location));
            Assert.All(result.Mods, mod =>
            {
                using var stream = mod.Files.TryOpen("mod.toml");
                Assert.NotNull(stream);
            });
            Assert.Equal([("D", (long?)5000), ("E", 77)], result.Missing.Select(need => (need.ModId, need.SizeBytes)));
            Assert.DoesNotContain(result.Notices, notice => notice.Kind == PreviewNoticeKind.PlannerFallback);
        }

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task PlanMods_WhenThePlannerFails_GivesAFallbackNotice_AndStillNamesEveryRequiredMod()
    {
        var a = Mod("A", Sha('1'));
        var target = Target(Installed(a, "token-a"));
        OwnedFolder(target.InstanceId, "A", "token-a");

        using var result = await Sources(new FailingPlanner(), new FakeModRepository([]), new MemoryInstanceRepository(target)).PlanModsAsync(Vehicle(Sha('9'), ["A", "D"]), target);

        Assert.Contains(result.Notices, notice => notice.Kind == PreviewNoticeKind.PlannerFallback);
        Assert.Equal(["Core", "A"], result.Mods.Select(mod => mod.ModId));
        var missing = Assert.Single(result.Missing);
        Assert.Equal(("D", (long?)null), (missing.ModId, missing.SizeBytes));
    }

    [Fact]
    public async Task PlanMods_ListsADependencyOfANeededModThatNothingHas()
    {
        var c = Mod("C", Sha('3'), dependencies: "F");

        using var result = await Sources(offline: new FakeModRepository([c])).PlanModsAsync(Vehicle(Sha('9'), ["C"]), Target());

        Assert.Equal(["C", "F"], result.Missing.Select(need => need.ModId));
    }

    [Fact]
    public async Task PlanMods_CancelledAfterTheModsAreFound_ClosesTheCachedZip()
    {
        var cZip = Zip(("mod.toml", "name = \"C\""));
        var c = Mod("C", Hash(cZip));
        var zip = CacheArchive(cZip);
        var sources = Sources(offline: new FakeModRepository([c]), modState: new CancellingModState());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sources.PlanModsAsync(Vehicle(Sha('9'), ["C"]), Target()));

        // FileShare.None fails while any other handle is open, on Windows and through the file lock of .NET on Unix.
        using (File.Open(zip, FileMode.Open, FileAccess.Read, FileShare.None))
        {
        }
    }

    [Fact]
    public async Task FindVehicle_InstalledAndUnchanged_ReadsTheFolder()
    {
        var vehicle = Vehicle(Sha('9'));
        var folder = await InstallVehicleAsync(vehicle);

        using var source = await Sources().FindVehicleAsync(vehicle, Target());

        Assert.Equal((PreviewVehicleLocation.Installed, folder), (source.Location, source.Path));
        Assert.True(source.Files!.Exists("vehicle.xml"));
    }

    [Fact]
    public async Task FindVehicle_RecordOfAnOlderRelease_IsNotUsed_AndTheFetchSizeOrTheCachedArchiveIsGiven()
    {
        await InstallVehicleAsync(Vehicle(Sha('8'), version: "0.9.0"));
        var zip = Zip(("meta.toml", "name = \"Rover\""), ("vehicle.xml", "<Vehicle />"));
        var vehicle = Vehicle(Hash(zip), size: 1234);
        var sources = Sources();

        using (var fetch = await sources.FindVehicleAsync(vehicle, Target()))
            Assert.Equal((PreviewVehicleLocation.NeedsFetch, (long?)1234, (object?)null), (fetch.Location, fetch.SizeBytes, (object?)fetch.Files));

        var cached = CacheArchive(zip);
        var before = Snapshot();
        using (var source = await sources.FindVehicleAsync(vehicle, Target()))
        {
            Assert.Equal((PreviewVehicleLocation.Cached, cached), (source.Location, source.Path));
            Assert.True(source.Files!.Exists("vehicle.xml"));
        }

        Assert.Equal(before, Snapshot());
    }

    private PreviewSources Sources(IInstallPlanner? planner = null, IModRepository? offline = null, IInstanceRepository? instances = null, IModStateRepository? modState = null) =>
        new(_paths, planner ?? new FakePlanner(), offline ?? new FakeModRepository([]), instances ?? new MemoryInstanceRepository(), modState ?? new FileModStateRepository(_paths), _records, _store, _archives, new NoGameVersion(), new WindowsPlatform());

    private Instance Target(params InstalledMod[] mods) =>
        Instance.FromExisting(_targetId, "Target", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, mods, false);

    private async Task<string> InstallVehicleAsync(ModVersionMetadata release)
    {
        var folder = Path.Combine(_paths.GetInstanceVehiclesFolder(_targetId), "Rover");
        Write(Path.Combine(folder, "meta.toml"), "name = \"Rover\"");
        Write(Path.Combine(folder, "vehicle.xml"), "<Vehicle />");
        var record = new InstalledContentRecord(release, "Rover", DateTimeOffset.UnixEpoch, await InstalledContentHashes.ComputeAsync(folder));
        await _records.AddAsync(_targetId, record);
        return folder;
    }

    private void OwnedFolder(Guid instanceId, string modId, string token)
    {
        var folder = Path.Combine(_paths.GetInstanceModsFolder(instanceId), modId);
        Write(Path.Combine(folder, "mod.toml"), $"name = \"{modId}\"");
        Write(Path.Combine(folder, ModFolders.OwnershipFileName), token);
    }

    private string CacheArchive(byte[] zip)
    {
        var path = Path.Combine(_paths.GetPreviewCacheFolder(), "archives", Hash(zip) + ".zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, zip);
        return path;
    }

    /// <summary>Every file and folder below the temp root with the size of each file, which a write would change.</summary>
    private List<string> Snapshot() =>
        Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .Select(path => File.Exists(path) ? $"{path}|{new FileInfo(path).Length}" : path)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static InstalledMod Installed(ModVersionMetadata release, string token) =>
        new(release.ModId, release.Version, InstallReason.Manual, DateTimeOffset.UnixEpoch, release, release.Download.Sha256, ownershipToken: token);

    private static ModVersionMetadata Mod(string id, string? sha256, long? size = 1, params string[] dependencies) =>
        new(1, id, ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.7.4.2131", 2131, new DownloadInfo($"https://example.com/{id}.zip", sha256, size, "application/zip"), null, Required(dependencies));

    private static ModVersionMetadata Vehicle(string sha256, string[]? dependencies = null, long? size = 1, string version = "1.0.0") =>
        new(1, "Rover", ModVersion.Parse(version), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.7.4.2131", 2131, new DownloadInfo("https://example.com/rover.zip", sha256, size, "application/zip"), null, Required(dependencies ?? []), ContentType.Vehicle, install: new InstallInfo(null, derived: false, folder: "Rover"));

    private static List<ModDependency> Required(string[] ids) => ids.Select(id => new ModDependency(id, ModDependencyKind.Required)).ToList();

    private static string Sha(char digit) => new(digit, 64);

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class RecordingPlanner(IInstallPlanner inner) : IInstallPlanner
    {
        public IModRepository? Repository { get; private set; }

        public Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
        {
            Repository = request.Repository;
            return inner.PlanAsync(request, cancellationToken);
        }
    }

    private sealed class FailingPlanner : IInstallPlanner
    {
        public Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The index snapshot cannot be read.");
    }

    /// <summary>A manifest read that sees a cancelled token, which is the last step before the found mods go into the result.</summary>
    private sealed class CancellingModState : IModStateRepository
    {
        public Task<IReadOnlyList<ModManifestEntry>> GetEntriesAsync(Guid instanceId, CancellationToken cancellationToken = default) => throw new OperationCanceledException();

        public Task<bool> IsActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetAllActiveModIdsAsync(Guid instanceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ModEntryAddResult> AddEntryAsync(Guid instanceId, string modId, bool enabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> SetActiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> SetInactiveAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> ReorderAsync(Guid instanceId, IReadOnlyList<string> modIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> PutGameContentFirstAsync(Guid instanceId, string gameDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoGameVersion : IInstalledGameVersionProvider
    {
        public InstalledGameVersion? GetInstalledVersion() => null;
    }

    private sealed class WindowsPlatform : IGamePlatform
    {
        public OsPlatform Current => OsPlatform.Windows;
    }
}
