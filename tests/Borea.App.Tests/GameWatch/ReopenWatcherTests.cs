using Borea.App.GameWatch;
using Borea.App.Tests.Links;

namespace Borea.App.Tests.GameWatch;

public sealed class ReopenWatcherTests
{
    [Theory]
    [InlineData("/opt/Borea/borea", "/opt/Borea/borea")]
    [InlineData("C:/Program Files/dotnet/dotnet.exe", null)]
    [InlineData(null, null)]
    public void ProgramPath_PublishedProgramOrDotnetHost_IsTheProgramOrNone(string? processPath, string? expected)
    {
        Assert.Equal(expected, ReopenWatcher.ProgramPath(processPath));
    }

    [Fact]
    public void StartInfo_LoaderNames_StartsTheHiddenCommandWithoutConsoleWindowOrRedirects()
    {
        var program = Path.Combine(Path.GetTempPath(), "Borea", "borea.exe");

        var startInfo = ReopenWatcher.StartInfo(program, ReopenWatcher.Arguments(["StarMap"]));

        Assert.Equal(program, startInfo.FileName);
        Assert.Equal(Path.GetDirectoryName(program), startInfo.WorkingDirectory);
        Assert.True(startInfo.CreateNoWindow);
        Assert.False(startInfo.UseShellExecute);
        Assert.False(startInfo.RedirectStandardInput || startInfo.RedirectStandardOutput || startInfo.RedirectStandardError);
        Assert.Equal(["reopen-after-game", "--process", "StarMap"], startInfo.ArgumentList);
    }

    [Theory]
    [InlineData(true, "The game closed, so Borea opened again.")]
    [InlineData(false, "No game started within 90 seconds, so Borea opened again.")]
    public async Task RunAsync_GameEndedOrNeverStarted_StartsBoreaOnceAfterTheWaitAndLogsOneLine(bool gameEnded, string line)
    {
        var log = new RecordingLog();
        var steps = new List<string>();
        IReadOnlyList<string>? waitedFor = null;

        var exitCode = await ReopenWatcher.RunAsync(
            ["--process", "StarMap"],
            names =>
            {
                waitedFor = names;
                steps.Add("wait");
                return Task.FromResult(gameEnded);
            },
            () => steps.Add("start"),
            log);

        Assert.Equal(0, exitCode);
        Assert.Equal(["StarMap"], waitedFor);
        Assert.Equal(["wait", "start"], steps);
        Assert.Equal(line, Assert.Single(log.Lines));
    }

    [Fact]
    public async Task RunAsync_WatchThrows_StartsBoreaAtOnceAndLogsOneLine()
    {
        var log = new RecordingLog();
        var starts = 0;

        var exitCode = await ReopenWatcher.RunAsync([], _ => throw new InvalidOperationException("No process list."), () => starts++, log);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, starts);
        Assert.Single(log.Lines);
    }
}
