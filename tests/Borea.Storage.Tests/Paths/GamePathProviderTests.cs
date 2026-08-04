using Borea.Storage.Paths;

namespace Borea.Storage.Tests.Paths;

public sealed class GamePathProviderTests
{
    [Fact]
    public void Constructor_NullGameAndStarMapPaths_DoesNotThrow()
    {
        var provider = new GamePathProvider(null, null);

        Assert.Null(provider.GetGameDirectoryPath());
        Assert.Null(provider.GetStarMapDirectoryPath());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhitespaceGamePath_ThrowsArgumentException(string gamePath)
    {
        Assert.Throws<ArgumentException>(() => new GamePathProvider(gamePath, null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WhitespaceStarMapPath_ThrowsArgumentException(string starMapPath)
    {
        Assert.Throws<ArgumentException>(() => new GamePathProvider(null, starMapPath));
    }

    [Fact]
    public void Constructor_ValidPaths_ReturnsThemUnchanged()
    {
        var provider = new GamePathProvider(@"C:\Games\KSA", @"C:\Games\StarMap");

        Assert.Equal(@"C:\Games\KSA", provider.GetGameDirectoryPath());
        Assert.Equal(@"C:\Games\StarMap", provider.GetStarMapDirectoryPath());
    }

    [Fact]
    public void InstancePaths_AreNestedUnderInstanceRoot()
    {
        var provider = new GamePathProvider(null, null);
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
        var provider = new GamePathProvider(null, null);
        var instanceId = Guid.NewGuid();

        var root = provider.GetInstanceRoot(instanceId);

        Assert.Contains(instanceId.ToString(), root);
    }

    [Fact]
    public void DifferentInstanceIds_ProduceDifferentPaths()
    {
        var provider = new GamePathProvider(null, null);

        var pathA = provider.GetInstanceRoot(Guid.NewGuid());
        var pathB = provider.GetInstanceRoot(Guid.NewGuid());

        Assert.NotEqual(pathA, pathB);
    }

    [Fact]
    public void GlobalPaths_AreDistinctFromEachOther()
    {
        // Regression guard: confirms active-instance pointer, both favorites
        // files, and Borea settings never accidentally collide on one path.
        var provider = new GamePathProvider(null, null);

        var paths = new[]
        {
            provider.GetActiveInstancePointerPath(),
            provider.GetModFavoritesPath(),
            provider.GetModPackFavoritesPath(),
            provider.GetBoreaSettingsPath(),
        };

        Assert.Equal(paths.Length, paths.Distinct().Count());
    }

    [Fact]
    public void GlobalPaths_AreNotNestedUnderInstancesRoot()
    {
        var provider = new GamePathProvider(null, null);

        Assert.DoesNotContain(provider.GetInstancesRoot(), provider.GetActiveInstancePointerPath());
        Assert.DoesNotContain(provider.GetInstancesRoot(), provider.GetBoreaSettingsPath());
    }
}
