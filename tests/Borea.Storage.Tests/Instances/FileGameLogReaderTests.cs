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

    private void Write(string text)
    {
        var path = _paths.GetInstanceGameLogPath(_instanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
