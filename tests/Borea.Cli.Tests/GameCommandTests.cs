using System.Text.Json;
using Borea.Core.Game;

namespace Borea.Cli.Tests;

public sealed class GameCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Version_NoGameDirectory_SaysUnknown_AndPrintsTheLatest()
    {
        var run = await _host.RunAsync("game", "version");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Installed build: unknown (no game directory is set", run.Output);
        Assert.Contains("Latest public build: 2026.9.7.5402", run.Output);
        Assert.Contains(FakeLatestVersionPing.DownloadUrl, run.Output);
        Assert.Equal(string.Empty, run.Error);
    }

    [Fact]
    public async Task Version_GameDirectoryWithoutTheAssembly_SaysUnknown_NamingTheDirectory()
    {
        await _host.RunAsync("settings", "set", "game", _host.Root);

        var run = await _host.RunAsync("game", "version");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains($"Installed build: unknown (no KSA.dll with a version was found in {_host.Root})", run.Output);
    }

    [Fact]
    public async Task Version_PrintsBothBuilds_AndSaysANewerOneIsAvailable()
    {
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.8.22.5348"), "2026.8.22.5348") };

        var run = await _host.RunAsync("game", "version");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Installed build: 2026.8.22.5348", run.Output);
        Assert.Contains("Latest public build: 2026.9.7.5402", run.Output);
        Assert.Contains("A newer build is available.", run.Output);
    }

    [Fact]
    public async Task Version_InstalledIsTheLatest_SaysItIsCurrent()
    {
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.9.7.5402"), "2026.9.7.5402") };

        var run = await _host.RunAsync("game", "version");

        Assert.Contains("The installed build is current.", run.Output);
    }

    [Fact]
    public async Task Version_Json_CarriesBothHalves()
    {
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.8.22.5348"), "2026.8.22.5348") };

        var run = await _host.RunAsync("game", "version", "--json");

        Assert.Equal(0, run.ExitCode);
        var installed = run.Json.GetProperty("installed");
        Assert.Equal("2026.8.22.5348", installed.GetProperty("version").GetString());
        Assert.Equal(5348, installed.GetProperty("revision").GetInt32());
        Assert.Equal("2026.8.22.5348", installed.GetProperty("raw").GetString());
        var latest = run.Json.GetProperty("latest");
        Assert.Equal("2026.9.7.5402", latest.GetProperty("version").GetString());
        Assert.Equal(5402, latest.GetProperty("revision").GetInt32());
        Assert.Equal("2026.9.7.5402", latest.GetProperty("raw").GetString());
        Assert.Equal(FakeLatestVersionPing.DownloadUrl, latest.GetProperty("downloadUrl").GetString());
    }

    [Fact]
    public async Task Version_Json_NoGameDirectory_HasNullInstalled()
    {
        var run = await _host.RunAsync("game", "version", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(JsonValueKind.Null, run.Json.GetProperty("installed").ValueKind);
        Assert.Equal("2026.9.7.5402", run.Json.GetProperty("latest").GetProperty("version").GetString());
    }

    [Fact]
    public async Task Version_InstalledThatDoesNotParse_KeepsTheRawString()
    {
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(null, "1.0.0.0") };

        var human = await _host.RunAsync("game", "version");
        var json = await _host.RunAsync("game", "version", "--json");

        Assert.Contains("Installed build: 1.0.0.0 (this did not parse as a game version)", human.Output);
        Assert.DoesNotContain("newer build", human.Output);
        var installed = json.Json.GetProperty("installed");
        Assert.Equal(JsonValueKind.Null, installed.GetProperty("version").ValueKind);
        Assert.Equal("1.0.0.0", installed.GetProperty("raw").GetString());
    }

    [Fact]
    public async Task Version_AnswerThatDoesNotParse_KeepsTheRawString()
    {
        _host.LatestVersion.Answer = new LatestVersionInfo(null, "weird", FakeLatestVersionPing.DownloadUrl);

        var human = await _host.RunAsync("game", "version");
        var json = await _host.RunAsync("game", "version", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("Latest public build: weird (this did not parse as a game version)", human.Output);
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
    public async Task Version_HostUnreachable_WithAnInstalledBuild_WarnsAndStillAnswers()
    {
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.9.7.5402"), "2026.9.7.5402") };
        _host.LatestVersion.Failure = new HttpRequestException("No such host is known.");

        var human = await _host.RunAsync("game", "version");
        var json = await _host.RunAsync("game", "version", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("Installed build: 2026.9.7.5402", human.Output);
        Assert.DoesNotContain("Latest public build", human.Output);
        Assert.Contains("warning: The master server could not be reached. No such host is known.", human.Error);
        Assert.Equal(0, json.ExitCode);
        Assert.Equal(JsonValueKind.Null, json.Json.GetProperty("latest").ValueKind);
        Assert.Equal("2026.9.7.5402", json.Json.GetProperty("installed").GetProperty("version").GetString());
    }

    [Fact]
    public async Task Version_HostUnreachable_NoInstalledBuild_Fails_WithBothReasons()
    {
        _host.LatestVersion.Failure = new HttpRequestException("No such host is known.");

        var run = await _host.RunAsync("game", "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("no game directory is set", run.Error);
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
    public async Task Version_NoAnswer_NoInstalledBuild_Fails()
    {
        _host.LatestVersion.Answer = new LatestVersionInfo(null, string.Empty, string.Empty);

        var run = await _host.RunAsync("game", "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The master server gave no version.", run.Error);
    }

    [Fact]
    public async Task Version_Timeout_NoInstalledBuild_Fails_WithTheReason()
    {
        // HttpClient reports its own timeout as a cancellation the caller did not ask for.
        _host.LatestVersion.Failure = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");

        var run = await _host.RunAsync("game", "version");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("did not answer in time", run.Error);
        Assert.Contains("HttpClient.Timeout", run.Error);
    }

    public void Dispose() => _host.Dispose();
}
