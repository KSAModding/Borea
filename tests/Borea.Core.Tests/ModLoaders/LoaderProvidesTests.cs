using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Core.Tests.ModLoaders;

public sealed class LoaderProvidesTests
{
    [Fact]
    public void Constructor_TheStarMapShape_IsAccepted()
    {
        var provides = new LoaderProvides(
            launch: "StarMap.exe",
            contentDir: InstallAnchor.Mods,
            configure: new LoaderConfigure("StarMapConfig.json", ConfigureFormat.Json, "GameLocation"));

        Assert.Equal("StarMap.exe", provides.Launch);
        Assert.Equal(InstallAnchor.Mods, provides.ContentDir);
        Assert.Null(provides.ContentPath);
        Assert.Equal("GameLocation", provides.Configure!.GamePath);
    }

    [Fact]
    public void Constructor_NothingStated_ReadsNoContentDirectory()
    {
        var provides = new LoaderProvides();

        Assert.Null(provides.Launch);
        Assert.Null(provides.ContentDir);
        Assert.Null(provides.Configure);
        Assert.Null(provides.Instance);
    }

    [Fact]
    public void Constructor_InstanceTable_IsKept()
    {
        var instance = new InstanceHandover("-InstancePath", "STARMAP_INSTANCE_PATH");

        var provides = new LoaderProvides(launch: "StarMap.exe", instance: instance);

        Assert.Same(instance, provides.Instance);
    }

    [Fact]
    public void Constructor_NoPlatformTable_HasNoPlatformEntries()
    {
        Assert.Empty(new LoaderProvides(launch: "StarMap.exe").Platforms);
    }

    [Fact]
    public void Constructor_PlatformEntries_AreKept()
    {
        var linux = new LoaderPlatformLaunch("StarMap.dll", "dotnet");

        var provides = new LoaderProvides(launch: "StarMap.exe", platforms: new Dictionary<OsPlatform, LoaderPlatformLaunch> { [OsPlatform.Linux] = linux });

        Assert.Same(linux, Assert.Single(provides.Platforms).Value);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("dotnet", LoaderRuntime.Dotnet)]
    [InlineData("Dotnet", LoaderRuntime.Unknown)]
    [InlineData("mono", LoaderRuntime.Unknown)]
    public void PlatformLaunch_Runtime_IsReadOrKeptAsUnknown(string? runtime, LoaderRuntime? expected)
    {
        var entry = new LoaderPlatformLaunch("StarMap.dll", runtime);

        Assert.Equal(expected, entry.Runtime);
        Assert.Equal(runtime, entry.RuntimeName);
    }

    [Theory]
    [InlineData("/absolute/StarMap.dll")]
    [InlineData("../StarMap.dll")]
    [InlineData("")]
    public void PlatformLaunch_LaunchLeavingItsAnchor_ThrowsArgumentException(string launch)
    {
        Assert.Throws<ArgumentException>(() => new LoaderPlatformLaunch(launch));
    }

    [Fact]
    public void Constructor_UnknownContentDir_IsKeptSoTheListingStaysReadable()
    {
        var provides = new LoaderProvides(contentDir: InstallAnchor.Unknown);

        Assert.Equal(InstallAnchor.Unknown, provides.ContentDir);
    }

    [Fact]
    public void Constructor_ContentPathWithoutItsAnchor_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new LoaderProvides(contentPath: "loaders"));
    }

    [Theory]
    [InlineData("/absolute/StarMap.exe")]
    [InlineData("../StarMap.exe")]
    public void Constructor_LaunchLeavingItsAnchor_ThrowsArgumentException(string launch)
    {
        Assert.Throws<ArgumentException>(() => new LoaderProvides(launch: launch));
    }

    [Fact]
    public void Configure_UnknownFormat_IsKeptSoTheListingStaysReadable()
    {
        var configure = new LoaderConfigure("Config.ini", ConfigureFormat.Unknown);

        Assert.Equal(ConfigureFormat.Unknown, configure.Format);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Configure_NoFile_ThrowsArgumentException(string? file)
    {
        Assert.Throws<ArgumentException>(() => new LoaderConfigure(file!, ConfigureFormat.Json));
    }

    [Fact]
    public void Configure_FileLeavingItsAnchor_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new LoaderConfigure("../StarMapConfig.json", ConfigureFormat.Json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a..b")]
    [InlineData("a.")]
    [InlineData(".a")]
    public void Configure_GamePathKeyWithAnEmptySegment_ThrowsArgumentException(string gamePath)
    {
        Assert.Throws<ArgumentException>(() => new LoaderConfigure("Config.json", ConfigureFormat.Json, gamePath));
    }

    [Fact]
    public void Configure_NestedGamePathKey_IsAccepted()
    {
        // Addressed dot-separated from the document root.
        var configure = new LoaderConfigure("Config.toml", ConfigureFormat.Toml, "loader.game.path");

        Assert.Equal("loader.game.path", configure.GamePath);
    }
}
