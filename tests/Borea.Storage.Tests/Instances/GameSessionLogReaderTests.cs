using Borea.Storage.Instances;

namespace Borea.Storage.Tests.Instances;

public sealed class GameSessionLogReaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    private string LogsFolder => Path.Combine(_tempRoot, "logs");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Read_CleanClose_LastsFromTheFirstToTheLastTimedLine()
    {
        var log = Write(Path.Combine("Archives", "KittenSpaceAgency.260915.0.log"),
        [
            "09:52:52.093  INFO loaded settings from settings.toml",
            "09:52:54.166 DEBUG Found dedicated compute queue family at index 2",
            "09:54:24.733  INFO you are running the latest version",
            "10:32:47.769 DEBUG Shutting down application",
            "10:32:48.770  INFO [AFC] Unloaded.",
        ]);

        var read = GameSessionLogReader.Read(log);

        Assert.True(read.Readable);
        Assert.NotNull(read.Session);
        Assert.True(read.Session.ClosedCleanly);
        Assert.Equal(new TimeSpan(0, 0, 39, 56, 677), read.Session.Length);
        Assert.Equal(Local(new DateTime(2026, 9, 15, 10, 32, 48, 770)), read.Session.End);
    }

    [Fact]
    public void Read_CrashWithAStackTrace_EndsAtTheLastTimedLine()
    {
        var log = Write(Path.Combine("Archives", "Brutal.260221.19.log"),
        [
            "Brutal.Monitor Fallback file logger started.",
            "12:59:10.177  INFO loaded settings from settings.toml",
            "13:20:11.152  INFO Current ring maximum meshes instances: 110413, impostor instances: 990645, VRAM usage: 23 MB.",
            "13:25:11.311 ERROR Unhandled exception System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation",
            " ---> System.IO.DirectoryNotFoundException: Could not find a part of the path 'Content\\Fonts\\3270-Regular.ttf'",
            "   at KSA.FontManager.RegenerateFonts()",
            "   at KSA.Program..ctor(IReadOnlyList`1 inArgs)",
            "   at StarMap.Program.Main(String[] args).",
        ]);

        var session = GameSessionLogReader.Read(log).Session;

        Assert.NotNull(session);
        Assert.False(session.ClosedCleanly);
        Assert.Equal(new TimeSpan(0, 0, 26, 1, 134), session.Length);
    }

    [Fact]
    public void Read_PastMidnight_EndsOnTheDayOfTheArchiveName()
    {
        var log = Write(Path.Combine("Archives", "Brutal.260915.0.log"),
        [
            "23:43:52.250  INFO loaded settings from settings.toml",
            "00:05:35.999 DEBUG Shutting down application",
            "00:05:36.773  INFO [DvMap] Unloaded.",
        ]);

        var session = GameSessionLogReader.Read(log).Session;

        Assert.NotNull(session);
        Assert.Equal(new TimeSpan(0, 0, 21, 44, 523), session.Length);
        Assert.Equal(Local(new DateTime(2026, 9, 14, 23, 43, 52, 250)), session.Start);
    }

    [Fact]
    public void Read_RunStartedBeforeMidnight_StartsOnTheNextDay()
    {
        var log = Write("KittenSpaceAgency.260915-235958.43720.log",
        [
            "[Brutal.Monitor] recovered 13 log lines from KittenSpaceAgency.260915-111840.37972.log.ring",
            "00:00:03.000  INFO loaded settings from settings.toml",
            "00:12:03.000  INFO [AFC] Unloaded.",
        ]);

        var session = GameSessionLogReader.Read(log).Session;

        Assert.NotNull(session);
        Assert.Equal(Local(new DateTime(2026, 9, 16, 0, 0, 3)), session.Start);
        Assert.Equal(TimeSpan.FromMinutes(12), session.Length);
    }

    [Fact]
    public void Read_UndatedLogWrittenJustAfterMidnight_EndsOnTheDayBefore()
    {
        var log = Write("KittenSpaceAgency.log",
        [
            "23:16:22.058  INFO loaded settings from settings.toml",
            "23:59:59.950  INFO [AFC] Unloaded.",
        ],
        lastWrite: new DateTime(2026, 9, 15, 0, 0, 0, 20));

        var session = GameSessionLogReader.Read(log).Session;

        Assert.NotNull(session);
        Assert.Equal(Local(new DateTime(2026, 9, 14, 23, 59, 59, 950)), session.End);
    }

    [Fact]
    public void Read_NoLineWithATime_IsUnreadable()
    {
        var log = Write("KittenSpaceAgency.log",
        [
            "[2026-09-15 11:24:36] loaded settings from settings.toml",
            "[2026-09-15 11:58:02] Shutting down application",
        ]);

        var read = GameSessionLogReader.Read(log);

        Assert.False(read.Readable);
        Assert.Null(read.Session);
    }

    [Fact]
    public void Read_EmptyLog_IsReadableWithoutASession()
    {
        var read = GameSessionLogReader.Read(Write("KittenSpaceAgency.log", []));

        Assert.True(read.Readable);
        Assert.Null(read.Session);
    }

    [Fact]
    public void Read_OneTimedLine_LastsNoTime()
    {
        var session = GameSessionLogReader.Read(Write("KittenSpaceAgency.log", ["11:24:36.689  INFO loaded settings from settings.toml"])).Session;

        Assert.NotNull(session);
        Assert.Equal(TimeSpan.Zero, session.Length);
    }

    private GameLogFile Write(string relativePath, string[] lines, DateTime? lastWrite = null)
    {
        var path = Path.Combine(LogsFolder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        if (lastWrite is { } written)
            File.SetLastWriteTime(path, written);

        return GameLogFiles.Find(Path.Combine(LogsFolder, "KittenSpaceAgency.log")).Single(log => log.File.Name == Path.GetFileName(path));
    }

    private static DateTimeOffset Local(DateTime local) => new(local, TimeZoneInfo.Local.GetUtcOffset(local));
}
