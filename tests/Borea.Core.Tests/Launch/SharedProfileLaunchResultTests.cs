using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class SharedProfileLaunchResultTests
{
    private static LaunchPlan SamplePlan() => GameExecutable.Plan(Path.Combine(Path.GetTempPath(), "BoreaTest", "Game"), "KSA.exe");

    [Fact]
    public void Success_CarriesThePlanAndTheProcessId()
    {
        var plan = SamplePlan();

        var result = SharedProfileLaunchResult.Success(plan, 4242, "Started the game.");

        Assert.True(result.Started);
        Assert.Equal(SharedProfileLaunchOutcome.Started, result.Outcome);
        Assert.Same(plan, result.Plan);
        Assert.Equal(4242, result.ProcessId);
        Assert.Equal("Started the game.", result.Message);
    }

    [Fact]
    public void Failed_HasNoProcess_AndKeepsAPlanWhenGiven()
    {
        var plan = SamplePlan();

        var withoutPlan = SharedProfileLaunchResult.Failed(SharedProfileLaunchOutcome.NoGameDirectory, "No game directory.");
        var withPlan = SharedProfileLaunchResult.Failed(SharedProfileLaunchOutcome.ExecutableMissing, "Not there.", plan);

        Assert.False(withoutPlan.Started);
        Assert.Null(withoutPlan.Plan);
        Assert.Null(withoutPlan.ProcessId);
        Assert.Same(plan, withPlan.Plan);
        Assert.Null(withPlan.ProcessId);
    }

    [Fact]
    public void Failed_WithTheStartedOutcome_Throws()
    {
        Assert.Throws<ArgumentException>(() => SharedProfileLaunchResult.Failed(SharedProfileLaunchOutcome.Started, "Started."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AResultWithoutAMessage_Throws(string message)
    {
        Assert.Throws<ArgumentException>(() => SharedProfileLaunchResult.Failed(SharedProfileLaunchOutcome.StartFailed, message));
    }

    [Fact]
    public void Success_WithoutAPlan_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SharedProfileLaunchResult.Success(null!, 1, "Started."));
    }
}
