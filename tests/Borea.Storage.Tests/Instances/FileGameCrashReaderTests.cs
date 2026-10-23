using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;
using Borea.Storage.Tests.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Instances;

public sealed class FileGameCrashReaderTests : IDisposable
{
    private static readonly DateTimeOffset LastLaunch = new(2026, 9, 26, 10, 25, 39, TimeSpan.Zero);

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly FileGameCrashReader _reader;
    private readonly Instance _instance = new("Main", InstanceSource.Custom.Value);

    public FileGameCrashReaderTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        _reader = new FileGameCrashReader(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string Write(string name, DateTimeOffset writtenAt, params string[] lines)
    {
        var path = Path.Combine(Path.GetDirectoryName(_paths.GetInstanceGameLogPath(_instance.InstanceId))!, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines.Length == 0 ? ["Unhandled exception System.NullReferenceException: Object reference not set to an instance of an object.", "   at KSA.Program.Main(String[] inArgs)."] : lines);
        File.SetLastWriteTimeUtc(path, writtenAt.UtcDateTime);
        return path;
    }

    [Fact]
    public async Task GetUnreported_CrashLogNewerThanTheLastLaunch_ReturnsTheNewest()
    {
        Write("KittenSpaceAgency.260926-122314.7296.20260926_122343.abnormal-exit.log", LastLaunch.AddMinutes(1));
        var newest = Write("KittenSpaceAgency.260926-122536.43512.20260926_122557.abnormal-exit.log", LastLaunch.AddMinutes(3));

        var crash = await _reader.GetUnreportedAsync(_instance, LastLaunch);

        Assert.NotNull(crash);
        Assert.Equal(newest, crash.LogPath);
        Assert.Equal("KittenSpaceAgency.260926-122536.43512", crash.Run);
        Assert.Equal("System.NullReferenceException: Object reference not set to an instance of an object.", crash.Exception);
        Assert.Equal(LastLaunch.AddMinutes(3), crash.WrittenAt);
    }

    [Fact]
    public async Task GetUnreported_CrashLogsOlderThanTheLastLaunchOrOtherLogs_ReturnsNull()
    {
        Write("KittenSpaceAgency.260926-122314.7296.previous-crash.log", LastLaunch.AddMinutes(-1));
        Write("KittenSpaceAgency.260926-122536.43512.log", LastLaunch.AddMinutes(3));
        Write("borea-launch.log", LastLaunch.AddMinutes(3));

        Assert.Null(await _reader.GetUnreportedAsync(_instance, LastLaunch));
    }

    [Fact]
    public async Task GetUnreported_AfterTheCrashWasShown_ReturnsNeitherLogOfItsRun()
    {
        Write("KittenSpaceAgency.260926-122536.43512.20260926_122557.abnormal-exit.log", LastLaunch.AddMinutes(1));
        var shown = await _reader.GetUnreportedAsync(_instance, LastLaunch);

        await _reader.MarkReportedAsync(_instance.InstanceId, shown!);
        Write("KittenSpaceAgency.260926-122536.43512.previous-crash.log", LastLaunch.AddMinutes(5));

        Assert.Null(await _reader.GetUnreportedAsync(_instance, LastLaunch));

        var next = Write("KittenSpaceAgency.260926-123010.5120.previous-crash.log", LastLaunch.AddMinutes(6));
        Assert.Equal(next, (await _reader.GetUnreportedAsync(_instance, LastLaunch))?.LogPath);
    }

    [Fact]
    public async Task GetUnreported_TwoCrashesAfterTheLastLaunch_ReturnsEachOnceThenNull()
    {
        var older = Write("KittenSpaceAgency.260926-122314.7296.20260926_122343.abnormal-exit.log", LastLaunch.AddMinutes(1));
        var newer = Write("KittenSpaceAgency.260926-122536.30280.20260926_122557.abnormal-exit.log", LastLaunch.AddMinutes(3));
        var shown = new List<string?>();

        for (var start = 0; start < 3; start++)
        {
            var crash = await _reader.GetUnreportedAsync(_instance, LastLaunch);
            shown.Add(crash?.LogPath);
            if (crash is not null)
                await _reader.MarkReportedAsync(_instance.InstanceId, crash);
        }

        Assert.Equal([newer, older, null], shown);
    }

    [Fact]
    public async Task MarkReported_RunWhoseLogsAreGone_LeavesTheRecord()
    {
        var gone = Write("KittenSpaceAgency.260926-122314.7296.20260926_122343.abnormal-exit.log", LastLaunch.AddMinutes(1));
        await _reader.MarkReportedAsync(_instance.InstanceId, (await _reader.GetUnreportedAsync(_instance, LastLaunch))!);
        File.Delete(gone);
        Write("KittenSpaceAgency.260926-122536.30280.20260926_122557.abnormal-exit.log", LastLaunch.AddMinutes(3));

        await _reader.MarkReportedAsync(_instance.InstanceId, (await _reader.GetUnreportedAsync(_instance, LastLaunch))!);

        var record = Path.Combine(Path.GetDirectoryName(_paths.GetInstanceGameLogPath(_instance.InstanceId))!, FileGameCrashReader.ReportedFileName);
        Assert.Equal(["KittenSpaceAgency.260926-122536.30280"], File.ReadAllLines(record));
    }

    [Fact]
    public async Task GetUnreported_ExceptionFromAMod_BlamesThatMod()
    {
        var release = MetadataFixtures.MinimalRelease("KSArmory", "0.8.44");
        _instance.AddMod(new InstalledMod("KSArmory", release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release));
        Write(
            "KittenSpaceAgency.260926-122536.43512.previous-crash.log",
            LastLaunch.AddMinutes(1),
            "Unhandled exception System.MissingMethodException: Method not found: 'Void KSA.Vehicle.Stage()'.",
            "   at KSArmory.RoundFollowable.DrawAxes()",
            "   at KSA.Program.Main(String[] inArgs).");

        var crash = await _reader.GetUnreportedAsync(_instance, LastLaunch);

        Assert.Equal("KSArmory", crash?.BlamedModId);
        Assert.Equal("System.MissingMethodException: Method not found: 'Void KSA.Vehicle.Stage()'.", crash?.Exception);
    }

    [Fact]
    public async Task GetUnreported_ExceptionFromAnAssemblyInAModFolderSpelledInOtherCase_BlamesThatMod()
    {
        var release = MetadataFixtures.MinimalRelease("ExampleMod", "1.0.0");
        _instance.AddMod(new InstalledMod("ExampleMod", release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release));
        var folder = Path.Combine(_paths.GetInstanceModsFolder(_instance.InstanceId), "examplemod");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "ExampleHelper.dll"), []);
        Write(
            "KittenSpaceAgency.260926-122536.43512.previous-crash.log",
            LastLaunch.AddMinutes(1),
            "Unhandled exception System.NullReferenceException: Object reference not set to an instance of an object.",
            "   at ExampleHelper.Patches.Apply()",
            "   at KSA.Program.Main(String[] inArgs).");

        var crash = await _reader.GetUnreportedAsync(_instance, LastLaunch);

        Assert.Equal("ExampleMod", crash?.BlamedModId);
    }
}
