using Borea.Core.Game;

namespace Borea.Core.Tests.Game;

public sealed class GameReleaseListTests
{
    private static readonly GameReleaseList Releases = new(
    [
        "2026.7.2.4824",
        "2026.7.10.5056",
        "2026.7.5.4892",
        "2026.8.3.5117",
        "2026.8.5.5168",
    ]);

    [Fact]
    public void TryResolveLowerBound_Month_IsTheFirstRevisionOfThatMonth()
    {
        Assert.True(Releases.TryResolveLowerBound("2026.7", out var revision));
        Assert.Equal(4824, revision);
    }

    [Fact]
    public void TryResolveUpperBound_MonthWithABuildOfALaterMonth_IsTheLastRevisionOfThatMonth()
    {
        Assert.True(Releases.TryResolveUpperBound("2026.7", out var revision));
        Assert.Equal(5056, revision);
    }

    [Fact]
    public void TryResolveUpperBound_NewestMonth_StaysOpen()
    {
        Assert.True(Releases.TryResolveUpperBound("2026.8", out var upper));
        Assert.Null(upper);
        Assert.True(Releases.TryResolveLowerBound("2026.8", out var lower));
        Assert.Equal(5117, lower);
    }

    [Fact]
    public void WithBuild_OfALaterMonth_ClosesTheNewestMonthWithoutChangingTheList()
    {
        var withBuild = Releases.WithBuild(new GameVersion(2026, 9, 7, 5402));

        Assert.True(withBuild.TryResolveUpperBound("2026.8", out var closed));
        Assert.Equal(5168, closed);
        Assert.True(Releases.TryResolveUpperBound("2026.8", out var open));
        Assert.Null(open);
    }

    [Fact]
    public void MonthWithoutAKnownBuild_DoesNotResolve()
    {
        Assert.False(Releases.TryResolveLowerBound("2026.9", out _));
        Assert.False(Releases.TryResolveUpperBound("2026.9", out _));
        Assert.False(Releases.TryResolveLowerBound("2026.6", out _));
        Assert.False(GameReleaseList.Empty.TryResolveUpperBound("2026.7", out _));
    }

    [Fact]
    public void FullVersion_ResolvesToItsOwnRevisionWithoutTheList()
    {
        Assert.True(GameReleaseList.Empty.TryResolveLowerBound("2026.9.7.5402", out var lower));
        Assert.True(GameReleaseList.Empty.TryResolveUpperBound("2026.9.7.5402", out var upper));
        Assert.Equal(5402, lower);
        Assert.Equal(5402, upper);
    }

    [Theory]
    [InlineData("2026.7.2")]
    [InlineData("v2026.7")]
    [InlineData("2026.13")]
    [InlineData("26.7")]
    [InlineData("")]
    [InlineData(null)]
    public void NeitherAVersionNorAMonth_DoesNotResolve(string? bound)
    {
        Assert.False(Releases.TryResolveLowerBound(bound, out _));
        Assert.False(Releases.TryResolveUpperBound(bound, out _));
    }
}
