using Borea.Storage.Game;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Game;

public sealed class FileGamePatchNotesReaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    private FileGamePatchNotesReader Reader(bool hasGameDirectory = true) =>
        new(new TestGamePathProvider(_tempRoot, hasGameDirectory));

    private void WriteVersionFile(string name, string json)
    {
        var path = Path.Combine(_tempRoot, "Game", "Content", "Versions", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static string ChangeLog(string build, int fromRevision, int toRevision, string commits) =>
        $$"""{ "build": "{{build}}", "date": "2026-09-15", "fromRevision": {{fromRevision}}, "toRevision": {{toRevision}}, "commits": [{{commits}}] }""";

    [Fact]
    public async Task ReadAsync_VersionsFolder_ReturnsEveryBuildNewestFirstAndSkipsBrokenFiles()
    {
        WriteVersionFile("v2026.9.X.5402.json", ChangeLog("2026.9.7.5402", 5400, 5402, """
            { "rev": 5401, "date": "2026-09-01", "author": "Dev", "lines": ["Older change."] },
            { "rev": 5402, "date": "2026-09-02", "author": "Dev", "lines": ["Newer change.", "  ", "Second line."] }
            """));
        WriteVersionFile("v2026.9.X.5438.json", ChangeLog("2026.9.10.5438", 5402, 5438, """
            { "rev": 5403, "date": "2026-09-03", "author": "Dev", "lines": ["Fixed the Milky Way."] }
            """));
        WriteVersionFile("v2026.8.X.5348.json", ChangeLog("2026.8.3.5348", 5261, 5348, ""));
        WriteVersionFile("broken.json", """{ "build": "2026.9.11.5500", "commits": [ """);
        WriteVersionFile("no-commits.json", """{ "build": "2026.9.12.5600", "toRevision": 5600 }""");
        WriteVersionFile("readme.txt", "not a version file");

        var notes = await Reader().ReadAsync();

        Assert.Equal(["2026.9.10.5438", "2026.9.7.5402", "2026.8.3.5348"], notes.Select(entry => entry.Build));
        Assert.Equal([5438, 5402, 5348], notes.Select(entry => entry.Revision));
        Assert.Equal([5402, 5400, 5261], notes.Select(entry => entry.FromRevision));
        Assert.Equal(new DateOnly(2026, 9, 15), notes[0].Date);
        Assert.Equal(["Fixed the Milky Way."], notes[0].Lines);
        Assert.Equal(["Newer change.", "Second line.", "Older change."], notes[1].Lines);
        Assert.Empty(notes[2].Lines);
    }

    [Fact]
    public async Task ReadAsync_FileInASubfolderWithoutADate_IsReadWithNoDate()
    {
        WriteVersionFile(Path.Combine("Older", "v2025.8.X.2091.json"), """{ "build": "2025.8.33.2091", "toRevision": 2091, "commits": [ { "rev": 2091, "lines": ["First."] } ] }""");

        var notes = await Reader().ReadAsync();

        var entry = Assert.Single(notes);
        Assert.Equal("2025.8.33.2091", entry.Build);
        Assert.Null(entry.Date);
        Assert.Equal(["First."], entry.Lines);
    }

    [Fact]
    public async Task ReadAsync_NoVersionsFolder_ReturnsNothing()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Game"));

        Assert.Empty(await Reader().ReadAsync());
    }

    [Fact]
    public async Task ReadAsync_NoGameFolderSet_ReturnsNothing()
    {
        WriteVersionFile("v2026.9.X.5438.json", ChangeLog("2026.9.10.5438", 5402, 5438, ""));

        Assert.Empty(await Reader(hasGameDirectory: false).ReadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
