using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class ModVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("0.0.0", 0, 0, 0, null)]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("10.20.30-rc-1", 10, 20, 30, "rc-1")] // Hyphens are valid inside a pre-release identifier.
    [InlineData("1.0.0+build", 1, 0, 0, null)] // Build metadata is accepted and discarded.
    [InlineData("1.0.0-beta+exp.sha-5114f85", 1, 0, 0, "beta")] // Build metadata never joins the pre-release.
    [InlineData("1.0.0+0.01", 1, 0, 0, null)] // Build identifiers may have leading zeros, unlike pre-release ones.
    public void TryParse_ValidInput_ProducesExpectedComponents(string input, int major, int minor, int patch, string? preRelease)
    {
        var success = ModVersion.TryParse(input, out var result);

        Assert.True(success);
        Assert.Equal(major, result.Major);
        Assert.Equal(minor, result.Minor);
        Assert.Equal(patch, result.Patch);
        Assert.Equal(preRelease, result.PreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2")]           // Too few components.
    [InlineData("1.2.3.4")]       // Too many components.
    [InlineData("a.b.c")]         // Non-numeric.
    [InlineData("1.2.")]          // Trailing dot, empty component.
    [InlineData("01.2.3")]        // Leading zero in a core component.
    [InlineData("1.0.01")]        // Same, in the patch position.
    [InlineData(" 1.2.3")]        // Whitespace inside the core.
    [InlineData("1.2.3 ")]
    [InlineData("1.2.3-")]        // Dash with no pre-release text.
    [InlineData("1.2.3+")]        // Plus with no build metadata.
    [InlineData("1.2.3+bad_meta")] // Underscore is outside the identifier charset.
    [InlineData("1.2.3-beta..1")] // Empty pre-release identifier.
    [InlineData("1.2.3-01")]      // Numeric identifier with a leading zero.
    [InlineData("1.2.3- hotfix")] // Free text is not a pre-release label.
    public void TryParse_InvalidInput_ReturnsFalse(string? input)
    {
        var success = ModVersion.TryParse(input, out _);

        Assert.False(success);
    }

    [Fact]
    public void Parse_InvalidInput_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => ModVersion.Parse("not-a-version"));
    }

    [Fact]
    public void Parse_ValidInput_DoesNotThrow()
    {
        var version = ModVersion.Parse("1.2.3");

        Assert.Equal(1, version.Major);
    }

    [Theory]
    [InlineData(-1, 0, 0, "major")]
    [InlineData(0, -1, 0, "minor")]
    [InlineData(0, 0, -1, "patch")]
    public void Constructor_NegativeComponent_ThrowsNamingTheComponent(int major, int minor, int patch, string paramName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ModVersion(major, minor, patch));

        Assert.Equal(paramName, exception.ParamName);
    }

    [Fact]
    public void Constructor_EmptyPreRelease_IsTreatedAsNull()
    {
        var version = new ModVersion(1, 0, 0, "");

        Assert.Null(version.PreRelease);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(" hotfix")]
    [InlineData("beta..1")]
    [InlineData("01")]
    public void Constructor_InvalidPreRelease_ThrowsArgumentException(string preRelease)
    {
        Assert.Throws<ArgumentException>(() => new ModVersion(1, 0, 0, preRelease));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1")] // Patch bump.
    [InlineData("1.0.0", "1.1.0")] // Minor bump.
    [InlineData("1.0.0", "2.0.0")] // Major bump.
    [InlineData("1.0.0-beta", "1.0.0")] // No pre-release outranks any pre-release.
    [InlineData("1.0.0-alpha", "1.0.0-beta")] // Alphanumeric identifiers compare ordinally.
    [InlineData("1.0.0-beta.9", "1.0.0-beta.10")] // Numeric identifiers compare numerically.
    [InlineData("1.0.0-1", "1.0.0-alpha")] // Numeric ranks below alphanumeric.
    [InlineData("1.0.0-beta", "1.0.0-beta.2")] // Equal prefix: the shorter list ranks lower.
    public void CompareTo_LesserVersionComesFirst(string lesser, string greater)
    {
        var left = ModVersion.Parse(lesser);
        var right = ModVersion.Parse(greater);

        Assert.True(left < right);
        Assert.True(right > left);
        Assert.True(left <= right);
        Assert.True(right >= left);
        Assert.False(left > right);
        Assert.False(right < left);
    }

    [Fact]
    public void CompareTo_EqualVersions_AreEqual()
    {
        var left = ModVersion.Parse("1.2.3");
        var right = ModVersion.Parse("1.2.3");

        Assert.Equal(0, left.CompareTo(right));
        Assert.True(left == right);
        Assert.True(left <= right);
        Assert.True(left >= right);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3-beta.1")]
    [InlineData("1.0.0-beta+exp", "1.0.0-beta")] // Build metadata is gone after the first parse.
    public void ToString_RoundTripsThroughParse(string input, string expected)
    {
        var version = ModVersion.Parse(input);

        Assert.Equal(expected, version.ToString());
        Assert.Equal(version, ModVersion.Parse(version.ToString()));
    }

    [Fact]
    public void CompareTo_FollowsTheSemVerOrderingTable()
    {
        // The example chain from the SemVer 2.0.0 spec, item 11.
        var ordered = new[]
        {
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta",
            "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0",
        };

        for (var i = 0; i < ordered.Length - 1; i++)
        {
            Assert.True(
                ModVersion.Parse(ordered[i]) < ModVersion.Parse(ordered[i + 1]),
                $"{ordered[i]} should rank below {ordered[i + 1]}");
        }
    }

    [Fact]
    public void CompareTo_IgnoresBuildMetadata()
    {
        var plain = ModVersion.Parse("1.0.0-beta");
        var withBuild = ModVersion.Parse("1.0.0-beta+exp.sha-5114f85");

        Assert.Equal(0, plain.CompareTo(withBuild));
        Assert.Equal(plain, withBuild);
    }

    [Fact]
    public void CompareTo_LargeNumericIdentifiers_CompareNumerically()
    {
        // Timestamp-sized identifiers exceed int range; length-then-ordinal
        // comparison must still order them correctly.
        var older = ModVersion.Parse("1.0.0-alpha.20250101120000");
        var newer = ModVersion.Parse("1.0.0-alpha.20260101120000");

        Assert.True(older < newer);
    }

    [Fact]
    public void RecordStruct_Equality_IsValueBased()
    {
        var a = new ModVersion(1, 2, 3, "beta");
        var b = new ModVersion(1, 2, 3, "beta");

        Assert.Equal(a, b);
        Assert.True(a == b);
    }
}
