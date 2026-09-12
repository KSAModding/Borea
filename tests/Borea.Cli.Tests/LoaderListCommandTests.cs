using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;

namespace Borea.Cli.Tests;

public sealed class LoaderListCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task List_PrintsLoaderIdsAndExcludesOrdinaryMods()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing("StarMap"));
        _host.Mods.Listings.Add(ModListing("AdvancedFlightComputer"));
        _host.Mods.Listings.Add(LoaderFixtures.Listing("OtherLoader"));

        var run = await _host.RunAsync("loader", "list");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($"StarMap{Environment.NewLine}OtherLoader{Environment.NewLine}", run.Output);
        Assert.Empty(run.Error);
    }

    [Theory]
    [InlineData("--versions")]
    [InlineData("-v")]
    public async Task List_VersionsFlagPrintsDescendingUniqueNonYankedVersions(string flag)
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.5"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.7"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.6"));
        _host.Mods.Releases.Add(LoaderFixtures.Release(version: "0.4.8", yanked: true));
        _host.Mods.AvailableVersions = (_, _) => Task.FromResult<IReadOnlyList<ModVersion>>(
            [
                ModVersion.Parse("0.4.5"),
                ModVersion.Parse("0.4.7"),
                ModVersion.Parse("0.4.6"),
                ModVersion.Parse("0.4.7"),
                ModVersion.Parse("0.4.8"),
            ]);

        var run = await _host.RunAsync("loader", "list", flag);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            $"StarMap{Environment.NewLine}  0.4.7{Environment.NewLine}  0.4.6{Environment.NewLine}  0.4.5{Environment.NewLine}",
            run.Output);
        Assert.Empty(run.Error);
    }

    [Fact]
    public async Task List_VersionsWithNoReleases_PrintsOnlyTheLoaderId()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.AvailableVersions = (_, _) => Task.FromResult<IReadOnlyList<ModVersion>>(
            [ModVersion.Parse("0.4.6")]);

        var run = await _host.RunAsync("loader", "list", "--versions");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($"StarMap{Environment.NewLine}", run.Output);
        Assert.Empty(run.Error);
    }

    [Fact]
    public async Task List_EmptyCatalogue_ReportsNoAvailableLoaders()
    {
        var run = await _host.RunAsync("loader", "list");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($"No mod loaders available{Environment.NewLine}", run.Output);
        Assert.Empty(run.Error);
    }

    [Fact]
    public async Task List_RepositoryFailure_IsReportedAsAnOperationFailure()
    {
        _host.Mods.AvailableMods = _ => throw new IOException("Catalogue is unavailable.");

        var run = await _host.RunAsync("loader", "list");

        Assert.Equal(1, run.ExitCode);
        Assert.Empty(run.Output);
        Assert.Contains("error: Catalogue is unavailable.", run.Error);
    }

    public void Dispose() => _host.Dispose();

    private static ModMetadata ModListing(string id) => new(
        specVersion: 1,
        modId: id,
        source: "index",
        name: id,
        authors: new[] { "Mod author" },
        abstractText: "A mod.",
        license: "MIT",
        links: new Dictionary<string, string> { ["forums"] = "https://example.invalid/forums" },
        gameMin: "2026.9.7.5402",
        type: ContentType.Mod,
        install: new InstallDescriptor(target: InstallAnchor.Mods));
}
