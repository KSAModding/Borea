using Borea.Core.Game;

namespace Borea.Core.Tests;

public sealed class GameVersionTests
{
    [Fact]
    public void TryParse_ValidInput_ProducesExpectedComponents()
    {
        var success = GameVersion.TryParse("2026.7.4.2131", out var result);

        Assert.True(success);
        Assert.Equal(2026, result.Year);
        Assert.Equal(7, result.Month);
        Assert.Equal(4, result.BuildNumber);
        Assert.Equal(2131, result.LastCommitNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026.7.4")]        // Too few components.
    [InlineData("2026.7.4.2131.1")] // Too many components.
    [InlineData("-2000.7.4.2131")]  // Invalid year.
    [InlineData("2026.13.4.2131")]  // Invalid month.
    [InlineData("2026.0.4.2131")]   // Invalid month (zero).
    [InlineData("2026.7.-1.2131")]  // Invalid build number.
    [InlineData("2026.7.4.-138")]   // Invalid last commit number.
    [InlineData("a.b.c.d")]         // Non-numeric.
    public void TryParse_InvalidInput_ReturnsFalse(string? input)
    {
        var success = GameVersion.TryParse(input, out _);

        Assert.False(success);
    }

    [Fact]
    public void Parse_InvalidInput_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => GameVersion.Parse("not-a-version"));
    }

    [Fact]
    public void ToString_RoundTripsThroughParse()
    {
        var version = GameVersion.Parse("2026.7.4.2131");

        Assert.Equal("2026.7.4.2131", version.ToString());
    }

    [Fact]
    public void Equality_IsExactMatchOnly_SameLastCommitDifferentBuild()
    {
        // Confirms the "no ordering" design holds even for the field
        // RocketWerkz called "most core" — same LastCommitNumber, different
        // BuildNumber still means "different," not "equal or comparable."
        var a = GameVersion.Parse("2026.7.4.2131");
        var b = GameVersion.Parse("2026.7.9.2131");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equality_IsValueBased_ForIdenticalComponents()
    {
        var a = GameVersion.Parse("2026.7.4.2131");
        var b = GameVersion.Parse("2026.7.4.2131");

        Assert.Equal(a, b);
        Assert.True(a == b);
    }
}