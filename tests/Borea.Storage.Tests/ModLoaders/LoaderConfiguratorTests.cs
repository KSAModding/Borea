using System.Text.Json.Nodes;
using Borea.Core.Game;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.ModLoaders;
using Borea.Storage.Tests.Game;
using Borea.Storage.Tests.Launch;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Tests.ModLoaders;

public sealed class LoaderConfiguratorTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> Links = new Dictionary<string, string>
    {
        ["forums"] = "https://forums.ahwoo.com/threads/starmap-mod-loader.384/",
    };

    private readonly string _tempRoot;
    private readonly string _loaderDirectory;
    private readonly string _gameDirectory;
    private readonly LoaderConfigurator _configurator = new();
    private readonly List<string> _links = [];

    public LoaderConfiguratorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _loaderDirectory = Path.Combine(_tempRoot, "StarMap");
        _gameDirectory = Path.Combine(_tempRoot, "Game");
        Directory.CreateDirectory(_loaderDirectory);
    }

    public void Dispose() => WineFixtures.Delete(_tempRoot, _links);

    private string ConfigPath(string file = "StarMapConfig.json") =>
        Path.Combine(_loaderDirectory, file.Replace('/', Path.DirectorySeparatorChar));

    private static ModMetadata Loader(LoaderConfigure? configure, ContentType type = ContentType.ModLoader) => new(
        specVersion: 1,
        modId: "StarMap",
        source: "index",
        name: "StarMap",
        authors: new[] { "KlaasWhite" },
        abstractText: "Mod loader that runs code mods for Kitten Space Agency.",
        license: "MIT",
        links: Links,
        gameMin: "2026.8.3.5117",
        type: type,
        install: type == ContentType.ModLoader ? new InstallDescriptor(target: InstallAnchor.Standalone) : null,
        provides: type == ContentType.ModLoader ? new LoaderProvides("StarMap.exe", InstallAnchor.Mods, configure: configure) : null);

    private static LoaderConfigure Json(string? gamePath = "GameLocation", string file = "StarMapConfig.json") =>
        new(file, ConfigureFormat.Json, gamePath);

    private static LoaderConfigure Toml(string? gamePath = "GameLocation", string file = "loader.toml") =>
        new(file, ConfigureFormat.Toml, gamePath);

    private Task<string?> ConfigureAsync(LoaderConfigure? configure) =>
        _configurator.ConfigureAsync(Loader(configure), _loaderDirectory, _gameDirectory);

    private Task WriteConfigAsync(string text, string file = "StarMapConfig.json") =>
        File.WriteAllTextAsync(ConfigPath(file), text);

    private async Task<JsonObject> ReadJsonAsync(string file = "StarMapConfig.json") =>
        (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath(file)))!;

    private async Task<TomlTable> ReadTomlAsync(string file = "loader.toml") =>
        TomlSerializer.Deserialize<TomlTable>(await File.ReadAllTextAsync(ConfigPath(file)))!;

    #region JSON

    [Fact]
    public async Task ConfigureAsync_Json_NoFile_CreatesItWithOnlyTheKey()
    {
        var written = await ConfigureAsync(Json());

        Assert.Equal(ConfigPath(), written);
        var json = await ReadJsonAsync();
        Assert.Equal(_gameDirectory, (string?)json["GameLocation"]);
        Assert.Single(json);
    }

    [Fact]
    public async Task ConfigureAsync_Json_ExistingKeysSurvive()
    {
        // What StarMap writes on its first run, plus a nested table.
        await WriteConfigAsync("""
            {
              "GameLocation": "",
              "RepositoryLocation": "C:\\Repos",
              "GameArguments": ["-a", "-b"],
              "Extra": { "kept": true }
            }
            """);

        await ConfigureAsync(Json());

        var json = await ReadJsonAsync();
        Assert.Equal(_gameDirectory, (string?)json["GameLocation"]);
        Assert.Equal(@"C:\Repos", (string?)json["RepositoryLocation"]);
        Assert.Equal(2, json["GameArguments"]!.AsArray().Count);
        Assert.True((bool?)json["Extra"]!["kept"]);
        Assert.Equal(4, json.Count);
    }

    [Fact]
    public async Task ConfigureAsync_Json_NestedKey_CreatesTheObjects()
    {
        await ConfigureAsync(Json("loader.game.path"));

        var json = await ReadJsonAsync();
        Assert.Equal(_gameDirectory, (string?)json["loader"]!["game"]!["path"]);
    }

    [Fact]
    public async Task ConfigureAsync_Json_NestedKeyIntoAnExistingObject_KeepsItsSiblings()
    {
        await WriteConfigAsync("""{ "loader": { "verbose": true } }""");

        await ConfigureAsync(Json("loader.game.path"));

        var json = await ReadJsonAsync();
        Assert.True((bool?)json["loader"]!["verbose"]);
        Assert.Equal(_gameDirectory, (string?)json["loader"]!["game"]!["path"]);
    }

    [Fact]
    public async Task ConfigureAsync_Json_ValueWhereAnObjectIsExpected_ThrowsAndLeavesTheFile()
    {
        const string original = """{ "loader": "text" }""";
        await WriteConfigAsync(original);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigureAsync(Json("loader.game")));

        Assert.Contains("'loader'", ex.Message);
        Assert.Equal(original, await File.ReadAllTextAsync(ConfigPath()));
    }

    [Fact]
    public async Task ConfigureAsync_Json_RootIsNotAnObject_Throws()
    {
        await WriteConfigAsync("[1, 2]");

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigureAsync(Json()));
    }

    [Fact]
    public async Task ConfigureAsync_Json_InvalidJson_ThrowsAndLeavesTheFile()
    {
        const string original = "{ not json";
        await WriteConfigAsync(original);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigureAsync(Json()));

        Assert.Contains(ConfigPath(), ex.Message);
        Assert.Equal(original, await File.ReadAllTextAsync(ConfigPath()));
    }

    [Fact]
    public async Task ConfigureAsync_Json_DuplicateKey_ThrowsAndLeavesTheFile()
    {
        const string original = """{ "GameLocation": "a", "GameLocation": "b", "RepositoryLocation": "kept" }""";
        await WriteConfigAsync(original);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigureAsync(Json()));

        Assert.Contains(ConfigPath(), ex.Message);
        Assert.Equal(original, await File.ReadAllTextAsync(ConfigPath()));
    }

    [Fact]
    public async Task ConfigureAsync_Json_CommentsAndTrailingCommas_AreRead()
    {
        await WriteConfigAsync("""
            {
              // hand-edited
              "RepositoryLocation": "kept",
            }
            """);

        await ConfigureAsync(Json());

        var json = await ReadJsonAsync();
        Assert.Equal("kept", (string?)json["RepositoryLocation"]);
        Assert.Equal(_gameDirectory, (string?)json["GameLocation"]);
    }

    [Fact]
    public async Task ConfigureAsync_Json_WhitespaceOnlyFile_IsTreatedAsEmpty()
    {
        await WriteConfigAsync("  \n");

        await ConfigureAsync(Json());

        Assert.Equal(_gameDirectory, (string?)(await ReadJsonAsync())["GameLocation"]);
    }

    [Fact]
    public async Task ConfigureAsync_FileInASubdirectory_CreatesIt()
    {
        var written = await ConfigureAsync(Json(file: "config/loader.json"));

        Assert.Equal(ConfigPath("config/loader.json"), written);
        Assert.True(File.Exists(written));
    }

    [Fact]
    public async Task ConfigureAsync_TrailingSeparatorOnTheGameDirectory_IsNotWritten()
    {
        await _configurator.ConfigureAsync(Loader(Json()), _loaderDirectory, _gameDirectory + Path.DirectorySeparatorChar);

        Assert.Equal(_gameDirectory, (string?)(await ReadJsonAsync())["GameLocation"]);
    }

    #endregion

    #region TOML

    [Fact]
    public async Task ConfigureAsync_Toml_NoFile_CreatesItWithOnlyTheKey()
    {
        var written = await ConfigureAsync(Toml());

        Assert.Equal(ConfigPath("loader.toml"), written);
        var toml = await ReadTomlAsync();
        Assert.Equal(_gameDirectory, toml["GameLocation"]);
        Assert.Single(toml);
    }

    [Fact]
    public async Task ConfigureAsync_Toml_ExistingKeysAndTablesSurvive()
    {
        await WriteConfigAsync("""
            name = "kept"
            ports = [1, 2]

            [paths]
            other = "kept too"
            """, "loader.toml");

        await ConfigureAsync(Toml("paths.game"));

        var toml = await ReadTomlAsync();
        Assert.Equal("kept", toml["name"]);
        Assert.Equal(2, ((TomlArray)toml["ports"]).Count);
        var paths = (TomlTable)toml["paths"];
        Assert.Equal("kept too", paths["other"]);
        Assert.Equal(_gameDirectory, paths["game"]);
    }

    [Fact]
    public async Task ConfigureAsync_Toml_Comments_Survive()
    {
        await WriteConfigAsync("""
            # hand-edited
            name = "kept"
            """, "loader.toml");

        await ConfigureAsync(Toml());

        Assert.Contains("# hand-edited", await File.ReadAllTextAsync(ConfigPath("loader.toml")));
    }

    [Fact]
    public async Task ConfigureAsync_Toml_NestedKey_CreatesTheTables()
    {
        await ConfigureAsync(Toml("loader.game.path"));

        var toml = await ReadTomlAsync();
        Assert.Equal(_gameDirectory, ((TomlTable)((TomlTable)toml["loader"])["game"])["path"]);
    }

    [Fact]
    public async Task ConfigureAsync_Toml_ValueWhereATableIsExpected_ThrowsAndLeavesTheFile()
    {
        const string original = "loader = \"text\"\n";
        await WriteConfigAsync(original, "loader.toml");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigureAsync(Toml("loader.game")));

        Assert.Contains("'loader'", ex.Message);
        Assert.Equal(original, await File.ReadAllTextAsync(ConfigPath("loader.toml")));
    }

    [Theory]
    [InlineData("GameLocation = \"a\"\nGameLocation = \"b\"\n", "GameLocation")]
    [InlineData("[paths]\nx = 1\n[paths]\nx = 2\n", "x")]
    public async Task ConfigureAsync_Toml_DuplicateKey_ThrowsAndLeavesTheFile(string original, string key)
    {
        await WriteConfigAsync(original, "loader.toml");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigureAsync(Toml()));

        Assert.Contains(ConfigPath("loader.toml"), ex.Message);
        Assert.Contains($"'{key}'", ex.Message);
        Assert.Equal(original, await File.ReadAllTextAsync(ConfigPath("loader.toml")));
    }

    [Theory]
    [InlineData("a.b = 1\na.c = 2\n")]
    [InlineData("[a]\nb = 1\n[a.c]\nd = 2\n")]
    [InlineData("[[arr]]\nx = 1\n[[arr]]\nx = 2\n")]
    public async Task ConfigureAsync_Toml_KeysThatOnlyLookRepeated_AreAccepted(string original)
    {
        await WriteConfigAsync(original, "loader.toml");

        await ConfigureAsync(Toml());

        Assert.Equal(_gameDirectory, (await ReadTomlAsync())["GameLocation"]);
    }

    [Fact]
    public async Task ConfigureAsync_Toml_InvalidToml_ThrowsAndLeavesTheFile()
    {
        const string original = "name = \n";
        await WriteConfigAsync(original, "loader.toml");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ConfigureAsync(Toml()));

        Assert.Contains(ConfigPath("loader.toml"), ex.Message);
        Assert.Equal(original, await File.ReadAllTextAsync(ConfigPath("loader.toml")));
    }

    #endregion

    #region Wine prefix

    private string Prefix => Path.Combine(_tempRoot, "prefix");

    private string GameInPrefix => Path.Combine(Prefix, "drive_c", "Program Files", "Kitten Space Agency");

    /// <summary>A configurator whose probe finds the prefix with c: on drive_c and, unless left out, z: on the root of the host.</summary>
    private LoaderConfigurator WineConfigurator(bool withZ = true)
    {
        var drives = new Dictionary<char, string> { ['c'] = Path.Combine(Prefix, "drive_c") };
        if (withZ)
            drives['z'] = Path.GetPathRoot(_tempRoot)!;

        return new LoaderConfigurator(new FakeWineProbe(new WineInstall(Prefix, WineFixtures.Drives(drives), Wrapper: null)));
    }

    [Fact]
    public async Task ConfigureAsync_GameInAWinePrefix_WritesTheWindowsPathAndReadsTheGameDirectoryBack()
    {
        var configurator = WineConfigurator();

        await configurator.ConfigureAsync(Loader(Json()), _loaderDirectory, GameInPrefix);

        Assert.Equal(@"C:\Program Files\Kitten Space Agency", (string?)(await ReadJsonAsync())["GameLocation"]);
        Assert.Equal(GameInPrefix, await configurator.ReadConfiguredGamePathAsync(Loader(Json()), _loaderDirectory, GameInPrefix));
    }

    [Fact]
    public async Task ConfigureAsync_GameInAPrefixThatNoDriveHolds_ThrowsAndWritesNothing()
    {
        var game = Path.Combine(Prefix, "games", "Kitten Space Agency");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => WineConfigurator(withZ: false).ConfigureAsync(Loader(Json()), _loaderDirectory, game));

        Assert.Contains("no drive of that prefix holds it", error.Message);
        Assert.False(File.Exists(ConfigPath()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigureAsync_NativeLinuxBuildInAWinePrefix_WritesTheHostPath(bool withWindowsBuild)
    {
        Directory.CreateDirectory(GameInPrefix);
        File.WriteAllText(Path.Combine(GameInPrefix, "KSA"), "app host");
        if (withWindowsBuild)
            File.WriteAllText(Path.Combine(GameInPrefix, "KSA.exe"), "game");

        await WineConfigurator().ConfigureAsync(Loader(Json()), _loaderDirectory, GameInPrefix);

        Assert.Equal(GameInPrefix, (string?)(await ReadJsonAsync())["GameLocation"]);
    }

    [Fact]
    public async Task ReadConfiguredGamePathAsync_AnotherWindowsPath_MapsItThroughThePrefixOfTheGame()
    {
        await WriteConfigAsync("""{ "GameLocation": "C:\\Games\\Old KSA\\" }""");

        var configured = await WineConfigurator().ReadConfiguredGamePathAsync(Loader(Json()), _loaderDirectory, GameInPrefix);

        Assert.Equal(Path.Combine(Prefix, "drive_c", "Games", "Old KSA"), configured);
    }

    [Fact]
    public async Task ReadConfiguredGamePathAsync_NoGameDirectory_MapsThroughThePrefixOfTheLoader()
    {
        var loaderInPrefix = Directory.CreateDirectory(Path.Combine(Prefix, "drive_c", "Program Files", "StarMap")).FullName;
        await File.WriteAllTextAsync(Path.Combine(loaderInPrefix, "StarMapConfig.json"), """{ "GameLocation": "C:\\Program Files\\Kitten Space Agency" }""");

        var configured = await WineConfigurator().ReadConfiguredGamePathAsync(Loader(Json()), loaderInPrefix);

        Assert.Equal(GameInPrefix, configured);
    }

    [Fact]
    public async Task ReadConfiguredGamePathAsync_WindowsPathOutsideEveryPrefix_StaysAsItIs()
    {
        await WriteConfigAsync("""{ "GameLocation": "C:\\Program Files\\Kitten Space Agency" }""");

        var configured = await WineConfigurator().ReadConfiguredGamePathAsync(Loader(Json()), _loaderDirectory, _gameDirectory);

        Assert.Equal(@"C:\Program Files\Kitten Space Agency", configured);
    }

    [UnixFact("On Windows a host path starts with a drive letter too, so only a Linux or macOS host can tell it from a Windows path.")]
    public async Task ReadConfiguredGamePathAsync_HostPathAnOlderBoreaWrote_KeepsIt()
    {
        await WriteConfigAsync(new JsonObject { ["GameLocation"] = GameInPrefix }.ToJsonString());

        var configured = await WineConfigurator().ReadConfiguredGamePathAsync(Loader(Json()), _loaderDirectory, GameInPrefix);

        Assert.Equal(GameInPrefix, configured);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefreshForWineAsync_HostPathAnOlderBoreaWroteOrNoValue_WritesTheWindowsPath(bool hostPathWritten)
    {
        await WriteConfigAsync(hostPathWritten
            ? new JsonObject { ["GameLocation"] = GameInPrefix + Path.DirectorySeparatorChar, ["Other"] = "kept" }.ToJsonString()
            : new JsonObject { ["Other"] = "kept" }.ToJsonString());

        var written = await WineConfigurator().RefreshForWineAsync(Loader(Json()), _loaderDirectory, GameInPrefix);

        Assert.Equal(ConfigPath(), written);
        var json = await ReadJsonAsync();
        Assert.Equal(@"C:\Program Files\Kitten Space Agency", (string?)json["GameLocation"]);
        Assert.Equal("kept", (string?)json["Other"]);
    }

    [Theory]
    [InlineData(@"C:\\Program Files\\Kitten Space Agency")]
    [InlineData(@"D:\\Games\\KSA")]
    public async Task RefreshForWineAsync_ValueInTheFormOfThePrefix_LeavesTheFile(string configured)
    {
        var text = $$"""{ "GameLocation": "{{configured}}" }""";
        await WriteConfigAsync(text);

        var written = await WineConfigurator().RefreshForWineAsync(Loader(Json()), _loaderDirectory, GameInPrefix);

        Assert.Null(written);
        Assert.Equal(text, await File.ReadAllTextAsync(ConfigPath()));
    }

    [Fact]
    public async Task RefreshForWineAsync_FileThatIsNoJson_LeavesTheFileForTheLaunchToRefuse()
    {
        const string text = """{ "GameLocation": """;
        await WriteConfigAsync(text);

        var written = await WineConfigurator().RefreshForWineAsync(Loader(Json()), _loaderDirectory, GameInPrefix);

        Assert.Null(written);
        Assert.Equal(text, await File.ReadAllTextAsync(ConfigPath()));
    }

    [Fact]
    public async Task RefreshForWineAsync_GameOutsideAPrefix_LeavesTheHostPath()
    {
        var text = new JsonObject { ["GameLocation"] = _gameDirectory }.ToJsonString();
        await WriteConfigAsync(text);

        var written = await WineConfigurator().RefreshForWineAsync(Loader(Json()), _loaderDirectory, _gameDirectory);

        Assert.Null(written);
        Assert.Equal(text, await File.ReadAllTextAsync(ConfigPath()));
    }

    [UnixFact("Windows cannot name a file 'c:', so the drive links exist only on Linux and macOS.")]
    public async Task ConfigureAsync_GameInARealPrefix_WritesTheWindowsPathAndReadsTheGameDirectoryBack()
    {
        WineFixtures.Prefix(Prefix);
        _links.Add(WineFixtures.LinkDrive(Prefix, 'c', "../drive_c"));
        _links.Add(WineFixtures.LinkDrive(Prefix, 'z', "/"));
        Directory.CreateDirectory(GameInPrefix);

        await _configurator.ConfigureAsync(Loader(Json()), _loaderDirectory, GameInPrefix);

        Assert.Equal(@"C:\Program Files\Kitten Space Agency", (string?)(await ReadJsonAsync())["GameLocation"]);
        Assert.Equal(GameInPrefix, await _configurator.ReadConfiguredGamePathAsync(Loader(Json()), _loaderDirectory, GameInPrefix));
    }

    #endregion

    #region Launch preparation

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareForLaunchAsync_NoFile_WritesTheGamePath(bool isAdopted)
    {
        var written = await _configurator.PrepareForLaunchAsync(Loader(Json()), _loaderDirectory, _gameDirectory, isAdopted);

        Assert.Equal(ConfigPath(), written);
        Assert.Equal(_gameDirectory, (string?)(await ReadJsonAsync())["GameLocation"]);
    }

    [Fact]
    public async Task PrepareForLaunchAsync_BoreaFolderPointsAtAnotherGame_WritesTheGameOfTheInstance()
    {
        await WriteConfigAsync(new JsonObject { ["GameLocation"] = PlaceGame("Other game"), ["Other"] = "kept" }.ToJsonString());

        var written = await _configurator.PrepareForLaunchAsync(Loader(Json()), _loaderDirectory, _gameDirectory, isAdopted: false);

        Assert.Equal(ConfigPath(), written);
        var json = await ReadJsonAsync();
        Assert.Equal(_gameDirectory, (string?)json["GameLocation"]);
        Assert.Equal("kept", (string?)json["Other"]);
    }

    [Fact]
    public async Task PrepareForLaunchAsync_AdoptedFolderPointsAtAFolderWithoutTheGame_WritesTheGameOfTheInstance()
    {
        await WriteConfigAsync(new JsonObject { ["GameLocation"] = Directory.CreateDirectory(Path.Combine(_tempRoot, "Empty")).FullName }.ToJsonString());

        var written = await _configurator.PrepareForLaunchAsync(Loader(Json()), _loaderDirectory, _gameDirectory, isAdopted: true);

        Assert.Equal(ConfigPath(), written);
        Assert.Equal(_gameDirectory, (string?)(await ReadJsonAsync())["GameLocation"]);
    }

    [Theory]
    [InlineData(false, "Game")]
    [InlineData(true, "Other game")]
    public async Task PrepareForLaunchAsync_ValueTheFolderKeeps_LeavesTheFile(bool isAdopted, string configuredGame)
    {
        PlaceGame("Game");
        var text = new JsonObject { ["GameLocation"] = PlaceGame(configuredGame) }.ToJsonString();
        await WriteConfigAsync(text);

        var written = await _configurator.PrepareForLaunchAsync(Loader(Json()), _loaderDirectory, _gameDirectory, isAdopted);

        Assert.Null(written);
        Assert.Equal(text, await File.ReadAllTextAsync(ConfigPath()));
    }

    private string PlaceGame(string name)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_tempRoot, name)).FullName;
        File.WriteAllText(Path.Combine(directory, "KSA.dll"), "game");
        return directory;
    }

    #endregion

    #region Nothing to write, and refusals

    [Fact]
    public async Task ConfigureAsync_NoConfigureTable_ReturnsNullAndWritesNothing()
    {
        var written = await ConfigureAsync(null);

        Assert.Null(written);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_loaderDirectory));
    }

    [Fact]
    public async Task ConfigureAsync_NoGamePathKey_ReturnsNullAndWritesNothing()
    {
        var written = await ConfigureAsync(Json(gamePath: null));

        Assert.Null(written);
        Assert.False(File.Exists(ConfigPath()));
    }

    [Fact]
    public async Task ConfigureAsync_UnknownFormat_IsNotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => ConfigureAsync(new LoaderConfigure("loader.cfg", ConfigureFormat.Unknown, "GameLocation")));

        Assert.False(File.Exists(ConfigPath("loader.cfg")));
    }

    [Fact]
    public async Task ConfigureAsync_ModListing_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _configurator.ConfigureAsync(Loader(null, ContentType.Mod), _loaderDirectory, _gameDirectory));
    }

    [Theory]
    [InlineData("relative/loader")]
    [InlineData("")]
    public async Task ConfigureAsync_RelativeLoaderDirectory_ThrowsArgumentException(string directory)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _configurator.ConfigureAsync(Loader(Json()), directory, _gameDirectory));
    }

    [Theory]
    [InlineData("relative/game")]
    [InlineData("")]
    public async Task ConfigureAsync_RelativeGameDirectory_ThrowsArgumentException(string directory)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _configurator.ConfigureAsync(Loader(Json()), _loaderDirectory, directory));
    }

    [Fact]
    public async Task ConfigureAsync_NullLoader_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _configurator.ConfigureAsync(null!, _loaderDirectory, _gameDirectory));
    }

    #endregion
}
