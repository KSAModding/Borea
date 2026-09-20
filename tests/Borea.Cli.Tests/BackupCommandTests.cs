using Borea.Core.Instances;
using Borea.Storage.Instances;

namespace Borea.Cli.Tests;

public sealed class BackupCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Backups_NoBackups_SaysSo()
    {
        await CreateInstanceAsync();

        var human = await _host.RunAsync("instance", "backups", "Alpha");
        var json = await _host.RunAsync("instance", "backups", "Alpha", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("No backups of 'Alpha'.", human.Output);
        Assert.Empty(json.Json.EnumerateArray());
    }

    [Fact]
    public async Task Backups_ListsWhatEachBackupWasNewestFirst()
    {
        var instanceId = await CreateInstanceAsync();
        var saves = new FileGameSaveStore(_host.Paths);
        await saves.BackUpAsync(instanceId, await AddSaveAsync(instanceId, "Orbit", 10));
        Directory.CreateDirectory(Path.Combine(_host.Paths.GetBackupsRoot(), instanceId.ToString(), "saves", "Moon-2020-01-01T000000Z"));

        var human = await _host.RunAsync("instance", "backups", "Alpha");
        var json = await _host.RunAsync("instance", "backups", "Alpha", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("save", human.Output);
        Assert.Contains("backed-up", human.Output);
        var entries = json.Json.EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.EndsWith(".zip", entries[0].GetProperty("id").GetString());
        Assert.Equal("save", entries[0].GetProperty("kind").GetString());
        Assert.Equal("Orbit", entries[0].GetProperty("folder").GetString());
        Assert.Equal("backed-up", entries[0].GetProperty("reason").GetString());
        Assert.True(entries[0].GetProperty("archive").GetBoolean());
        Assert.True(entries[0].GetProperty("canRestore").GetBoolean());
        Assert.Equal("saves/Moon-2020-01-01T000000Z", entries[1].GetProperty("id").GetString());
        Assert.Equal("unknown", entries[1].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RestoreBackup_OccupiedTarget_NeedsReplace()
    {
        var instanceId = await CreateInstanceAsync();
        var orbit = await AddSaveAsync(instanceId, "Orbit", 300);
        var moved = await new FileGameSaveStore(_host.Paths).DeleteAsync(instanceId, orbit);
        await AddSaveAsync(instanceId, "Orbit", 20);
        var id = "saves/" + Path.GetFileName(moved);

        var refused = await _host.RunAsync("instance", "restore-backup", "Alpha", id);

        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("--replace", refused.Error);
        Assert.Equal(20, new FileInfo(Path.Combine(orbit.Path, "universe.xml")).Length);

        var restored = await _host.RunAsync("instance", "restore-backup", "Alpha", Path.GetFileName(moved), "--replace", "--json");

        Assert.Equal(0, restored.ExitCode);
        Assert.Equal(id, restored.Json.GetProperty("id").GetString());
        Assert.Equal(300, new FileInfo(Path.Combine(orbit.Path, "universe.xml")).Length);
        var list = await _host.RunAsync("instance", "backups", "Alpha", "--json");
        Assert.Equal("replaced", Assert.Single(list.Json.EnumerateArray()).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task RestoreBackup_EmptyTarget_PutsItBack()
    {
        var instanceId = await CreateInstanceAsync();
        var orbit = await AddSaveAsync(instanceId, "Orbit", 300);
        var moved = await new FileGameSaveStore(_host.Paths).DeleteAsync(instanceId, orbit);

        var restored = await _host.RunAsync("instance", "restore-backup", "Alpha", "saves/" + Path.GetFileName(moved));

        Assert.Equal(0, restored.ExitCode);
        Assert.Contains("Restored 'Orbit' into 'Alpha'.", restored.Output);
        Assert.True(File.Exists(Path.Combine(orbit.Path, "universe.xml")));
    }

    [Fact]
    public async Task DeleteBackup_DeletesItForGood()
    {
        var instanceId = await CreateInstanceAsync();
        var zip = await new FileGameSaveStore(_host.Paths).BackUpAsync(instanceId, await AddSaveAsync(instanceId, "Orbit", 10));

        var deleted = await _host.RunAsync("instance", "delete-backup", "Alpha", "saves/" + Path.GetFileName(zip), "--json");

        Assert.Equal(0, deleted.ExitCode);
        Assert.Equal("Orbit", deleted.Json.GetProperty("folder").GetString());
        Assert.False(File.Exists(zip));
        Assert.Empty((await _host.RunAsync("instance", "backups", "Alpha", "--json")).Json.EnumerateArray());
    }

    [Fact]
    public async Task DeleteBackup_UnknownBackup_Fails()
    {
        await CreateInstanceAsync();

        var result = await _host.RunAsync("instance", "delete-backup", "Alpha", "saves/Nothing");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("has no backup 'saves/Nothing'", result.Error);
    }

    private async Task<Guid> CreateInstanceAsync()
    {
        var created = await _host.RunAsync("instance", "create", "Alpha", "--json");
        return Guid.Parse(created.Json.GetProperty("id").GetString()!);
    }

    private async Task<GameSaveEntry> AddSaveAsync(Guid instanceId, string name, int universeBytes)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_host.Paths.GetInstanceSavesFolder(instanceId), name)).FullName;
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"name = \"{name}\"\n");
        File.WriteAllBytes(Path.Combine(folder, "universe.xml"), new byte[universeBytes]);
        return (await new FileGameSaveStore(_host.Paths).ListAsync(instanceId, GameSaveKind.Save)).Single();
    }
}
