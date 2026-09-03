using Borea.Storage.Paths;

namespace Borea.Storage.Tests.Paths;

public sealed class GamePathProviderTests
{
    private static Dictionary<string, string> StarMapAt(string path = @"C:\Games\StarMap") =>
        new() { ["StarMap"] = path };

    [Fact]
    public void Constructor_NoGameAndNoLoaders_DoesNotThrow()
    {
        var provider = new GamePathProvider(null);

        Assert.Null(provider.GetGameDirectoryPath());
        Assert.Null(provider.GetLoaderDirectoryPath("StarMap"));
    }

    [Fact]
    public void Constructor_NoBoreaRoot_RootsBoreaPathsUnderLocalAppData()
    {
        var provider = new GamePathProvider(null);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(Path.Combine(localAppData, "Borea"), provider.GetBoreaSettingsPath());
    }

    [Fact]
    public void Constructor_BoreaRoot_RootsBoreaPathsThere()
    {
        var provider = new GamePathProvider(null, boreaRoot: @"D:\Portable\Borea");

        Assert.StartsWith(@"D:\Portable\Borea", provider.GetBoreaSettingsPath());
        Assert.StartsWith(@"D:\Portable\Borea", provider.GetInstancesRoot());
        Assert.StartsWith(@"D:\Portable\Borea", provider.GetModFavoritesPath());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhitespaceBoreaRoot_ThrowsArgumentException(string boreaRoot)
    {
        Assert.Throws<ArgumentException>(() => new GamePathProvider(null, boreaRoot: boreaRoot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhitespaceGamePath_ThrowsArgumentException(string gamePath)
    {
        Assert.Throws<ArgumentException>(() => new GamePathProvider(gamePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhitespaceLoaderPath_ThrowsArgumentException(string loaderPath)
    {
        Assert.Throws<ArgumentException>(() => new GamePathProvider(null, StarMapAt(loaderPath)));
    }

    [Fact]
    public void Constructor_ValidPaths_ReturnsThemUnchanged()
    {
        var provider = new GamePathProvider(@"C:\Games\KSA", StarMapAt());

        Assert.Equal(@"C:\Games\KSA", provider.GetGameDirectoryPath());
        Assert.Equal(@"C:\Games\StarMap", provider.GetLoaderDirectoryPath("StarMap"));
    }

    [Fact]
    public void GetLoaderDirectoryPath_ComparesTheIdCaseInsensitively()
    {
        var provider = new GamePathProvider(null, StarMapAt());

        Assert.Equal(@"C:\Games\StarMap", provider.GetLoaderDirectoryPath("starmap"));
    }

    [Fact]
    public void GetLoaderDirectoryPath_LoaderThatIsNotInstalled_ReturnsNull()
    {
        var provider = new GamePathProvider(null, StarMapAt());

        Assert.Null(provider.GetLoaderDirectoryPath("Cheese-Loader"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetLoaderDirectoryPath_NoIdAtAll_ThrowsArgumentException(string? loaderId)
    {
        var provider = new GamePathProvider(null, StarMapAt());

        Assert.Throws<ArgumentException>(() => provider.GetLoaderDirectoryPath(loaderId!));
    }

    [Fact]
    public void Constructor_LoaderIdsCollidingByCase_ThrowsArgumentException()
    {
        // Same rule as BoreaSettings
        var paths = new Dictionary<string, string>
        {
            ["StarMap"] = @"C:\Games\StarMap",
            ["starmap"] = @"C:\Games\Other",
        };

        Assert.Throws<ArgumentException>(() => new GamePathProvider(null, paths));
    }

    [Theory]
    [InlineData("not a valid id")]
    [InlineData(".hidden")]
    [InlineData("CON")]
    public void Constructor_InvalidLoaderId_ThrowsArgumentException(string loaderId)
    {
        var paths = new Dictionary<string, string> { [loaderId] = @"C:\Games\Loader" };

        Assert.Throws<ArgumentException>(() => new GamePathProvider(null, paths));
    }

    [Fact]
    public void GetLoaderDirectoryPath_SeveralLoaders_AnswersEachOne()
    {
        var provider = new GamePathProvider(null, new Dictionary<string, string>
        {
            ["StarMap"] = @"C:\Games\StarMap",
            ["Cheese-Loader"] = @"C:\Games\Cheese",
        });

        Assert.Equal(@"C:\Games\StarMap", provider.GetLoaderDirectoryPath("StarMap"));
        Assert.Equal(@"C:\Games\Cheese", provider.GetLoaderDirectoryPath("Cheese-Loader"));
    }

    [Fact]
    public void GetIndexPath_ReturnsIndexJsonUnderRoot()
    {
        var provider = new GamePathProvider(null, null);

        var path = provider.GetIndexPath();

        Assert.Equal("index.json", Path.GetFileName(path));
        Assert.DoesNotContain(provider.GetInstancesRoot(), path);
    }

    [Fact]
    public void InstancePaths_AreNestedUnderInstanceRoot()
    {
        var provider = new GamePathProvider(null);
        var instanceId = Guid.NewGuid();

        var root = provider.GetInstanceRoot(instanceId);

        Assert.StartsWith(provider.GetInstancesRoot(), root);
        Assert.StartsWith(root, provider.GetInstanceModsFolder(instanceId));
        Assert.StartsWith(root, provider.GetInstanceSavesFolder(instanceId));
        Assert.StartsWith(root, provider.GetInstanceVehiclesFolder(instanceId));
        Assert.StartsWith(root, provider.GetInstanceSettingsPath(instanceId));
        Assert.StartsWith(root, provider.GetInstanceManifestPath(instanceId));
        Assert.StartsWith(root, provider.GetInstanceMetadataPath(instanceId));
    }

    [Fact]
    public void InstanceRoot_IncludesInstanceIdInPath()
    {
        var provider = new GamePathProvider(null);
        var instanceId = Guid.NewGuid();

        var root = provider.GetInstanceRoot(instanceId);

        Assert.Contains(instanceId.ToString(), root);
    }

    [Fact]
    public void DifferentInstanceIds_ProduceDifferentPaths()
    {
        var provider = new GamePathProvider(null);

        var pathA = provider.GetInstanceRoot(Guid.NewGuid());
        var pathB = provider.GetInstanceRoot(Guid.NewGuid());

        Assert.NotEqual(pathA, pathB);
    }

    [Fact]
    public void GlobalPaths_AreDistinctFromEachOther()
    {
        // Regression guard: confirms active-instance pointer, both favorites
        // files, and Borea settings never accidentally collide on one path.
        var provider = new GamePathProvider(null);

        var paths = new[]
        {
            provider.GetActiveInstancePointerPath(),
            provider.GetModFavoritesPath(),
            provider.GetModPackFavoritesPath(),
            provider.GetBoreaSettingsPath(),
            provider.GetIndexPath()
        };

        Assert.Equal(paths.Length, paths.Distinct().Count());
    }

    [Fact]
    public void GlobalPaths_AreNotNestedUnderInstancesRoot()
    {
        var provider = new GamePathProvider(null);

        Assert.DoesNotContain(provider.GetInstancesRoot(), provider.GetActiveInstancePointerPath());
        Assert.DoesNotContain(provider.GetInstancesRoot(), provider.GetBoreaSettingsPath());
        Assert.DoesNotContain(provider.GetInstancesRoot(), provider.GetIndexPath());
    }
}
