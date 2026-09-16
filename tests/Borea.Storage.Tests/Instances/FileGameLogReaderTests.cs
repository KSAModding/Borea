using System.Text;
using Borea.Storage.Instances;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FileGameLogReaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileGameLogReader _reader;
    private readonly Guid _instanceId = Guid.NewGuid();

    public FileGameLogReaderTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _reader = new FileGameLogReader(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task ReadGameLogAsync_NoLog_ReportsTheGamePathAsMissing()
    {
        var log = await _reader.ReadGameLogAsync(_instanceId);

        Assert.False(log.Exists);
        Assert.Empty(log.Lines);
        Assert.Equal(Path.Combine(_paths.GetInstanceRoot(_instanceId), "logs", "KittenSpaceAgency.log"), log.Path);
    }

    [Fact]
    public async Task ReadGameLogAsync_ShortLog_ReturnsEveryLine()
    {
        Write("first\r\nsecond\nthird\n");

        var log = await _reader.ReadGameLogAsync(_instanceId);

        Assert.True(log.Exists);
        Assert.Equal(["first", "second", "third"], log.Lines);
    }

    [Fact]
    public async Task ReadGameLogAsync_LongLog_ReturnsOnlyTheLastWholeLines()
    {
        var text = new StringBuilder();
        for (var line = 1; line <= 5000; line++)
            text.Append("line ").Append(line).Append(' ', 40).Append('\n');
        Write(text.ToString());

        var log = await _reader.ReadGameLogAsync(_instanceId);

        Assert.Equal(FileGameLogReader.MaxLines, log.Lines.Count);
        Assert.StartsWith("line 4801 ", log.Lines[0]);
        Assert.StartsWith("line 5000 ", log.Lines[^1]);
    }

    [Fact]
    public async Task ReadGameLogAsync_LogHeldOpenByTheGame_StillReads()
    {
        var path = _paths.GetInstanceGameLogPath(_instanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        writer.Write(Encoding.UTF8.GetBytes("running\n"));
        writer.Flush();

        var log = await _reader.ReadGameLogAsync(_instanceId);

        Assert.Equal(["running"], log.Lines);
    }

    [Fact]
    public async Task ReadGameLogAsync_RunLogs_ReadsTheNewestRunAndNoArchive()
    {
        var now = DateTime.UtcNow;
        Touch("KittenSpaceAgency.log", now.AddDays(-1), "older game");
        Touch("KittenSpaceAgency.260915-092000.1200.log", now.AddHours(-2), "earlier run");
        Touch("KittenSpaceAgency.260915-112433.43720.log", now.AddMinutes(-1), "current run");
        Touch(Path.Combine("Archives", "KittenSpaceAgency.260915.0.log"), now, "archived run");

        var log = await _reader.ReadGameLogAsync(_instanceId);

        Assert.Equal(["current run"], log.Lines);
        Assert.Equal("KittenSpaceAgency.260915-112433.43720.log", Path.GetFileName(log.Path));
    }

    [Fact]
    public async Task GetLastWriteAsync_NoLogs_ReturnsNull()
    {
        Assert.Null(await _reader.GetLastWriteAsync(_instanceId));
    }

    [Fact]
    public async Task GetLastWriteAsync_SeveralSessionLogs_ReturnsTheNewestWrite()
    {
        var newest = new DateTime(2026, 9, 15, 9, 34, 0, DateTimeKind.Utc);
        Touch("KittenSpaceAgency.log", newest.AddDays(-1));
        Touch(Path.Combine("Archives", "Brutal.260914.3.log"), newest.AddDays(-2));
        Touch(Path.Combine("Archives", "KittenSpaceAgency.260915.0.log"), newest.AddHours(-2));
        Touch("KittenSpaceAgency.260915-112433.43720.log", newest);

        Assert.Equal(new DateTimeOffset(newest), await _reader.GetLastWriteAsync(_instanceId));
    }

    [Fact]
    public async Task GetLastWriteAsync_CrashTailsAndTheLaunchLog_AreNotSessions()
    {
        Touch("KittenSpaceAgency.260915-111840.37972.previous-crash.log", DateTime.UtcNow);
        Touch("KittenSpaceAgency.260915-111840.37972.abnormal-exit.log", DateTime.UtcNow);
        Touch("borea-launch.log", DateTime.UtcNow);

        Assert.Null(await _reader.GetLastWriteAsync(_instanceId));
    }

    private void Touch(string relativePath, DateTime lastWriteUtc, string line = "09:34:00.000  INFO loaded settings from settings.toml")
    {
        var path = Path.Combine(Path.GetDirectoryName(_paths.GetInstanceGameLogPath(_instanceId))!, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, line + "\n");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }

    private void Write(string text)
    {
        var path = _paths.GetInstanceGameLogPath(_instanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
