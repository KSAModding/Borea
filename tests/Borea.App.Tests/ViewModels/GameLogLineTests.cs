using Borea.App.ViewModels;

namespace Borea.App.Tests.ViewModels;

public sealed class GameLogLineTests
{
    [Fact]
    public void Parse_SplitsTimeLevelTagAndMessage()
    {
        var line = Assert.Single(GameLogLine.Parse(["11:30:11.558  INFO [AFC] Game version: v2026.9.7.5402"]));

        Assert.Equal("11:30:11.558 ", line.Time);
        Assert.Equal(" INFO ", line.Level);
        Assert.Equal("[AFC] ", line.Tag);
        Assert.Equal("Game version: v2026.9.7.5402", line.Message);
        Assert.True(line.IsInformation);
    }

    [Theory]
    [InlineData("TRACE", GameLogLevel.Trace)]
    [InlineData("DEBUG", GameLogLevel.Debug)]
    [InlineData(" WARN", GameLogLevel.Warning)]
    [InlineData("ERROR", GameLogLevel.Error)]
    [InlineData(" CRIT", GameLogLevel.Critical)]
    public void Parse_ReadsEveryLevel(string level, GameLogLevel severity)
    {
        var line = Assert.Single(GameLogLine.Parse([$"11:30:04.064 {level} Swapchain created with 3 images"]));

        Assert.Equal(severity, line.Severity);
        Assert.Equal("", line.Tag);
        Assert.Equal("Swapchain created with 3 images", line.Message);
    }

    [Fact]
    public void Parse_LineWithoutTime_ContinuesTheEntryAbove()
    {
        var lines = GameLogLine.Parse(["11:31:02.100 ERROR Mod failed to load", "   at Mod.Load()"]);

        Assert.True(lines[1].IsError);
        Assert.Equal("", lines[1].Time);
        Assert.Equal("", lines[1].Level);
        Assert.Equal("   at Mod.Load()", lines[1].Message);
    }

    [Fact]
    public void Parse_TextBeforeTheFirstEntry_HasNoLevel()
    {
        var line = Assert.Single(GameLogLine.Parse(["loaded settings"]));

        Assert.Equal(GameLogLevel.None, line.Severity);
        Assert.False(line.IsMuted || line.IsInformation || line.IsWarning || line.IsError);
    }
}
