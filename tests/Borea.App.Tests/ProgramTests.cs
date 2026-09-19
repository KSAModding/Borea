using Borea.App.SingleInstance;

namespace Borea.App.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void ChooseStartMode_NoArguments_SharedConsole_OpensTheApp()
    {
        Assert.Equal(StartMode.App, Program.ChooseStartMode([], consoleOwnedAlone: false));
    }

    [Fact]
    public void ChooseStartMode_NoArguments_ConsoleOwnedAlone_FreesTheConsoleFirst()
    {
        Assert.Equal(StartMode.AppWithoutConsole, Program.ChooseStartMode([], consoleOwnedAlone: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChooseStartMode_AnyArgument_RunsTheCommandLine(bool consoleOwnedAlone)
    {
        Assert.Equal(StartMode.Cli, Program.ChooseStartMode(["--help"], consoleOwnedAlone));
        Assert.Equal(StartMode.Cli, Program.ChooseStartMode([""], consoleOwnedAlone));
    }

    [Fact]
    public void ExitCodeWithoutApp_HandedOver_EndsWithZero()
    {
        var log = new List<string>();
        var shown = new List<string>();

        Assert.Equal(0, Program.ExitCodeWithoutApp(new ElectionResult(null, 42, null), log.Add, shown.Add));
        Assert.Contains("process 42", Assert.Single(log), StringComparison.Ordinal);
        Assert.Empty(shown);
    }

    [Fact]
    public void ExitCodeWithoutApp_Failed_ShowsTheReason_AndEndsWithThree()
    {
        var log = new List<string>();
        var shown = new List<string>();

        Assert.Equal(3, Program.ExitCodeWithoutApp(new ElectionResult(null, null, "No running Borea App answered."), log.Add, shown.Add));
        Assert.Contains("No running Borea App answered.", Assert.Single(shown), StringComparison.Ordinal);
        Assert.Equal(shown, log);
    }

    [Fact]
    public async Task ExitCodeWithoutApp_Primary_OpensTheApp()
    {
        var lockPath = Path.Combine(Path.GetTempPath(), "BoreaProgram_" + Guid.NewGuid(), "app.lock");
        try
        {
            var election = await AppElection.RunAsync(lockPath, [], _ => true, _ => { }, HandoverTimeouts.Default);
            using var primary = election.Primary;

            Assert.Null(Program.ExitCodeWithoutApp(election, _ => { }, _ => { }));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(lockPath)!, recursive: true);
        }
    }
}
