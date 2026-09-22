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

    [Theory]
    [InlineData("0.4.7", "StarMap 0.4.5 or newer, 0.4.7 installed, needed by flight-tools", true)]
    [InlineData("0.4.3", "StarMap 0.4.5 or newer, 0.4.3 installed, outside the range, needed by flight-tools", false)]
    public async Task Show_LoaderIsInstalled_PrintsTheVersionAndWhetherTheModsAcceptIt(string installed, string line, bool accepted)
    {
        await RecordStarMapAsync(installed);
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap", "0.4.5"));

        var human = await _host.RunAsync("instance", "show", "Flight Test");
        var json = await _host.RunAsync("instance", "show", "Flight Test", "--json");

        Assert.Contains($"Mod loader: {line}", human.Output);
        var loader = Assert.Single(json.Json.GetProperty("modLoaders").EnumerateArray());
        Assert.True(loader.GetProperty("installed").GetBoolean());
        Assert.Equal(installed, loader.GetProperty("installedVersion").GetString());
        Assert.Equal(accepted, loader.GetProperty("accepted").GetBoolean());
    }

    [Fact]
    public async Task Show_LoaderInstalledWithoutAKnownVersion_SaysSo()
    {
        await RecordStarMapAsync(version: null);
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap", "0.4.5"));

        var human = await _host.RunAsync("instance", "show", "Flight Test");
        var json = await _host.RunAsync("instance", "show", "Flight Test", "--json");

        Assert.Contains("Mod loader: StarMap 0.4.5 or newer, installed, version unknown", human.Output);
        var loader = Assert.Single(json.Json.GetProperty("modLoaders").EnumerateArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, loader.GetProperty("installedVersion").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, loader.GetProperty("accepted").ValueKind);
    }

    [Fact]
    public async Task Show_ModsAskForVersionsThatDoNotOverlap_NamesWhatEachAsksFor()
    {
        await SaveInstanceAsync(
            NeedsLoader("flight-tools", "StarMap", "0.4.5"),
            NeedsLoader("orbit-tools", "StarMap", "0.3.0", max: "0.3.9"));

        var human = await _host.RunAsync("instance", "show", "Flight Test");
        var json = await _host.RunAsync("instance", "show", "Flight Test", "--json");

        Assert.Contains("Mod loader: StarMap, the mods need versions that do not overlap: flight-tools needs 0.4.5 or newer, orbit-tools needs 0.3.0 to 0.3.9", human.Output);
        Assert.DoesNotContain("0.4.5 to 0.3.9", human.Output);
        var loader = Assert.Single(json.Json.GetProperty("modLoaders").EnumerateArray());
        Assert.True(loader.GetProperty("conflict").GetBoolean());
        var requirements = loader.GetProperty("requirements").EnumerateArray().ToList();
        Assert.Equal("orbit-tools", requirements[1].GetProperty("modId").GetString());
        Assert.Equal("0.3.9", requirements[1].GetProperty("maxVersion").GetString());
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

    /// <summary>A StarMap recorded in the settings file, with a version or without one.</summary>
    private async Task RecordStarMapAsync(string? version)
    {
        Directory.CreateDirectory(_host.Root);
        var versionLine = version is null ? "" : $"Version = '{version}'\n";
        await File.WriteAllTextAsync(_host.Paths.GetBoreaSettingsPath(),
            "[LoaderInstallations.StarMap]\n" +
            "DirectoryPath = 'C:\\Loaders\\StarMap'\n" +
            versionLine +
            "IsAdopted = false\n");
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
