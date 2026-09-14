using Borea.Core.Logging;
using Borea.Storage.Logging;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Logging;

public sealed class FileBoreaLogTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 9, 14, 12, 30, 5, 250, TimeSpan.Zero));

    public FileBoreaLogTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Write_AppendsTimedLinesMarkedWithTheSourceToTheDayFile()
    {
        var log = new FileBoreaLog(_paths, BoreaLogSource.Cli, _time);
        Assert.False(Directory.Exists(_paths.GetLogsFolder()));

        log.Write("Install of Example 1.0.0 started.");
        log.Write("Install of Example 1.0.0 finished.");

        Assert.Equal(Path.Combine(_paths.GetLogsFolder(), "borea-2026-09-14.log"), log.CurrentFilePath);
        Assert.Equal(
            [
                "2026-09-14 12:30:05.250 +00:00 [cli] Install of Example 1.0.0 started.",
                "2026-09-14 12:30:05.250 +00:00 [cli] Install of Example 1.0.0 finished.",
            ],
            File.ReadAllLines(log.CurrentFilePath));
    }

    [Fact]
    public void ReadRecentLines_ReturnsTheLastLinesOfTodaysFile()
    {
        var log = new FileBoreaLog(_paths, BoreaLogSource.App, _time);
        log.Write("First");
        log.Write("Second");
        log.Write("Third");

        Assert.Equal(
            [
                "2026-09-14 12:30:05.250 +00:00 [app] Second",
                "2026-09-14 12:30:05.250 +00:00 [app] Third",
            ],
            log.ReadRecentLines(2));
    }

    [Fact]
    public void ReadRecentLines_NoFile_IsEmpty()
    {
        var log = new FileBoreaLog(_paths, BoreaLogSource.App, _time);

        Assert.Empty(log.ReadRecentLines(200));
    }

    [Fact]
    public void Write_Exception_AddsTheStackTrace()
    {
        var log = new FileBoreaLog(_paths, BoreaLogSource.App, _time);
        Exception exception;
        try
        {
            throw new InvalidOperationException("The archive does not hold a mod.");
        }
        catch (InvalidOperationException thrown)
        {
            exception = thrown;
        }

        log.Write("Install of Example 1.0.0 failed while extracting.", exception);

        var text = File.ReadAllText(log.CurrentFilePath);
        Assert.Contains("[app] Install of Example 1.0.0 failed while extracting." + Environment.NewLine + "System.InvalidOperationException: The archive does not hold a mod.", text);
        Assert.Contains(nameof(Write_Exception_AddsTheStackTrace), text);
    }

    [Fact]
    public void Write_PathsUnderTheUserProfile_AreWrittenWithTilde()
    {
        var profile = Path.Combine(_tempRoot, "Users", "Someone");
        var log = new FileBoreaLog(_paths, BoreaLogSource.App, _time, profile);

        log.Write($"Launch of '{Path.Combine(profile, "Games", "StarMap", "StarMap.exe")}' started.");

        var text = File.ReadAllText(log.CurrentFilePath);
        Assert.Contains($"'~{Path.DirectorySeparatorChar}{Path.Combine("Games", "StarMap", "StarMap.exe")}'", text);
        Assert.DoesNotContain("Someone", text);
    }

    [Fact]
    public void Write_FirstLineOfTheDay_KeepsSevenDaysAndOtherFiles()
    {
        var folder = Directory.CreateDirectory(_paths.GetLogsFolder()).FullName;
        string[] old = ["borea-2026-09-07.log", "borea-2026-08-01.log"];
        string[] kept = ["borea-2026-09-08.log", "borea-2026-09-13.log", "borea-notes.log", "other.txt"];
        foreach (var name in old.Concat(kept))
            File.WriteAllText(Path.Combine(folder, name), "");
        var log = new FileBoreaLog(_paths, BoreaLogSource.App, _time);

        log.Write("Borea started.");

        Assert.All(old, name => Assert.False(File.Exists(Path.Combine(folder, name)), name));
        Assert.All(kept, name => Assert.True(File.Exists(Path.Combine(folder, name)), name));
        Assert.True(File.Exists(log.CurrentFilePath));
    }

    [Fact]
    public void Write_NextDay_StartsANewFile()
    {
        var log = new FileBoreaLog(_paths, BoreaLogSource.App, _time);
        log.Write("First day.");
        var first = log.CurrentFilePath;

        _time.Now = _time.Now.AddDays(1);
        log.Write("Second day.");

        Assert.NotEqual(first, log.CurrentFilePath);
        Assert.EndsWith("borea-2026-09-15.log", log.CurrentFilePath);
        Assert.Single(File.ReadAllLines(first));
        Assert.Single(File.ReadAllLines(log.CurrentFilePath));
    }

    [Fact]
    public void Write_TwoLogsInTurnOnOneFile_KeepEveryLine()
    {
        var app = new FileBoreaLog(_paths, BoreaLogSource.App, _time);
        var cli = new FileBoreaLog(_paths, BoreaLogSource.Cli, _time);

        app.Write("From the App.");
        cli.Write("From the CLI.");
        app.Write("From the App again.");

        Assert.Equal(3, File.ReadAllLines(app.CurrentFilePath).Length);
    }

    [Fact]
    public void Write_FolderCannotBeCreated_DoesNotThrow()
    {
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(_paths.GetLogsFolder(), "a file where the folder should be");
        var log = new FileBoreaLog(_paths, BoreaLogSource.App, _time);

        log.Write("Borea started.");

        Assert.False(File.Exists(log.CurrentFilePath));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
