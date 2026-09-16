using Borea.Composition;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Storage.Instances;

namespace Borea.Cli.Tests;

public sealed class ModListCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    public ModListCommandTests()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "helper-lib", version: "1.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "flight-tools", version: "2.0.0", dependencies: [new ModDependency("helper-lib", ModDependencyKind.Required)]));
        _host.InstallerFactory = graph => new FolderInstaller(graph);
    }

    [Fact]
    public async Task Export_WithoutAFile_PrintsTheModlistInLoadOrder()
    {
        await SeedAlphaAsync();

        var run = await _host.RunAsync("instance", "export", "Alpha");

        Assert.Equal(0, run.ExitCode);
        var modList = new TomlModListFormat().Read(run.Output);
        Assert.Equal("Alpha", modList.Name);
        Assert.Equal(["helper-lib 1.0.0 True", "flight-tools 2.0.0 False"], modList.Mods.Select(entry => $"{entry.ModId} {entry.Version} {entry.Enabled}"));
    }

    [Fact]
    public async Task Export_ToAFileThatExists_NeedsForce()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        var file = ModListPath("alpha.toml");
        await File.WriteAllTextAsync(file, "keep");

        var refused = await _host.RunAsync("instance", "export", "Alpha", file);
        var kept = await File.ReadAllTextAsync(file);
        var forced = await _host.RunAsync("instance", "export", "Alpha", file, "--force");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("--force", refused.Error);
        Assert.Equal("keep", kept);
        Assert.Equal(0, forced.ExitCode);
        Assert.Contains($"to {file}.", forced.Output);
        Assert.Empty(new TomlModListFormat().Read(await File.ReadAllTextAsync(file)).Mods);
    }

    [Fact]
    public async Task ExportThenImport_RoundTripsTheInstance()
    {
        await SeedAlphaAsync();
        var file = ModListPath("alpha.toml");
        await _host.RunAsync("instance", "export", "Alpha", file);

        var run = await _host.RunAsync("instance", "import", file);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Created instance 'Alpha (2)'", run.Output);
        var imported = await InstanceNamedAsync("Alpha (2)");
        Assert.Equal(InstanceSource.Custom.Value, imported.Source);
        Assert.Equal(["flight-tools 2.0.0 Manual", "helper-lib 1.0.0 Dependency"], Describe(imported));
        Assert.Equal(await EntriesAsync("Alpha"), await EntriesAsync("Alpha (2)"));
    }

    [Fact]
    public async Task Import_UnknownId_IsReportedBeforeAnythingIsCreated()
    {
        var file = await WriteModListAsync(("flight-tools", "2.0.0"), ("helper-lib", "1.0.0"), ("ghost-mod", "3.0.0"));

        var run = await _host.RunAsync("instance", "import", file);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("  unknown   ghost-mod 3.0.0", run.Output);
        Assert.Contains("--skip-unknown", run.Error);
        Assert.Empty(await new FileInstanceRepository(_host.Paths).GetAllAsync());
    }

    [Fact]
    public async Task Import_SkipUnknown_CreatesTheInstanceWithoutTheUnknownMod()
    {
        var file = await WriteModListAsync(("flight-tools", "2.0.0"), ("helper-lib", "1.0.0"), ("ghost-mod", "3.0.0"));

        var run = await _host.RunAsync("instance", "import", file, "--skip-unknown", "--json");

        Assert.Equal(0, run.ExitCode);
        var json = run.Json;
        Assert.True(json.GetProperty("created").GetBoolean());
        Assert.Equal("Shared", json.GetProperty("name").GetString());
        Assert.Equal(["available", "available", "unknown"], json.GetProperty("mods").EnumerateArray().Select(mod => mod.GetProperty("state").GetString()));
        var imported = await InstanceNamedAsync("Shared");
        Assert.Equal(Guid.Parse(json.GetProperty("instanceId").GetString()!), imported.InstanceId);
        Assert.Equal(["flight-tools 2.0.0 Manual", "helper-lib 1.0.0 Dependency"], Describe(imported));
    }

    [Fact]
    public async Task Import_YankedRelease_NeedsTheExplicitOption()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "map-tools", version: "1.0.0", yanked: true, yankedReason: "Broken build."));
        var file = await WriteModListAsync(("helper-lib", "1.0.0"), ("map-tools", "1.0.0"));

        var refused = await _host.RunAsync("instance", "import", file);
        var afterRefusal = await new FileInstanceRepository(_host.Paths).GetAllAsync();
        var accepted = await _host.RunAsync("instance", "import", file, "--proceed-with-yanked", "MAP-TOOLS", "--json");

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("  yanked    map-tools 1.0.0: Broken build.", refused.Output);
        Assert.DoesNotContain("warning: Broken build.", refused.Output);
        Assert.Contains("error: These releases are yanked: map-tools 1.0.0. Pass --proceed-with-yanked", refused.Error);
        Assert.Empty(afterRefusal);
        Assert.Equal(0, accepted.ExitCode);
        Assert.Equal(["available", "yanked"], accepted.Json.GetProperty("mods").EnumerateArray().Select(mod => mod.GetProperty("state").GetString()));
        Assert.Equal(["helper-lib 1.0.0 Manual", "map-tools 1.0.0 Manual"], Describe(await InstanceNamedAsync("Shared")));
    }

    [Fact]
    public async Task Import_DryRun_PrintsThePlanAndCreatesNothing()
    {
        var file = await WriteModListAsync(("flight-tools", "2.0.0"), ("helper-lib", "1.0.0"));

        var run = await _host.RunAsync("instance", "import", file, "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Install flight-tools 2.0.0.", run.Output);
        Assert.Empty(await new FileInstanceRepository(_host.Paths).GetAllAsync());
    }

    [Fact]
    public async Task Import_NewerFormat_FailsAndSaysToUpdate()
    {
        var file = ModListPath("future.toml");
        await File.WriteAllTextAsync(file, "format = 2\n");

        var run = await _host.RunAsync("instance", "import", file);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("format 2", run.Error);
        Assert.Contains("Update Borea", run.Error);
        Assert.Empty(await new FileInstanceRepository(_host.Paths).GetAllAsync());
    }

    [Fact]
    public async Task Duplicate_CopiesTheModsAndListsTheFoldersItDoesNotCopy()
    {
        await SeedAlphaAsync();
        var alpha = await InstanceNamedAsync("Alpha");
        var local = Directory.CreateDirectory(Path.Combine(_host.Paths.GetInstanceModsFolder(alpha.InstanceId), "LocalOnly"));
        await File.WriteAllTextAsync(Path.Combine(local.FullName, "mod.toml"), "name = \"LocalOnly\"");
        await _host.RunAsync("instance", "scan", "Alpha");

        var run = await _host.RunAsync("instance", "duplicate", "Alpha", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Alpha (copy)", run.Json.GetProperty("name").GetString());
        Assert.Equal(["LocalOnly"], run.Json.GetProperty("notCopied").EnumerateArray().Select(folder => folder.GetString()));
        var copy = await InstanceNamedAsync("Alpha (copy)");
        Assert.Equal(["flight-tools 2.0.0 Manual", "helper-lib 1.0.0 Dependency"], Describe(copy));
        Assert.Equal(await EntriesAsync("Alpha"), await EntriesAsync("Alpha (copy)"));
        Assert.Empty(copy.ForeignMods);
        Assert.False(Directory.Exists(Path.Combine(_host.Paths.GetInstanceModsFolder(copy.InstanceId), "LocalOnly")));
    }

    [Fact]
    public async Task Duplicate_InstallFails_RemovesTheNewInstance()
    {
        await SeedAlphaAsync();
        _host.InstallerFactory = graph => new FolderInstaller(graph, failOn: "flight-tools");

        var run = await _host.RunAsync("instance", "duplicate", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The disk is full.", run.Error);
        Assert.Equal("Alpha", Assert.Single(await new FileInstanceRepository(_host.Paths).GetAllAsync()).Name);
        Assert.Single(Directory.GetDirectories(_host.Paths.GetInstancesRoot()));
    }

    [Fact]
    public async Task Duplicate_NameInUse_Fails()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        await _host.RunAsync("instance", "create", "Beta");

        var run = await _host.RunAsync("instance", "duplicate", "Alpha", "--name", "beta");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("already in use", run.Error);
        Assert.Equal(2, (await new FileInstanceRepository(_host.Paths).GetAllAsync()).Count);
    }

    private async Task SeedAlphaAsync()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        Assert.Equal(0, (await _host.RunAsync("install", "flight-tools", "--version", "2.0.0", "--instance", "Alpha")).ExitCode);
        Assert.Equal(0, (await _host.RunAsync("disable", "flight-tools", "--instance", "Alpha")).ExitCode);
    }

    private async Task<Instance> InstanceNamedAsync(string name) =>
        (await new FileInstanceRepository(_host.Paths).GetAllAsync()).Single(instance => instance.Name == name);

    private static IReadOnlyList<string> Describe(Instance instance) =>
        instance.Mods.OrderBy(mod => mod.ModId, StringComparer.Ordinal).Select(mod => $"{mod.ModId} {mod.Version} {mod.Reason}").ToList();

    private async Task<IReadOnlyList<string>> EntriesAsync(string instance)
    {
        var run = await _host.RunAsync("instance", "mods", instance, "--json");
        return run.Json.EnumerateArray().Select(entry => $"{entry.GetProperty("id").GetString()} {entry.GetProperty("enabled").GetBoolean()}").ToList();
    }

    private async Task<string> WriteModListAsync(params (string Id, string Version)[] mods)
    {
        var path = ModListPath("shared.toml");
        var modList = new ModList("Shared", mods.Select(mod => new ModListEntry(mod.Id, ModVersion.Parse(mod.Version), enabled: true)).ToList());
        await File.WriteAllTextAsync(path, new TomlModListFormat().Write(modList));
        return path;
    }

    private string ModListPath(string fileName)
    {
        Directory.CreateDirectory(_host.Root);
        return Path.Combine(_host.Root, fileName);
    }

    public void Dispose() => _host.Dispose();

    /// <summary>
    /// Installs without a download: the folder with its mod.toml and ownership file, the record, and the manifest entry.
    /// </summary>
    private sealed class FolderInstaller(BoreaServices graph, string? failOn = null) : IModInstaller
    {
        public Task<InstallResult> InstallAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task<GuardedInstallResult> InstallGuardedAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, InstallPlanningState expectedState, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (ModIds.Equals(release.ModId, failOn))
                throw new IOException("The disk is full.");

            var token = Guid.NewGuid().ToString("N");
            var folder = Directory.CreateDirectory(Path.Combine(graph.Paths.GetInstanceModsFolder(instanceId), release.ModId));
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "mod.toml"), $"name = \"{release.ModId}\"", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, ".borea-owner"), token, cancellationToken);
            var installed = new InstalledMod(release.ModId, release.Version, reason, DateTimeOffset.UtcNow, release, release.Download.Sha256, ModInstallOwnership.Borea, token);
            var state = await graph.Instances.UpdateAsync(instanceId, current =>
            {
                if (!expectedState.Matches(current))
                    throw new InvalidOperationException("The instance changed after Borea planned the operation.");

                current.AddMod(installed);
                return InstallPlanningState.Capture(current);
            }, cancellationToken);
            var entry = await graph.ModState.AddEntryAsync(instanceId, release.ModId, enable, cancellationToken);
            var download = new DownloadResult(release.Download.Url, release.Download.SizeBytes ?? 0, release.Download.Sha256 ?? string.Empty);
            return new GuardedInstallResult(new InstallResult(installed, download, entry), state);
        }
    }
}
