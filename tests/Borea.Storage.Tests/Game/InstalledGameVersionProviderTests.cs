using Borea.Storage.Game;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Game;

public sealed class InstalledGameVersionProviderTests : IDisposable
{
    private const string RealBuildFixture = "GameVersionFixture.dll";
    private const string ForeignBuildFixture = "UnparseableVersionFixture.dll";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    private InstalledGameVersionProvider Provider(bool hasGameDirectory = true) =>
        new(new TestGamePathProvider(_tempRoot, hasGameDirectory));

    private string GameDirectory()
    {
        var directory = Path.Combine(_tempRoot, "Game");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Puts a fixture where the game's own assembly would be.</summary>
    private void PlaceGameAssembly(string fixtureFileName) =>
        File.Copy(Path.Combine(AppContext.BaseDirectory, fixtureFileName),
                  Path.Combine(GameDirectory(), "KSA.dll"));

    [Fact]
    public void GetInstalledVersion_GameLocationUnknown_ReturnsNull()
    {
        Assert.Null(Provider(hasGameDirectory: false).GetInstalledVersion());
    }

    [Fact]
    public void GetInstalledVersion_AssemblyMissing_ReturnsNull()
    {
        GameDirectory();

        Assert.Null(Provider().GetInstalledVersion());
    }

    [Fact]
    public void GetInstalledVersion_FileCarriesNoVersionResource_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(GameDirectory(), "KSA.dll"), "not a portable executable");

        Assert.Null(Provider().GetInstalledVersion());
    }

    [Fact]
    public void GetInstalledVersion_VersionThatDoesNotParse_KeepsTheRawString()
    {
        PlaceGameAssembly(ForeignBuildFixture);

        var installed = Provider().GetInstalledVersion();

        Assert.NotNull(installed);
        Assert.Null(installed!.Version);
        Assert.Equal("1.0.0.0", installed.RawVersion);
    }

    [Fact]
    public void GetInstalledVersion_RealBuildNumber_ParsesIt()
    {
        PlaceGameAssembly(RealBuildFixture);

        var installed = Provider().GetInstalledVersion();

        Assert.NotNull(installed);
        Assert.Equal("2026.8.3.5117", installed!.RawVersion);
        Assert.Equal(5117, installed.Version!.Value.Revision);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
