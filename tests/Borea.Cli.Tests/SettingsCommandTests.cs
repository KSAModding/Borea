using System.Text.Json;
using Borea.Core.Settings;

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
    public async Task SetLoader_AdoptsTheLoader_AndKeepsTheGame()
    {
        var game = Path.Combine(_host.Root, "Game");
        var starMap = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root, game);
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release());

        await _host.RunAsync("settings", "set", "game", game);
        var set = await _host.RunAsync("settings", "set", "loader", "StarMap", starMap);
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, set.ExitCode);
        Assert.Contains("Adopted StarMap", set.Output);
        Assert.Equal(game, show.Json.GetProperty("gameDirectory").GetString());
        var loaders = show.Json.GetProperty("loaderDirectories");
        Assert.Equal(starMap, loaders.GetProperty("StarMap").GetString());
    }

    [Fact]
    public async Task SetLoader_MissingLaunchFile_FailsWithoutWritingARecord()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_host.Root, "StarMap")).FullName;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());

        var set = await _host.RunAsync("settings", "set", "loader", "StarMap", directory);
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(1, set.ExitCode);
        Assert.Contains("does not hold the listed launch file", set.Error);
        Assert.Empty(show.Json.GetProperty("loaderDirectories").EnumerateObject());
    }

    [Fact]
    public async Task Show_ListsTheLoaders()
    {
        var starMap = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        _host.Mods.Listings.Add(LoaderFixtures.Listing());

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
            [LoaderInstallations.StarMap]
            DirectoryPath = 'C:\Games\StarMap'
            IsAdopted = true

            [LoaderInstallations.starmap]
            DirectoryPath = 'C:\Games\Other'
            IsAdopted = true
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

    [Fact]
    public async Task Show_NoSettingsFile_ReportsTheStableChannel()
    {
        var text = await _host.RunAsync("settings", "show");
        var json = await _host.RunAsync("settings", "show", "--json");

        Assert.Contains("Release channel: stable", text.Output);
        Assert.Equal("stable", json.Json.GetProperty("releaseChannel").GetString());
    }

    [Fact]
    public async Task Show_SettingsFileWrittenBeforeTheChannelExisted_ReportsStable()
    {
        Directory.CreateDirectory(_host.Root);
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), "GameDirectoryPath = 'C:\\Games\\KSA'\n");

        var run = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("stable", run.Json.GetProperty("releaseChannel").GetString());
        Assert.Equal(@"C:\Games\KSA", run.Json.GetProperty("gameDirectory").GetString());
    }

    [Theory]
    [InlineData("testing", "testing")]
    [InlineData("DEV", "dev")]
    [InlineData("stable", "stable")]
    public async Task SetChannel_SavesIt_AndShowReadsItBack(string given, string saved)
    {
        var set = await _host.RunAsync("settings", "set", "channel", given);
        var text = await _host.RunAsync("settings", "show");
        var json = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, set.ExitCode);
        Assert.Contains($"Release channel: {saved}", set.Output);
        Assert.Contains($"Release channel: {saved}", text.Output);
        Assert.Equal(saved, json.Json.GetProperty("releaseChannel").GetString());
    }

    [Fact]
    public async Task SetChannel_AndSetGame_KeepEachOther()
    {
        var game = Directory.CreateDirectory(Path.Combine(_host.Root, "Game")).FullName;

        await _host.RunAsync("settings", "set", "game", game);
        await _host.RunAsync("settings", "set", "channel", "testing");
        await _host.RunAsync("settings", "set", "game", game);
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(game, show.Json.GetProperty("gameDirectory").GetString());
        Assert.Equal("testing", show.Json.GetProperty("releaseChannel").GetString());
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task SetChannel_NameThatIsNoChannel_IsAUsageError_ThatWritesNothing(string name)
    {
        var run = await _host.RunAsync("settings", "set", "channel", name);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("is not a release channel", run.Error);
        Assert.False(File.Exists(_host.Paths.GetBoreaSettingsPath()));
    }

    [Fact]
    public async Task Show_NoSettingsFile_ReportsTheDefaultLibraryFolder()
    {
        var text = await _host.RunAsync("settings", "show");
        var json = await _host.RunAsync("settings", "show", "--json");

        Assert.Contains($"Library folder: {_host.Root} (default)", text.Output);
        Assert.Equal(_host.Root, json.Json.GetProperty("libraryFolder").GetString());
        Assert.True(json.Json.GetProperty("libraryFolderIsDefault").GetBoolean());
    }

    [Fact]
    public async Task SetLibrary_MovesTheInstances_AndShowReadsItBack()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        var library = Path.Combine(_host.Root, "..", Path.GetFileName(_host.Root) + "-Library");
        var fullLibrary = Path.GetFullPath(library);

        try
        {
            var set = await _host.RunAsync("settings", "set", "library", library, "--json");
            var show = await _host.RunAsync("settings", "show", "--json");
            var list = await _host.RunAsync("instance", "list", "--json");

            Assert.Equal(0, set.ExitCode);
            Assert.Equal(fullLibrary, set.Json.GetProperty("libraryFolder").GetString());
            Assert.Equal(_host.Root, set.Json.GetProperty("previousLibraryFolder").GetString());
            Assert.Equal("moved", set.Json.GetProperty("outcome").GetString());
            Assert.Equal(fullLibrary, show.Json.GetProperty("libraryFolder").GetString());
            Assert.False(show.Json.GetProperty("libraryFolderIsDefault").GetBoolean());
            Assert.Equal("Alpha", Assert.Single(list.Json.EnumerateArray()).GetProperty("name").GetString());
            Assert.True(Directory.Exists(Path.Combine(fullLibrary, "Instances")));
            Assert.False(Directory.Exists(Path.Combine(_host.Root, "Instances")));
        }
        finally
        {
            if (Directory.Exists(fullLibrary))
                Directory.Delete(fullLibrary, recursive: true);
        }
    }

    [Fact]
    public async Task SetLibrary_OtherVolume_WritesProgressToStderr_AndDefaultMovesItBack()
    {
        _host.LibraryOnSameVolume = (_, _) => false;
        await _host.RunAsync("instance", "create", "Alpha");
        var library = Path.GetFullPath(Path.Combine(_host.Root, "..", Path.GetFileName(_host.Root) + "-Library"));

        try
        {
            var set = await _host.RunAsync("settings", "set", "library", library);
            var back = await _host.RunAsync("settings", "set", "library", "--default");
            var show = await _host.RunAsync("settings", "show", "--json");

            Assert.Equal(0, set.ExitCode);
            Assert.Contains($"Library folder: {library}", set.Output);
            Assert.Contains("Copying 1 file (", set.Error);
            Assert.Contains("Copied 100% (1 of 1 files)", set.Error);
            Assert.Contains("Deleting the old files", set.Error);
            Assert.Equal(0, back.ExitCode);
            Assert.Contains($"Library folder: {_host.Root}", back.Output);
            Assert.True(show.Json.GetProperty("libraryFolderIsDefault").GetBoolean());
            Assert.Single(Directory.GetDirectories(Path.Combine(_host.Root, "Instances")));
        }
        finally
        {
            if (Directory.Exists(library))
                Directory.Delete(library, recursive: true);
        }
    }

    [Fact]
    public async Task SetLibrary_OldFilesRemain_WarnsAndSaysSoInTheJson()
    {
        var library = Path.GetFullPath(Path.Combine(_host.Root, "..", "Library"));
        _host.LibraryChanger = new FixedLibraryChanger(new LibraryFolderChangeResult(LibraryFolderChangeOutcome.Moved, library, _host.Root, "Moved.") { OldFilesRemain = true });

        var set = await _host.RunAsync("settings", "set", "library", library, "--json");

        Assert.Equal(0, set.ExitCode);
        Assert.Contains($"warning: Borea could not delete every old file in {_host.Root}.", set.Error);
        Assert.True(set.Json.GetProperty("oldFilesRemain").GetBoolean());
        Assert.Equal("moved", set.Json.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task SetLibrary_FolderInsideTheCurrentLibrary_FailsAndChangesNothing()
    {
        await _host.RunAsync("instance", "create", "Alpha");
        var inside = Path.Combine(_host.Root, "Library");

        var set = await _host.RunAsync("settings", "set", "library", inside, "--json");
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(1, set.ExitCode);
        Assert.Contains("inside the current library folder", set.Error);
        Assert.Equal(string.Empty, set.Output);
        Assert.True(show.Json.GetProperty("libraryFolderIsDefault").GetBoolean());
        Assert.False(Directory.Exists(inside));
    }

    [Theory]
    [InlineData]
    [InlineData("--default", "somewhere")]
    [InlineData("  ")]
    public async Task SetLibrary_NotExactlyOneOfDirectoryAndDefault_IsAUsageError(params string[] arguments)
    {
        var run = await _host.RunAsync(["settings", "set", "library", .. arguments]);

        Assert.Equal(2, run.ExitCode);
        Assert.False(File.Exists(_host.Paths.GetBoreaSettingsPath()));
    }

    [Fact]
    public async Task Show_NoSettingsFile_ReportsTheSharedModStoreOn()
    {
        var text = await _host.RunAsync("settings", "show");
        var json = await _host.RunAsync("settings", "show", "--json");

        Assert.Contains("Shared mod store: on", text.Output);
        Assert.True(json.Json.GetProperty("sharedModStore").GetBoolean());
    }

    [Fact]
    public async Task SetSharedStore_OffThenOn_SavesItAndShowReadsItBack()
    {
        var off = await _host.RunAsync("settings", "set", "shared-store", "off");
        var shown = await _host.RunAsync("settings", "show", "--json");
        var on = await _host.RunAsync("settings", "set", "shared-store", "on");

        Assert.Equal(0, off.ExitCode);
        Assert.Contains("Shared mod store: off", off.Output);
        Assert.False(shown.Json.GetProperty("sharedModStore").GetBoolean());
        Assert.Equal(0, on.ExitCode);
        Assert.True((await _host.RunAsync("settings", "show", "--json")).Json.GetProperty("sharedModStore").GetBoolean());
    }

    [Fact]
    public async Task SetSharedStore_OffWhileTheGameRuns_FailsAndChangesNothing()
    {
        _host.GameRunning = true;

        var run = await _host.RunAsync("settings", "set", "shared-store", "off");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The game is running", run.Error);
        Assert.False(File.Exists(_host.Paths.GetBoreaSettingsPath()));
    }

    [Fact]
    public async Task SetSharedStore_OffWhileAnotherBoreaRuns_FailsAndChangesNothing()
    {
        _host.OtherBoreaRunning = true;

        var run = await _host.RunAsync("settings", "set", "shared-store", "off");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Another Borea window or command is running", run.Error);
        Assert.False(File.Exists(_host.Paths.GetBoreaSettingsPath()));
    }

    [Fact]
    public async Task SetSharedStore_NeitherOnNorOff_IsAUsageError()
    {
        var run = await _host.RunAsync("settings", "set", "shared-store", "maybe");

        Assert.Equal(2, run.ExitCode);
        Assert.False(File.Exists(_host.Paths.GetBoreaSettingsPath()));
    }

    public void Dispose() => _host.Dispose();

    private sealed class FixedLibraryChanger(LibraryFolderChangeResult result) : ILibraryFolderChanger
    {
        public Task<LibraryFolderChangeResult> ChangeAsync(string? folder, IProgress<LibraryMoveProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }
}
