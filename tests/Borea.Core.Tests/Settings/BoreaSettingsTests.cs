using System;
using Borea.Core.Settings;
using Xunit;

namespace Borea.Core.Tests.Settings;

public sealed class BoreaSettingsTests
{
    [Fact]
    public void Constructor_BothNull_DoesNotThrow()
    {
        var settings = new BoreaSettings(null, null);

        Assert.Null(settings.GameDirectoryPath);
        Assert.Null(settings.StarMapDirectoryPath);
    }

    [Fact]
    public void Constructor_OnlyGamePathProvided_LeavesStarMapNull()
    {
        var settings = new BoreaSettings(@"C:\Games\KSA", null);

        Assert.Equal(@"C:\Games\KSA", settings.GameDirectoryPath);
        Assert.Null(settings.StarMapDirectoryPath);
    }

    [Fact]
    public void Constructor_OnlyStarMapPathProvided_LeavesGameNull()
    {
        var settings = new BoreaSettings(null, @"C:\Games\StarMap");

        Assert.Null(settings.GameDirectoryPath);
        Assert.Equal(@"C:\Games\StarMap", settings.StarMapDirectoryPath);
    }

    [Fact]
    public void Constructor_BothProvided_SetsBoth()
    {
        var settings = new BoreaSettings(@"C:\Games\KSA", @"C:\Games\StarMap");

        Assert.Equal(@"C:\Games\KSA", settings.GameDirectoryPath);
        Assert.Equal(@"C:\Games\StarMap", settings.StarMapDirectoryPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhitespaceGamePath_ThrowsArgumentException(string gamePath)
    {
        Assert.Throws<ArgumentException>(() => new BoreaSettings(gamePath, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhitespaceStarMapPath_ThrowsArgumentException(string starMapPath)
    {
        Assert.Throws<ArgumentException>(() => new BoreaSettings(null, starMapPath));
    }
}
