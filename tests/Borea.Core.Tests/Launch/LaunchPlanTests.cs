using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class LaunchPlanTests
{
    private static readonly string LoaderDirectory = Path.Combine(Path.GetTempPath(), "BoreaTest", "StarMap");
    private static readonly string InstanceRoot = Path.Combine(Path.GetTempPath(), "BoreaTest", "Instances", "one");
    private static readonly InstanceHandover Handover = new("-InstancePath", "LOADER_INSTANCE_PATH");

    [Fact]
    public void ForLoader_PassesTheInstanceRootAsFlagAndVariable()
    {
        var plan = LaunchPlan.ForLoader(LoaderDirectory, "StarMap.exe", Handover, InstanceRoot);

        Assert.Equal(Path.Combine(LoaderDirectory, "StarMap.exe"), plan.Executable);
        Assert.Equal(new[] { "-InstancePath", InstanceRoot }, plan.Arguments);
        Assert.Equal(InstanceRoot, plan.EnvironmentVariables["LOADER_INSTANCE_PATH"]);
        Assert.Single(plan.EnvironmentVariables);
    }

    [Fact]
    public void ForLoader_StartsTheProcessInTheLoaderDirectory()
    {
        var plan = LaunchPlan.ForLoader(LoaderDirectory, "StarMap.exe", Handover, InstanceRoot);

        Assert.Equal(LoaderDirectory, plan.WorkingDirectory);
    }

    [Fact]
    public void ForLoader_FlagOnlyHandover_SetsNoVariable()
    {
        var plan = LaunchPlan.ForLoader(LoaderDirectory, "StarMap.exe", new InstanceHandover("-Instance", null), InstanceRoot);

        Assert.Equal(new[] { "-Instance", InstanceRoot }, plan.Arguments);
        Assert.Empty(plan.EnvironmentVariables);
    }

    [Fact]
    public void ForLoader_VariableOnlyHandover_PassesNoArguments()
    {
        var plan = LaunchPlan.ForLoader(LoaderDirectory, "StarMap.exe", new InstanceHandover(null, "LOADER_INSTANCE"), InstanceRoot);

        Assert.Empty(plan.Arguments);
        Assert.Equal(InstanceRoot, plan.EnvironmentVariables["LOADER_INSTANCE"]);
    }

    [Fact]
    public void ForLoader_NestedLaunchTarget_TranslatesTheSeparator()
    {
        var plan = LaunchPlan.ForLoader(LoaderDirectory, "bin/StarMap", Handover, InstanceRoot);

        Assert.Equal(Path.Combine(LoaderDirectory, "bin", "StarMap"), plan.Executable);
    }

    [Theory]
    [InlineData("../StarMap.exe")]
    [InlineData("/StarMap.exe")]
    [InlineData("bin/../../StarMap.exe")]
    public void ForLoader_LaunchTargetLeavingTheLoaderDirectory_ThrowsArgumentException(string launch)
    {
        Assert.Throws<ArgumentException>(() => LaunchPlan.ForLoader(LoaderDirectory, launch, Handover, InstanceRoot));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForLoader_NoLaunchTarget_ThrowsArgumentException(string? launch)
    {
        Assert.Throws<ArgumentException>(() => LaunchPlan.ForLoader(LoaderDirectory, launch!, Handover, InstanceRoot));
    }

    [Fact]
    public void ForLoader_NoHandover_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => LaunchPlan.ForLoader(LoaderDirectory, "StarMap.exe", null!, InstanceRoot));
    }

    [Fact]
    public void ForLoader_RelativeLoaderDirectory_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LaunchPlan.ForLoader("StarMap", "StarMap.exe", Handover, InstanceRoot));
    }

    [Fact]
    public void ForLoader_RelativeInstanceRoot_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LaunchPlan.ForLoader(LoaderDirectory, "StarMap.exe", Handover, "Instances/one"));
    }

    [Fact]
    public void Constructor_CopiesTheArgumentsAndVariables()
    {
        var arguments = new List<string> { "-InstancePath", InstanceRoot };
        var variables = new Dictionary<string, string> { ["LOADER_INSTANCE_PATH"] = InstanceRoot };

        var plan = new LaunchPlan(Path.Combine(LoaderDirectory, "StarMap.exe"), arguments, LoaderDirectory, variables);
        arguments.Add("--extra");
        variables["OTHER"] = "value";

        Assert.Equal(2, plan.Arguments.Count);
        Assert.Single(plan.EnvironmentVariables);
    }

    [Fact]
    public void Constructor_NullArgument_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new LaunchPlan(
            Path.Combine(LoaderDirectory, "StarMap.exe"),
            new string?[] { "-InstancePath", null }!,
            LoaderDirectory,
            new Dictionary<string, string>()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankVariableName_ThrowsArgumentException(string name)
    {
        Assert.Throws<ArgumentException>(() => new LaunchPlan(
            Path.Combine(LoaderDirectory, "StarMap.exe"),
            Array.Empty<string>(),
            LoaderDirectory,
            new Dictionary<string, string> { [name] = "value" }));
    }

    [Fact]
    public void Constructor_NullVariableValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new LaunchPlan(
            Path.Combine(LoaderDirectory, "StarMap.exe"),
            Array.Empty<string>(),
            LoaderDirectory,
            new Dictionary<string, string?> { ["LOADER_INSTANCE_PATH"] = null }!));
    }

    [Fact]
    public void Constructor_RelativeExecutable_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new LaunchPlan(
            "StarMap.exe",
            Array.Empty<string>(),
            LoaderDirectory,
            new Dictionary<string, string>()));
    }
}
