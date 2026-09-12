using System.Text.Json;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Cli.Tests;

public sealed class LoaderCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Install_UsesTheNewestReleaseAndRequestedDirectory()
    {
        var installer = new FakeLoaderInstaller();
        var directory = Path.Combine(_host.Root, "Loader");
        _host.LoaderInstaller = installer;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.5"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.6"));

        var run = await _host.RunAsync("loader", "install", "StarMap", "--directory", directory);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("0.4.6", Assert.Single(installer.Calls).Release.Version.ToString());
        Assert.Equal(directory, installer.Calls[0].Directory);
        Assert.Contains("Installed StarMap 0.4.6", run.Output);
    }

    [Fact]
    public async Task Install_RequestedReleaseThatDoesNotExist_Fails()
    {
        _host.LoaderInstaller = new FakeLoaderInstaller();
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release());

        var run = await _host.RunAsync("loader", "install", "StarMap", "--version", "0.4.5");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("has no release '0.4.5'", run.Error);
    }

    [Fact]
    public async Task Install_InvalidVersion_IsAUsageError()
    {
        var run = await _host.RunAsync("loader", "install", "StarMap", "--version", "latest");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("not a valid semantic version", run.Error);
        Assert.Equal(0, _host.Builds);
    }

    [Fact]
    public async Task Install_ServiceFailure_IsReportedAsAnOperationFailure()
    {
        _host.LoaderInstaller = new FakeLoaderInstaller { Failure = new InvalidOperationException("Destination has foreign files.") };
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release());

        var run = await _host.RunAsync("loader", "install", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Destination has foreign files", run.Error);
    }

    [Fact]
    public async Task Install_ExplicitYankedRelease_FailsBeforeTheInstallerRuns()
    {
        var installer = new FakeLoaderInstaller();
        _host.LoaderInstaller = installer;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(yanked: true));

        var run = await _host.RunAsync("loader", "install", "StarMap", "--version", "0.4.6");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("is yanked and cannot be installed", run.Error);
        Assert.Empty(installer.Calls);
    }

    [Fact]
    public async Task Adopt_MissingLaunchFile_FailsWithoutARecord()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_host.Root, "Loader")).FullName;
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release());

        var run = await _host.RunAsync("loader", "adopt", "StarMap", directory);
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("does not hold the listed launch file", run.Error);
        Assert.Empty(show.Json.GetProperty("loaderDirectories").EnumerateObject());
    }

    [Fact]
    public async Task Adopt_UnreadableVersion_RecordsTheLoaderAndWarns()
    {
        var directory = CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release());

        var run = await _host.RunAsync("loader", "adopt", "StarMap", directory);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Loader version: unknown", run.Output);
        Assert.Contains("could not read a file version", run.Error);
    }

    [Fact]
    public async Task Adopt_MismatchedGamePath_RecordsTheLoaderAndWarnsWithoutChangingIt()
    {
        var managedGame = Directory.CreateDirectory(Path.Combine(_host.Root, "ManagedGame")).FullName;
        var otherGame = Directory.CreateDirectory(Path.Combine(_host.Root, "OtherGame")).FullName;
        var directory = CreateLoaderDirectory("StarMap", "not a program", _host.Root, otherGame);
        _host.Mods.Listings.Add(LoaderFixtures.Listing());

        await _host.RunAsync("settings", "set", "game", managedGame);
        var run = await _host.RunAsync("loader", "adopt", "StarMap", directory);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("not the game directory Borea manages", run.Error);
        using var configuration = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "StarMapConfig.json")));
        Assert.Equal(otherGame, configuration.RootElement.GetProperty("GameLocation").GetString());
    }

    [Fact]
    public async Task Uninstall_ReportsWhetherTheDirectoryWasRemoved()
    {
        var uninstaller = new FakeLoaderUninstaller
        {
            Result = new LoaderUninstallResult("StarMap", "C:\\Loaders\\StarMap", RecordRemoved: true, DirectoryRemoved: false),
        };
        _host.LoaderUninstaller = uninstaller;

        var run = await _host.RunAsync("loader", "uninstall", "StarMap");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("StarMap", Assert.Single(uninstaller.LoaderIds));
        Assert.Contains("remains", run.Output);
    }

    internal static string CreateLoaderDirectory(string loaderId, string executableContents, string root, string? gameDirectory = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, loaderId)).FullName;
        File.WriteAllText(Path.Combine(directory, "StarMap.exe"), executableContents);
        File.WriteAllText(
            Path.Combine(directory, "StarMapConfig.json"),
            $$"""
            {
              "GameLocation": "{{(gameDirectory ?? Path.Combine(root, "Game")).Replace("\\", "\\\\")}}"
            }
            """);
        return directory;
    }

    public void Dispose() => _host.Dispose();

    private sealed class FakeLoaderInstaller : ILoaderInstaller
    {
        public List<(ModMetadata Listing, ModVersionMetadata Release, string? Directory)> Calls { get; } = new();

        public Exception? Failure { get; init; }

        public Task<LoaderInstallResult> InstallAsync(
            ModMetadata loader,
            ModVersionMetadata release,
            string? directory = null,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                throw Failure;

            Calls.Add((loader, release, directory));
            return Task.FromResult(new LoaderInstallResult(
                loader.ModId,
                release.Version,
                directory ?? "C:\\Loaders\\StarMap",
                new DownloadResult(release.Download.Url, 100, "ABC"),
                "StarMapConfig.json",
                Replaced: false));
        }
    }

    private sealed class FakeLoaderUninstaller : ILoaderUninstaller
    {
        public List<string> LoaderIds { get; } = new();

        public required LoaderUninstallResult Result { get; init; }

        public Task<LoaderUninstallResult> UninstallAsync(string loaderId, CancellationToken cancellationToken = default)
        {
            LoaderIds.Add(loaderId);
            return Task.FromResult(Result);
        }
    }
}
