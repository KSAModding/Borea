namespace Borea.Cli.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void ShowsStartHint_NoArguments_ConsoleOwnedAlone()
    {
        Assert.True(Program.ShowsStartHint([], consoleOwnedAlone: true, inputRedirected: false, outputRedirected: false));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void ShowsStartHint_NoArguments_SharedConsoleOrRedirected_RunsTheParser(bool consoleOwnedAlone, bool inputRedirected, bool outputRedirected)
    {
        Assert.False(Program.ShowsStartHint([], consoleOwnedAlone, inputRedirected, outputRedirected));
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
