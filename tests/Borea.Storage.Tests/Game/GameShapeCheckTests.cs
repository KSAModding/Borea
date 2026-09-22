using Borea.Core.Game;
using Borea.Storage.Game;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Game;

/// <summary>
/// Runs the check against the copies of real game files in Game/Fixtures, so a
/// passing test says that the check accepts what the game writes.
/// </summary>
public sealed class GameShapeCheckTests : IDisposable
{
    private const string GameAssemblyFixture = "GameVersionFixture.dll";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;

    public GameShapeCheckTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        Copy(Fixture("game"), _paths.GetGameDirectoryPath()!);
        Copy(Fixture("profile"), _paths.GetSharedProfileRoot());
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, GameAssemblyFixture),
            Path.Combine(_paths.GetGameDirectoryPath()!, "KSA.dll"));
    }

    private GameShapeCheck Check() => new(_paths, new InstalledGameVersionProvider(_paths));

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Game", "Fixtures", name);

    private static void Copy(string source, string target)
    {
        foreach (var folder in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, folder)));

        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    [Fact]
    public async Task GetAsync_TheRealFiles_EveryAssumptionHolds()
    {
        var shape = await Check().GetAsync();

        Assert.Equal(GameShapeStatus.Verified, shape.Status);
        Assert.All(shape.Results, result => Assert.Equal(GameAssumptionState.Holds, result.State));
        Assert.Equal(Enum.GetValues<GameAssumption>(), shape.Results.Select(result => result.Assumption));
    }

    [Fact]
    public async Task GetAsync_TheRealFiles_CountsTheManifestEntries()
    {
        var shape = await Check().GetAsync();

        Assert.EndsWith("lists 1 entry.", shape.Results.Single(result => result.Assumption == GameAssumption.ContentManifest).Detail);
        Assert.EndsWith("lists 2 entries.", shape.Results.Single(result => result.Assumption == GameAssumption.ProfileManifest).Detail);
    }

    [Fact]
    public async Task GetAsync_NoGameAndNoProfile_ChecksNothing()
    {
        Directory.Delete(_tempRoot, recursive: true);

        var shape = await Check().GetAsync();

        Assert.Equal(GameShapeStatus.Unknown, shape.Status);
        Assert.True(shape.AllowsWrites);
    }

    [Fact]
    public async Task GetAsync_ContentManifestRenamed_IsBroken()
    {
        File.Move(
            Path.Combine(_paths.GetGameDirectoryPath()!, "Content", "manifest.toml"),
            Path.Combine(_paths.GetGameDirectoryPath()!, "Content", "content.toml"));

        var shape = await Check().GetAsync();

        Assert.False(shape.AllowsWrites);
        Assert.Equal(GameAssumption.ContentManifest, Assert.Single(shape.Broken).Assumption);
    }

    [Fact]
    public async Task GetAsync_PatchNotesInAnotherFormat_IsBroken()
    {
        var folder = Path.Combine(_paths.GetGameDirectoryPath()!, "Content", "Versions");
        foreach (var file in Directory.GetFiles(folder, "*.json"))
            await File.WriteAllTextAsync(file, "{ \"changes\": [] }");

        var shape = await Check().GetAsync();

        Assert.Equal(GameAssumption.PatchNotes, Assert.Single(shape.Broken).Assumption);
    }

    [Fact]
    public async Task GetAsync_ManifestThatIsNotToml_IsBroken()
    {
        await File.WriteAllTextAsync(Path.Combine(_paths.GetSharedProfileRoot(), "manifest.toml"), "mods: [ { id: Core } ]");

        var shape = await Check().GetAsync();

        Assert.Equal(GameAssumption.ProfileManifest, Assert.Single(shape.Broken).Assumption);
    }

    [Fact]
    public async Task GetAsync_ModDefinitionRenamed_IsBroken()
    {
        var mod = Directory.GetDirectories(Path.Combine(_paths.GetSharedProfileRoot(), "mods"))[0];
        File.Move(Path.Combine(mod, "mod.toml"), Path.Combine(mod, "mod.json"));

        var shape = await Check().GetAsync();

        Assert.Equal(GameAssumption.ModFolder, Assert.Single(shape.Broken).Assumption);
    }

    [Fact]
    public async Task GetAsync_LogsUnderOtherNames_IsBroken()
    {
        var logs = Path.Combine(_paths.GetSharedProfileRoot(), "logs");
        Directory.Delete(Path.Combine(logs, "Archives"), recursive: true);

        // The names the game gives a log are built from dots, so a session named without them is one Borea cannot place.
        var index = 0;
        foreach (var file in Directory.GetFiles(logs, "*.log"))
            File.Move(file, Path.Combine(logs, $"session-{index++}.log"));

        var shape = await Check().GetAsync();

        Assert.Equal(GameAssumption.SessionLog, Assert.Single(shape.Broken).Assumption);
    }

    [Fact]
    public async Task GetAsync_ProfileThatHoldsNothingTheGameWrites_IsBroken()
    {
        var root = _paths.GetSharedProfileRoot();
        Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(Path.Combine(root, "Content"));

        var shape = await Check().GetAsync();

        Assert.Equal(GameAssumption.ProfileLayout, Assert.Single(shape.Broken).Assumption);
    }

    [Fact]
    public async Task GetAsync_AFolderTheManifestDoesNotName_SaysNothingAboutTheGame()
    {
        // The user unpacked an archive into the mods folder by hand, which is
        // what 'borea instance scan' is there for.
        Directory.CreateDirectory(Path.Combine(_paths.GetSharedProfileRoot(), "mods", "Downloaded", "inner"));

        var shape = await Check().GetAsync();

        Assert.Equal(GameShapeStatus.Verified, shape.Status);
        Assert.True(shape.AllowsWrites);
    }

    [Fact]
    public async Task GetForInstanceAsync_AFolderTheUserMade_DoesNotStopWrites()
    {
        var instanceId = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(_paths.GetInstanceModsFolder(instanceId), "Downloaded"));

        var shape = await Check().GetForInstanceAsync(instanceId);

        Assert.True(shape.AllowsWrites);
        Assert.Equal(GameAssumptionState.NotChecked, shape.Results.Single(result => result.Assumption == GameAssumption.ModFolder).State);
    }

    [Fact]
    public async Task GetAsync_ARecoveredCrashTail_IsNotALogBoreaCannotPlace()
    {
        var logs = Path.Combine(_paths.GetSharedProfileRoot(), "logs");
        Directory.Delete(Path.Combine(logs, "Archives"), recursive: true);
        foreach (var file in Directory.GetFiles(logs, "*.log"))
            File.Delete(file);

        await File.WriteAllTextAsync(Path.Combine(logs, "borea-launch.log"), "started\n");
        await File.WriteAllTextAsync(Path.Combine(logs, "KittenSpaceAgency.260915-111840.37972.previous-crash.log"), "crashed\n");

        var shape = await Check().GetAsync();

        Assert.Equal(GameAssumptionState.NotChecked, shape.Results.Single(result => result.Assumption == GameAssumption.SessionLog).State);
        Assert.Empty(shape.Broken);
    }

    [Fact]
    public async Task GetForInstanceAsync_AnEmptyInstance_ChecksOnlyTheInstallation()
    {
        var shape = await Check().GetForInstanceAsync(Guid.NewGuid());

        Assert.True(shape.AllowsWrites);
        Assert.All(
            shape.Results.Where(result => result.Assumption is not (GameAssumption.GameAssembly or GameAssumption.ContentManifest or GameAssumption.PatchNotes)),
            result => Assert.Equal(GameAssumptionState.NotChecked, result.State));
    }

    [Fact]
    public async Task GetAsync_TheInstallation_IsCheckedOncePerBuild()
    {
        var check = Check();
        var first = await check.GetAsync();

        File.Delete(Path.Combine(_paths.GetGameDirectoryPath()!, "Content", "manifest.toml"));
        var second = await check.GetAsync();

        Assert.Equal(GameShapeStatus.Verified, first.Status);
        Assert.Equal(GameShapeStatus.Verified, second.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
