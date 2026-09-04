using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class LaunchResultTests
{
    private static LaunchPlan SamplePlan() => LaunchPlan.ForLoader(
        Path.Combine(Path.GetTempPath(), "BoreaTest", "StarMap"),
        "StarMap.exe",
        InstanceHandover.Known("StarMap")!,
        Path.Combine(Path.GetTempPath(), "BoreaTest", "Instances", "one"));

    [Fact]
    public void Success_CarriesThePlanAndTheProcessId()
    {
        var plan = SamplePlan();

        var result = LaunchResult.Success(plan, 4242, "Started StarMap for instance 'Test'.");

        Assert.True(result.Started);
        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        Assert.Same(plan, result.Plan);
        Assert.Equal(4242, result.ProcessId);
        Assert.Equal("Started StarMap for instance 'Test'.", result.Message);
    }

    [Fact]
    public void Failed_HasNoPlanAndNoProcess()
    {
        var result = LaunchResult.Failed(LaunchOutcome.NoLoaderDirectory, "Borea does not know where StarMap is installed.");

        Assert.False(result.Started);
        Assert.Equal(LaunchOutcome.NoLoaderDirectory, result.Outcome);
        Assert.Null(result.Plan);
        Assert.Null(result.ProcessId);
    }

    [Fact]
    public void Failed_AfterThePlanWasBuilt_KeepsIt()
    {
        var plan = SamplePlan();

        var result = LaunchResult.Failed(LaunchOutcome.LaunchTargetMissing, "StarMap.exe is not there.", plan);

        Assert.False(result.Started);
        Assert.Same(plan, result.Plan);
        Assert.Null(result.ProcessId);
    }

    [Fact]
    public void Failed_WithTheStartedOutcome_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LaunchResult.Failed(LaunchOutcome.Started, "Contradiction."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Failed_WithoutMessage_ThrowsArgumentException(string? message)
    {
        Assert.Throws<ArgumentException>(() => LaunchResult.Failed(LaunchOutcome.NoLoader, message!));
    }

    [Fact]
    public void Success_WithoutPlan_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => LaunchResult.Success(null!, 1, "Started."));
    }
}
