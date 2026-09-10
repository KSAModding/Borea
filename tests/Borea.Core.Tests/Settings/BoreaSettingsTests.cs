using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;

namespace Borea.Core.Tests.Settings;

public sealed class BoreaSettingsTests
{
    private static Dictionary<string, LoaderInstallation> StarMapAt(string path = @"C:\Games\StarMap") =>
        new()
        {
            ["StarMap"] = new LoaderInstallation(path, ModVersion.Parse("0.4.6"), "0.4.6.0", isAdopted: true),
        };

    [Fact]
    public void Constructor_NothingProvided_LeavesGameNullAndNoLoader()
    {
        var settings = new BoreaSettings(null);

        Assert.Null(settings.GameDirectoryPath);
        Assert.Empty(settings.LoaderInstallations);
    }

    [Fact]
    public void Constructor_OnlyGamePathProvided_LeavesNoLoader()
    {
        var settings = new BoreaSettings(@"C:\Games\KSA");

        Assert.Equal(@"C:\Games\KSA", settings.GameDirectoryPath);
        Assert.Empty(settings.LoaderInstallations);
    }

    [Fact]
    public void Constructor_OnlyLoaderProvided_LeavesGameNull()
    {
        var settings = new BoreaSettings(null, StarMapAt());

        Assert.Null(settings.GameDirectoryPath);
        Assert.Equal(@"C:\Games\StarMap", settings.LoaderInstallations["StarMap"].DirectoryPath);
    }

    [Fact]
    public void Constructor_SeveralLoaders_KeepsEachOne()
    {
        var installations = StarMapAt();
        installations["Cheese-Loader"] = new LoaderInstallation(@"C:\Games\Cheese", null, null, isAdopted: false);

        var settings = new BoreaSettings(@"C:\Games\KSA", installations);

        Assert.Equal(2, settings.LoaderInstallations.Count);
        Assert.Equal(@"C:\Games\Cheese", settings.LoaderInstallations["Cheese-Loader"].DirectoryPath);
    }

    [Fact]
    public void Constructor_LoaderId_ComparesCaseInsensitivelyAndKeepsTheAuthoredCasing()
    {
        var settings = new BoreaSettings(null, StarMapAt());

        Assert.Equal(@"C:\Games\StarMap", settings.LoaderInstallations["starmap"].DirectoryPath);
        Assert.Contains("StarMap", settings.LoaderInstallations.Keys);
    }

    [Fact]
    public void Constructor_LoaderIdsCollidingByCase_ThrowsArgumentException()
    {
        var installations = new Dictionary<string, LoaderInstallation>
        {
            ["StarMap"] = new LoaderInstallation(@"C:\Games\StarMap", null, null, isAdopted: true),
            ["starmap"] = new LoaderInstallation(@"C:\Games\Other", null, null, isAdopted: true),
        };

        Assert.Throws<ArgumentException>(() => new BoreaSettings(null, installations));
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
    public void LoaderInstallation_WhitespaceDirectoryPath_ThrowsArgumentException(string loaderPath)
    {
        Assert.Throws<ArgumentException>(() => new LoaderInstallation(loaderPath, null, null, isAdopted: true));
    }

    [Theory]
    [InlineData("not a valid id")]
    [InlineData(".hidden")]
    [InlineData("CON")]
    public void Constructor_InvalidLoaderId_ThrowsArgumentException(string loaderId)
    {
        var installations = new Dictionary<string, LoaderInstallation>
        {
            [loaderId] = new LoaderInstallation(@"C:\Games\Loader", null, null, isAdopted: true),
        };

        Assert.Throws<ArgumentException>(() => new BoreaSettings(null, installations));
    }

    [Fact]
    public void LoaderInstallations_IsACopy_SoLaterEditsDoNotReachIt()
    {
        var installations = StarMapAt();
        var settings = new BoreaSettings(null, installations);

        installations["StarMap"] = new LoaderInstallation(@"C:\Somewhere\Else", null, null, isAdopted: true);

        Assert.Equal(@"C:\Games\StarMap", settings.LoaderInstallations["StarMap"].DirectoryPath);
    }

    [Fact]
    public void WithGameDirectory_ReplacesTheGame_AndKeepsTheLoaders()
    {
        var settings = new BoreaSettings(@"C:\Games\KSA", StarMapAt());

        var changed = settings.WithGameDirectory(@"D:\KSA");

        Assert.Equal(@"D:\KSA", changed.GameDirectoryPath);
        Assert.Equal(@"C:\Games\StarMap", changed.LoaderInstallations["StarMap"].DirectoryPath);
        Assert.Equal(@"C:\Games\KSA", settings.GameDirectoryPath);
    }

    [Fact]
    public void WithLoaderInstallation_AddsALoader_AndKeepsTheRest()
    {
        var settings = new BoreaSettings(@"C:\Games\KSA", StarMapAt());
        var installation = new LoaderInstallation(@"C:\Games\Cheese", null, null, isAdopted: true);

        var changed = settings.WithLoaderInstallation("Cheese-Loader", installation);

        Assert.Equal(@"C:\Games\KSA", changed.GameDirectoryPath);
        Assert.Equal(@"C:\Games\StarMap", changed.LoaderInstallations["StarMap"].DirectoryPath);
        Assert.Equal(@"C:\Games\Cheese", changed.LoaderInstallations["Cheese-Loader"].DirectoryPath);
        Assert.Single(settings.LoaderInstallations);
    }

    [Fact]
    public void WithLoaderInstallation_SameIdInAnotherCase_ReplacesTheEntryAndItsCasing()
    {
        var settings = new BoreaSettings(null, StarMapAt());
        var installation = new LoaderInstallation(@"C:\Games\Other", null, null, isAdopted: true);

        var changed = settings.WithLoaderInstallation("starmap", installation);

        var loader = Assert.Single(changed.LoaderInstallations);
        Assert.Equal("starmap", loader.Key);
        Assert.Equal(@"C:\Games\Other", loader.Value.DirectoryPath);
    }

    [Theory]
    [InlineData("not a valid id")]
    [InlineData("")]
    public void WithLoaderInstallation_InvalidId_ThrowsArgumentException(string loaderId)
    {
        var settings = new BoreaSettings(null);
        var installation = new LoaderInstallation(@"C:\Games\Loader", null, null, isAdopted: true);

        Assert.Throws<ArgumentException>(() => settings.WithLoaderInstallation(loaderId, installation));
    }

    [Fact]
    public void WithLoaderInstallation_NullInstallation_ThrowsArgumentNullException()
    {
        var settings = new BoreaSettings(null);

        Assert.Throws<ArgumentNullException>(() => settings.WithLoaderInstallation("StarMap", null!));
    }
}
