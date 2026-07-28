using Borea.Core.Mods;

namespace Borea.Core.Tests;

public sealed class VersionRangeTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3", true)]  // Bare version = exact match.
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("=1.2.3", "1.2.3", true)]
    [InlineData("==1.2.3", "1.2.3", true)] // "==" must be recognized distinctly from "=", not double-parsed.
    [InlineData(">=1.2.0", "1.2.0", true)]
    [InlineData(">=1.2.0", "1.1.9", false)]
    [InlineData(">1.2.0", "1.2.0", false)] // Strictly greater, boundary excluded.
    [InlineData(">1.2.0", "1.2.1", true)]
    [InlineData("<=2.0.0", "2.0.0", true)]
    [InlineData("<2.0.0", "2.0.0", false)]
    public void Satisfies_SingleClause_MatchesExpected(string expression, string versionToCheck, bool expected)
    {
        var range = VersionRange.Parse(expression);
        var version = ModVersion.Parse(versionToCheck);

        Assert.Equal(expected, range.Satisfies(version));
    }

    [Theory]
    [InlineData(">=1.0.0 <2.0.0", "1.5.0", true)]   // Inside the range.
    [InlineData(">=1.0.0 <2.0.0", "1.0.0", true)]   // On the lower boundary (inclusive).
    [InlineData(">=1.0.0 <2.0.0", "2.0.0", false)]  // On the upper boundary (exclusive).
    [InlineData(">=1.0.0 <2.0.0", "0.9.9", false)]  // Below the range entirely.
    public void Satisfies_MultipleClauses_CombinedWithAnd(string expression, string versionToCheck, bool expected)
    {
        var range = VersionRange.Parse(expression);
        var version = ModVersion.Parse(versionToCheck);

        Assert.Equal(expected, range.Satisfies(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(">=not-a-version")]
    [InlineData(">= 1.0.0 not-a-clause")] // One bad clause invalidates the whole range.
    public void TryParse_InvalidInput_ReturnsFalse(string? expression)
    {
        var success = VersionRange.TryParse(expression, out _);

        Assert.False(success);
    }

    [Fact]
    public void Parse_InvalidInput_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => VersionRange.Parse("garbage"));
    }

    [Fact]
    public void ToString_ReturnsOriginalExpressionVerbatim()
    {
        var range = VersionRange.Parse(">=1.0.0   <2.0.0"); // Irregular spacing preserved in Expression.

        Assert.Equal(">=1.0.0   <2.0.0", range.ToString());
    }

    [Fact]
    public void Satisfies_ExtraWhitespaceBetweenClauses_StillParsesCorrectly()
    {
        // RemoveEmptyEntries should absorb the extra space without producing an empty/invalid token.
        var range = VersionRange.Parse(">=1.0.0    <2.0.0");

        Assert.True(range.Satisfies(ModVersion.Parse("1.5.0")));
    }
}