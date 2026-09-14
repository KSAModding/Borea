using System.Security.Cryptography;
using System.Text.Json;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Mods;

namespace Borea.Cli.Tests;

public sealed class InstanceCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task List_NoInstances_SaysSo()
    {
        var human = await _host.RunAsync("instance", "list");
        var json = await _host.RunAsync("instance", "list", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("No instances.", human.Output);
        Assert.Equal(0, json.ExitCode);
        Assert.Empty(json.Json.EnumerateArray());
    }

    [Fact]
    public async Task Create_ThenList_ShowsItInactive()
    {
        var create = await _host.RunAsync("instance", "create", "Alpha");
        var list = await _host.RunAsync("instance", "list", "--json");

        Assert.Equal(0, create.ExitCode);
        Assert.Contains("Alpha", create.Output);
        var entry = Assert.Single(list.Json.EnumerateArray());
        Assert.Equal("Alpha", entry.GetProperty("name").GetString());
        Assert.False(entry.GetProperty("active").GetBoolean());
        Assert.Equal("custom", entry.GetProperty("source").GetProperty("kind").GetString());
        Assert.True(Guid.TryParse(entry.GetProperty("id").GetString(), out _));
    }

    [Fact]
    public async Task List_InstanceFromAModPack_NamesThePackAndItsVersion()
    {
        var packed = Instance.FromExisting(Guid.NewGuid(), "Packed", new InstanceSource.FromModPack("SomePack", ModVersion.Parse("1.2.0")),
            DateTimeOffset.UtcNow, Array.Empty<InstalledMod>(), isFavorite: false);
        await new FileInstanceRepository(_host.Paths).SaveAsync(packed);

        var human = await _host.RunAsync("instance", "list");
        var json = await _host.RunAsync("instance", "list", "--json");

        Assert.Contains("modpack SomePack 1.2.0", human.Output);
        var source = Assert.Single(json.Json.EnumerateArray()).GetProperty("source");
        Assert.Equal("modpack", source.GetProperty("kind").GetString());
        Assert.Equal("SomePack", source.GetProperty("modPackId").GetString());
        Assert.Equal("1.2.0", source.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Create_NameTakenInAnotherCase_Fails()
    {
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("instance", "create", "alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("already in use", run.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Create_BlankName_IsAUsageError(string name)
    {
        var run = await _host.RunAsync("instance", "create", name);

        Assert.Equal(2, run.ExitCode);
    }

    [Fact]
    public async Task Activate_ByNameInAnotherCase_MarksItInTheList()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        await _host.RunAsync("instance", "create", "Beta");

        var activate = await _host.RunAsync("instance", "activate", "alpha");
        var human = await _host.RunAsync("instance", "list");
        var json = await _host.RunAsync("instance", "list", "--json");

        Assert.Equal(0, activate.ExitCode);
        Assert.Contains("* Alpha", human.Output);
        Assert.Contains("  Beta", human.Output);
        var entries = json.Json.EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!, e => e.GetProperty("active").GetBoolean());
        Assert.True(entries["Alpha"]);
        Assert.False(entries["Beta"]);
    }

    [Fact]
    public async Task Activate_ById_SetsTheActivePointer()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        var list = await _host.RunAsync("instance", "list", "--json");
        var id = Guid.Parse(Assert.Single(list.Json.EnumerateArray()).GetProperty("id").GetString()!);

        var activate = await _host.RunAsync("instance", "activate", id.ToString());

        Assert.Equal(0, activate.ExitCode);
        Assert.Equal(id, await new FileInstanceRepository(_host.Paths).GetActiveInstanceIdAsync());
    }

    [Fact]
    public async Task Rename_ChangesTheName()
    {
        await _host.RunAsync("instance", "create", "Alpha");

        var rename = await _host.RunAsync("instance", "rename", "Alpha", "Beta");
        var list = await _host.RunAsync("instance", "list", "--json");

        Assert.Equal(0, rename.ExitCode);
        var entry = Assert.Single(list.Json.EnumerateArray());
        Assert.Equal("Beta", entry.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rename_ToATakenName_Fails()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        await _host.RunAsync("instance", "create", "Beta");

        var run = await _host.RunAsync("instance", "rename", "Alpha", "beta");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("already in use", run.Error);
    }

    [Fact]
    public async Task Delete_RemovesTheInstanceAndItsFolder()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        var before = await _host.RunAsync("instance", "list", "--json");
        var id = Guid.Parse(Assert.Single(before.Json.EnumerateArray()).GetProperty("id").GetString()!);

        var delete = await _host.RunAsync("instance", "delete", "Alpha");
        var after = await _host.RunAsync("instance", "list", "--json");

        Assert.Equal(0, delete.ExitCode);
        Assert.Empty(after.Json.EnumerateArray());
        Assert.False(Directory.Exists(_host.Paths.GetInstanceRoot(id)));
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("delete")]
    public async Task UnknownInstance_Fails(string command)
    {
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("instance", command, "Nope");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No instance is named 'Nope'.", run.Error);
    }

    [Fact]
    public async Task UnknownId_Fails_SayingTheIdMatchedNothing()
    {
        var id = Guid.NewGuid().ToString();

        var run = await _host.RunAsync("instance", "activate", id);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"No instance has the id '{id}'", run.Error);
    }

    [Fact]
    public async Task NamesThatDifferOnlyInCase_NeedTheId()
    {
        // The repository refuses to create such a pair, so the files are written directly.
        var repository = new FileInstanceRepository(_host.Paths);
        var upper = Instance.FromExisting(Guid.NewGuid(), "Alpha", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, Array.Empty<InstalledMod>(), isFavorite: false);
        var lower = Instance.FromExisting(Guid.NewGuid(), "alpha", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, Array.Empty<InstalledMod>(), isFavorite: false);
        await repository.SaveAsync(upper);
        await repository.SaveAsync(lower);

        var byName = await _host.RunAsync("instance", "activate", "Alpha");
        var byId = await _host.RunAsync("instance", "activate", lower.InstanceId.ToString());

        Assert.Equal(1, byName.ExitCode);
        Assert.Contains(upper.InstanceId.ToString(), byName.Error);
        Assert.Contains(lower.InstanceId.ToString(), byName.Error);
        Assert.Equal(0, byId.ExitCode);
        Assert.Equal(lower.InstanceId, await repository.GetActiveInstanceIdAsync());
    }

    [Fact]
    public async Task List_ActivePointerThatIsNotToml_Fails_NamingTheFile()
    {
        var pointer = _host.Paths.GetActiveInstancePointerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
        await File.WriteAllTextAsync(pointer, "ActiveInstanceId = \n");

        var run = await _host.RunAsync("instance", "list");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(pointer, run.Error);
        Assert.DoesNotContain("Unhandled exception", run.Error);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public async Task Mods_PrintsManifestEntriesInLoadOrder()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        var list = await _host.RunAsync("instance", "list", "--json");
        var instanceId = Guid.Parse(Assert.Single(list.Json.EnumerateArray()).GetProperty("id").GetString()!);
        var repository = new Borea.Storage.State.FileModStateRepository(_host.Paths);

        var firstFolder = Directory.CreateDirectory(Path.Combine(_host.Paths.GetInstanceModsFolder(instanceId), "First"));
        var secondFolder = Directory.CreateDirectory(Path.Combine(_host.Paths.GetInstanceModsFolder(instanceId), "Second"));
        await File.WriteAllTextAsync(Path.Combine(firstFolder.FullName, "mod.toml"), "name = \"First\"");
        await File.WriteAllTextAsync(Path.Combine(secondFolder.FullName, "mod.toml"), "name = \"Second\"");
        Assert.Equal(Borea.Core.State.ModEntryAddResult.Added, await repository.AddEntryAsync(instanceId, "First", enabled: true));
        Assert.Equal(Borea.Core.State.ModEntryAddResult.Added, await repository.AddEntryAsync(instanceId, "Second", enabled: false));

        var human = await _host.RunAsync("instance", "mods", "Alpha");
        var json = await _host.RunAsync("instance", "mods", "Alpha", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Equal(new[] { "enabled   First", "disabled  Second" }, human.Output.Trim().Split(Environment.NewLine));
        Assert.Equal(new[] { "First", "Second" }, json.Json.EnumerateArray().Select(entry => entry.GetProperty("id").GetString()));
        Assert.Equal(new[] { true, false }, json.Json.EnumerateArray().Select(entry => entry.GetProperty("enabled").GetBoolean()));
    }

    [Fact]
    public async Task Mods_NoEntries_SaysSo()
    {
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("instance", "mods", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("No mods in 'Alpha'.", run.Output);
    }

    [Fact]
    public async Task Mods_UnknownInstance_Fails()
    {
        var run = await _host.RunAsync("instance", "mods", "Nope");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No instance is named 'Nope'.", run.Error);
    }

    [Fact]
    public async Task Scan_NoForeignFolders_SaysSo()
    {
        await _host.RunAsync("instance", "create", "Alpha");

        var human = await _host.RunAsync("instance", "scan", "Alpha");
        var json = await _host.RunAsync("instance", "scan", "Alpha", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("No mod folders in 'Alpha' that Borea did not install.", human.Output);
        Assert.Equal(0, json.ExitCode);
        Assert.Empty(json.Json.EnumerateArray());
    }

    [Fact]
    public async Task Scan_ListsForeignFolders_AndWhetherTheIndexListsThem()
    {
        var instanceId = await CreateInstanceAsync("Alpha");
        WriteMod(instanceId, "flight-tools", """
            [[StarMap.ModDependencies]]
            ModId = "HelperMod"
            Optional = true
            """);
        WriteMod(instanceId, "LocalOnly", "name = \"LocalOnly\"");
        Directory.CreateDirectory(Path.Combine(_host.Paths.GetInstanceModsFolder(instanceId), "NotAMod"));
        IndexWithListing("flight-tools");

        var human = await _host.RunAsync("instance", "scan", "Alpha");
        var json = await _host.RunAsync("instance", "scan", "Alpha", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Equal(new[] { "flight-tools  in the content index", "LocalOnly     not in the content index" }, human.Output.Trim().Split(Environment.NewLine));
        var entries = json.Json.EnumerateArray().ToList();
        Assert.Equal(new[] { "flight-tools", "LocalOnly" }, entries.Select(entry => entry.GetProperty("folder").GetString()));
        Assert.Equal(new[] { true, false }, entries.Select(entry => entry.GetProperty("inIndex").GetBoolean()));
        var dependency = Assert.Single(entries[0].GetProperty("dependencies").EnumerateArray());
        Assert.Equal("HelperMod", dependency.GetProperty("id").GetString());
        Assert.True(dependency.GetProperty("optional").GetBoolean());
        Assert.Equal(JsonValueKind.Null, entries[0].GetProperty("dependencyReadError").ValueKind);
        var saved = await new FileInstanceRepository(_host.Paths).GetByIdAsync(instanceId);
        Assert.Equal(new[] { "flight-tools", "LocalOnly" }, saved!.ForeignMods.Select(mod => mod.FolderName));
    }

    [Fact]
    public async Task Scan_ModTomlThatDoesNotParse_WarnsAndStillListsTheFolder()
    {
        var instanceId = await CreateInstanceAsync("Alpha");
        WriteMod(instanceId, "Broken", "name = ");

        var human = await _host.RunAsync("instance", "scan", "Alpha");
        var json = await _host.RunAsync("instance", "scan", "Alpha", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("Broken", human.Output);
        Assert.Contains("warning: The mod.toml of 'Broken' could not be read.", human.Error);
        var entry = Assert.Single(json.Json.EnumerateArray());
        Assert.False(string.IsNullOrEmpty(entry.GetProperty("dependencyReadError").GetString()));
    }

    [Fact]
    public async Task Scan_IndexCannotBeRead_StillListsTheFolders()
    {
        var instanceId = await CreateInstanceAsync("Alpha");
        WriteMod(instanceId, "flight-tools", "name = \"Flight Tools\"");
        _host.IndexReader.Read = _ => throw new IOException("No cached index exists.");

        var human = await _host.RunAsync("instance", "scan", "Alpha");
        var json = await _host.RunAsync("instance", "scan", "Alpha", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Equal("flight-tools  content index not available", human.Output.Trim());
        Assert.Contains("warning: The content index could not be read. No cached index exists.", human.Error);
        Assert.Equal(0, json.ExitCode);
        var entry = Assert.Single(json.Json.EnumerateArray());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("inIndex").ValueKind);
    }

    [Fact]
    public async Task Scan_UnknownInstance_Fails()
    {
        var run = await _host.RunAsync("instance", "scan", "Nope");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No instance is named 'Nope'.", run.Error);
    }

    [Fact]
    public async Task Adopt_ArchiveMatchesARelease_RecordsTheFolderAsThatRelease()
    {
        var instanceId = await CreateInstanceAsync("Alpha");
        var folder = WriteMod(instanceId, "flight-tools", "name = \"Flight Tools\"");
        var archive = WriteArchive(new byte[] { 1, 2, 3 });
        var lookup = UseArchiveLookup();
        lookup.Releases.Add(ReleaseFor("flight-tools", archive));

        var run = await _host.RunAsync("instance", "adopt", "Alpha", "flight-tools", "--archive", archive);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Adopted 'flight-tools' 2.0.0 in 'Alpha'.", run.Output);
        var saved = await new FileInstanceRepository(_host.Paths).GetByIdAsync(instanceId);
        var mod = Assert.Single(saved!.Mods);
        Assert.Equal("flight-tools", mod.ModId);
        Assert.Equal(ModInstallOwnership.Foreign, mod.Ownership);
        Assert.Empty(saved.ForeignMods);
        Assert.True(File.Exists(Path.Combine(folder, "mod.toml")));
    }

    [Fact]
    public async Task Adopt_ArchiveMatchesNoRelease_FailsAndKeepsTheFolderForeign()
    {
        var instanceId = await CreateInstanceAsync("Alpha");
        var folder = WriteMod(instanceId, "flight-tools", "name = \"Flight Tools\"");
        var archive = WriteArchive(new byte[] { 4, 5, 6 });
        var lookup = UseArchiveLookup();
        lookup.Releases.Add(ReleaseFor("flight-tools", WriteArchive(new byte[] { 7, 8, 9 })));

        var run = await _host.RunAsync("instance", "adopt", "Alpha", "flight-tools", "--archive", archive);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("matches no release of 'flight-tools' in the content index", run.Error);
        Assert.Equal(string.Empty, run.Output);
        var saved = await new FileInstanceRepository(_host.Paths).GetByIdAsync(instanceId);
        Assert.Empty(saved!.Mods);
        Assert.Equal("flight-tools", Assert.Single(saved.ForeignMods).FolderName);
        Assert.True(File.Exists(Path.Combine(folder, "mod.toml")));
    }

    [Fact]
    public async Task Adopt_FolderAlreadyAdopted_Fails()
    {
        var instanceId = await CreateInstanceAsync("Alpha");
        WriteMod(instanceId, "flight-tools", "name = \"Flight Tools\"");
        var archive = WriteArchive(new byte[] { 1, 2, 3 });
        UseArchiveLookup().Releases.Add(ReleaseFor("flight-tools", archive));
        await _host.RunAsync("instance", "adopt", "Alpha", "flight-tools", "--archive", archive);

        var run = await _host.RunAsync("instance", "adopt", "Alpha", "flight-tools", "--archive", archive);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Mod 'flight-tools' is already installed in this instance.", run.Error);
        Assert.DoesNotContain("Unhandled exception", run.Error);
    }

    [Fact]
    public async Task Adopt_NoSuchFolder_Fails()
    {
        await CreateInstanceAsync("Alpha");
        var archive = WriteArchive(new byte[] { 1 });

        var run = await _host.RunAsync("instance", "adopt", "Alpha", "flight-tools", "--archive", archive);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("has no foreign mod folder 'flight-tools'", run.Error);
    }

    [Fact]
    public async Task Adopt_ArchiveDoesNotExist_Fails()
    {
        var instanceId = await CreateInstanceAsync("Alpha");
        WriteMod(instanceId, "flight-tools", "name = \"Flight Tools\"");

        var run = await _host.RunAsync("instance", "adopt", "Alpha", "flight-tools", "--archive", Path.Combine(_host.Root, "missing.zip"));

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("does not exist", run.Error);
        Assert.DoesNotContain("Unhandled exception", run.Error);
    }

    [Theory]
    [InlineData("flight-tools")]
    [InlineData("flight-tools", "--archive", " ")]
    [InlineData("..", "--archive", "mod.zip")]
    [InlineData("mods/flight-tools", "--archive", "mod.zip")]
    public async Task Adopt_BadArguments_AreUsageErrors(params string[] arguments)
    {
        var run = await _host.RunAsync(new[] { "instance", "adopt", "Alpha" }.Concat(arguments).ToArray());

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(0, _host.Builds);
    }

    private async Task<Guid> CreateInstanceAsync(string name)
    {
        var created = await new FileInstanceRepository(_host.Paths).CreateAsync(name, InstanceSource.Custom.Value);
        return created.InstanceId;
    }

    private string WriteMod(Guid instanceId, string folderName, string manifest)
    {
        var folder = Path.Combine(_host.Paths.GetInstanceModsFolder(instanceId), folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mod.toml"), manifest);
        return folder;
    }

    private string WriteArchive(byte[] bytes)
    {
        Directory.CreateDirectory(_host.Root);
        var path = Path.Combine(_host.Root, Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private void IndexWithListing(string id)
    {
        var listing = ContentCommandFixtures.Listing(id: id);
        _host.IndexReader.Snapshot = new ContentIndexSnapshot(
            1,
            new[] { new ContentIndexListing(listing.ModId, listing, new[] { ContentCommandFixtures.Release(id: id) }, null) },
            Array.Empty<ContentIndexPack>(),
            null,
            Array.Empty<ContentIndexDiagnostic>());
    }

    private FakeArchiveReleaseLookup UseArchiveLookup()
    {
        var lookup = new FakeArchiveReleaseLookup();
        _host.ForeignModAdopterFactory = graph => new FileForeignModAdopter(graph.Paths, graph.Instances, lookup);
        return lookup;
    }

    private static ModVersionMetadata ReleaseFor(string id, string archive)
    {
        var release = ContentCommandFixtures.Release(id: id);
        var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive)));
        return new ModVersionMetadata(
            specVersion: 1,
            modId: id,
            version: release.Version,
            releaseStatus: release.ReleaseStatus,
            releaseDate: release.ReleaseDate,
            gameMin: release.GameMin,
            gameMinRevision: release.GameMinRevision,
            download: new DownloadInfo(release.Download.Url, sha256, release.Download.SizeBytes, release.Download.ContentType),
            installSizeBytes: release.InstallSizeBytes,
            dependencies: release.Dependencies,
            source: "index");
    }

    public void Dispose() => _host.Dispose();
}
