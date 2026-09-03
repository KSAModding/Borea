namespace Borea.Cli.Tests;

public sealed class BoreaCliTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Help_ExitsZero_AndListsTheCommands()
    {
        var run = await _host.RunAsync("--help");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("settings", run.Output);
        Assert.Contains("game", run.Output);
        Assert.Contains("instance", run.Output);
        Assert.Contains("enable", run.Output);
        Assert.Contains("disable", run.Output);
    }

    [Fact]
    public async Task NoArguments_IsAUsageError()
    {
        var run = await _host.RunAsync();

        Assert.Equal(2, run.ExitCode);
    }

    [Fact]
    public async Task UnknownCommand_IsAUsageError_ThatNamesTheToken()
    {
        var run = await _host.RunAsync("frobnicate");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("frobnicate", run.Error);
    }

    [Fact]
    public async Task ParseError_BuildsNoServices()
    {
        await _host.RunAsync("frobnicate");
        await _host.RunAsync("--help");

        Assert.Equal(0, _host.Builds);
    }

    [Fact]
    public async Task Command_BuildsTheServicesOnce()
    {
        await _host.RunAsync("settings", "show");

        Assert.Equal(1, _host.Builds);
    }

    [Fact]
    public void Build_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BoreaCli.Build(null!));
    }

    public void Dispose() => _host.Dispose();
}
