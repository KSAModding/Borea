using Borea.Core.Game;
using Borea.Core.Index;
using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.State;

namespace Borea.Cli.Tests;

public sealed class LaunchCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Launch_StarMapStartsWithTheHandoverItsListingNames()
    {
        await RecordStarMapAsync();
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(0, run.ExitCode);
        var plan = Assert.Single(_host.ProcessStarter.Plans);
        Assert.Equal("-InstancePath", plan.Arguments[0]);
        Assert.True(Path.IsPathFullyQualified(plan.Arguments[1]));
        Assert.Equal(plan.Arguments[1], plan.EnvironmentVariables["STARMAP_INSTANCE_PATH"]);
        Assert.Contains("Process id: 42", run.Output);
    }

    [Fact]
    public async Task Launch_HeldIndexListsTheLoader_StartsWithoutAskingTheSources()
    {
        await RecordStarMapAsync();
        await _host.RunAsync("instance", "create", "Flight Test");
        _host.IndexReader.Snapshot = new ContentIndexSnapshot(1, [new ContentIndexListing("StarMap", LoaderFixtures.Listing(), [LoaderFixtures.Release()], null)], [], null, []);
        _host.Mods.AvailableMods = _ => new TaskCompletionSource<IReadOnlyList<ModMetadata>>().Task;

        var run = await _host.RunAsync("launch", "Flight Test").WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(0, run.ExitCode);
        Assert.Single(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_InstalledLoaderDoesNotSupportTheGame_WarnsAndStillStarts()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), $"""
            [LoaderInstallations.StarMap]
            DirectoryPath = '{loaderDirectory}'
            Version = '0.4.7'
            IsAdopted = false
            """);
        await SaveInstanceAsync(ContentCommandFixtures.Release("flight-tools", loader: new LoaderRequirement("StarMap", ModVersion.Parse("0.4.0"), ModVersion.Parse("0.4.8"))));
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.10.19.5638"), "2026.10.19.5638") };
        _host.IndexReader.Snapshot = new ContentIndexSnapshot(
            1,
            [new ContentIndexListing("StarMap", LoaderFixtures.Listing(), [LoaderFixtures.Release(version: "0.4.7", gameMax: "2026.10.13.5602"), LoaderFixtures.Release(version: "0.4.8", gameMin: "2026.10.19.5638"), LoaderFixtures.Release(version: "0.4.9", gameMin: "2026.10.19.5638")], null)],
            [],
            null,
            []);

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("warning: StarMap 0.4.7 does not support KSA 2026.10.19.5638. Run 'borea loader install StarMap --version 0.4.8' to install 0.4.8.", run.Error);
        Assert.Single(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_ManifestWithoutCore_PutsCoreBeforeTheModsFirst()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        var game = Path.Combine(_host.Root, "Game");
        Directory.CreateDirectory(Path.Combine(game, "Content"));
        await File.WriteAllTextAsync(Path.Combine(game, "Content", "manifest.toml"), "[[mods]]\nid = \"Core\"\nenabled = true\n");
        await _host.RunAsync("settings", "set", "game", game);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        await _host.RunAsync("instance", "create", "Flight Test");
        var instance = Assert.Single(await new FileInstanceRepository(_host.Paths).GetAllAsync());
        await File.WriteAllTextAsync(_host.Paths.GetInstanceManifestPath(instance.InstanceId), "[[mods]]\nid = \"KSArmory\"\nenabled = true\n");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(0, run.ExitCode);
        var entries = await new FileModStateRepository(_host.Paths).GetEntriesAsync(instance.InstanceId);
        Assert.Equal(new[] { "Core", "KSArmory" }, entries.Select(entry => entry.ModId));
    }

    [Theory]
    [InlineData(true, "KeptContent")]
    [InlineData(false, "SettingsContent")]
    public async Task Launch_PutsTheContentOfTheBuildTheInstanceStartsFirst(bool kept, string expected)
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        var game = Path.Combine(_host.Root, "Game");
        await WriteContentAsync(game, "SettingsContent");
        await _host.RunAsync("settings", "set", "game", game);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        await _host.RunAsync("instance", "create", "Flight Test");
        if (kept)
        {
            await _host.KeepGameBuildAsync("Flight Test");
            await WriteContentAsync(_host.Paths.GetGameBuildFolder(CliHost.KeptBuild), "KeptContent");
        }

        var instance = Assert.Single(await new FileInstanceRepository(_host.Paths).GetAllAsync());
        await File.WriteAllTextAsync(_host.Paths.GetInstanceManifestPath(instance.InstanceId), "[[mods]]\nid = \"KSArmory\"\nenabled = true\n");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(0, run.ExitCode);
        var entries = await new FileModStateRepository(_host.Paths).GetEntriesAsync(instance.InstanceId);
        Assert.Equal(new[] { expected, "KSArmory" }, entries.Select(entry => entry.ModId));
    }

    [Fact]
    public async Task Launch_KeptBuildIsGone_StartsNothingAndUsesNoOtherGameFolder()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        var game = Path.Combine(_host.Root, "Game");
        await WriteContentAsync(game, "SettingsContent");
        await _host.RunAsync("settings", "set", "game", game);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        await _host.RunAsync("instance", "create", "Flight Test");
        await _host.KeepGameBuildAsync("Flight Test", withFolder: false);
        var instance = Assert.Single(await new FileInstanceRepository(_host.Paths).GetAllAsync());
        await File.WriteAllTextAsync(_host.Paths.GetInstanceManifestPath(instance.InstanceId), "[[mods]]\nid = \"KSArmory\"\nenabled = true\n");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(CliHost.KeptBuild.ToString(), run.Error);
        Assert.Contains("\"Use the current game build\"", run.Error);
        Assert.Contains("remove GameBuild from the instance.toml", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
        var entries = await new FileModStateRepository(_host.Paths).GetEntriesAsync(instance.InstanceId);
        Assert.Equal(new[] { "KSArmory" }, entries.Select(entry => entry.ModId));
    }

    [Fact]
    public async Task Launch_WhileAKeptBuildRunsFromTheSameFolder_FailsAndLeavesTheFile()
    {
        var file = await SetUpStarMapFolderAsync();
        await _host.RunAsync("instance", "create", "Kept");
        await _host.RunAsync("instance", "create", "Current");
        await _host.KeepGameBuildAsync("Kept");
        // both instances start the one StarMap folder, so the start of the kept build leaves its folder in the file
        Assert.Equal(0, (await _host.RunAsync("launch", "Kept", "StarMap")).ExitCode);
        _host.GameRunning = true;
        var before = await File.ReadAllBytesAsync(file);

        var run = await _host.RunAsync("launch", "Current", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Close the running game first", run.Error);
        Assert.Equal(before, await File.ReadAllBytesAsync(file));
        Assert.Single(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_LoaderFileThatCannotBeRead_FailsAndLeavesTheFile()
    {
        var file = await SetUpStarMapFolderAsync();
        await _host.RunAsync("instance", "create", "Current");
        await File.WriteAllTextAsync(file, "{");

        var run = await _host.RunAsync("launch", "Current", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Repair or delete the file", run.Error);
        Assert.Equal("{", await File.ReadAllTextAsync(file));
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_KeptBuildIsGoneAndNoLoaderIsInstalled_NamesTheBuildAndNoLoader()
    {
        _host.IndexReader.Snapshot = StarMapIndex();
        await _host.RunAsync("instance", "create", "Flight Test");
        await _host.KeepGameBuildAsync("Flight Test", withFolder: false);

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"runs on KSA {CliHost.KeptBuild}", run.Error);
        Assert.DoesNotContain("borea loader install", run.Error);
    }

    [Fact]
    public async Task Launch_KeptBuildWithOnlyANewerLoader_WarnsWithTheVersionThatFitsIt()
    {
        var versions = Path.Combine(_host.Paths.GetLoadersRoot(), "StarMap");
        var recorded = LoaderCommandTests.CreateLoaderDirectory("0.4.8", "not a program", versions);
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), $"""
            [LoaderInstallations.StarMap]
            DirectoryPath = '{recorded}'
            Version = '0.4.8'
            IsAdopted = false
            """);
        await _host.RunAsync("instance", "create", "Flight Test");
        await _host.KeepGameBuildAsync("Flight Test");
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.10.19.5638"), "2026.10.19.5638") };
        _host.IndexReader.Snapshot = new ContentIndexSnapshot(
            1,
            [new ContentIndexListing("StarMap", LoaderFixtures.Listing(), [LoaderFixtures.Release(version: "0.4.6", gameMin: CliHost.KeptBuild.ToString(), gameMax: "2026.10.13.5602"), LoaderFixtures.Release(version: "0.4.8", gameMin: "2026.10.19.5638")], null)],
            [],
            null,
            []);

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains($"warning: StarMap 0.4.8 does not support KSA {CliHost.KeptBuild}. Run 'borea loader install StarMap --version 0.4.6' to install 0.4.6.", run.Error);
        Assert.Single(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_LoaderStopsRightAway_FailsWithWhatItWrote()
    {
        await RecordStarMapAsync();
        await _host.RunAsync("instance", "create", "Flight Test");
        _host.ProcessStarter.CrashExitCode = -532462766;
        _host.ProcessStarter.CrashOutput.Add("Unhandled exception. System.TypeLoadException: Method 'DrawAxes' from assembly 'KSArmory, Version=0.8.44.0' does not have an implementation.");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("exit code -532462766", run.Error);
        Assert.Contains("DrawAxes", run.Error);
        Assert.DoesNotContain("Process id", run.Output);
    }

    [Fact]
    public async Task Launch_LoaderCannotLoadAGameAssembly_NamesTheInstallCommand()
    {
        await RecordStarMapAsync();
        await _host.RunAsync("instance", "create", "Flight Test");
        _host.ProcessStarter.CrashExitCode = -532462766;
        _host.ProcessStarter.CrashOutput.Add("Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.");
        _host.ProcessStarter.CrashOutput.Add("   at StarMap.GameSurveyer.TryLoadCoreAndGame()");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("'borea loader install StarMap'", run.Error);
    }

    [Fact]
    public async Task Launch_GameAssemblyNamedWithoutALoaderFrame_NamesNoInstallCommand()
    {
        await RecordStarMapAsync();
        await _host.RunAsync("instance", "create", "Flight Test");
        _host.ProcessStarter.CrashExitCode = -532462766;
        _host.ProcessStarter.CrashOutput.Add("File name: 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'");
        _host.ProcessStarter.CrashOutput.Add("System.IO.FileNotFoundException: Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain("borea loader install", run.Error);
    }

    [Fact]
    public async Task Launch_GameStarts_RecordsWhenTheInstanceWasPlayed()
    {
        await RecordStarMapAsync();
        await _host.RunAsync("instance", "create", "Flight Test");
        var before = DateTimeOffset.UtcNow;

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(0, run.ExitCode);
        var playedAt = Assert.Single(await new FileInstanceRepository(_host.Paths).GetAllAsync()).LastPlayedAt;
        Assert.NotNull(playedAt);
        Assert.InRange(playedAt.Value, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Launch_LoaderStopsRightAway_RecordsNoPlay()
    {
        await RecordStarMapAsync();
        await _host.RunAsync("instance", "create", "Flight Test");
        _host.ProcessStarter.CrashExitCode = -532462766;

        await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Null(Assert.Single(await new FileInstanceRepository(_host.Paths).GetAllAsync()).LastPlayedAt);
    }

    [Fact]
    public async Task Launch_LoaderWhoseListingHasNoInstanceTable_FailsWithoutStarting()
    {
        _host.Mods.Listings.Add(LoaderFixtures.ListingWithoutInstance("OtherLoader"));
        var directory = LoaderCommandTests.CreateLoaderDirectory("OtherLoader", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "OtherLoader", directory);
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "OtherLoader");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The listing of OtherLoader does not say how it takes an instance", run.Error);
        Assert.Contains("[provides.instance]", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_LoaderThatRunsThroughAnUnknownRuntime_FailsWithoutStarting()
    {
        _host.Mods.Listings.Add(LoaderFixtures.ListingWithRuntime("mono"));
        var directory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "StarMap", directory);
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("StarMap runs through 'mono'", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_UnknownInstance_WritesTheCommandAndTheFailureToTheCliLog()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());

        await _host.RunAsync("launch", "Missing", "StarMap");

        var logs = Directory.GetFiles(Path.Combine(_host.Root, "Logs"), "borea-*.log");
        var text = File.ReadAllText(Assert.Single(logs));
        Assert.Contains("[cli] Command: borea launch Missing StarMap", text);
        Assert.Contains("[cli] Command failed." + Environment.NewLine + "System.InvalidOperationException: No instance is named 'Missing'.", text);
    }

    [Fact]
    public async Task InstanceList_WritesTheExitCodeToTheCliLog()
    {
        var run = await _host.RunAsync("instance", "list");

        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_host.Root, "Logs"), "borea-*.log")));
        Assert.Contains($"[cli] Command finished with exit code {run.ExitCode}.", text);
    }

    [Fact]
    public async Task Launch_UnknownInstance_Fails()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());

        var run = await _host.RunAsync("launch", "Missing", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No instance is named", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_CancelledAfterLoaderLookup_DoesNotStartAProcess()
    {
        var listing = LoaderFixtures.Listing();
        _host.Mods.Listings.Add(listing);
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        await _host.RunAsync("instance", "create", "Flight Test");
        using var cancellation = new CancellationTokenSource();
        _host.Mods.AvailableMods = _ =>
        {
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<Borea.Core.Mods.ModMetadata>>([listing]);
        };

        var run = await _host.RunAsync(cancellation.Token, "launch", "Flight Test", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("command was cancelled", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_WithoutLoaderId_UsesTheOneLoaderTheModsNeed()
    {
        await RecordStarMapAsync();
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap"), NeedsLoader("orbit-tools", "starmap"), ContentCommandFixtures.Release("parts-pack"));

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("The mods in 'Flight Test' need StarMap.", run.Output);
        var plan = Assert.Single(_host.ProcessStarter.Plans);
        Assert.Equal("-InstancePath", plan.Arguments[0]);
    }

    [Fact]
    public async Task Launch_WithoutLoaderId_ArgumentsAfterTheSeparator_GoToTheLoaderNotTheLoaderId()
    {
        await RecordStarMapAsync();
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap"));

        var run = await _host.RunAsync("launch", "Flight Test", "--", "-windowed", "a b");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["-windowed", "a b"], Assert.Single(_host.ProcessStarter.Plans).Arguments.Skip(2));
    }

    [Fact]
    public async Task Launch_SavedAndGivenArguments_FollowTheHandoverAsSeparateArguments()
    {
        await RecordStarMapAsync();
        await _host.RunAsync("instance", "create", "Flight Test");
        await _host.RunAsync("instance", "set-arguments", "Flight Test", "--", "-saved", "saved value");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap", "--", "-given", "given value", "--json");

        Assert.Equal(0, run.ExitCode);
        var plan = Assert.Single(_host.ProcessStarter.Plans);
        Assert.Equal("-InstancePath", plan.Arguments[0]);
        Assert.Equal(["-saved", "saved value", "-given", "given value", "--json"], plan.Arguments.Skip(2));
    }

    [Fact]
    public async Task Launch_HandoverFlagAfterTheSeparator_FailsWithoutStarting()
    {
        await RecordStarMapAsync();
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap", "--", "-INSTANCEPATH", "D:/Other");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("'-INSTANCEPATH'", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_ExtraArgumentWithoutTheSeparator_IsBadUsage()
    {
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap", "-windowed");

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_WithoutLoaderId_NeededLoaderNotInstalled_NamesTheNeededLoader()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap"));

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The mods in 'Flight Test' need StarMap, and it is not installed.", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_GivenLoaderId_IsUsedWhenTheModsNeedAnother()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Listings.Add(LoaderFixtures.Listing("OtherLoader"));
        var directory = LoaderCommandTests.CreateLoaderDirectory("OtherLoader", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "OtherLoader", directory);
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap"));

        var run = await _host.RunAsync("launch", "Flight Test", "OtherLoader");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("The mods in", run.Output);
        Assert.Contains("Started OtherLoader", run.Output);
        Assert.Single(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_WithoutLoaderId_ModsNeedNoLoaderAndNoInstalledLoaderTakesAnInstance_PointsToGameLaunch()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Listings.Add(LoaderFixtures.ListingWithoutInstance("OtherLoader"));
        var directory = LoaderCommandTests.CreateLoaderDirectory("OtherLoader", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "OtherLoader", directory);
        await SaveInstanceAsync(ContentCommandFixtures.Release("parts-pack"));

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No mod in 'Flight Test' needs a mod loader, and no installed mod loader takes an instance.", run.Error);
        Assert.Contains("'borea game launch'", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_WithoutLoaderId_ModsNeedNoLoader_UsesAndNamesTheInstalledLoaderThatTakesAnInstance()
    {
        await RecordStarMapAsync();
        await SaveInstanceAsync(ContentCommandFixtures.Release("parts-pack"));

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("No mod in 'Flight Test' needs a mod loader. Using StarMap, which takes an instance.", run.Output);
        Assert.Equal("-InstancePath", Assert.Single(_host.ProcessStarter.Plans).Arguments[0]);
    }

    [Fact]
    public async Task Launch_Json_CarriesTheLoaderAndWhetherTheModsNeedIt()
    {
        await RecordStarMapAsync();
        await SaveInstanceAsync(ContentCommandFixtures.Release("parts-pack"));

        var run = await _host.RunAsync("launch", "Flight Test", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("StarMap", run.Json.GetProperty("loaderId").GetString());
        Assert.False(run.Json.GetProperty("requiredByMods").GetBoolean());
        Assert.Equal(42, run.Json.GetProperty("processId").GetInt32());
    }

    [Fact]
    public async Task Launch_Json_ModsNeedTheLoader_SaysTheModsRequireIt()
    {
        await RecordStarMapAsync();
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap"));

        var run = await _host.RunAsync("launch", "Flight Test", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("StarMap", run.Json.GetProperty("loaderId").GetString());
        Assert.True(run.Json.GetProperty("requiredByMods").GetBoolean());
    }

    [Fact]
    public async Task Launch_Json_CarriesTheArgumentsOfTheStartedProcess()
    {
        await RecordStarMapAsync();
        await _host.RunAsync("instance", "create", "Flight Test");
        await _host.RunAsync("instance", "set-arguments", "Flight Test", "--", "-saved");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap", "--json", "--", "-given");

        Assert.Equal(0, run.ExitCode);
        var arguments = run.Json.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()).ToList();
        Assert.Equal("-InstancePath", arguments[0]);
        Assert.Equal(["-saved", "-given"], arguments.Skip(2));
    }

    [Fact]
    public async Task Launch_GivenLoaderIdNotInstalled_NamesIt()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Mod loader 'StarMap' is not installed.", run.Error);
        Assert.Contains("'borea loader install StarMap'", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_GivenIdOfAMod_SaysItIsNoModLoader()
    {
        _host.Mods.Listings.Add(ContentCommandFixtures.Listing("parts-pack"));
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "parts-pack");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("'parts-pack' is a Mod, not a mod loader.", run.Error);
        Assert.DoesNotContain("borea loader install", run.Error);
    }

    [Fact]
    public async Task Launch_GivenLoaderIdNoSourceLists_SaysItIsNotAvailable()
    {
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMapp");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Mod loader 'StarMapp' is not available from the configured sources.", run.Error);
        Assert.DoesNotContain("borea loader install", run.Error);
    }

    [Fact]
    public async Task Launch_WithoutLoaderId_InstalledLoaderNoSourceLists_NamesIt()
    {
        await RecordStarMapAsync();
        _host.Mods.Listings.Clear();
        await SaveInstanceAsync(ContentCommandFixtures.Release("parts-pack"));

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No configured source has a listing for StarMap.", run.Error);
        Assert.DoesNotContain("'borea game launch'", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_WithoutLoaderId_ModsNeedTwoLoaders_AsksForTheId()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Listings.Add(LoaderFixtures.Listing("OtherLoader"));
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap"), NeedsLoader("orbit-tools", "OtherLoader"));

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("need different mod loaders: OtherLoader, StarMap", run.Error);
        Assert.Contains("<loader-id>", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_InvalidLoaderId_IsBadUsage()
    {
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "not a valid id");

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_RecordedFolderWasDeleted_ReportsItAndMakesNoFolder()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "game", Directory.CreateDirectory(Path.Combine(_host.Root, "Game")).FullName);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        await _host.RunAsync("instance", "create", "Flight Test");
        Directory.Delete(loaderDirectory, recursive: true);

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"The folder '{loaderDirectory}' of StarMap is not there.", run.Error);
        Assert.False(Directory.Exists(loaderDirectory));
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_PinnedVersionBesideTheRecordedOne_StartsThePinnedFolder()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var versions = Path.Combine(_host.Paths.GetLoadersRoot(), "StarMap");
        var recorded = LoaderCommandTests.CreateLoaderDirectory("0.4.8", "not a program", versions);
        var pinned = LoaderCommandTests.CreateLoaderDirectory("0.4.7", "not a program", versions);
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(), $"""
            [LoaderInstallations.StarMap]
            DirectoryPath = '{recorded}'
            Version = '0.4.8'
            IsAdopted = false
            """);
        await SavePinnedInstanceAsync("0.4.7");

        var run = await _host.RunAsync("launch", "Flight Test", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("0.4.7", run.Json.GetProperty("loaderVersion").GetString());
        var plan = Assert.Single(_host.ProcessStarter.Plans);
        Assert.Equal(Path.GetFullPath(pinned), plan.WorkingDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(pinned), "StarMap.exe"), plan.Executable);
    }

    [Fact]
    public async Task Launch_PinnedVersionNotInstalled_NamesTheVersionToInstall()
    {
        _host.IndexReader.Snapshot = StarMapIndex();
        await SavePinnedInstanceAsync("0.4.7");

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("'borea loader install StarMap --version 0.4.7'", run.Error);
        Assert.DoesNotContain("Mod loader 'StarMap' is not installed", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_InstalledVersionsOutsideTheChannelAndNoReleaseToOffer_FailsWithAMessage()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        LoaderCommandTests.CreateLoaderDirectory("0.5.0-beta.1", "not a program", Path.Combine(_host.Paths.GetLoadersRoot(), "StarMap"));
        _host.IndexReader.Snapshot = new ContentIndexSnapshot(
            1,
            [new ContentIndexListing("StarMap", LoaderFixtures.Listing(), [LoaderFixtures.Release(version: "0.5.0-beta.1", releaseStatus: ReleaseStatus.Testing)], null)],
            [],
            null,
            []);
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No installed version of StarMap is in the release channel", run.Error);
        Assert.Contains("'borea loader install StarMap --version <version>'", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_NoLoaderInstalledForAnEmptyInstance_NamesTheNewestStarMapThatFitsTheGame()
    {
        _host.IndexReader.Snapshot = StarMapIndex();
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.10.19.5638"), "2026.10.19.5638") };
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("'borea loader install StarMap --version 0.4.8'", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    internal static ContentIndexSnapshot StarMapIndex() => new(
        1,
        [new ContentIndexListing("StarMap", LoaderFixtures.Listing(), [LoaderFixtures.Release(version: "0.4.7", gameMax: "2026.10.13.5602"), LoaderFixtures.Release(version: "0.4.8", gameMin: "2026.10.19.5638")], null)],
        [],
        null,
        []);

    private async Task SavePinnedInstanceAsync(string version)
    {
        var instance = new Instance("Flight Test", InstanceSource.Custom.Value);
        instance.SetLoader(LoaderSetting.Pinned("StarMap", ModVersion.Parse(version)));
        await new FileInstanceRepository(_host.Paths).SaveAsync(instance);
    }

    /// <summary>Sets a game folder and a recorded StarMap folder, and gives the file that holds its game path.</summary>
    private async Task<string> SetUpStarMapFolderAsync()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "game", Directory.CreateDirectory(Path.Combine(_host.Root, "Game")).FullName);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        return Path.Combine(loaderDirectory, "StarMapConfig.json");
    }

    private async Task SaveInstanceAsync(params ModVersionMetadata[] releases)
    {
        var mods = releases
            .Select(release => new InstalledMod(release.ModId, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release))
            .ToList();
        var instance = Instance.FromExisting(Guid.NewGuid(), "Flight Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, mods, isFavorite: false);
        await new FileInstanceRepository(_host.Paths).SaveAsync(instance);
    }

    private static Task WriteContentAsync(string gameDirectory, string contentId)
    {
        var content = Directory.CreateDirectory(Path.Combine(gameDirectory, "Content")).FullName;
        return File.WriteAllTextAsync(Path.Combine(content, "manifest.toml"), $"[[mods]]\nid = \"{contentId}\"\nenabled = true\n");
    }

    private static ModVersionMetadata NeedsLoader(string id, string loaderId) =>
        ContentCommandFixtures.Release(id, loader: new LoaderRequirement(loaderId, ModVersion.Parse("0.4.0")));

    private async Task RecordStarMapAsync()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
    }

    public void Dispose() => _host.Dispose();
}
