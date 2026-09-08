using System.Text.Json;

namespace Borea.Cli.Tests;

public sealed class SettingsCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Show_NoSettingsFile_ReportsNothingSet()
    {
        var run = await _host.RunAsync("settings", "show");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Game directory: not set", run.Output);
        Assert.Contains("Loader directories: none", run.Output);
    }

    [Fact]
    public async Task Show_NoSettingsFile_JsonHasNullGameAndNoLoaders()
    {
        var run = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(JsonValueKind.Null, run.Json.GetProperty("gameDirectory").ValueKind);
        Assert.Empty(run.Json.GetProperty("loaderDirectories").EnumerateObject());
    }

    [Fact]
    public async Task SetGame_SavesTheDirectory_AndShowReadsItBack()
    {
        var game = Directory.CreateDirectory(Path.Combine(_host.Root, "Game")).FullName;

        var set = await _host.RunAsync("settings", "set", "game", game);
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, set.ExitCode);
        Assert.Contains(game, set.Output);
        Assert.Equal(string.Empty, set.Error);
        Assert.Equal(game, show.Json.GetProperty("gameDirectory").GetString());
    }

    [Fact]
    public async Task SetGame_DirectoryThatDoesNotExist_SavesItAndWarns()
    {
        var game = Path.Combine(_host.Root, "Missing");

        var set = await _host.RunAsync("settings", "set", "game", game);
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, set.ExitCode);
        Assert.Contains("warning:", set.Error);
        Assert.Contains(game, set.Error);
        Assert.Equal(game, show.Json.GetProperty("gameDirectory").GetString());
    }

    [Fact]
    public async Task SetGame_RelativePath_IsStoredAbsolute()
    {
        var set = await _host.RunAsync("settings", "set", "game", "Game");
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, set.ExitCode);
        var stored = show.Json.GetProperty("gameDirectory").GetString()!;
        Assert.True(Path.IsPathRooted(stored));
        Assert.EndsWith("Game", stored);
    }

    [Fact]
    public async Task SetLoader_AddsTheLoader_AndKeepsTheRest()
    {
        var game = Path.Combine(_host.Root, "Game");
        var starMap = Path.Combine(_host.Root, "StarMap");
        var other = Path.Combine(_host.Root, "Other");

        await _host.RunAsync("settings", "set", "game", game);
        var first = await _host.RunAsync("settings", "set", "loader", "StarMap", starMap);
        var second = await _host.RunAsync("settings", "set", "loader", "Other-Loader", other);
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(game, show.Json.GetProperty("gameDirectory").GetString());
        var loaders = show.Json.GetProperty("loaderDirectories");
        Assert.Equal(starMap, loaders.GetProperty("StarMap").GetString());
        Assert.Equal(other, loaders.GetProperty("Other-Loader").GetString());
    }

    [Fact]
    public async Task SetLoader_SameIdInAnotherCase_ReplacesTheEntry()
    {
        var first = Path.Combine(_host.Root, "First");
        var second = Path.Combine(_host.Root, "Second");

        await _host.RunAsync("settings", "set", "loader", "StarMap", first);
        await _host.RunAsync("settings", "set", "loader", "starmap", second);
        var show = await _host.RunAsync("settings", "show", "--json");

        var loaders = show.Json.GetProperty("loaderDirectories").EnumerateObject().ToList();
        var loader = Assert.Single(loaders);
        Assert.Equal("starmap", loader.Name);
        Assert.Equal(second, loader.Value.GetString());
    }

    [Fact]
    public async Task Show_ListsTheLoaders()
    {
        var starMap = Path.Combine(_host.Root, "StarMap");

        await _host.RunAsync("settings", "set", "loader", "StarMap", starMap);
        var show = await _host.RunAsync("settings", "show");

        Assert.Contains("Loader directories:", show.Output);
        Assert.Contains($"StarMap: {starMap}", show.Output);
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("CON")]
    [InlineData("-leading-dash")]
    public async Task SetLoader_InvalidId_IsAUsageError_ThatWritesNothing(string loaderId)
    {
        var run = await _host.RunAsync("settings", "set", "loader", loaderId, _host.Root);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("not a valid content id", run.Error);
        Assert.False(File.Exists(_host.Paths.GetBoreaSettingsPath()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetGame_BlankDirectory_IsAUsageError(string directory)
    {
        var run = await _host.RunAsync("settings", "set", "game", directory);

        Assert.Equal(2, run.ExitCode);
        Assert.False(File.Exists(_host.Paths.GetBoreaSettingsPath()));
    }

    [Fact]
    public async Task AnyCommand_SettingsFileThatDoesNotLoad_Fails()
    {
        // Loader ids that collide by case are rejected when the settings load.
        Directory.CreateDirectory(_host.Root);
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), """
            [LoaderDirectoryPaths]
            StarMap = 'C:\Games\StarMap'
            starmap = 'C:\Games\Other'
            """);

        var run = await _host.RunAsync("settings", "show");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("settings could not be read", run.Error);
    }

    [Fact]
    public async Task AnyCommand_SettingsFileThatIsNotToml_Fails_NamingTheFile()
    {
        Directory.CreateDirectory(_host.Root);
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), "GameDirectoryPath = \n");

        var run = await _host.RunAsync("settings", "show");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(_host.Paths.GetBoreaSettingsPath(), run.Error);
        Assert.DoesNotContain("Unhandled exception", run.Error);
    }

    public void Dispose() => _host.Dispose();
}
