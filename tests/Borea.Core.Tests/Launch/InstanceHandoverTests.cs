using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class InstanceHandoverTests
{
    [Fact]
    public void Known_StarMap_TakesTheFlagAndTheVariable()
    {
        var handover = InstanceHandover.Known("StarMap");

        Assert.NotNull(handover);
        Assert.Equal("-InstancePath", handover!.Flag);
        Assert.Equal("STARMAP_INSTANCE_PATH", handover.Variable);
    }

    [Fact]
    public void Known_IdDifferingOnlyInCase_IsTheSameLoader()
    {
        Assert.Same(InstanceHandover.Known("StarMap"), InstanceHandover.Known("starmap"));
    }

    [Fact]
    public void Known_OtherLoader_IsNull()
    {
        Assert.Null(InstanceHandover.Known("OtherLoader"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a valid id!")]
    public void Known_InvalidId_ThrowsArgumentException(string? loaderId)
    {
        Assert.Throws<ArgumentException>(() => InstanceHandover.Known(loaderId!));
    }

    [Fact]
    public void Constructor_FlagOnly_IsAccepted()
    {
        var handover = new InstanceHandover("-Instance", null);

        Assert.Equal("-Instance", handover.Flag);
        Assert.Null(handover.Variable);
    }

    [Fact]
    public void Constructor_VariableOnly_IsAccepted()
    {
        var handover = new InstanceHandover(null, "LOADER_INSTANCE");

        Assert.Null(handover.Flag);
        Assert.Equal("LOADER_INSTANCE", handover.Variable);
    }

    [Fact]
    public void Constructor_Neither_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new InstanceHandover(null, null));
    }

    [Theory]
    [InlineData("", "VAR")]
    [InlineData("   ", "VAR")]
    [InlineData("-Flag", "")]
    [InlineData("-Flag", "   ")]
    public void Constructor_Whitespace_ThrowsArgumentException(string flag, string variable)
    {
        Assert.Throws<ArgumentException>(() => new InstanceHandover(flag, variable));
    }
}
