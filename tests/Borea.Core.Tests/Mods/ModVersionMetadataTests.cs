using Borea.Core.Dependencies;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class ModVersionMetadataTests
{
    private static ModVersionMetadata Build(
        string gameMin = "2026.7.4.2131",
        int gameMinRevision = 2131,
        string? gameMax = null,
        int? gameMaxRevision = null,
        ContentType type = ContentType.Mod,
        InstallInfo? install = null,
        LoaderRequirement? loader = null,
        IReadOnlyList<ModDependency>? dependencies = null) =>
        new(
            specVersion: 1,
            modId: "test-mod",
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UtcNow,
            gameMin: gameMin,
            gameMinRevision: gameMinRevision,
            download: TestFixtures.SampleDownload(),
            installSizeBytes: 2048,
            dependencies: dependencies ?? Array.Empty<ModDependency>(),
            type: type,
            gameMax: gameMax,
            gameMaxRevision: gameMaxRevision,
            install: install,
            loader: loader);

    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var metadata = Build(gameMax: "2026.8.3.5117", gameMaxRevision: 5117);

        Assert.Equal("test-mod", metadata.ModId);
        Assert.Equal("semver", metadata.VersionScheme);
        Assert.Equal(2131, metadata.GameMinRevision);
        Assert.Equal(5117, metadata.GameMaxRevision);
        Assert.False(metadata.Yanked);
        Assert.Null(metadata.Listing);
    }

    [Fact]
    public void Constructor_GameMaxWithoutRevision_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(gameMax: "2026.8.3.5117"));
    }

    [Fact]
    public void Constructor_GameMaxRevisionWithoutDisplayString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(gameMaxRevision: 5117));
    }

    [Fact]
    public void Constructor_GameMinDisplayStringAndRevisionDisagree_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(gameMin: "2026.7.4.2131", gameMinRevision: 9999));
    }

    [Fact]
    public void Constructor_MonthFormGameMin_IsAccepted()
    {
        var metadata = Build(gameMin: "2026.7", gameMinRevision: 2131);

        Assert.Equal("2026.7", metadata.GameMin);
    }

    [Fact]
    public void Constructor_GameMaxRevisionBelowMinimum_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(gameMax: "2026.7.4.2000", gameMaxRevision: 2000));
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ModVersionMetadata(1, "test-mod", ModVersion.Parse("1.0.0"), ReleaseStatus.Stable,
                DateTimeOffset.UtcNow, "2026.7.4.2131", 2131, TestFixtures.SampleDownload(), 2048, null!));
    }

    [Fact]
    public void Constructor_PackType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(type: ContentType.ModPack));
    }

    [Fact]
    public void Constructor_InstallOnModLoader_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(type: ContentType.ModLoader, install: new InstallInfo("test-mod", derived: true)));
    }

    [Fact]
    public void Constructor_LoaderOnModLoader_ThrowsArgumentException()
    {
        var loader = new LoaderRequirement("StarMap", ModVersion.Parse("0.4.5"));

        Assert.Throws<ArgumentException>(() => Build(type: ContentType.ModLoader, loader: loader));
    }
}
