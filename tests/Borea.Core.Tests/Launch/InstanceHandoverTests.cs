using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class InstanceHandoverTests
{
    [Theory]
    [InlineData("-InstancePath", "STARMAP_INSTANCE_PATH")]
    [InlineData("-Instance", null)]
    [InlineData(null, "LOADER_INSTANCE")]
    public void Constructor_FlagOrVariableOrBoth_KeepsThem(string? flag, string? variable)
    {
        var handover = new InstanceHandover(flag, variable);

        Assert.Equal(flag, handover.Flag);
        Assert.Equal(variable, handover.Variable);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "VAR")]
    [InlineData("   ", "VAR")]
    [InlineData("-Instance Path", "VAR")]
    [InlineData("-Instance\tPath", null)]
    [InlineData("-Flag", "")]
    [InlineData("-Flag", "   ")]
    [InlineData(null, "LOADER INSTANCE")]
    [InlineData(null, "LOADER_INSTANCE\n")]
    public void Constructor_NeitherOrNotOneToken_ThrowsArgumentException(string? flag, string? variable)
    {
        Assert.Throws<ArgumentException>(() => new InstanceHandover(flag, variable));
    }

    [Fact]
    public void FlagIn_IgnoresCaseLikeStarMap()
    {
        var handover = new InstanceHandover("-InstancePath", "STARMAP_INSTANCE_PATH");

        Assert.Equal("-instancepath", handover.FlagIn(["-windowed", "-instancepath", "D:/Other"]));
        Assert.Null(handover.FlagIn(["-windowed", "-InstancePathX", "InstancePath"]));
    }

    [Fact]
    public void FlagIn_VariableOnlyHandover_FindsNothing()
    {
        Assert.Null(new InstanceHandover(null, "LOADER_INSTANCE").FlagIn(["-InstancePath"]));
    }
}
