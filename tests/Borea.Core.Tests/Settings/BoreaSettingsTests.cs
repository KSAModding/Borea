using System;
using System.Collections.Generic;
using Borea.Core.Settings;
using Xunit;

namespace Borea.Core.Tests.Settings;

public sealed class BoreaSettingsTests
{
    private static Dictionary<string, string> StarMapAt(string path = @"C:\Games\StarMap") =>
        new() { ["StarMap"] = path };

    [Fact]
    public void Constructor_NothingProvided_LeavesGameNullAndNoLoader()
    {
        var settings = new BoreaSettings(null);

        Assert.Null(settings.GameDirectoryPath);
        Assert.Empty(settings.LoaderDirectoryPaths);
    }

    [Fact]
    public void Constructor_OnlyGamePathProvided_LeavesNoLoader()
    {
        var settings = new BoreaSettings(@"C:\Games\KSA");

        Assert.Equal(@"C:\Games\KSA", settings.GameDirectoryPath);
        Assert.Empty(settings.LoaderDirectoryPaths);
    }

    [Fact]
    public void Constructor_OnlyLoaderProvided_LeavesGameNull()
    {
        var settings = new BoreaSettings(null, StarMapAt());

        Assert.Null(settings.GameDirectoryPath);
        Assert.Equal(@"C:\Games\StarMap", settings.LoaderDirectoryPaths["StarMap"]);
    }

    [Fact]
    public void Constructor_SeveralLoaders_KeepsEachOne()
    {
        var settings = new BoreaSettings(@"C:\Games\KSA", new Dictionary<string, string>
        {
            ["StarMap"] = @"C:\Games\StarMap",
            ["Cheese-Loader"] = @"C:\Games\Cheese",
        });

        Assert.Equal(2, settings.LoaderDirectoryPaths.Count);
        Assert.Equal(@"C:\Games\Cheese", settings.LoaderDirectoryPaths["Cheese-Loader"]);
    }

    [Fact]
    public void Constructor_LoaderId_ComparesCaseInsensitivelyAndKeepsTheAuthoredCasing()
    {
        var settings = new BoreaSettings(null, StarMapAt());

        Assert.Equal(@"C:\Games\StarMap", settings.LoaderDirectoryPaths["starmap"]);
        Assert.Contains("StarMap", settings.LoaderDirectoryPaths.Keys);
    }

    [Fact]
    public void Constructor_LoaderIdsCollidingByCase_ThrowsArgumentException()
    {
        // TOML keys are case-sensitive, ids are not.
        var paths = new Dictionary<string, string>
        {
            ["StarMap"] = @"C:\Games\StarMap",
            ["starmap"] = @"C:\Games\Other",
        };

        Assert.Throws<ArgumentException>(() => new BoreaSettings(null, paths));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhitespaceGamePath_ThrowsArgumentException(string gamePath)
    {
        Assert.Throws<ArgumentException>(() => new BoreaSettings(gamePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhitespaceLoaderPath_ThrowsArgumentException(string loaderPath)
    {
        Assert.Throws<ArgumentException>(() => new BoreaSettings(null, StarMapAt(loaderPath)));
    }

    [Theory]
    [InlineData("not a valid id")]
    [InlineData(".hidden")]
    [InlineData("CON")]
    public void Constructor_InvalidLoaderId_ThrowsArgumentException(string loaderId)
    {
        var paths = new Dictionary<string, string> { [loaderId] = @"C:\Games\Loader" };

        Assert.Throws<ArgumentException>(() => new BoreaSettings(null, paths));
    }

    [Fact]
    public void LoaderDirectoryPaths_IsACopy_SoLaterEditsDoNotReachIt()
    {
        var paths = StarMapAt();
        var settings = new BoreaSettings(null, paths);

        paths["StarMap"] = @"C:\Somewhere\Else";

        Assert.Equal(@"C:\Games\StarMap", settings.LoaderDirectoryPaths["StarMap"]);
    }
}
