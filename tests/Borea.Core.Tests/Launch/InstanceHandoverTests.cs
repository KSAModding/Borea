using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class InstanceHandoverTests
{
    [Fact]
    public void Constructor_FlagAndVariable_KeepsBoth()
    {
        var handover = new InstanceHandover("-InstancePath", "STARMAP_INSTANCE_PATH");

        Assert.Equal("-InstancePath", handover.Flag);
        Assert.Equal("STARMAP_INSTANCE_PATH", handover.Variable);
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
    [InlineData("-Instance Path", "VAR")]
    [InlineData("-Instance\tPath", null)]
    [InlineData("-Flag", "")]
    [InlineData("-Flag", "   ")]
    [InlineData(null, "LOADER INSTANCE")]
    [InlineData(null, "LOADER_INSTANCE\n")]
    public void Constructor_EmptyOrWhitespace_ThrowsArgumentException(string? flag, string? variable)
    {
        Assert.Throws<ArgumentException>(() => new InstanceHandover(flag, variable));
    }
}
