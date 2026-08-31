using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class InstallDescriptorTests
{
    [Fact]
    public void Constructor_NothingStated_LeavesEveryFieldAbsent()
    {
        var install = new InstallDescriptor();

        Assert.Null(install.Root);
        Assert.Null(install.Target);
        Assert.Null(install.Path);
        Assert.Null(install.Manages);
        Assert.Null(install.Steps);
        Assert.Null(install.Uninstall);
    }

    [Fact]
    public void Constructor_TheStarMapShape_IsAccepted()
    {
        var install = new InstallDescriptor(
            target: InstallAnchor.Standalone,
            uninstall: new[] { "Delete the StarMap directory." });

        Assert.Equal(InstallAnchor.Standalone, install.Target);
        Assert.Single(install.Uninstall!);
    }

    [Fact]
    public void Constructor_EmptyProseList_StaysDistinctFromAbsent()
    {
        // Absent is not empty.
        var install = new InstallDescriptor(steps: Array.Empty<string>());

        Assert.Empty(install.Steps!);
        Assert.Null(install.Uninstall);
    }

    [Fact]
    public void Constructor_UnknownTarget_IsKeptSoTheListingStaysReadable()
    {
        var install = new InstallDescriptor(target: InstallAnchor.Unknown);

        Assert.Equal(InstallAnchor.Unknown, install.Target);
    }

    [Theory]
    [InlineData("/absolute")]
    [InlineData("~/home")]
    [InlineData("back\\slash")]
    [InlineData("C:/drive")]
    [InlineData("../escape")]
    public void Constructor_PathLeavingItsAnchor_ThrowsArgumentException(string path)
    {
        Assert.Throws<ArgumentException>(() => new InstallDescriptor(path: path));
    }

    [Fact]
    public void Constructor_ManagedPathLeavingItsAnchor_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new InstallDescriptor(manages: new[] { "../elsewhere.json" }));
    }

    [Theory]
    [InlineData("manifest.toml")]
    [InlineData("Manifest.TOML")]
    [InlineData("./manifest.toml")]
    [InlineData("manifest.toml/")]
    public void Constructor_ManagesTheGamesManifest_ThrowsArgumentException(string claimed)
    {
        // ModManifest.Save regenerates it from the game's own list.
        Assert.Throws<ArgumentException>(() =>
            new InstallDescriptor(target: InstallAnchor.UserData, manages: new[] { claimed }));
    }

    [Fact]
    public void Constructor_ManifestBelowAPath_IsSomebodyElsesFile()
    {
        // This claims Saves/manifest.toml, which the game does not own.
        var install = new InstallDescriptor(
            target: InstallAnchor.UserData,
            path: "Saves",
            manages: new[] { "manifest.toml" });

        Assert.Single(install.Manages!);
    }

    [Fact]
    public void Constructor_ManifestUnderAnotherAnchor_IsAccepted()
    {
        // The rule is about the game's file, not the name.
        var install = new InstallDescriptor(target: InstallAnchor.Standalone, manages: new[] { "manifest.toml" });

        Assert.Single(install.Manages!);
    }

    [Fact]
    public void Manages_IsACopy_SoLaterEditsDoNotReachIt()
    {
        var manages = new List<string> { "config/settings.json" };
        var install = new InstallDescriptor(manages: manages);

        manages.Add("another.json");

        Assert.Single(install.Manages!);
    }
}
