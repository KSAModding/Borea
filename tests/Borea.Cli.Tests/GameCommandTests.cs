using System.Text.Json;
using Borea.Core.Game;

namespace Borea.Cli.Tests;

public sealed class GameCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Version_PrintsTheMasterServerAnswer()
    {
        var run = await _host.RunAsync("game", "version");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Latest public build: 2026.9.7.5402", run.Output);
        Assert.Contains(FakeLatestVersionPing.DownloadUrl, run.Output);
    }

    [Fact]
    public async Task Version_Json_CarriesTheVersionItsRevisionAndTheUrl()
    {
        var run = await _host.RunAsync("game", "version", "--json");

        Assert.Equal(0, run.ExitCode);
        var latest = run.Json.GetProperty("latest");
        Assert.Equal("2026.9.7.5402", latest.GetProperty("version").GetString());
        Assert.Equal(5402, latest.GetProperty("revision").GetInt32());
        Assert.Equal("2026.9.7.5402", latest.GetProperty("raw").GetString());
        Assert.Equal(FakeLatestVersionPing.DownloadUrl, latest.GetProperty("downloadUrl").GetString());
    }

    [Fact]
    public async Task Version_AnswerThatDoesNotParse_KeepsTheRawString()
    {
        _host.LatestVersion.Answer = new LatestVersionInfo(null, "weird", FakeLatestVersionPing.DownloadUrl);

        var human = await _host.RunAsync("game", "version");
        var json = await _host.RunAsync("game", "version", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("weird", human.Output);
        Assert.Contains("did not parse", human.Output);
        var latest = json.Json.GetProperty("latest");
        Assert.Equal(JsonValueKind.Null, latest.GetProperty("version").ValueKind);
        Assert.Equal(JsonValueKind.Null, latest.GetProperty("revision").ValueKind);
        Assert.Equal("weird", latest.GetProperty("raw").GetString());
    }

    [Fact]
    public async Task Version_AnswerWithoutADownloadPage_LeavesItOut()
    {
        _host.LatestVersion.Answer = new LatestVersionInfo(GameVersion.Parse("2026.9.7.5402"), "2026.9.7.5402", string.Empty);

        var human = await _host.RunAsync("game", "version");
        var json = await _host.RunAsync("game", "version", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.DoesNotContain("Download:", human.Output);
        Assert.Equal(JsonValueKind.Null, json.Json.GetProperty("latest").GetProperty("downloadUrl").ValueKind);
    }

    [Fact]
    public async Task Version_NoAnswer_Fails()
    {
        _host.LatestVersion.Answer = new LatestVersionInfo(null, string.Empty, string.Empty);

        var run = await _host.RunAsync("game", "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("no version", run.Error);
    }

    [Fact]
    public async Task Version_HostUnreachable_Fails_WithTheReason()
    {
        _host.LatestVersion.Failure = new HttpRequestException("No such host is known.");

        var run = await _host.RunAsync("game", "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No such host is known.", run.Error);
    }

    [Fact]
    public async Task Version_HostUnreachable_Json_LeavesStdoutEmpty()
    {
        _host.LatestVersion.Failure = new HttpRequestException("No such host is known.");

        var run = await _host.RunAsync("game", "version", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(string.Empty, run.Output);
    }

    [Fact]
    public async Task Version_Timeout_Fails_WithTheReason()
    {
        // HttpClient reports its own timeout as a cancellation the caller did not ask for.
        _host.LatestVersion.Failure = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");

        var run = await _host.RunAsync("game", "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("HttpClient.Timeout", run.Error);
    }

    public void Dispose() => _host.Dispose();
}
