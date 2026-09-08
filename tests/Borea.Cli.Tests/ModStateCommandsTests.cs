using Borea.Storage.Instances;
using Borea.Storage.State;

namespace Borea.Cli.Tests;

public sealed class ModStateCommandsTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Enable_WithInstance_MakesTheModActive()
    {
        var instanceId = await CreateAsync("Alpha");
        CreateModFolder(instanceId, "SomeMod");

        var run = await _host.RunAsync("enable", "SomeMod", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Enabled SomeMod in 'Alpha'.", run.Output);
        Assert.True(await ModState.IsActiveAsync(instanceId, "SomeMod"));
    }

    [Fact]
    public async Task Enable_WithoutInstance_UsesTheActiveOne()
    {
        await CreateAsync("Other");
        var alpha = await CreateAsync("Alpha");
        CreateModFolder(alpha, "SomeMod");
        await _host.RunAsync("instance", "activate", "Alpha");

        var run = await _host.RunAsync("enable", "SomeMod");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("'Alpha'", run.Output);
        Assert.True(await ModState.IsActiveAsync(alpha, "SomeMod"));
    }

    [Fact]
    public async Task Enable_ModThatIsNotInstalled_Fails()
    {
        await CreateAsync("Alpha");

        var run = await _host.RunAsync("enable", "SomeMod", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("no mod folder named 'SomeMod'", run.Error);
    }

    [Fact]
    public async Task Enable_ModWhoseFolderIsGone_StillFlipsItsEntry()
    {
        var instanceId = await CreateAsync("Alpha");
        var manifest = _host.Paths.GetInstanceManifestPath(instanceId);
        await File.WriteAllTextAsync(manifest, """
            [[mods]]
            id="SomeMod"
            enabled = false
            """);

        var run = await _host.RunAsync("enable", "SomeMod", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.True(await ModState.IsActiveAsync(instanceId, "SomeMod"));
    }

    [Fact]
    public async Task Enable_AlreadyEnabled_SaysSoAndChangesNothing()
    {
        var instanceId = await CreateAsync("Alpha");
        CreateModFolder(instanceId, "SomeMod");
        await _host.RunAsync("enable", "SomeMod", "--instance", "Alpha");

        var run = await _host.RunAsync("enable", "SomeMod", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Enabled SomeMod in 'Alpha'.", run.Output);
        Assert.Single(await ModState.GetEntriesAsync(instanceId));
    }

    [Fact]
    public async Task Enable_NoActiveInstance_Fails()
    {
        await CreateAsync("Alpha");

        var run = await _host.RunAsync("enable", "SomeMod");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No instance is active", run.Error);
    }

    [Fact]
    public async Task Enable_ActiveInstanceDeleted_Fails()
    {
        await CreateAsync("Alpha");
        await _host.RunAsync("instance", "activate", "Alpha");
        await _host.RunAsync("instance", "delete", "Alpha");

        var run = await _host.RunAsync("enable", "SomeMod");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("does not exist anymore", run.Error);
    }

    [Fact]
    public async Task Enable_UnknownInstance_Fails()
    {
        var run = await _host.RunAsync("enable", "SomeMod", "--instance", "Nope");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No instance is named 'Nope'.", run.Error);
    }

    [Fact]
    public async Task Enable_KeepsAnotherDisabledModDisabled()
    {
        var instanceId = await CreateAsync("Alpha");
        await _host.RunAsync("instance", "activate", "Alpha");
        CreateModFolder(instanceId, "ModA");
        CreateModFolder(instanceId, "ModB");
        CreateModFolder(instanceId, "ModC");
        await ModState.AddEntryAsync(instanceId, "ModA", enabled: true);
        await ModState.AddEntryAsync(instanceId, "ModB", enabled: false);

        var run = await _host.RunAsync("enable", "ModC");

        Assert.Equal(0, run.ExitCode);
        Assert.True(await ModState.IsActiveAsync(instanceId, "ModA"));
        Assert.False(await ModState.IsActiveAsync(instanceId, "ModB"));
        Assert.True(await ModState.IsActiveAsync(instanceId, "ModC"));
    }

    [Fact]
    public async Task Disable_MakesTheModInactive()
    {
        var instanceId = await CreateAsync("Alpha");
        CreateModFolder(instanceId, "SomeMod");
        await _host.RunAsync("enable", "SomeMod", "--instance", "Alpha");

        var run = await _host.RunAsync("disable", "somemod", "--instance", "alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Disabled somemod in 'Alpha'.", run.Output);
        Assert.False(await ModState.IsActiveAsync(instanceId, "SomeMod"));
    }

    [Fact]
    public async Task Disable_ModTheGameNamedByItsFolder_Works()
    {
        // The game writes the folder name as the id, and a folder name is not bound by the content id rules.
        var instanceId = await CreateAsync("Alpha");
        var manifest = _host.Paths.GetInstanceManifestPath(instanceId);
        await File.WriteAllTextAsync(manifest, """
            [[mods]]
            id="my mod"
            enabled = true
            """);

        var run = await _host.RunAsync("disable", "my mod", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.False(await ModState.IsActiveAsync(instanceId, "my mod"));
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    public async Task BlankModId_IsAUsageError(string command)
    {
        await CreateAsync("Alpha");

        var run = await _host.RunAsync(command, " ", "--instance", "Alpha");

        Assert.Equal(2, run.ExitCode);
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    public async Task BlankInstanceOption_IsAUsageError(string command)
    {
        await CreateAsync("Alpha");

        var run = await _host.RunAsync(command, "SomeMod", "--instance", "");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--instance", run.Error);
    }

    private void CreateModFolder(Guid instanceId, string folderName)
    {
        var modFolder = Path.Combine(_host.Paths.GetInstanceModsFolder(instanceId), folderName);
        Directory.CreateDirectory(modFolder);
        File.WriteAllText(Path.Combine(modFolder, "mod.toml"), $"name = \"{folderName}\"\n");
    }

    private FileModStateRepository ModState => new(_host.Paths);

    private async Task<Guid> CreateAsync(string name)
    {
        await _host.RunAsync("instance", "create", name);
        var instances = await new FileInstanceRepository(_host.Paths).GetAllAsync();
        return instances.Single(instance => instance.Name == name).InstanceId;
    }

    public void Dispose() => _host.Dispose();
}
