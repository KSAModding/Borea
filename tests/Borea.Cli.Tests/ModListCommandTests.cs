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
        Assert.Contains($"[cli] Instance \"Alpha (2)\" ({imported.InstanceId}) created from a modlist.", LogText());
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
        Assert.True(json.GetProperty("activated").GetBoolean());
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
    public async Task Import_InstallFails_RemovesTheNewInstanceAndLeavesNoInstanceActive()
    {
        var file = await WriteModListAsync(("flight-tools", "2.0.0"), ("helper-lib", "1.0.0"));
        _host.InstallerFactory = graph => new FolderInstaller(graph, failOn: "flight-tools");

        var run = await _host.RunAsync("instance", "import", file);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The disk is full.", run.Error);
        Assert.Empty(await new FileInstanceRepository(_host.Paths).GetAllAsync());
        Assert.False(File.Exists(_host.Paths.GetActiveInstancePointerPath()));
        Assert.Matches(@"\[cli\] Instance ""Shared"" \(.+\) deleted, no instance is active now\.", LogText());
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
        Assert.False(run.Json.GetProperty("activated").GetBoolean());
        Assert.Equal(["LocalOnly"], run.Json.GetProperty("notCopied").EnumerateArray().Select(folder => folder.GetString()));
        var copy = await InstanceNamedAsync("Alpha (copy)");
        Assert.Equal(["flight-tools 2.0.0 Manual", "helper-lib 1.0.0 Dependency"], Describe(copy));
        Assert.Equal(await EntriesAsync("Alpha"), await EntriesAsync("Alpha (copy)"));
        Assert.Empty(copy.ForeignMods);
        Assert.False(Directory.Exists(Path.Combine(_host.Paths.GetInstanceModsFolder(copy.InstanceId), "LocalOnly")));
        Assert.Contains($"[cli] Instance \"Alpha (copy)\" ({copy.InstanceId}) created as a duplicate.", LogText());
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

    private string LogText() =>
        File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_host.Root, "Logs"), "borea-*.log")));

    private string ModListPath(string fileName)
    {
        Directory.CreateDirectory(_host.Root);
        return Path.Combine(_host.Root, fileName);
    }

    public void Dispose() => _host.Dispose();
}
