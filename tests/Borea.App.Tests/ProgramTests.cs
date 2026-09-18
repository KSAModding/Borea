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
}
