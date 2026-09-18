namespace Borea.Cli.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void ShowsStartHint_NoArguments_ConsoleOwnedAlone()
    {
        Assert.True(Program.ShowsStartHint([], consoleOwnedAlone: true, inputRedirected: false, outputRedirected: false));
    }

    [Fact]
    public void ShowsStartHint_NoArguments_SharedConsole_RunsTheParser()
    {
        Assert.False(Program.ShowsStartHint([], consoleOwnedAlone: false, inputRedirected: false, outputRedirected: false));
    }

    [Fact]
    public void ShowsStartHint_NoArguments_InputRedirected_RunsTheParser()
    {
        Assert.False(Program.ShowsStartHint([], consoleOwnedAlone: true, inputRedirected: true, outputRedirected: false));
    }

    [Fact]
    public void ShowsStartHint_NoArguments_OutputRedirected_RunsTheParser()
    {
        Assert.False(Program.ShowsStartHint([], consoleOwnedAlone: true, inputRedirected: false, outputRedirected: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShowsStartHint_Arguments_RunsTheCommand(bool consoleOwnedAlone)
    {
        Assert.False(Program.ShowsStartHint(["--help"], consoleOwnedAlone, inputRedirected: false, outputRedirected: false));
    }

    [Fact]
    public void StartHint_NamesTheHelpCommandAndTheCliArchivePrefix()
    {
        Assert.Contains("borea --help", Program.StartHint, StringComparison.Ordinal);
        Assert.Contains("Borea-Cli-", Program.StartHint, StringComparison.Ordinal);
    }
}
