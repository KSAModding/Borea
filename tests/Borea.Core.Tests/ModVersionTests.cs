using Borea.Core.Mods;

namespace Borea.Core.Tests;

public sealed class ModVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("0.0.0", 0, 0, 0, null)]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("10.20.30-rc-1", 10, 20, 30, "rc-1")] // First '-' only splits the core; remainder (including further dashes) is the pre-release label verbatim.
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
    [InlineData("1.2.3-")]        // Dash with no pre-release text.
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
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Constructor_NegativeComponent_ThrowsArgumentOutOfRangeException(int major, int minor, int patch)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModVersion(major, minor, patch));
    }

    [Fact]
    public void Constructor_WhitespacePreRelease_IsTreatedAsNull()
    {
        var version = new ModVersion(1, 0, 0, "   ");

        Assert.Null(version.PreRelease);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1")] // Patch bump.
    [InlineData("1.0.0", "1.1.0")] // Minor bump.
    [InlineData("1.0.0", "2.0.0")] // Major bump.
    [InlineData("1.0.0-beta", "1.0.0")] // No pre-release outranks any pre-release.
    [InlineData("1.0.0-alpha", "1.0.0-beta")] // Pre-release labels compare ordinally.
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
    public void ToString_RoundTripsThroughParse(string input, string expected)
    {
        var version = ModVersion.Parse(input);

        Assert.Equal(expected, version.ToString());
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