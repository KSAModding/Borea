using Borea.Core.Mods;
using Borea.Core.Updates;

namespace Borea.Core.Tests.Updates;

public sealed class BoreaArchiveTests
{
    private static BoreaRelease Release(params string[] assetNames) =>
        new(ModVersion.Parse("0.2.0"), "v0.2.0", "https://github.com/KSAModding/Borea/releases/tag/v0.2.0")
        {
            Assets = assetNames
                .Select(name => new BoreaReleaseAsset(name, "https://github.com/KSAModding/Borea/releases/download/v0.2.0/" + name))
                .ToList(),
        };

    [Theory]
    [InlineData("win-x64", "win-x64")]
    [InlineData("linux-x64", "linux-x64")]
    [InlineData("osx-arm64", "macos-arm64")]
    public void PlatformOf_APublishedRuntime_IsTheNameTheArchivesUse(string runtimeIdentifier, string expected)
        => Assert.Equal(expected, BoreaArchive.PlatformOf(runtimeIdentifier));

    [Theory]
    [InlineData("osx-x64")]
    [InlineData("linux-arm64")]
    [InlineData("")]
    [InlineData(null)]
    public void PlatformOf_ARuntimeWithoutRelease_IsNull(string? runtimeIdentifier)
        => Assert.Null(BoreaArchive.PlatformOf(runtimeIdentifier));

    [Fact]
    public void ExtensionOf_FollowsHowTheWorkflowPacksEachPlatform()
    {
        Assert.Equal(".zip", BoreaArchive.ExtensionOf("win-x64"));
        Assert.Equal(".tar.gz", BoreaArchive.ExtensionOf("linux-x64"));
        Assert.Equal(".tar.gz", BoreaArchive.ExtensionOf("macos-arm64"));
    }

    [Fact]
    public void Find_TheAppArchive_IsNotTheCliArchiveOfTheSamePlatform()
    {
        var release = Release("Borea-0.2.0-win-x64.zip", "Borea-Cli-0.2.0-win-x64.zip", "SHA256SUMS.txt");

        Assert.Equal("Borea-0.2.0-win-x64.zip", BoreaArchive.Find(release, BoreaProduct.App, "win-x64")!.Name);
        Assert.Equal("Borea-Cli-0.2.0-win-x64.zip", BoreaArchive.Find(release, BoreaProduct.Cli, "win-x64")!.Name);
    }

    [Fact]
    public void Find_AnotherPlatformOrTheSoftwareBillOfMaterials_IsNotTheArchive()
    {
        var release = Release("Borea-0.2.0-linux-x64.tar.gz", "Borea-0.2.0-macos-arm64.tar.gz", "Borea-0.2.0.cdx.json");

        Assert.Equal("Borea-0.2.0-linux-x64.tar.gz", BoreaArchive.Find(release, BoreaProduct.App, "linux-x64")!.Name);
        Assert.Null(BoreaArchive.Find(release, BoreaProduct.App, "win-x64"));
        Assert.Null(BoreaArchive.Find(release, BoreaProduct.Cli, "linux-x64"));
    }

    [Fact]
    public void FindChecksums_IsTheFileTheWorkflowPublishes()
    {
        var release = Release("Borea-0.2.0-win-x64.zip", "SHA256SUMS.txt");

        Assert.Equal("SHA256SUMS.txt", BoreaArchive.FindChecksums(release)!.Name);
        Assert.Null(BoreaArchive.FindChecksums(Release("Borea-0.2.0-win-x64.zip")));
    }

    [Fact]
    public void Matches_ReadsTheWholeName()
    {
        Assert.False(BoreaArchive.Matches("Borea-0.2.0-win-x64.zip.sig", BoreaProduct.App, "win-x64"));
        Assert.False(BoreaArchive.Matches("Something-0.2.0-win-x64.zip", BoreaProduct.App, "win-x64"));
        Assert.False(BoreaArchive.Matches(null, BoreaProduct.App, "win-x64"));
    }
}
