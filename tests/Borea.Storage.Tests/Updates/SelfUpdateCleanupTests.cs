using System.Diagnostics;
using Borea.Core.Updates;
using Borea.Storage.Updates;

namespace Borea.Storage.Tests.Updates;

public sealed class SelfUpdateCleanupTests : IDisposable
{
    private const string Token = "0123456789ABCDEF0123456789ABCDEF";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly string _running;
    private readonly string _previous;

    public SelfUpdateCleanupTests()
    {
        _running = Build("Borea");
        _previous = SelfUpdateCleanup.ReplacedPath(_running);
        File.WriteAllText(_previous, "the build that was replaced");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>A folder with the three files a release archive holds, and the path of the program in it.</summary>
    private string Build(string folderName, string programName = "borea.exe")
    {
        var folder = Path.Combine(_root, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, programName), folderName);
        File.WriteAllText(Path.Combine(folder, "LICENSE"), "MIT");
        File.WriteAllText(Path.Combine(folder, "THIRD-PARTY-NOTICES.txt"), "Avalonia");
        return Path.Combine(folder, programName);
    }

    /// <summary>A handover of a process number that no process has, with the receipt the staging build left.</summary>
    private SelfUpdateHandover Ended(string previous, string? running = null)
    {
        var handover = new SelfUpdateHandover(previous, int.MaxValue, Token);
        SelfUpdateReceipt.Write(Path.GetDirectoryName(running ?? _running)!, previous, Token);
        return handover;
    }

    /// <summary>The shell of this system, which is a real program that ends by itself after about a second.</summary>
    // A program started directly keeps its own name. /bin/sh on macOS starts bash in its place,
    // which would not carry the name the cleanup checks before it waits.
    private static (string FileName, string Program, string[] Arguments) LongRunning()
        => OperatingSystem.IsWindows()
            ? ("cmd.exe", "cmd.exe", ["/c", "ping", "-n", "2", "127.0.0.1"])
            : ("sleep", "/bin/sleep", ["1"]);

    [Fact]
    public void Run_RemovesTheProgramFileThisBuildTookThePlaceOf()
    {
        var message = SelfUpdateCleanup.Run(Ended(_previous), _running);

        Assert.False(File.Exists(_previous));
        Assert.True(File.Exists(_running));
        Assert.Contains("removed", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_KeepsEverythingElseInTheFolderBoreaRunsIn()
    {
        var folder = Path.GetDirectoryName(_running)!;
        File.WriteAllText(Path.Combine(folder, "my-notes.txt"), "keep me");

        SelfUpdateCleanup.Run(Ended(_previous), _running);

        Assert.Equal(["LICENSE", "THIRD-PARTY-NOTICES.txt", "borea.exe", "my-notes.txt"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Run_TakesTheReceiptWithIt_SoTheSameHandoverWorksOnlyOnce()
    {
        var handover = Ended(_previous);

        SelfUpdateCleanup.Run(handover, _running);
        var again = SelfUpdateCleanup.Run(handover, _running);

        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_running)!, SelfUpdateReceipt.FileName)));
        Assert.Contains("left alone", again, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_TheProgramThisBuildRunsFrom_IsLeftAlone()
    {
        var message = SelfUpdateCleanup.Run(Ended(_running), _running);

        Assert.True(File.Exists(_running));
        Assert.Contains("left alone", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_AProgramWithAnotherName_IsLeftAlone()
    {
        var other = Path.Combine(Path.GetDirectoryName(_running)!, "something-else.exe.old");
        File.WriteAllText(other, "not Borea");

        var message = SelfUpdateCleanup.Run(Ended(other), _running);

        Assert.True(File.Exists(other));
        Assert.Contains("left alone", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_AProgramThisBuildHasNoReceiptFor_IsLeftAlone()
    {
        var message = SelfUpdateCleanup.Run(new SelfUpdateHandover(_previous, int.MaxValue, Token), _running);

        Assert.True(File.Exists(_previous));
        Assert.Contains("no receipt", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_AnotherTokenThanTheReceipt_IsLeftAlone()
    {
        Ended(_previous);

        var message = SelfUpdateCleanup.Run(new SelfUpdateHandover(_previous, int.MaxValue, "FFFF"), _running);

        Assert.True(File.Exists(_previous));
        Assert.Contains("no receipt", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_AProgramInAnotherFolder_IsLeftAlone()
    {
        var elsewhere = SelfUpdateCleanup.ReplacedPath(Build("Borea-0.1.0-win-x64"));
        File.WriteAllText(elsewhere, "another Borea");
        SelfUpdateReceipt.Write(Path.GetDirectoryName(_running)!, elsewhere, Token);

        var message = SelfUpdateCleanup.Run(new SelfUpdateHandover(elsewhere, int.MaxValue, Token), _running);

        Assert.True(File.Exists(elsewhere));
        Assert.Contains("took the place of", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_AReplacedProgramThatIsAlreadyGone_SaysSoAndDoesNotThrow()
    {
        var handover = Ended(_previous);
        File.Delete(_previous);

        var message = SelfUpdateCleanup.Run(handover, _running);

        Assert.Contains("removed", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_AReplacedBuildThatStillRuns_WaitsForItAndRemovesItAfterwards()
    {
        var (fileName, program, arguments) = LongRunning();
        var running = Build("Borea-in-use", fileName);
        var previous = SelfUpdateCleanup.ReplacedPath(running);
        File.WriteAllText(previous, "the build that was replaced");
        using var child = Start(program, arguments);
        var handover = new SelfUpdateHandover(previous, child.Id, Token);
        SelfUpdateReceipt.Write(Path.GetDirectoryName(running)!, previous, Token);

        var message = SelfUpdateCleanup.Run(handover, running);

        Assert.True(child.HasExited);
        Assert.False(File.Exists(previous));
        Assert.Contains("removed", message, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitForPreviousExit_ARunningProcess_WaitsForItAndDeletesNothing()
    {
        var (fileName, program, arguments) = LongRunning();
        var running = Build("Borea-still-starting", fileName);
        var previous = SelfUpdateCleanup.ReplacedPath(running);
        File.WriteAllText(previous, "the build that was replaced");
        using var child = Start(program, arguments);
        var handover = new SelfUpdateHandover(previous, child.Id, Token);
        SelfUpdateReceipt.Write(Path.GetDirectoryName(running)!, previous, Token);

        SelfUpdateCleanup.WaitForPreviousExit(handover, running);

        Assert.True(child.HasExited);
        Assert.True(File.Exists(previous));
    }

    private static Process Start(string program, string[] arguments)
    {
        var startInfo = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"No process was started for '{program}'.");
    }
}
