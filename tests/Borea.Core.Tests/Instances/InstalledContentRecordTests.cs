using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Tests.Mods;

namespace Borea.Core.Tests.Instances;

public sealed class InstalledContentRecordTests
{
    private const string Hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private static ModVersionMetadata Release(ContentType type) => new(
        specVersion: 1,
        modId: "ExampleRover",
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.10.19.5638",
        gameMinRevision: 5638,
        download: TestFixtures.SampleDownload(),
        installSizeBytes: 1,
        dependencies: [],
        type: type,
        install: type.IsVehicleOrSave() ? new InstallInfo(null, true, folder: "Example Rover") : null);

    private static InstalledContentRecord Build(ContentType type = ContentType.Vehicle, string folder = "Example Rover", string path = "vehicle.xml", string hash = Hash) =>
        new(Release(type), folder, DateTimeOffset.UnixEpoch, new Dictionary<string, string> { [path] = hash });

    [Theory]
    [InlineData(ContentType.Vehicle)]
    [InlineData(ContentType.Save)]
    public void Constructor_TakesTheTypeListingAndVersionFromTheRelease(ContentType type)
    {
        var record = Build(type);

        Assert.Equal(type, record.Type);
        Assert.Equal("ExampleRover", record.ListingId);
        Assert.Equal(ModVersion.Parse("1.0.0"), record.Version);
    }

    [Theory]
    [InlineData(ContentType.Mod)]
    [InlineData(ContentType.ModLoader)]
    public void Constructor_OtherTypes_ThrowArgumentException(ContentType type)
    {
        Assert.Throws<ArgumentException>(() => Build(type));
    }

    [Theory]
    [InlineData("")]
    [InlineData("CON")]
    [InlineData(" Example Rover")]
    public void Constructor_FolderTheGameChanges_ThrowsArgumentException(string folder)
    {
        Assert.Throws<ArgumentException>(() => Build(folder: folder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("parts\\a.xml")]
    [InlineData("/vehicle.xml")]
    [InlineData("parts//a.xml")]
    [InlineData("../vehicle.xml")]
    [InlineData("parts/./a.xml")]
    public void Constructor_PathThatIsNotRelativeWithForwardSlashes_ThrowsArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() => Build(path: path));
    }

    [Theory]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("ba7816bf")]
    [InlineData("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Constructor_HashThatIsNotLowercaseHexSha256_ThrowsArgumentException(string hash)
    {
        Assert.Throws<ArgumentException>(() => Build(hash: hash));
    }

    [Fact]
    public void WithFolderName_KeepsEverythingElse()
    {
        var record = Build(path: "parts/a.xml");

        var renamed = record.WithFolderName("Rover Two");

        Assert.Equal("Rover Two", renamed.FolderName);
        Assert.Same(record.Metadata, renamed.Metadata);
        Assert.Equal(record.Files, renamed.Files);
        Assert.Equal(record.InstalledAt, renamed.InstalledAt);
    }

    [Fact]
    public void Changes_AreUnchangedOnlyWhenEveryListIsEmpty()
    {
        Assert.True(new InstalledContentChanges([], [], []).IsUnchanged);
        Assert.False(new InstalledContentChanges([], [], ["vehicle.xml"]).IsUnchanged);
    }
}
