using System.ComponentModel;

namespace Borea.Cli.Tests;

/// <summary>
/// <c>borea game launch</c> against the fake process starter. The host pins
/// the platform to Windows, so the executable is KSA.exe on every test runner.
/// </summary>
public sealed class GameLaunchCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    private string GameDirectory => Path.Combine(_host.Root, "Game");

    private async Task SetGameDirectoryAsync(bool withExecutable = true)
    {
        Directory.CreateDirectory(GameDirectory);
        if (withExecutable)
            File.WriteAllBytes(Path.Combine(GameDirectory, "KSA.exe"), Array.Empty<byte>());

        await _host.RunAsync("settings", "set", "game", GameDirectory);
    }

    [Fact]
    public async Task Launch_StartsTheGameInItsDirectory_WithoutLoaderOrInstance()
    {
        await SetGameDirectoryAsync();

        var run = await _host.RunAsync("game", "launch");

        Assert.Equal(0, run.ExitCode);
        var plan = Assert.Single(_host.ProcessStarter.Plans);
        Assert.Equal(Path.Combine(Path.GetFullPath(GameDirectory), "KSA.exe"), plan.Executable);
        Assert.Equal(Path.GetFullPath(GameDirectory), plan.WorkingDirectory);
        Assert.Empty(plan.Arguments);
        Assert.Empty(plan.EnvironmentVariables);
        Assert.Contains("shared profile, not a Borea instance", run.Output);
        Assert.Contains("Process id: 42", run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task Launch_Json_CarriesTheExecutableAndTheProcess()
    {
        await SetGameDirectoryAsync();

        var run = await _host.RunAsync("game", "launch", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(Path.Combine(Path.GetFullPath(GameDirectory), "KSA.exe"), run.Json.GetProperty("executable").GetString());
        Assert.Equal(Path.GetFullPath(GameDirectory), run.Json.GetProperty("workingDirectory").GetString());
        Assert.Equal(42, run.Json.GetProperty("processId").GetInt32());
    }

    [Fact]
    public async Task Launch_ArgumentsAfterTheSeparator_GoToTheGameAndOptionsBeforeItStillWork()
    {
        await SetGameDirectoryAsync();

        var run = await _host.RunAsync("game", "launch", "--json", "--", "-windowed", "a b", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["-windowed", "a b", "--json"], Assert.Single(_host.ProcessStarter.Plans).Arguments);
        Assert.Equal(["-windowed", "a b", "--json"], run.Json.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()));
    }

    [Fact]
    public async Task Launch_NoGameDirectory_FailsWithoutStarting()
    {
        var run = await _host.RunAsync("game", "launch");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: Borea does not know where the game is installed.", run.Error);
        Assert.Equal(string.Empty, run.Output);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_ExecutableMissing_NamesThePath()
    {
        await SetGameDirectoryAsync(withExecutable: false);

        var run = await _host.RunAsync("game", "launch");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"'{Path.Combine(Path.GetFullPath(GameDirectory), "KSA.exe")}' is not there.", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_StartRefused_Fails()
    {
        await SetGameDirectoryAsync();
        _host.ProcessStarter.Failure = new Win32Exception(5, "Access is denied.");

        var run = await _host.RunAsync("game", "launch");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The system did not start", run.Error);
        Assert.Contains("Access is denied.", run.Error);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public async Task Launch_Json_Failure_WritesNoJson()
    {
        var run = await _host.RunAsync("game", "launch", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
        Assert.Contains("error:", run.Error);
    }

    [Fact]
    public async Task Launch_ExtraArgument_IsBadUsage()
    {
        await SetGameDirectoryAsync();

        var run = await _host.RunAsync("game", "launch", "Flight Test");

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    public void Dispose() => _host.Dispose();
}
