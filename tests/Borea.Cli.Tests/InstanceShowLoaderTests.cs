using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Instances;

namespace Borea.Cli.Tests;

/// <summary><c>borea instance show</c> names the mod loader the mods need (#445).</summary>
public sealed class InstanceShowLoaderTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Show_ModsNeedALoader_PrintsTheRangeTheStateAndWhoNeedsIt()
    {
        await SaveInstanceAsync(
            NeedsLoader("flight-tools", "StarMap", "0.4.0"),
            NeedsLoader("orbit-tools", "starmap", "0.4.5", max: "0.9.0"),
            ContentCommandFixtures.Release("parts-pack"));

        var human = await _host.RunAsync("instance", "show", "Flight Test");
        var json = await _host.RunAsync("instance", "show", "Flight Test", "--json");

        Assert.Equal(0, human.ExitCode);
        Assert.Contains("Mod loader: StarMap 0.4.5 to 0.9.0, not installed, needed by flight-tools, orbit-tools", human.Output);
        var loader = Assert.Single(json.Json.GetProperty("modLoaders").EnumerateArray());
        Assert.Equal("StarMap", loader.GetProperty("id").GetString());
        Assert.Equal("0.4.5", loader.GetProperty("minVersion").GetString());
        Assert.Equal("0.9.0", loader.GetProperty("maxVersion").GetString());
        Assert.False(loader.GetProperty("installed").GetBoolean());
        Assert.False(loader.GetProperty("conflict").GetBoolean());
    }

    [Fact]
    public async Task Show_NoModNeedsALoader_SaysSo()
    {
        await SaveInstanceAsync(ContentCommandFixtures.Release("parts-pack"));

        var human = await _host.RunAsync("instance", "show", "Flight Test");
        var json = await _host.RunAsync("instance", "show", "Flight Test", "--json");

        Assert.Contains("Mod loader: none needed by the mods", human.Output);
        Assert.Empty(json.Json.GetProperty("modLoaders").EnumerateArray());
    }

    [Fact]
    public async Task Show_ModsNeedDifferentLoaders_ListsBoth()
    {
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap", "0.4.0"), NeedsLoader("orbit-tools", "OtherLoader", "1.0.0"));

        var human = await _host.RunAsync("instance", "show", "Flight Test");

        Assert.Contains("OtherLoader 1.0.0 or newer", human.Output);
        Assert.Contains("StarMap 0.4.0 or newer", human.Output);
        Assert.Contains("one launch can only start one of them", human.Output);
    }

    private async Task SaveInstanceAsync(params ModVersionMetadata[] releases)
    {
        var mods = releases
            .Select(release => new InstalledMod(release.ModId, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release))
            .ToList();
        var instance = Instance.FromExisting(Guid.NewGuid(), "Flight Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, mods, isFavorite: false);
        await new FileInstanceRepository(_host.Paths).SaveAsync(instance);
    }

    private static ModVersionMetadata NeedsLoader(string id, string loaderId, string min, string? max = null) =>
        ContentCommandFixtures.Release(id, loader: new LoaderRequirement(loaderId, ModVersion.Parse(min), max is null ? null : ModVersion.Parse(max)));

    public void Dispose() => _host.Dispose();
}
