using Borea.Core.Mods;
using Borea.Core.Updates;

namespace Borea.Core.Tests.Updates;

public sealed class BoreaReleaseTests
{
    private static BoreaRelease Release(string version) =>
        new(ModVersion.Parse(version), "v" + version, "https://github.com/KSAModding/Borea/releases/tag/v" + version);

    [Theory]
    [InlineData("0.5.0", "0.4.0", true)]
    [InlineData("0.4.1", "0.4.0+abc123", true)]
    [InlineData("0.4.0", "0.4.0+abc123", false)] // Build metadata takes no part in precedence.
    [InlineData("0.4.0", "0.4.0", false)]
    [InlineData("0.3.0", "0.4.0", false)]
    [InlineData("0.4.0", "0.4.0-beta.1", true)] // The release ranks above its own pre-release.
    [InlineData("0.4.0", "0.5.0-dev.3", false)]
    public void IsNewerThan_ComparesAsSemVer(string release, string running, bool newer)
    {
        Assert.Equal(newer, Release(release).IsNewerThan(running));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("local build")]
    public void IsNewerThan_UnparseableRunningVersion_IsFalse(string? running)
    {
        Assert.False(Release("99.0.0").IsNewerThan(running));
    }

    [Theory]
    [InlineData("v0.4.0", "0.4.0")]
    [InlineData("0.4.0", "0.4.0")]
    [InlineData("v1.0.0-beta.1", "1.0.0-beta.1")]
    [InlineData("v1.0.0+build.5", "1.0.0")]
    public void TryParseTag_ReleaseTag_Parses(string tag, string expected)
    {
        Assert.True(BoreaRelease.TryParseTag(tag, out var version));
        Assert.Equal(ModVersion.Parse(expected), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("vv1.2.3")]
    [InlineData("V1.2.3")] // The release workflow removes only a lower-case "v".
    [InlineData("v1.2")]
    [InlineData("nightly")]
    public void TryParseTag_OtherTag_DoesNotParse(string? tag)
    {
        Assert.False(BoreaRelease.TryParseTag(tag, out _));
    }
}
