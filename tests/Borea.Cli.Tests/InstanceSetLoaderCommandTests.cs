using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;

namespace Borea.Cli.Tests;

/// <summary><c>borea instance set-loader</c> and the loader setting that <c>instance show</c> prints.</summary>
public sealed class InstanceSetLoaderCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task SetLoader_PinThenAuto_RoundTripsInTheInstanceFile()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var id = await CreateInstanceAsync();

        var pin = await _host.RunAsync("instance", "set-loader", "Flight Test", "starmap", "--version", "0.4.7");
        var pinned = await ReadInstanceAsync(id);
        var pinnedFile = await File.ReadAllTextAsync(_host.Paths.GetInstanceMetadataPath(id));
        var auto = await _host.RunAsync("instance", "set-loader", "Flight Test", "auto", "--json");
        var automatic = await ReadInstanceAsync(id);

        Assert.Equal(0, pin.ExitCode);
        Assert.Contains("'borea loader install StarMap --version 0.4.7'", pin.Error);
        Assert.Equal(LoaderSetting.Pinned("StarMap", ModVersion.Parse("0.4.7")), pinned.Loader);
        Assert.Contains("StarMap", pinnedFile);
        Assert.Contains("0.4.7", pinnedFile);
        Assert.Equal(0, auto.ExitCode);
        Assert.Equal("automatic", auto.Json.GetProperty("loader").GetProperty("mode").GetString());
        Assert.Equal(LoaderSetting.Automatic, automatic.Loader);
        Assert.DoesNotContain("0.4.7", await File.ReadAllTextAsync(_host.Paths.GetInstanceMetadataPath(id)));
    }

    [Theory]
    [InlineData("OtherLoader", "0.4.7", "not available from the configured sources")]
    [InlineData("StarMap", "not-a-version", "'not-a-version' is not a version")]
    public async Task SetLoader_UnknownLoaderOrVersion_IsRefusedAndChangesNothing(string loaderId, string version, string reason)
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var id = await CreateInstanceAsync();

        var run = await _host.RunAsync("instance", "set-loader", "Flight Test", loaderId, "--version", version);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(reason, run.Error);
        Assert.Equal(LoaderSetting.Automatic, (await ReadInstanceAsync(id)).Loader);
    }

    [Fact]
    public async Task Show_NoLoaderInstalled_NamesTheReleaseToInstall()
    {
        _host.IndexReader.Snapshot = LaunchCommandTests.StarMapIndex();
        _host.InstalledVersion = new FakeInstalledGameVersionProvider { Installed = new InstalledGameVersion(GameVersion.Parse("2026.10.19.5638"), "2026.10.19.5638") };
        await CreateInstanceAsync();

        var human = await _host.RunAsync("instance", "show", "Flight Test");
        var json = await _host.RunAsync("instance", "show", "Flight Test", "--json");

        Assert.Contains("Mod loader setting: StarMap automatic (needs 0.4.8, not installed)", human.Output);
        var loader = json.Json.GetProperty("loader");
        Assert.Equal("StarMap", loader.GetProperty("id").GetString());
        Assert.Equal("automatic", loader.GetProperty("mode").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, loader.GetProperty("pinnedVersion").ValueKind);
        Assert.Equal("0.4.8", loader.GetProperty("version").GetString());
        Assert.False(loader.GetProperty("installed").GetBoolean());
    }

    private async Task<Guid> CreateInstanceAsync()
    {
        var created = await _host.RunAsync("instance", "create", "Flight Test", "--json");
        return created.Json.GetProperty("id").GetGuid();
    }

    private async Task<Instance> ReadInstanceAsync(Guid id)
        => await new FileInstanceRepository(_host.Paths).GetByIdAsync(id) ?? throw new InvalidOperationException("The instance is gone.");

    public void Dispose() => _host.Dispose();
}
