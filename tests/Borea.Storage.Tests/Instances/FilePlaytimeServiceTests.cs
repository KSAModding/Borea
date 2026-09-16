using Borea.Storage.Instances;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FilePlaytimeServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FilePlaytimeService _service;
    private readonly Guid _instanceId = Guid.NewGuid();

    public FilePlaytimeServiceTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _service = new FilePlaytimeService(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task GetPlaytime_NoLogs_IsZeroAndKnown()
    {
        var playtime = await _service.GetPlaytimeAsync(_instanceId);

        Assert.Equal(TimeSpan.Zero, playtime.Total);
        Assert.Equal(0, playtime.Sessions);
        Assert.True(playtime.IsKnown);
        Assert.False(File.Exists(_paths.GetInstancePlaytimePath(_instanceId)));
    }

    [Fact]
    public async Task GetPlaytime_ArchivesAndTheLastLog_AddsUpEverySession()
    {
        WriteLog(Archive("Brutal.260914.2.log"), Session("20:00:00.000", "20:40:00.000"));
        WriteLog(Archive("KittenSpaceAgency.260915.0.log"), Session("09:00:00.000", "10:00:00.000"));
        WriteLog("KittenSpaceAgency.log", Session("08:00:00.000", "08:20:00.000"));

        var playtime = await _service.GetPlaytimeAsync(_instanceId);

        Assert.Equal(TimeSpan.FromHours(2), playtime.Total);
        Assert.Equal(3, playtime.Sessions);
        Assert.False(playtime.IncludesRunningSession);
        Assert.True(playtime.IsKnown);
    }

    [Fact]
    public async Task GetPlaytime_StartupCrashOfFiveSeconds_AddsNoTimeAndNoSession()
    {
        WriteLog(Archive("KittenSpaceAgency.260915.0.log"), Session("09:00:00.000", "10:00:00.000"));
        WriteLog(Archive("KittenSpaceAgency.260915.1.log"),
        [
            "12:59:10.177  INFO loaded settings from settings.toml",
            "12:59:15.311 ERROR Unhandled exception System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation",
            "   at KSA.Program..ctor(IReadOnlyList`1 inArgs)",
        ]);

        var playtime = await _service.GetPlaytimeAsync(_instanceId);

        Assert.Equal(TimeSpan.FromHours(1), playtime.Total);
        Assert.Equal(1, playtime.Sessions);
    }

    [Fact]
    public async Task GetPlaytime_NewestLogUnreadable_IsUnknown()
    {
        WriteLog(Archive("KittenSpaceAgency.260915.0.log"), Session("09:00:00.000", "10:00:00.000"), DateTime.Now.AddHours(-2));
        WriteLog("KittenSpaceAgency.260915-112433.43720.log", ["[11:24:36] loaded settings from settings.toml", "[11:58:02] Shutting down application"]);

        var playtime = await _service.GetPlaytimeAsync(_instanceId);

        Assert.False(playtime.IsKnown);
        Assert.Equal(1, playtime.UnreadableLogs);
    }

    [Fact]
    public async Task GetPlaytime_OlderLogUnreadable_StaysKnown()
    {
        WriteLog(Archive("KittenSpaceAgency.260914.0.log"), ["[20:00:00] loaded settings from settings.toml"], DateTime.Now.AddHours(-3));
        WriteLog(Archive("KittenSpaceAgency.260915.0.log"), Session("09:00:00.000", "10:00:00.000"));

        var playtime = await _service.GetPlaytimeAsync(_instanceId);

        Assert.True(playtime.IsKnown);
        Assert.Equal(1, playtime.UnreadableLogs);
        Assert.Equal(TimeSpan.FromHours(1), playtime.Total);
    }

    [Fact]
    public async Task GetPlaytime_LogWithoutShutdownWrittenJustNow_CountsTheRunningSession()
    {
        WriteLog("KittenSpaceAgency.260915-112433.43720.log", Session("11:24:00.000", "11:39:00.000", clean: false), DateTime.Now);

        var playtime = await _service.GetPlaytimeAsync(_instanceId);

        Assert.True(playtime.IncludesRunningSession);
        Assert.Equal(TimeSpan.FromMinutes(15), playtime.Total);
        Assert.Equal(1, playtime.Sessions);
    }

    [Fact]
    public async Task GetPlaytime_LogWithoutShutdownWrittenLongAgo_CountsAFinishedSession()
    {
        WriteLog("KittenSpaceAgency.260915-112433.43720.log", Session("11:24:00.000", "11:39:00.000", clean: false), DateTime.Now - FilePlaytimeService.RunningWindow - TimeSpan.FromMinutes(1));

        var playtime = await _service.GetPlaytimeAsync(_instanceId);

        Assert.False(playtime.IncludesRunningSession);
        Assert.Equal(TimeSpan.FromMinutes(15), playtime.Total);
    }

    [Fact]
    public async Task GetPlaytime_SecondRead_ReadsOnlyTheNewArchives()
    {
        var known = WriteLog(Archive("KittenSpaceAgency.260915.0.log"), Session("09:00:00.000", "10:00:00.000"));
        await _service.GetPlaytimeAsync(_instanceId);

        // the size is the same and no line has a time, so reading it again would lose the session
        var bytes = File.ReadAllBytes(known);
        Array.Fill(bytes, (byte)'x');
        File.WriteAllBytes(known, bytes);
        WriteLog(Archive("KittenSpaceAgency.260915.1.log"), Session("11:00:00.000", "11:30:00.000"));

        var playtime = await new FilePlaytimeService(_paths).GetPlaytimeAsync(_instanceId);

        Assert.Equal(TimeSpan.FromMinutes(90), playtime.Total);
        Assert.Equal(2, playtime.Sessions);
        Assert.Equal(0, playtime.UnreadableLogs);
    }

    [Fact]
    public async Task GetPlaytime_ArchiveDeleted_KeepsItsSession()
    {
        var archive = WriteLog(Archive("KittenSpaceAgency.260915.0.log"), Session("09:00:00.000", "10:00:00.000"));
        await _service.GetPlaytimeAsync(_instanceId);
        File.Delete(archive);

        var playtime = await new FilePlaytimeService(_paths).GetPlaytimeAsync(_instanceId);

        Assert.Equal(TimeSpan.FromHours(1), playtime.Total);
        Assert.Equal(1, playtime.Sessions);
    }

    [Fact]
    public async Task GetPlaytime_ArchiveChangedSize_IsReadAgain()
    {
        WriteLog(Archive("KittenSpaceAgency.260915.0.log"), Session("09:00:00.000", "10:00:00.000"));
        await _service.GetPlaytimeAsync(_instanceId);
        WriteLog(Archive("KittenSpaceAgency.260915.0.log"),
        [
            "09:00:00.000  INFO loaded settings from settings.toml",
            "09:30:00.000  INFO you are running the latest version",
            "10:30:00.000 DEBUG Shutting down application",
        ]);

        var playtime = await _service.GetPlaytimeAsync(_instanceId);

        Assert.Equal(TimeSpan.FromMinutes(90), playtime.Total);
        Assert.Equal(1, playtime.Sessions);
    }

    [Theory]
    [InlineData("Sessions = [")]
    [InlineData("[[Sessions]]\nFile = \"KittenSpaceAgency.260914.0.log\"\nStart = \"yesterday\"\n")]
    public async Task GetPlaytime_CacheDoesNotParse_KeepsItAsideAndReadsTheArchivesAgain(string text)
    {
        WriteLog(Archive("KittenSpaceAgency.260915.0.log"), Session("09:00:00.000", "10:00:00.000"));
        var cache = _paths.GetInstancePlaytimePath(_instanceId);
        await File.WriteAllTextAsync(cache, text);

        var playtime = await _service.GetPlaytimeAsync(_instanceId);

        Assert.Equal(TimeSpan.FromHours(1), playtime.Total);
        Assert.Equal(text, await File.ReadAllTextAsync(cache + ".bad"));
        Assert.Contains("KittenSpaceAgency.260915.0.log", await File.ReadAllTextAsync(cache));
    }

    [Fact]
    public async Task GetPlaytime_CacheHeldOpen_DoesNotReplaceItWithFewerSessions()
    {
        var deleted = WriteLog(Archive("KittenSpaceAgency.260915.0.log"), Session("09:00:00.000", "10:00:00.000"));
        await _service.GetPlaytimeAsync(_instanceId);
        File.Delete(deleted);
        WriteLog(Archive("KittenSpaceAgency.260915.1.log"), Session("11:00:00.000", "11:30:00.000"));
        var cache = _paths.GetInstancePlaytimePath(_instanceId);

        // Windows refuses the read but still lets the file be replaced, and other systems lock a file only for FileShare.None
        using (new FileStream(cache, FileMode.Open, FileAccess.Read, OperatingSystem.IsWindows() ? FileShare.Delete : FileShare.None))
            await new FilePlaytimeService(_paths).GetPlaytimeAsync(_instanceId);

        var playtime = await new FilePlaytimeService(_paths).GetPlaytimeAsync(_instanceId);

        Assert.Equal(TimeSpan.FromMinutes(90), playtime.Total);
        Assert.Equal(2, playtime.Sessions);
    }

    private static string Archive(string name) => Path.Combine("Archives", name);

    private static string[] Session(string start, string end, bool clean = true) =>
    [
        $"{start}  INFO loaded settings from settings.toml",
        clean ? $"{end} DEBUG Shutting down application" : $"{end}  INFO Current ring maximum meshes instances: 110413, impostor instances: 990645, VRAM usage: 23 MB.",
    ];

    /// <summary>Writes a log below the instance's logs folder, last written an hour ago unless a time is given.</summary>
    private string WriteLog(string relativePath, string[] lines, DateTime? lastWrite = null)
    {
        var path = Path.Combine(Path.GetDirectoryName(_paths.GetInstanceGameLogPath(_instanceId))!, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        File.SetLastWriteTime(path, lastWrite ?? DateTime.Now.AddHours(-1));
        return path;
    }
}
