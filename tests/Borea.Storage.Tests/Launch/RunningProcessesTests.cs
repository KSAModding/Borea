using Borea.Core.Game;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Launch;

namespace Borea.Storage.Tests.Launch;

public sealed class RunningProcessesTests
{
    [Fact]
    public void ALoaderProcessNamedAfterTheLaunchFileCountsAsTheGameRunning()
    {
        var names = new LoaderProcessNames(
            new Listings(Loader("Oxidizer", "bin/oxidizer.exe"), Loader("Runner", "Runner.dll", "dotnet")),
            new Versions("Oxidizer", "Runner"),
            OsPlatform.Windows);

        Assert.Equal(["oxidizer"], names.Current());
        Assert.True(RunningProcesses.IsGameRunning(names.Current, name => name == "oxidizer"));
    }

    [Fact]
    public void NoInstalledLoaderMeansNoLoaderProcessCounts()
    {
        var names = new LoaderProcessNames(new Listings(Loader("Oxidizer", "oxidizer.exe")), new Versions(), OsPlatform.Windows);

        Assert.Empty(names.Current());
        Assert.False(RunningProcesses.IsGameRunning(names.Current, name => name == "oxidizer"));
    }

    [Fact]
    public void AKnownLoaderProcessCountsWithoutAnInstalledLoader()
    {
        var names = new LoaderProcessNames(new Listings(Loader("Oxidizer", "oxidizer.exe")), new Versions(), OsPlatform.Windows);

        Assert.Empty(names.Current());
        Assert.True(RunningProcesses.IsGameRunning(names.Current, name => name == "StarMap"));
    }

    [Fact]
    public void AFailingReadCountsNoLoaderProcess()
    {
        var failing = new LoaderProcessNames(new Listings(), new FailingVersions(), OsPlatform.Windows);

        Assert.Empty(failing.Current());
        Assert.False(RunningProcesses.IsGameRunning(failing.Current, name => name == "oxidizer"));
        Assert.True(RunningProcesses.IsGameRunning(failing.Current, name => name == "StarMap"));
    }

    private static ModMetadata Loader(string id, string launch, string? runtime = null) => new(
        specVersion: 1,
        modId: id,
        source: "index",
        name: id,
        authors: ["Author"],
        abstractText: "A loader.",
        license: "MIT",
        links: new Dictionary<string, string> { ["forums"] = "https://forums.ahwoo.com/threads/loader.1/" },
        gameMin: "2026.8.3.5117",
        type: ContentType.ModLoader,
        install: new InstallDescriptor(target: InstallAnchor.Standalone),
        provides: new LoaderProvides(
            launch,
            platforms: runtime is null ? null : new Dictionary<OsPlatform, LoaderPlatformLaunch>
            {
                [OsPlatform.Windows] = new(launch, runtime),
            }));

    private sealed class Versions(params string[] loaderIds) : ILoaderVersionStore
    {
        public Task<IReadOnlyList<InstalledLoaderVersion>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InstalledLoaderVersion>>(
                loaderIds.Select(id => new InstalledLoaderVersion(id, null, null, "C:/Loaders/" + id, false, true)).ToList());
    }

    private sealed class FailingVersions : ILoaderVersionStore
    {
        public Task<IReadOnlyList<InstalledLoaderVersion>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new FormatException("broken");
    }

    private sealed class Listings(params ModMetadata[] listings) : IModRepository
    {
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>(listings);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(null);

        public Task<ModVersionMetadata?> GetReleaseAsync(string modId, ModVersion version, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(null);

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(string modId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModVersion>>([]);

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }
}
