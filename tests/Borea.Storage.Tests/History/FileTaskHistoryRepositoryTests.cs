using Borea.Core.History;
using Borea.Storage.History;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.History;

public sealed class FileTaskHistoryRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileTaskHistoryRepository _repository;

    public FileTaskHistoryRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _repository = new FileTaskHistoryRepository(_pathProvider);
    }

    [Fact]
    public async Task SaveThenGet_KeepsEveryField()
    {
        var instanceId = Guid.NewGuid();
        TaskHistoryEntry[] entries =
        [
            new(TaskKind.ModInstall, TaskState.Failed, "AdvancedFlightComputer 0.7.5", instanceId, "Main", Start, Start.AddMinutes(2), "The download failed.", "AdvancedFlightComputer", "0.7.5"),
            new(TaskKind.IndexRefresh, TaskState.Finished, null, null, null, Start, Start.AddMinutes(1)),
            new(TaskKind.BoreaUpdate, TaskState.Failed, null, null, null, Start, Start, "The download failed, so Borea was not changed.", Version: "0.2.1"),
        ];

        await _repository.SaveAsync(entries);

        Assert.Equal(entries, await _repository.GetAsync());
    }

    [Fact]
    public async Task Save_MoreThanTheCap_KeepsTheNewestEntries()
    {
        var entries = Enumerable.Range(0, TaskHistoryEntry.MaxEntries + 20)
            .Select(minute => new TaskHistoryEntry(TaskKind.ModRemoval, TaskState.Finished, $"Mod{minute}", null, null, Start, Start.AddMinutes(minute)))
            .Reverse()
            .ToList();

        await _repository.SaveAsync(entries);

        var saved = await _repository.GetAsync();
        Assert.Equal(TaskHistoryEntry.MaxEntries, saved.Count);
        Assert.Equal(entries.Take(TaskHistoryEntry.MaxEntries), saved);
    }

    [Fact]
    public async Task Get_NoFile_IsEmpty()
    {
        Assert.Empty(await _repository.GetAsync());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("""{ "formatVersion": 99, "tasks": [] }""")]
    public async Task Get_BrokenFile_IsEmpty(string text)
    {
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(_pathProvider.GetTaskHistoryPath(), text);

        Assert.Empty(await _repository.GetAsync());
    }

    [Fact]
    public async Task Save_OverTheHistoryOfANewerFormat_KeepsThatFile()
    {
        const string Newer = """{ "formatVersion": 2, "tasks": { "moved": true } }""";
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(_pathProvider.GetTaskHistoryPath(), Newer);

        await _repository.SaveAsync([new(TaskKind.IndexRefresh, TaskState.Finished, null, null, null, Start, Start)]);

        Assert.Equal(Newer, await File.ReadAllTextAsync(_pathProvider.GetTaskHistoryPath()));
    }

    [Fact]
    public async Task Get_EntryOfANewerBorea_SkipsOnlyThatEntry()
    {
        Directory.CreateDirectory(_tempRoot);
        await File.WriteAllTextAsync(_pathProvider.GetTaskHistoryPath(), """
            {
              "formatVersion": 1,
              "tasks": [
                { "kind": "game-update", "state": "finished", "startedAt": "2026-09-16T12:00:00+00:00", "endedAt": "2026-09-16T12:05:00+00:00" },
                null,
                { "kind": "update-all", "state": "stopped", "instanceName": "Main", "startedAt": "2026-09-16T12:00:00+00:00", "endedAt": "2026-09-16T12:01:00+00:00" }
              ]
            }
            """);

        var entry = Assert.Single(await _repository.GetAsync());
        Assert.Equal(TaskKind.UpdateAll, entry.Kind);
        Assert.Equal(TaskState.Stopped, entry.State);
        Assert.Equal("Main", entry.InstanceName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
