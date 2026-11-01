using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Preview;
using Borea.Core.Sharing;
using Borea.Storage.Files;
using Borea.Storage.Game;
using Borea.Storage.Instances;
using Borea.Storage.Mods;
using Borea.Storage.Preview;
using Borea.Storage.State;
using Borea.Storage.Tests.Logging;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;
using static Borea.Storage.Tests.Instances.FileUserContentInstallerTests;
using static Borea.Storage.Tests.ModPacks.ModPackInstallerTests;

namespace Borea.Storage.Tests.Preview;

public sealed class VehiclePreviewTests : IDisposable
{
    private const string Build = "2026.10.19.5638";

    /// <summary>The file version of the KSA.dll in GameVersionFixture.dll.</summary>
    private static readonly GameVersion KeptBuild = GameVersion.Parse("2026.8.3.5117");

    private const string Rover = """
        <VehicleSaveData Id="Rover">
          <RootPartRef InstanceOf="Body" LocalInstanceId="1">
            <Transform><Position X="2" /></Transform>
            <PartRef InstanceOf="Wing" LocalInstanceId="2"><Transform><Position Y="3" /></Transform></PartRef>
          </RootPartRef>
        </VehicleSaveData>
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly Guid _targetId = Guid.NewGuid();
    private readonly FakeModDownloader _downloader = new();
    private readonly byte[] _wingsZip = Zip(("mod.toml", "name = \"Wings\"\nassets = [\"Wings.xml\"]"), ("Wings.xml", Assets("""<Part Id="Wing"><PartModel Id="m"><Mesh Id="BodyMesh" /></PartModel></Part>""")));

    public VehiclePreviewTests()
    {
        _paths = new TestGamePathProvider(_root);
        var content = Path.Combine(_paths.GetGameDirectoryPath()!, "Content");
        Write(Path.Combine(content, "manifest.toml"), "[[mods]]\nid = \"Core\"\nenabled = true\n");
        Write(Path.Combine(content, "Core", "mod.toml"), "name = \"Core\"\nassets = [\"Parts.xml\"]\n");
        Write(Path.Combine(content, "Core", "Parts.xml"), Assets("""<MeshAtlas Path="Meshes/All.glb" /><Part Id="Body"><PartModel Id="m"><Mesh Id="BodyMesh" /></PartModel></Part>"""));
        var glb = Path.Combine(content, "Core", "Meshes", "All.glb");
        Directory.CreateDirectory(Path.GetDirectoryName(glb)!);
        File.WriteAllBytes(glb, PreviewFilesTests.Glb("""{"asset":{"version":"2.0"},"meshes":[{"name":"BodyMesh"}]}""", []));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AFetchFromAShareHost_UsesItsOwnDownloader_OnceForAVersion_AndTheVehicleThenBuildsLocally()
    {
        var zip = VehicleZip();
        var release = Vehicle(zip, size: zip.Length);
        var shareFiles = new FakeModDownloader { Bytes = zip };
        using var preview = Preview(shareFiles: shareFiles);
        var host = new ShareHost("files", "files.example", new Uri("https://files.example/api/"), ["vehicle"], new Dictionary<string, long>());

        await preview.FetchFromShareHostAsync(release, host);
        await preview.FetchFromShareHostAsync(release, host);

        Assert.Single(shareFiles.ArchivePaths);
        Assert.Empty(_downloader.ArchivePaths);
        Assert.Equal(VehiclePreviewPlanState.CanBuildLocally, (await preview.PrepareAsync(release, null)).State);
    }

    [Fact]
    public async Task ACachedVehicle_BuildsWithoutADownload_ReusesTheCatalog_ClosesItsZip_AndOnlyMaintenanceSweeps()
    {
        var zip = VehicleZip();
        var cached = CacheArchive(zip);
        var stale = Path.Combine(Path.GetDirectoryName(cached)!, $"{new string('e', 64)}.abc.part");
        File.WriteAllBytes(stale, [1]);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-25));
        var staleStill = Path.Combine(Directory.CreateDirectory(Path.Combine(_paths.GetPreviewCacheFolder(), "thumbs")).FullName, "still.png.abc.part");
        File.WriteAllBytes(staleStill, [1]);
        File.SetLastWriteTimeUtc(staleStill, DateTime.UtcNow.AddHours(-25));
        var release = Vehicle(zip);
        var before = Snapshot();
        using var preview = Preview();

        var plan = await preview.PrepareAsync(release, null);
        Assert.Equal((VehiclePreviewPlanState.CanBuildLocally, Build), (plan.State, plan.GameBuild));
        Assert.Empty(plan.MissingMods);

        var stages = new List<VehiclePreviewStage>();
        var first = await preview.LoadAsync(plan, fetchVehicle: false, fetchMods: false, new Recorder(stages));
        Assert.True(first.IsReady);
        Assert.Equal([VehiclePreviewStage.ReadingParts, VehiclePreviewStage.Done], stages);
        var body = Assert.Single(first.Vehicle.Draws);
        Assert.Equal(("BodyMesh", new Vector3(2, 0, 0)), (first.Vehicle.Meshes[body.Mesh].Name, body.Transform.Translation));
        Assert.Equal((PlaceholderReason.UnknownPart, "Wing"), (Assert.Single(first.Vehicle.Placeholders).Reason, first.Vehicle.Placeholders[0].MissingId));
        Assert.Equal(Hash(zip), first.VehicleSha256);

        using (var second = await preview.LoadAsync(plan, fetchVehicle: false, fetchMods: false))
        {
            Assert.Equal(1, preview.CatalogBuilds);
            Assert.Equal(first.SceneKey, second.SceneKey);
        }

        first.Dispose();
        Assert.Empty(_downloader.ArchivePaths);
        using (File.Open(cached, FileMode.Open, FileAccess.Read, FileShare.None))
        {
        }

        Assert.Equal(before, Snapshot());
        var maintenance = preview.StartMaintenance();
        Assert.Same(maintenance, preview.StartMaintenance());
        await maintenance;
        Assert.False(File.Exists(stale));
        Assert.False(File.Exists(staleStill));
    }

    [Fact]
    public async Task PrepareAndLoad_DoTheirWorkOffTheCallingThread()
    {
        var zip = VehicleZip();
        CacheArchive(zip);
        var release = Vehicle(zip);
        var version = new FixedGameVersion();
        using var preview = Preview(gameVersion: version);

        // A dedicated thread is never a thread pool thread, so a read on the same thread means the call did not yield.
        var callerId = 0;
        Task<VehiclePreviewPlan>? prepare = null;
        var caller = new Thread(() =>
        {
            callerId = Environment.CurrentManagedThreadId;
            prepare = preview.PrepareAsync(release, null);
        });
        caller.Start();
        caller.Join();
        var plan = await prepare!;
        Assert.Equal(VehiclePreviewPlanState.CanBuildLocally, plan.State);
        Assert.NotEqual(callerId, version.LastReadThreadId);

        Task<VehiclePreviewResult>? load = null;
        caller = new Thread(() =>
        {
            callerId = Environment.CurrentManagedThreadId;
            load = preview.LoadAsync(plan, fetchVehicle: false, fetchMods: false);
        });
        caller.Start();
        caller.Join();
        using var result = await load!;
        Assert.True(result.IsReady);
        Assert.NotEqual(callerId, version.LastReadThreadId);
    }

    [Fact]
    public async Task AnUncachedVehicle_NeedsItsFile_AndIsFetchedOnce_OnlyWhenTheLoadMayFetch()
    {
        var zip = VehicleZip();
        var release = Vehicle(zip, size: 2468);
        _downloader.Bytes = zip;
        var before = Snapshot();
        using var preview = Preview();

        var plan = await preview.PrepareAsync(release, null);
        Assert.Equal((VehiclePreviewPlanState.NeedsVehicleFile, (long?)2468), (plan.State, plan.VehicleSizeBytes));

        using (var refused = await preview.LoadAsync(plan, fetchVehicle: false, fetchMods: false))
            Assert.Equal(VehiclePreviewLoadState.NeedsVehicleFile, refused.State);
        Assert.Empty(_downloader.ArchivePaths);

        using (var fetched = await preview.LoadAsync(plan, fetchVehicle: true, fetchMods: false))
            Assert.True(fetched.IsReady);
        using (var again = await preview.LoadAsync(plan, fetchVehicle: true, fetchMods: false))
            Assert.True(again.IsReady);

        Assert.Single(_downloader.ArchivePaths);
        var archive = Path.Combine(_paths.GetPreviewCacheFolder(), "archives", Hash(zip) + ".zip");
        var added = Snapshot().Except(before).ToList();
        Assert.Contains($"{archive}|{zip.Length}", added);
        Assert.All(added, path => Assert.StartsWith(Path.Combine(_root, "PreviewCache"), path, StringComparison.Ordinal));
        Assert.DoesNotContain(added, path => path.Contains(".part", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoGameFolder_GivesThatState()
    {
        using var preview = Preview(new TestGamePathProvider(_root, hasGameDirectory: false));

        var plan = await preview.PrepareAsync(Vehicle(VehicleZip()), null);
        using var result = await preview.LoadAsync(plan, fetchVehicle: true, fetchMods: true);

        Assert.Equal((VehiclePreviewPlanState.NoGameFolder, VehiclePreviewLoadState.NoGameFolder), (plan.State, result.State));
        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task ACorruptCachedZip_GivesVehicleDamaged()
    {
        var bytes = Encoding.UTF8.GetBytes("not a zip archive");
        CacheArchive(bytes);
        using var preview = Preview();

        var plan = await preview.PrepareAsync(Vehicle(bytes), null);
        using var result = await preview.LoadAsync(plan, fetchVehicle: true, fetchMods: false);

        Assert.Equal((VehiclePreviewPlanState.VehicleDamaged, VehiclePreviewLoadState.VehicleDamaged), (plan.State, result.State));
        Assert.Empty(_downloader.ArchivePaths);
    }

    [Fact]
    public async Task AMissingMod_IsNamedWithItsSize_ItsPartsShowAsNotLoaded_AndAFetchResolvesThem()
    {
        var zip = VehicleZip();
        CacheArchive(zip);
        var wings = Mod("Wings", Hash(_wingsZip), 4321);
        var release = Vehicle(zip, dependencies: ["Wings"]);
        using var preview = Preview(offline: new FakeModRepository([wings]));

        var plan = await preview.PrepareAsync(release, null);
        Assert.Equal(VehiclePreviewPlanState.CanBuildLocally, plan.State);
        Assert.Equal([new VehiclePreviewMissingMod("Wings", wings.Version, null, 4321, CanFetch: true)], plan.MissingMods);

        using (var local = await preview.LoadAsync(plan, fetchVehicle: false, fetchMods: false))
        {
            Assert.True(local.IsReady);
            Assert.Single(local.Vehicle.Draws);
            var box = Assert.Single(local.Vehicle.Placeholders);
            Assert.Equal(("Wing", PlaceholderReason.ModNotLoaded, "Wings"), (box.MissingId, box.Reason, box.ModId));
            Assert.Equal(1, Assert.Single(local.MissingMods).PartCount);
        }

        Assert.Empty(_downloader.ArchivePaths);
        _downloader.Bytes = _wingsZip;
        var stages = new List<VehiclePreviewStage>();
        using var fetched = await preview.LoadAsync(plan, fetchVehicle: false, fetchMods: true, new Recorder(stages));

        Assert.True(fetched.IsReady);
        Assert.Contains(VehiclePreviewStage.FetchingMods, stages);
        Assert.Single(_downloader.ArchivePaths);
        Assert.Empty(fetched.Vehicle.Placeholders);
        Assert.Empty(fetched.MissingMods);
        Assert.Equal([new Vector3(2, 0, 0), new Vector3(0, 3, 0)], fetched.Vehicle.Draws.Select(draw => draw.Transform.Translation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheFuelLinksOfTheVehicleFile_ReachTheScene_AlsoWhenAMissingModResolvesASecondTime(bool missingMod)
    {
        const string vehicleXml = """
            <VehicleSaveData Id="Rover">
              <RootPartRef InstanceOf="Body" LocalInstanceId="1">
                <PartRef InstanceOf="Body" LocalInstanceId="2"><Transform><Position X="3" /></Transform></PartRef>
                <PartRef InstanceOf="Wing" LocalInstanceId="3" />
              </RootPartRef>
              <FuelLink PartA="1" PartB="2" />
            </VehicleSaveData>
            """;
        var zip = Zip(("meta.toml", "name = \"Rover\""), ("vehicle.xml", vehicleXml));
        CacheArchive(zip);
        var release = Vehicle(zip, dependencies: missingMod ? ["Wings"] : null);
        using var preview = Preview(offline: new FakeModRepository([Mod("Wings", Hash(_wingsZip), 4321)]));

        var plan = await preview.PrepareAsync(release, null);
        using var result = await preview.LoadAsync(plan, fetchVehicle: false, fetchMods: false);

        Assert.True(result.IsReady);
        Assert.Equal(missingMod ? PlaceholderReason.ModNotLoaded : PlaceholderReason.UnknownPart, Assert.Single(result.Vehicle.Placeholders).Reason);
        Assert.Equal([new Vector3(0, 0, 0), new Vector3(3, 0, 0)], Assert.Single(result.Vehicle.HoseRoutes).Select(waypoint => waypoint.Position));
    }

    [Fact]
    public void TheSceneFormat_IsNewerThanTheStillsWithoutFuelLines()
    {
        // A still that a build without fuel lines stored names format 1, and only a new scene key makes the next preview replace it.
        Assert.InRange(VehiclePreview.FormatVersion, 2, int.MaxValue);
    }

    [Fact]
    public async Task DisposingTheService_ReleasesThePins_ClosesTheZips_AndLaterCallsThrow()
    {
        var zip = VehicleZip();
        var cached = CacheArchive(zip);
        var archives = new PreviewArchiveCache(_paths, new RecordingLog(), capacityBytes: 0);
        var preview = Preview(archives: archives);
        var plan = await preview.PrepareAsync(Vehicle(zip), null);
        var result = await preview.LoadAsync(plan, fetchVehicle: false, fetchMods: false);

        archives.Sweep();
        Assert.True(File.Exists(cached));

        preview.Dispose();

        Assert.Throws<ObjectDisposedException>(() => result.Assets!.TryOpenRead(new PreviewAssetRef("Core", "mod.toml")));
        archives.Sweep();
        Assert.False(File.Exists(cached));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => preview.PrepareAsync(plan.Release, null));
    }

    [Fact]
    public async Task ATargetOnAKeptBuild_ReadsThePartsOfThatBuild_PlansAgainstIt_AndKeysTheSceneByIt()
    {
        // only the kept build gives a Wing, so a part that resolves can only come from its folder
        var kept = _paths.GetGameBuildFolder(KeptBuild);
        Directory.CreateDirectory(kept);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(kept, "KSA.dll"));
        var content = Path.Combine(kept, "Content");
        Write(Path.Combine(content, "manifest.toml"), "[[mods]]\nid = \"Core\"\nenabled = true\n");
        Write(Path.Combine(content, "Core", "mod.toml"), "name = \"Core\"\nassets = [\"Parts.xml\"]\n");
        Write(Path.Combine(content, "Core", "Parts.xml"), Assets("""<MeshAtlas Path="Meshes/All.glb" /><Part Id="Body"><PartModel Id="m"><Mesh Id="BodyMesh" /></PartModel></Part><Part Id="Wing"><PartModel Id="m"><Mesh Id="BodyMesh" /></PartModel></Part>"""));
        Directory.CreateDirectory(Path.Combine(content, "Core", "Meshes"));
        File.WriteAllBytes(Path.Combine(content, "Core", "Meshes", "All.glb"), PreviewFilesTests.Glb("""{"asset":{"version":"2.0"},"meshes":[{"name":"BodyMesh"}]}""", []));

        var zip = VehicleZip();
        CacheArchive(zip);
        var planner = new GameVersionPlanner();
        using var preview = Preview(instances: new MemoryInstanceRepository(Keeping(KeptBuild)), planner: planner);

        var plan = await preview.PrepareAsync(Vehicle(zip), _targetId);
        using var onKept = await preview.LoadAsync(plan, fetchVehicle: false, fetchMods: false);
        using var onSettings = await preview.LoadAsync(await preview.PrepareAsync(Vehicle(zip), null), fetchVehicle: false, fetchMods: false);

        Assert.Equal((VehiclePreviewPlanState.CanBuildLocally, KeptBuild.ToString()), (plan.State, plan.GameBuild));
        Assert.True(onKept.IsReady);
        Assert.Empty(onKept.Vehicle.Placeholders);
        Assert.Equal(2, onKept.Vehicle.Draws.Count);
        Assert.Equal(KeptBuild.ToString(), onKept.GameBuild);
        Assert.Equal(KeptBuild, planner.GameVersions.First());

        Assert.Equal(("Wing", Build), (Assert.Single(onSettings.Vehicle!.Placeholders).MissingId, onSettings.GameBuild));
        Assert.NotEqual(onSettings.SceneKey, onKept.SceneKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AKeptBuildThatCannotBeUsed_StopsThePreview_AndTheGameOfTheSettingsNeverStandsInForIt(bool folderExists)
    {
        if (folderExists)
            Directory.CreateDirectory(_paths.GetGameBuildFolder(KeptBuild));

        var zip = VehicleZip();
        CacheArchive(zip);
        var planner = new GameVersionPlanner();
        using var preview = Preview(instances: new MemoryInstanceRepository(Keeping(KeptBuild)), planner: planner);

        var plan = await preview.PrepareAsync(Vehicle(zip), _targetId);
        using var result = await preview.LoadAsync(plan, fetchVehicle: true, fetchMods: true);

        Assert.Equal((VehiclePreviewPlanState.Failed, VehiclePreviewLoadState.Failed), (plan.State, result.State));
        Assert.Contains(KeptBuild.ToString(), plan.Message, StringComparison.Ordinal);
        Assert.Contains(KeptBuild.ToString(), result.Message, StringComparison.Ordinal);
        Assert.Empty(planner.GameVersions);
        Assert.Empty(_downloader.ArchivePaths);
    }

    private Instance Keeping(GameVersion build)
    {
        var instance = Instance.FromExisting(_targetId, "Target", InstanceSource.Custom.Value, DateTimeOffset.UnixEpoch, [], false);
        instance.SetGameBuild(build);
        return instance;
    }

    private VehiclePreview Preview(
        TestGamePathProvider? paths = null,
        IModRepository? offline = null,
        PreviewArchiveCache? archives = null,
        IInstalledGameVersionProvider? gameVersion = null,
        IInstanceRepository? instances = null,
        IInstallPlanner? planner = null,
        IModDownloader? shareFiles = null)
    {
        paths ??= _paths;
        return new VehiclePreview(
            paths,
            planner ?? new FakePlanner(),
            offline ?? new FakeModRepository([]),
            instances ?? new MemoryInstanceRepository(),
            new FileModStateRepository(paths),
            new FileInstalledContentRecords(paths),
            new ModStore(paths, new DirectoryLinker(), linksReleases: true),
            archives ?? new PreviewArchiveCache(paths, new RecordingLog()),
            new InstanceGameResolver(paths, gameVersion ?? new FixedGameVersion()),
            new WindowsPlatform(),
            _downloader,
            new PlentyOfSpace(),
            new RecordingLog(),
            new PreviewThumbnailCache(paths, new RecordingLog()),
            shareFiles is null ? null : _ => shareFiles);
    }

    private static byte[] VehicleZip() => Zip(("meta.toml", "name = \"Rover\""), ("vehicle.xml", Rover));

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

    private static string Assets(string body) => $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Assets>\n{body}\n</Assets>\n";

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static ModVersionMetadata Mod(string id, string sha256, long size) =>
        new(1, id, ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.7.4.2131", 2131, new DownloadInfo($"https://example.com/{id}.zip", sha256, size, "application/zip"), null, []);

    private static ModVersionMetadata Vehicle(byte[] zip, long size = 1, string[]? dependencies = null) =>
        new(1, "Rover", ModVersion.Parse("1.0.0"), ReleaseStatus.Stable, DateTimeOffset.UnixEpoch, "2026.7.4.2131", 2131, new DownloadInfo("https://example.com/rover.zip", Hash(zip), size, "application/zip"), null, (dependencies ?? []).Select(id => new ModDependency(id, ModDependencyKind.Required)).ToList(), ContentType.Vehicle, install: new InstallInfo(null, derived: false, folder: "Rover"));

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Records each stage on the calling thread, unlike <see cref="Progress{T}"/>, which posts to the thread pool.</summary>
    private sealed class Recorder(List<VehiclePreviewStage> stages) : IProgress<VehiclePreviewStage>
    {
        public void Report(VehiclePreviewStage value) => stages.Add(value);
    }

    private sealed class FixedGameVersion : IInstalledGameVersionProvider
    {
        /// <summary>The thread of the last read, so a test can see where the service did its work.</summary>
        public int LastReadThreadId { get; private set; }

        public InstalledGameVersion? GetInstalledVersion()
        {
            LastReadThreadId = Environment.CurrentManagedThreadId;
            return new(null, Build);
        }
    }

    /// <summary>Records the game version of each plan.</summary>
    private sealed class GameVersionPlanner : IInstallPlanner
    {
        private readonly FakePlanner _inner = new();

        public List<GameVersion?> GameVersions { get; } = [];

        public Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
        {
            lock (GameVersions)
                GameVersions.Add(request.GameVersion);

            return _inner.PlanAsync(request, cancellationToken);
        }
    }

    private sealed class WindowsPlatform : IGamePlatform
    {
        public OsPlatform Current => OsPlatform.Windows;
    }

    private sealed class PlentyOfSpace : IFreeSpaceProbe
    {
        public FreeSpace? Measure(string path) => new("Test", long.MaxValue);
    }
}
