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
        IReadOnlyList<ModDependency>? dependencies = null,
        int specVersion = SpecVersions.Highest,
        string? changelogText = null,
        DownloadInfo? download = null,
        bool yanked = false,
        IReadOnlyList<string>? manages = null) =>
        new(
            specVersion: specVersion,
            modId: "test-mod",
            version: ModVersion.Parse("1.0.0"),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UtcNow,
            gameMin: gameMin,
            gameMinRevision: gameMinRevision,
            download: download ?? TestFixtures.SampleDownload(),
            installSizeBytes: 2048,
            dependencies: dependencies ?? Array.Empty<ModDependency>(),
            type: type,
            gameMax: gameMax,
            gameMaxRevision: gameMaxRevision,
            install: install,
            loader: loader,
            changelogText: changelogText,
            yanked: yanked,
            manages: manages);

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
    public void IsOffered_OnlyForARelease_ThatIsNotYanked_AndWhoseDownloadIsNotGone()
    {
        var gone = new DownloadInfo("https://example.com/mod.zip", new string('A', 64), 1, "application/zip", unavailableSince: DateTimeOffset.UnixEpoch);

        Assert.True(Build().IsOffered);
        Assert.False(Build(yanked: true).IsOffered);
        Assert.False(Build(download: gone).IsOffered);
        Assert.Equal(DateTimeOffset.UnixEpoch, Build(download: gone).Download.UnavailableSince);
    }

    [Theory]
    [InlineData(ModVersionMetadata.MaxChangelogTextBytes, 'a', true)]
    [InlineData(ModVersionMetadata.MaxChangelogTextBytes + 1, 'a', false)]
    [InlineData(ModVersionMetadata.MaxChangelogTextBytes / 2 + 1, (char)0xE4, false)]
    public void Constructor_ChangelogText_IsLimitedInBytesOfUtf8(int length, char character, bool accepted)
    {
        var text = new string(character, length);

        if (accepted)
            Assert.Equal(text, Build(changelogText: text).ChangelogText);
        else
            Assert.Throws<ArgumentException>(() => Build(changelogText: text));
    }

    [Theory]
    [InlineData("2026.8.3.5117", null)]
    [InlineData(null, 5117)]
    public void Constructor_GameMaxAndRevisionNotBothPresent_ThrowsArgumentException(string? gameMax, int? gameMaxRevision)
    {
        Assert.Throws<ArgumentException>(() => Build(gameMax: gameMax, gameMaxRevision: gameMaxRevision));
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
    public void Constructor_InstallOnModLoader_IsAccepted()
    {
        // The shape the index stamps for StarMap.
        var install = new InstallInfo(null, derived: true, InstallAnchor.Standalone);

        var metadata = Build(type: ContentType.ModLoader, install: install);

        Assert.Null(metadata.Install!.Root);
        Assert.Equal(InstallAnchor.Standalone, metadata.Install.Target);
    }

    [Fact]
    public void Constructor_LoaderOnModLoader_ThrowsArgumentException()
    {
        var loader = new LoaderRequirement("StarMap", ModVersion.Parse("0.4.5"));

        Assert.Throws<ArgumentException>(() => Build(type: ContentType.ModLoader, loader: loader));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_SpecVersionBelowOne_ThrowsArgumentOutOfRangeException(int specVersion)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(specVersion: specVersion));
    }

    [Fact]
    public void Constructor_SpecVersionAboveHighest_IsAccepted()
    {
        var release = Build(specVersion: SpecVersions.Highest + 1);

        Assert.True(SpecVersions.IsAboveHighest(release.SpecVersion));
    }

    [Theory]
    [InlineData(ContentType.Vehicle)]
    [InlineData(ContentType.Save)]
    public void Constructor_VehicleOrSaveWithAValidFolder_KeepsIt(ContentType type)
    {
        var metadata = Build(type: type, install: new InstallInfo(null, derived: true, folder: "Example Rover"));

        Assert.Equal("Example Rover", metadata.Install!.Folder);
        Assert.Null(metadata.Install.Root);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("CON")]
    [InlineData("Example  Rover")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Constructor_VehicleWithoutAFolderTheGameKeeps_ThrowsArgumentException(string? folder)
    {
        Assert.Throws<ArgumentException>(() => Build(type: ContentType.Vehicle, install: new InstallInfo("Example Rover", derived: true, folder: folder)));
    }

    [Fact]
    public void Constructor_VehicleWithoutAnInstallTable_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(type: ContentType.Vehicle));
    }

    [Fact]
    public void Constructor_VehicleWithTargetPathOrManages_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Build(type: ContentType.Vehicle, install: new InstallInfo(null, true, target: InstallAnchor.UserData, folder: "Example Rover")));
        Assert.Throws<ArgumentException>(() => Build(type: ContentType.Vehicle, install: new InstallInfo(null, true, path: "Vehicles", folder: "Example Rover")));
        Assert.Throws<ArgumentException>(() => Build(type: ContentType.Vehicle, install: new InstallInfo(null, true, folder: "Example Rover"), manages: ["Vehicles/Example Rover"]));
    }

    [Fact]
    public void Constructor_ModWithoutAnInstallTable_IsStillAccepted()
    {
        Assert.Null(Build(type: ContentType.Mod).Install);
    }
}
