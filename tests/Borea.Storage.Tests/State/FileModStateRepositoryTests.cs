using Borea.Core.State;
using Borea.Storage.State;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.State;

public sealed class FileModStateRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileModStateRepository _repository;
    private readonly Guid _instanceId = Guid.NewGuid();

    public FileModStateRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + _instanceId);
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _repository = new FileModStateRepository(_pathProvider);
    }

    #region Reading

    [Fact]
    public async Task ReadsExistingManifest_MatchingTheGamesFormat()
    {
        WriteManifest("""
            [[mods]]
            id="Core"
            enabled = true

            [[mods]]
            id="some-other-mod"
            enabled = false
            """);

        Assert.True(await _repository.IsActiveAsync(_instanceId, "Core"));
        Assert.False(await _repository.IsActiveAsync(_instanceId, "some-other-mod"));
        Assert.Equal(new[] { "Core" }, await _repository.GetAllActiveModIdsAsync(_instanceId));

        // Ids compare case-insensitively, matching the folder-name identity.
        Assert.True(await _repository.IsActiveAsync(_instanceId, "core"));

        Assert.True(await _repository.SetInactiveAsync(_instanceId, "CORE"));
        Assert.False(await _repository.IsActiveAsync(_instanceId, "Core"));
        Assert.Empty(await _repository.GetAllActiveModIdsAsync(_instanceId));
    }

    [Fact]
    public async Task MissingManifest_ReturnsInactiveDefaults()
    {
        Assert.False(await _repository.IsActiveAsync(_instanceId, "anything"));
        Assert.Empty(await _repository.GetAllActiveModIdsAsync(_instanceId));
        Assert.Empty(await _repository.GetEntriesAsync(_instanceId));
    }

    [Fact]
    public async Task GetEntriesAsync_ReportsFileOrderAndState()
    {
        // ModLibrary.PrepareAll walks by index, so file order is load order.
        WriteManifest("""
            [[mods]]
            id = "Core"
            enabled = true

            [[mods]]
            id = "second"
            enabled = false

            [[mods]]
            id = "third"
            enabled = true
            """);

        var entries = await _repository.GetEntriesAsync(_instanceId);

        Assert.Equal(new[] { "Core", "second", "third" }, entries.Select(e => e.ModId));
        Assert.Equal(new[] { true, false, true }, entries.Select(e => e.Enabled));
    }

    [Fact]
    public async Task EntryWithoutEnabledKey_ReadsAsEnabled()
    {
        // ModEntry.Enabled starts out true, so the game loads such an entry.
        WriteManifest("""
            [[mods]]
            id = "Core"
            """);

        Assert.True(await _repository.IsActiveAsync(_instanceId, "Core"));
    }

    [Fact]
    public async Task Read_ToleratesCommentsQuotingAndUnknownKeys()
    {
        // The game and a human both edit this file.
        WriteManifest("""
            # a comment somebody left
            [[mods]]
            # and one inside an entry
            id = 'Core'
            enabled=true
            note = "a key the game will destroy on its next save"

            [[mods]]
            id = "second"
            enabled   =    false
            """);

        var entries = await _repository.GetEntriesAsync(_instanceId);

        Assert.Equal(new[] { "Core", "second" }, entries.Select(e => e.ModId));
        Assert.True(await _repository.IsActiveAsync(_instanceId, "Core"));
        Assert.False(await _repository.IsActiveAsync(_instanceId, "second"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task IsActiveAsync_IsTrueWhenAnyEntryForTheModIsEnabled(bool firstEnabled, bool secondEnabled)
    {
        // ModLibrary.AddMods compares ordinally so both entries exist, KeyHash.Make
        // lowercases so they are one mod, and PrepareAll skips the disabled one.
        WriteManifest($"""
            [[mods]]
            id = "MyMod"
            enabled = {TomlBool(firstEnabled)}

            [[mods]]
            id = "mymod"
            enabled = {TomlBool(secondEnabled)}
            """);

        Assert.True(await _repository.IsActiveAsync(_instanceId, "MYMOD"));
    }

    [Fact]
    public async Task GetAllActiveModIdsAsync_ReportsOneIdPerModAndNoBlanks()
    {
        // A blank id names no mod, and two enabled case variants are one loaded
        // mod, so neither may reach a caller that feeds ids back in.
        WriteManifest("""
            [[mods]]
            id = "MyMod"
            enabled = true

            [[mods]]
            enabled = true

            [[mods]]
            id = "mymod"
            enabled = true

            [[mods]]
            id = "other"
            enabled = true
            """);

        Assert.Equal(new[] { "MyMod", "other" }, await _repository.GetAllActiveModIdsAsync(_instanceId));
    }

    [Fact]
    public async Task EntryWithoutAnId_IsKeptRatherThanDropped()
    {
        // Borea does not silently drop an entry it did not understand.
        WriteManifest("""
            [[mods]]
            enabled = true

            [[mods]]
            id = "Core"
            enabled = true
            """);

        var entries = await _repository.GetEntriesAsync(_instanceId);
        Assert.Equal(2, entries.Count);
        Assert.Equal(string.Empty, entries[0].ModId);

        await _repository.SetInactiveAsync(_instanceId, "Core");

        Assert.Equal(2, (await _repository.GetEntriesAsync(_instanceId)).Count);
    }

    #endregion

    #region Adding an entry

    [Fact]
    public async Task AddEntryAsync_WritesTheEntryOnceTheModFolderIsThere()
    {
        CreateModFolder("new-mod");

        Assert.Equal(ModEntryAddResult.Added, await _repository.AddEntryAsync(_instanceId, "new-mod", enabled: true));
        Assert.True(await _repository.IsActiveAsync(_instanceId, "new-mod"));
    }

    [Fact]
    public async Task AddEntryAsync_WithoutTheModFolder_WritesNothing()
    {
        // ModLibrary.PrepareManifest deletes an entry written ahead of its files.
        Assert.Equal(ModEntryAddResult.NotOnDisk, await _repository.AddEntryAsync(_instanceId, "new-mod", enabled: true));
        Assert.False(File.Exists(ManifestPath));
    }

    [Fact]
    public async Task AddEntryAsync_WithoutAModToml_WritesNothing()
    {
        // ModEntry.Exists probes for the mod.toml, not for the folder.
        Directory.CreateDirectory(Path.Combine(_pathProvider.GetInstanceModsFolder(_instanceId), "new-mod"));

        Assert.Equal(ModEntryAddResult.NotOnDisk, await _repository.AddEntryAsync(_instanceId, "new-mod", enabled: true));
        Assert.False(File.Exists(ManifestPath));
    }

    [Fact]
    public async Task AddEntryAsync_WritesTheFolderNameRatherThanTheCallersSpelling()
    {
        // A case mismatch is a duplicate disabled entry on Windows, and one
        // ModLibrary.PrepareManifest deletes elsewhere.
        CreateModFolder("MyMod");

        Assert.Equal(ModEntryAddResult.Added, await _repository.AddEntryAsync(_instanceId, "mymod", enabled: true));

        var entries = await _repository.GetEntriesAsync(_instanceId);
        Assert.Equal(new[] { "MyMod" }, entries.Select(e => e.ModId));
        Assert.Contains("id = \"MyMod\"", await File.ReadAllTextAsync(ManifestPath));
    }

    [Fact]
    public async Task AddEntryAsync_AppendsAtTheEnd()
    {
        // ModLibrary.AddMods appends, and entry order is load order.
        WriteManifest("""
            [[mods]]
            id = "Core"
            enabled = true

            [[mods]]
            id = "second"
            enabled = true
            """);
        CreateModFolder("third");

        Assert.Equal(ModEntryAddResult.Added, await _repository.AddEntryAsync(_instanceId, "third", enabled: true));

        var entries = await _repository.GetEntriesAsync(_instanceId);
        Assert.Equal(new[] { "Core", "second", "third" }, entries.Select(e => e.ModId));
    }

    [Fact]
    public async Task AddEntryAsync_LeavesAnEntryThatAlreadyExists()
    {
        // A mod disabled in the game stays disabled through an install.
        WriteManifest("""
            [[mods]]
            id = "existing"
            enabled = false
            """);
        CreateModFolder("existing");

        Assert.Equal(ModEntryAddResult.AlreadyListed, await _repository.AddEntryAsync(_instanceId, "EXISTING", enabled: true));

        Assert.False(await _repository.IsActiveAsync(_instanceId, "existing"));
        Assert.Single(await _repository.GetEntriesAsync(_instanceId));
    }

    [Fact]
    public async Task AddEntryAsync_RejectsAnIdThatWouldCorruptTheManifest()
    {
        // ModManifest.Save writes the id unescaped.
        await Assert.ThrowsAsync<ArgumentException>(
            () => _repository.AddEntryAsync(_instanceId, "has\"quote", enabled: true));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData(".leading-dot")]
    [InlineData("Core")]
    public async Task AddEntryAsync_RejectsAnIdBoreaWillNotPublish(string modId)
    {
        // Borea policy, not a game rule: the game would load all three.
        // Core is the game's own, so a lookup still reaches it.
        await Assert.ThrowsAsync<ArgumentException>(
            () => _repository.AddEntryAsync(_instanceId, modId, enabled: true));
    }

    #endregion

    #region Enabling and disabling

    [Fact]
    public async Task SetActiveAsync_OnAModWithNoEntry_WritesNothing()
    {
        // Creating it here would decide load order as a side effect.
        CreateModFolder("new-mod");

        Assert.False(await _repository.SetActiveAsync(_instanceId, "new-mod"));
        Assert.False(File.Exists(ManifestPath));
    }

    [Fact]
    public async Task SetActiveAsync_FlipsAnEntryTheGameWrote()
    {
        WriteManifest("""
            [[mods]]
            id = "found-by-the-game"
            enabled = false
            """);

        Assert.True(await _repository.SetActiveAsync(_instanceId, "found-by-the-game"));
        Assert.True(await _repository.IsActiveAsync(_instanceId, "found-by-the-game"));
    }

    [Fact]
    public async Task SetActiveAsync_DoesNotMoveTheEntry()
    {
        // Entry order is load order (ModLibrary.PrepareAll walks by index).
        WriteManifest("""
            [[mods]]
            id = "first"
            enabled = false

            [[mods]]
            id = "second"
            enabled = true

            [[mods]]
            id = "third"
            enabled = true
            """);

        await _repository.SetActiveAsync(_instanceId, "first");
        await _repository.SetInactiveAsync(_instanceId, "second");

        var entries = await _repository.GetEntriesAsync(_instanceId);
        Assert.Equal(new[] { "first", "second", "third" }, entries.Select(e => e.ModId));
        Assert.Equal(new[] { true, false, true }, entries.Select(e => e.Enabled));
    }

    [Fact]
    public async Task SetActiveAsync_OnAnEntryAlreadyEnabled_WritesNothing()
    {
        WriteManifest("""
            [[mods]]
            id = "Core"
            enabled = true
            """);
        var before = await File.ReadAllTextAsync(ManifestPath);

        Assert.False(await _repository.SetActiveAsync(_instanceId, "Core"));
        Assert.Equal(before, await File.ReadAllTextAsync(ManifestPath));
    }

    [Fact]
    public async Task SetActiveAsync_EnablesOneOfTwoEntriesDifferingOnlyInCase()
    {
        // One enabled entry is enough to load the mod.
        WriteManifest("""
            [[mods]]
            id = "MyMod"
            enabled = false

            [[mods]]
            id = "mymod"
            enabled = false
            """);

        Assert.True(await _repository.SetActiveAsync(_instanceId, "mymod"));

        var entries = await _repository.GetEntriesAsync(_instanceId);
        Assert.Equal(new[] { true, false }, entries.Select(e => e.Enabled));
    }

    [Fact]
    public async Task SetInactiveAsync_ClearsEveryEntryForTheMod()
    {
        // Any enabled entry loads the mod.
        WriteManifest("""
            [[mods]]
            id = "MyMod"
            enabled = false

            [[mods]]
            id = "mymod"
            enabled = true
            """);

        Assert.True(await _repository.SetInactiveAsync(_instanceId, "MYMOD"));

        Assert.False(await _repository.IsActiveAsync(_instanceId, "MyMod"));
        Assert.Empty(await _repository.GetAllActiveModIdsAsync(_instanceId));
    }

    [Fact]
    public async Task SetInactiveAsync_OnAnEntryAlreadyDisabled_WritesNothing()
    {
        WriteManifest("""
            [[mods]]
            id = "Core"
            enabled = false
            """);
        var before = await File.ReadAllTextAsync(ManifestPath);

        Assert.False(await _repository.SetInactiveAsync(_instanceId, "Core"));
        Assert.Equal(before, await File.ReadAllTextAsync(ManifestPath));
    }

    [Fact]
    public async Task SetInactiveAsync_OnUntrackedMod_IsNoOp()
    {
        Assert.False(await _repository.SetInactiveAsync(_instanceId, "never-existed"));

        Assert.False(File.Exists(ManifestPath));
    }

    [Fact]
    public async Task SetActiveAsync_PreservesOtherEntries()
    {
        WriteManifest("""
            [[mods]]
            id = "mod-a"
            enabled = false

            [[mods]]
            id = "mod-b"
            enabled = true
            """);

        await _repository.SetActiveAsync(_instanceId, "mod-a");
        await _repository.SetInactiveAsync(_instanceId, "mod-a");

        Assert.False(await _repository.IsActiveAsync(_instanceId, "mod-a"));
        Assert.True(await _repository.IsActiveAsync(_instanceId, "mod-b"));
    }

    #endregion

    #region The file changing underneath

    [Fact]
    public async Task Write_KeepsAnEntryThatAppearedSinceTheLastRead()
    {
        // ModLibrary.AddMods adds a folder the user dropped in, so the file grows
        // between two Borea operations.
        WriteManifest("""
            [[mods]]
            id = "Core"
            enabled = true
            """);

        Assert.True(await _repository.IsActiveAsync(_instanceId, "Core"));

        WriteManifest("""
            [[mods]]
            id = "Core"
            enabled = true

            [[mods]]
            id = "dropped-in-by-the-user"
            enabled = false
            """);

        Assert.True(await _repository.SetInactiveAsync(_instanceId, "Core"));

        var entries = await _repository.GetEntriesAsync(_instanceId);
        Assert.Equal(new[] { "Core", "dropped-in-by-the-user" }, entries.Select(e => e.ModId));
        Assert.Equal(new[] { false, false }, entries.Select(e => e.Enabled));
    }

    [Fact]
    public async Task Write_EmitsOnlyIdAndEnabled()
    {
        // ModManifest.Save destroys anything else anyway, so Borea keeps no state here.
        WriteManifest("""
            [[mods]]
            id = "Core"
            enabled = true
            borea_note = "gone on the next save either way"
            """);

        await _repository.SetInactiveAsync(_instanceId, "Core");

        var written = await File.ReadAllTextAsync(ManifestPath);
        Assert.Contains("id = \"Core\"", written);
        Assert.Contains("enabled = false", written);
        Assert.DoesNotContain("borea_note", written);
    }

    [Fact]
    public async Task Write_ProducesAManifestTheRepositoryReadsBack()
    {
        CreateModFolder("written-by-borea");

        await _repository.AddEntryAsync(_instanceId, "written-by-borea", enabled: true);

        // The table name is the game's, so it survives in the game's spelling.
        Assert.Contains("[[mods]]", await File.ReadAllTextAsync(ManifestPath));
        Assert.True(await _repository.IsActiveAsync(_instanceId, "written-by-borea"));
    }

    [Fact]
    public async Task Write_LeavesNoTemporaryFileBehind()
    {
        CreateModFolder("written-by-borea");

        await _repository.AddEntryAsync(_instanceId, "written-by-borea", enabled: true);

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ManifestPath)!, "*.tmp"));
    }

    #endregion

    #region Reordering

    [Fact]
    public async Task ReorderAsync_ChangesLoadOrder()
    {
        WriteManifest("""
            [[mods]]
            id = "first"
            enabled = true

            [[mods]]
            id = "second"
            enabled = false

            [[mods]]
            id = "third"
            enabled = true
            """);

        Assert.True(await _repository.ReorderAsync(_instanceId, new[] { "third", "FIRST", "second" }));

        var entries = await _repository.GetEntriesAsync(_instanceId);
        Assert.Equal(new[] { "third", "first", "second" }, entries.Select(e => e.ModId));
        Assert.Equal(new[] { true, true, false }, entries.Select(e => e.Enabled));
    }

    [Fact]
    public async Task ReorderAsync_WithTheOrderItAlreadyHas_WritesNothing()
    {
        WriteManifest("""
            [[mods]]
            id = "first"
            enabled = true

            [[mods]]
            id = "second"
            enabled = true
            """);
        var before = await File.ReadAllTextAsync(ManifestPath);

        Assert.False(await _repository.ReorderAsync(_instanceId, new[] { "first", "second" }));
        Assert.Equal(before, await File.ReadAllTextAsync(ManifestPath));
    }

    [Fact]
    public async Task ReorderAsync_BreaksATieBetweenTwoCasesByFileOrder()
    {
        // One rule wherever an id has to pick an entry: the first match in file order.
        WriteManifest("""
            [[mods]]
            id = "MyMod"
            enabled = true

            [[mods]]
            id = "other"
            enabled = true

            [[mods]]
            id = "mymod"
            enabled = false
            """);

        Assert.True(await _repository.ReorderAsync(_instanceId, new[] { "other", "mymod", "MyMod" }));

        var entries = await _repository.GetEntriesAsync(_instanceId);
        Assert.Equal(new[] { "other", "MyMod", "mymod" }, entries.Select(e => e.ModId));
        Assert.Equal(new[] { true, true, false }, entries.Select(e => e.Enabled));
    }

    [Fact]
    public async Task ReorderAsync_KeepsAnEntryNamingNoModAtItsIndex()
    {
        // A caller cannot name it, and demanding it would make the manifest
        // unorderable until ModLibrary.PrepareManifest removes it.
        WriteManifest("""
            [[mods]]
            id = "first"
            enabled = true

            [[mods]]
            enabled = true

            [[mods]]
            id = "second"
            enabled = true
            """);

        Assert.True(await _repository.ReorderAsync(_instanceId, new[] { "second", "first" }));

        var entries = await _repository.GetEntriesAsync(_instanceId);
        Assert.Equal(new[] { "second", string.Empty, "first" }, entries.Select(e => e.ModId));
    }

    [Fact]
    public async Task ReorderAsync_RejectsAnOrderThatLeavesAnEntryOut()
    {
        WriteManifest("""
            [[mods]]
            id = "first"
            enabled = true

            [[mods]]
            id = "second"
            enabled = true
            """);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _repository.ReorderAsync(_instanceId, new[] { "first" }));

        var entries = await _repository.GetEntriesAsync(_instanceId);
        Assert.Equal(new[] { "first", "second" }, entries.Select(e => e.ModId));
    }

    [Fact]
    public async Task ReorderAsync_RejectsAnIdThatIsNotInTheManifest()
    {
        WriteManifest("""
            [[mods]]
            id = "first"
            enabled = true
            """);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _repository.ReorderAsync(_instanceId, new[] { "first", "stranger" }));
    }

    #endregion

    private string ManifestPath => _pathProvider.GetInstanceManifestPath(_instanceId);

    private static string TomlBool(bool value) => value ? "true" : "false";

    private void WriteManifest(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        File.WriteAllText(ManifestPath, content);
    }

    /// <summary>A mod folder the way the game recognizes one: a directory holding a mod.toml.</summary>
    private void CreateModFolder(string folderName)
    {
        var modFolder = Path.Combine(_pathProvider.GetInstanceModsFolder(_instanceId), folderName);
        Directory.CreateDirectory(modFolder);
        File.WriteAllText(Path.Combine(modFolder, "mod.toml"), $"name = \"{folderName}\"\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
