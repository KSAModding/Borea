using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Borea.Core.Dependencies;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.State;

namespace Borea.Cli.Tests;

public sealed class UserContentCommandTests : IDisposable
{
    private readonly CliHost _host = new();
    private readonly ZipDownloader _downloader = new();

    public UserContentCommandTests()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "parts-lib", version: "1.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "wheels", version: "1.0.0"));
        _host.InstallerFactory = graph => new FolderInstaller(graph);
        _host.Downloader = _downloader;
        _downloader.Bytes = VehicleArchive("Rover", "<Vehicle />");
        Index(Vehicle("1.0.0", [Required("parts-lib"), Required("wheels")]));
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Install_Vehicle_InstallsItsModsEnabledAndThenTheFolder()
    {
        var instance = await CreateAsync();

        var run = await _host.RunAsync("install", "rover", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Install dependency parts-lib 1.0.0.", run.Output);
        Assert.Contains("Install vehicle rover 1.0.0 into Vehicles/Rover in 'Alpha'.", run.Output);
        Assert.Equal(["parts-lib", "wheels"], (await Entries(instance)).Where(entry => entry.Enabled).Select(entry => entry.ModId).Order());
        Assert.True(File.Exists(Path.Combine(_host.Paths.GetInstanceVehiclesFolder(instance), "Rover", "vehicle.xml")));
        Assert.Equal("rover", Assert.Single(await new FileInstalledContentRecords(_host.Paths).ListAsync(instance)).ListingId);
    }

    [Fact]
    public async Task Install_InstalledAndDisabledRequiredMod_IsEnabled()
    {
        var instance = await CreateAsync();
        await _host.RunAsync("install", "parts-lib", "--instance", "Alpha");
        await _host.RunAsync("disable", "parts-lib", "--instance", "Alpha");

        var run = await _host.RunAsync("install", "rover", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Enable parts-lib.", run.Output);
        Assert.True((await Entries(instance)).Single(entry => entry.ModId == "parts-lib").Enabled);
    }

    [Fact]
    public async Task Install_AnyOfWhoseInstalledAlternativeIsDisabled_EnablesIt()
    {
        Index(Vehicle("1.0.0", [ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("wheels"), new ModDependencyAlternative("parts-lib")])]));
        var instance = await CreateAsync();
        await _host.RunAsync("install", "parts-lib", "--instance", "Alpha");
        await _host.RunAsync("disable", "parts-lib", "--instance", "Alpha");

        var run = await _host.RunAsync("install", "rover", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Enable parts-lib.", run.Output);
        Assert.DoesNotContain("wheels", run.Output);
        Assert.True((await Entries(instance)).Single(entry => entry.ModId == "parts-lib").Enabled);
    }

    [Fact]
    public async Task Install_TakenName_StopsBeforeAnythingChanges_AndNamesTheOption()
    {
        var instance = await CreateAsync();
        var mine = Directory.CreateDirectory(Path.Combine(_host.Paths.GetInstanceVehiclesFolder(instance), "rover")).FullName;
        File.WriteAllText(Path.Combine(mine, "meta.toml"), "name = \"My Own\"\n");

        var refused = await _host.RunAsync("install", "rover", "--instance", "Alpha");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("Run again with --name <new name>.", refused.Error);
        Assert.Empty((await Instance()).Mods);

        var renamed = await _host.RunAsync("install", "rover", "--instance", "Alpha", "--name", "Rover 2");

        Assert.Equal(0, renamed.ExitCode);
        Assert.StartsWith("name = \"Rover 2\"", File.ReadAllText(Path.Combine(_host.Paths.GetInstanceVehiclesFolder(instance), "Rover 2", "meta.toml")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_MissingRequiredMod_StopsWithoutAllowMissing_AndGoesOnWithIt()
    {
        Index(Vehicle("1.0.0", [Required("gone"), Required("wheels")]));
        var instance = await CreateAsync();

        var refused = await _host.RunAsync("install", "rover", "--instance", "Alpha");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("the game refuses the vehicle file", refused.Output);
        Assert.Contains("--allow-missing", refused.Error);
        Assert.Empty((await Instance()).Mods);
        Assert.False(Directory.Exists(_host.Paths.GetInstanceVehiclesFolder(instance)));

        var allowed = await _host.RunAsync("install", "rover", "--instance", "Alpha", "--allow-missing");

        Assert.Equal(0, allowed.ExitCode);
        Assert.Equal(["wheels"], (await Instance()).Mods.Select(mod => mod.ModId));
        Assert.True(Directory.Exists(Path.Combine(_host.Paths.GetInstanceVehiclesFolder(instance), "Rover")));
    }

    [Fact]
    public async Task InstallDryRun_Json_ShowsTheMissingModAndWritesNothing()
    {
        Index(Vehicle("1.0.0", [Required("gone")]));
        var instance = await CreateAsync();

        var run = await _host.RunAsync("install", "rover", "--instance", "Alpha", "--dry-run", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("vehicle", run.Json.GetProperty("type").GetString());
        Assert.Equal("Rover", run.Json.GetProperty("folder").GetString());
        var missing = run.Json.GetProperty("missing")[0];
        Assert.Equal(("missing-for-vehicle", "gone", "unsatisfied-dependency"), (missing.GetProperty("code").GetString(), missing.GetProperty("dependency").GetString(), missing.GetProperty("value").GetString()));
        Assert.False(Directory.Exists(_host.Paths.GetInstanceVehiclesFolder(instance)));
    }

    [Fact]
    public async Task Remove_ChangedVehicle_StopsWithoutDiscardChanges()
    {
        var instance = await CreateAsync();
        await _host.RunAsync("install", "rover", "--instance", "Alpha");
        File.WriteAllText(Path.Combine(_host.Paths.GetInstanceVehiclesFolder(instance), "Rover", "vehicle.xml"), "<Vehicle edited=\"yes\" />");

        var refused = await _host.RunAsync("remove", "rover", "--instance", "Alpha");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("vehicle.xml (changed)", refused.Error);
        Assert.Contains("--discard-changes", refused.Error);
        Assert.True(Directory.Exists(Path.Combine(_host.Paths.GetInstanceVehiclesFolder(instance), "Rover")));

        var removed = await _host.RunAsync("remove", "rover", "--instance", "Alpha", "--discard-changes");

        Assert.Equal(0, removed.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_host.Paths.GetInstanceVehiclesFolder(instance), "Rover")));
        Assert.Empty(await new FileInstalledContentRecords(_host.Paths).ListAsync(instance));
    }

    [Fact]
    public async Task Update_ChangedVehicle_NeedsDiscardChanges_ThenReplacesTheFolder()
    {
        var instance = await CreateAsync();
        await _host.RunAsync("install", "rover", "--instance", "Alpha");
        File.WriteAllText(Path.Combine(_host.Paths.GetInstanceVehiclesFolder(instance), "Rover", "added.txt"), "mine");
        Index(Vehicle("1.0.0", [Required("parts-lib"), Required("wheels")]), Vehicle("2.0.0", [Required("parts-lib"), Required("wheels")]));
        _downloader.Bytes = VehicleArchive("Rover", "<Vehicle version=\"2\" />");

        var refused = await _host.RunAsync("update", "rover", "--instance", "Alpha");
        var updated = await _host.RunAsync("update", "rover", "--instance", "Alpha", "--discard-changes");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("added.txt (added)", refused.Error);
        Assert.Equal(0, updated.ExitCode);
        Assert.Equal("<Vehicle version=\"2\" />", File.ReadAllText(Path.Combine(_host.Paths.GetInstanceVehiclesFolder(instance), "Rover", "vehicle.xml")));
    }

    [Fact]
    public async Task Update_WithVersion_ChangesToThatExactRelease()
    {
        var instance = await CreateAsync();
        Index(Vehicle("1.0.0", []), Vehicle("1.1.0", []), Vehicle("2.0.0", []));
        await _host.RunAsync("install", "rover", "--instance", "Alpha", "--version", "2.0.0");

        var older = await _host.RunAsync("update", "rover", "--instance", "Alpha", "--version", "1.1.0");
        var same = await _host.RunAsync("update", "rover", "--instance", "Alpha", "--version", "1.1.0");

        Assert.Equal(0, older.ExitCode);
        Assert.Equal(ModVersion.Parse("1.1.0"), Assert.Single(await new FileInstalledContentRecords(_host.Paths).ListAsync(instance)).Version);
        Assert.Contains("rover 1.1.0 is installed. Nothing to do.", same.Output);
    }

    [Fact]
    public async Task UpdateWithoutAnId_LeavesAVehicleWithANewerReleaseAlone()
    {
        var instance = await CreateAsync();
        await _host.RunAsync("install", "rover", "--instance", "Alpha");
        Index(Vehicle("1.0.0", [Required("parts-lib"), Required("wheels")]), Vehicle("2.0.0", [Required("parts-lib"), Required("wheels")]));

        var run = await _host.RunAsync("update", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("rover", run.Output);
        Assert.Equal(ModVersion.Parse("1.0.0"), Assert.Single(await new FileInstalledContentRecords(_host.Paths).ListAsync(instance)).Version);
    }

    [Fact]
    public async Task VehicleOptions_OnAModId_AreRefused()
    {
        await CreateAsync();

        var run = await _host.RunAsync("install", "wheels", "--instance", "Alpha", "--allow-missing");
        await _host.RunAsync("install", "wheels", "--instance", "Alpha");
        var update = await _host.RunAsync("update", "wheels", "--instance", "Alpha", "--version", "1.0.0");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("--allow-missing apply only to a vehicle or a save", run.Error);
        Assert.Equal(1, update.ExitCode);
        Assert.Contains("--version apply only to a vehicle or a save", update.Error);
    }

    [Fact]
    public async Task Search_FindsAVehicleOnlyWithTheVehicleType()
    {
        var plain = await _host.RunAsync("search", "rov");
        var vehicles = await _host.RunAsync("search", "rov", "--type", "vehicle");
        var saves = await _host.RunAsync("search", "rov", "--type", "save");
        var wrong = await _host.RunAsync("search", "rov", "--type", "spaceship");

        Assert.Equal(0, plain.ExitCode);
        Assert.DoesNotContain("rover", plain.Output);
        Assert.Contains("rover  Rover  1.0.0", vehicles.Output);
        Assert.Contains("No listings match 'rov'.", saves.Output);
        Assert.NotEqual(0, wrong.ExitCode);
    }

    [Fact]
    public async Task Show_Vehicle_PrintsTheFolderTheGameMinAndTheDependencies()
    {
        var run = await _host.RunAsync("show", "rover", "--version", "1.0.0");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("    Folder: Rover", run.Output);
        Assert.Contains("    Game: 2026.8.22.5348 to open", run.Output);
        Assert.Contains("parts-lib", run.Output);
    }

    [Fact]
    public async Task InstanceShow_ListsTheRecordedVehicles_AndWhetherTheyChanged()
    {
        var instance = await CreateAsync();
        await _host.RunAsync("install", "rover", "--instance", "Alpha");
        File.WriteAllText(Path.Combine(_host.Paths.GetInstanceVehiclesFolder(instance), "Rover", "added.txt"), "mine");

        var text = await _host.RunAsync("instance", "show", "Alpha");
        var json = await _host.RunAsync("instance", "show", "Alpha", "--json");

        Assert.Contains("Vehicles Borea installed:", text.Output);
        Assert.Contains("  rover 1.0.0  Rover  changed", text.Output);
        var vehicle = Assert.Single(json.Json.GetProperty("vehicles").EnumerateArray());
        Assert.Equal(("rover", "Rover", true), (vehicle.GetProperty("id").GetString(), vehicle.GetProperty("folder").GetString(), vehicle.GetProperty("changed").GetBoolean()));
        Assert.Empty(json.Json.GetProperty("saves").EnumerateArray());
    }

    private async Task<Guid> CreateAsync()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        return (await Instance()).InstanceId;
    }

    private async Task<Instance> Instance() => Assert.Single(await new FileInstanceRepository(_host.Paths).GetAllAsync());

    private Task<IReadOnlyList<Borea.Core.State.ModManifestEntry>> Entries(Guid instance) => new FileModStateRepository(_host.Paths).GetEntriesAsync(instance);

    private void Index(params ModVersionMetadata[] releases) =>
        _host.IndexReader.Snapshot = new ContentIndexSnapshot(1, [], [], null, [], vehicles:
        [
            new ContentIndexListing("rover", new ModMetadata(1, "rover", "index", "Rover", ["Author"], "A small rover.", "MIT", new Dictionary<string, string>(), null, ContentType.Vehicle), releases, null),
        ]);

    internal static ModVersionMetadata Vehicle(string version, IReadOnlyList<ModDependency> dependencies) => new(
        1, "rover", ModVersion.Parse(version), ReleaseStatus.Stable, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "2026.8.22.5348", 5348,
        new DownloadInfo("https://example.com/rover.zip", new string('A', 64), 1024, "application/zip"), 2048, dependencies, ContentType.Vehicle,
        install: new InstallInfo(null, derived: false, folder: "Rover"), source: "index");

    private static ModDependency Required(string id) => new(id, ModDependencyKind.Required);

    internal static byte[] VehicleArchive(string name, string vehicle)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in new[] { ($"{name}/meta.toml", $"name = \"{name}\"\n"), ($"{name}/vehicle.xml", vehicle) })
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    internal sealed class ZipDownloader : IModDownloader
    {
        public byte[] Bytes { get; set; } = [];

        public async Task<DownloadResult> DownloadAsync(ModVersionMetadata release, string archivePath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            await File.WriteAllBytesAsync(archivePath, Bytes, cancellationToken);
            return new DownloadResult(release.Download.Url, Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes)));
        }
    }
}
