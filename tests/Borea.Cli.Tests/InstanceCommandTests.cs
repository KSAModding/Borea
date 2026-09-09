using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;

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

    public void Dispose() => _host.Dispose();
}
