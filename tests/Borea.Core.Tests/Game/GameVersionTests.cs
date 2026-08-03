using Borea.Core.Game;

namespace Borea.Core.Tests.Game;

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
        Assert.Equal(2131, result.Revision);
        Assert.Equal(string.Empty, result.Suffix);
    }

    [Theory]
    [InlineData("v2026.7.4.2131")]                 // Leading v, as the game renders it.
    [InlineData("2026.7.4.2131+6b87889f")]         // Hash is accepted and discarded.
    [InlineData("v2026.7.4.2131+6b87889f")]        // Both at once.
    public void TryParse_AcceptsGameShapedDecorations(string input)
    {
        var success = GameVersion.TryParse(input, out var result);

        Assert.True(success);
        Assert.Equal(GameVersion.Parse("2026.7.4.2131"), result);
    }

    [Theory]
    [InlineData("2026.7.4.2131-LOCAL")]
    [InlineData("v2026.7.4.2131-LOCAL+6b87889f")]
    [InlineData("2026.7.4.2131--LOCAL")] // Collapses to one dash, like VersionInfo.ParseVersion.
    public void TryParse_KeepsSuffixAndDiscardsHash(string input)
    {
        var success = GameVersion.TryParse(input, out var result);

        Assert.True(success);
        Assert.Equal("-LOCAL", result.Suffix);
        Assert.Equal(2131, result.Revision);
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
    [InlineData("2026.7.4.-138")]   // Invalid revision.
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

    [Theory]
    [InlineData("2026.7.4.2131")]
    [InlineData("2026.8.3.5117-LOCAL")]
    public void ToString_RoundTripsThroughParse(string input)
    {
        var version = GameVersion.Parse(input);

        Assert.Equal(input, version.ToString());
    }

    [Fact]
    public void CompareTo_OrdersByRevisionAlone()
    {
        // Real shipped pair where the older release has the higher build number.
        var newer = GameVersion.Parse("2025.8.24.2263");
        var older = GameVersion.Parse("2025.8.33.2091");

        Assert.True(newer > older);
        Assert.True(older < newer);
        Assert.True(newer >= older);
        Assert.True(older <= newer);
    }

    [Fact]
    public void Sorting_ReproducesReleaseOrder()
    {
        // True release order; a four-part sort would invert both same-month pairs.
        var expected = new[]
        {
            GameVersion.Parse("2025.8.33.2091"),
            GameVersion.Parse("2025.8.24.2263"),
            GameVersion.Parse("2025.9.3.2279"),
            GameVersion.Parse("2025.9.2.2383"),
        };

        var shuffled = new[] { expected[2], expected[0], expected[3], expected[1] };
        var sorted = shuffled.OrderBy(v => v).ToArray();

        Assert.Equal(expected, sorted);
    }

    [Fact]
    public void SameRevisionDifferentBuild_ComparesAsNeitherNewerNorOlder()
    {
        // Same revision, different build: distinct versions, neither is newer.
        var a = GameVersion.Parse("2026.7.4.2131");
        var b = GameVersion.Parse("2026.7.9.2131");

        Assert.NotEqual(a, b);
        Assert.Equal(0, a.CompareTo(b));
        Assert.False(a < b);
        Assert.False(a > b);
    }

    [Fact]
    public void Equality_IsValueBased_ForIdenticalComponents()
    {
        var a = GameVersion.Parse("2026.7.4.2131");
        var b = GameVersion.Parse("2026.7.4.2131");

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_DistinguishesSuffix()
    {
        var plain = GameVersion.Parse("2026.7.4.2131");
        var local = GameVersion.Parse("2026.7.4.2131-LOCAL");

        Assert.NotEqual(plain, local);
        Assert.Equal(0, plain.CompareTo(local));
    }
}
