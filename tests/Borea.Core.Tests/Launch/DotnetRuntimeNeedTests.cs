using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class DotnetRuntimeNeedTests
{
    /// <summary>A runtimeconfig.json as the SDK writes it for a framework-dependent net10.0 program such as StarMap.</summary>
    private const string StarMapConfig = """
        {
          "runtimeOptions": {
            "tfm": "net10.0",
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "10.0.0"
            },
            "configProperties": {
              "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false
            }
          }
        }
        """;

    private static string Config(string version, string? rollForward = null) => $$"""
        {
          "runtimeOptions": {
            {{(rollForward is null ? "" : $"\"rollForward\": \"{rollForward}\",")}}
            "frameworks": [
              { "name": "Microsoft.WindowsDesktop.App", "version": "8.0.0" },
              { "name": "Microsoft.NETCore.App", "version": "{{version}}" }
            ]
          }
        }
        """;

    [Fact]
    public void Parse_FrameworkDependentProgram_ReadsTheVersionAndTheDownloadPageOfItsMajorVersion()
    {
        var need = DotnetRuntimeNeed.Parse(StarMapConfig)!;

        Assert.Equal("10.0.0", need.Version.ToString());
        Assert.Equal("10.0", need.Channel);
        Assert.Equal("https://dotnet.microsoft.com/download/dotnet/10.0", need.DownloadPage.AbsoluteUri);
    }

    [Fact]
    public void Parse_TheRuntimeInAListOfFrameworks_ReadsItsVersion()
    {
        Assert.Equal("9.0.4", DotnetRuntimeNeed.Parse(Config("9.0.4"))?.Version.ToString());
    }

    [Theory]
    [InlineData("""{ "runtimeOptions": { "tfm": "net10.0", "includedFrameworks": [ { "name": "Microsoft.NETCore.App", "version": "10.0.10" } ] } }""")]
    [InlineData("""{ "runtimeOptions": { "framework": { "name": "Microsoft.AspNetCore.App", "version": "10.0.0" } } }""")]
    [InlineData("""{ "runtimeOptions": { "framework": { "name": "Microsoft.NETCore.App", "version": "ten" } } }""")]
    [InlineData("""{ "runtimeOptions": { "framework": { "name": "Microsoft.NETCore.App" } } }""")]
    [InlineData("""{ "runtimeOptions": [] }""")]
    [InlineData("""{ }""")]
    [InlineData("""[]""")]
    [InlineData("""{ "runtimeOptions": { "framework": """)]
    [InlineData("")]
    public void Parse_NoSharedRuntimeOrATextBoreaCannotRead_IsNull(string runtimeConfig)
    {
        Assert.Null(DotnetRuntimeNeed.Parse(runtimeConfig));
    }

    [Fact]
    public void Parse_CommentsAndTrailingCommas_AreRead()
    {
        const string config = """
            {
              // written by hand
              "runtimeOptions": { "framework": { "name": "microsoft.netcore.app", "version": "10.0.2", }, },
            }
            """;

        Assert.Equal("10.0.2", DotnetRuntimeNeed.Parse(config)?.Version.ToString());
    }

    [Theory]
    [InlineData("10.0.0", true)]
    [InlineData("10.0.12", true)]
    [InlineData("10.2.1", true)]
    [InlineData("9.0.8", false)]
    [InlineData("11.0.0", false)]
    [InlineData("10.0.0-rc.2.25502.107", false)]
    [InlineData("10.1.0-rc.1.25451.107", false)]
    [InlineData("not-a-version", false)]
    public void IsMetBy_DefaultRollForward_TakesTheSameMajorVersionAtOrAboveTheNeed(string installed, bool met)
    {
        var need = DotnetRuntimeNeed.Parse(StarMapConfig)!;

        Assert.Equal(met, need.IsMetBy([installed]));
    }

    [Theory]
    [InlineData(null, "10.0.2", false)]
    [InlineData(null, "10.0.4", true)]
    [InlineData(null, "10.0.9", true)]
    [InlineData("LatestPatch", "10.0.9", true)]
    [InlineData("LatestPatch", "10.1.0", false)]
    [InlineData("LatestMinor", "10.1.0", true)]
    [InlineData("Major", "11.0.0", true)]
    [InlineData("latestmajor", "12.0.1", true)]
    [InlineData("Disable", "10.0.4", true)]
    [InlineData("Disable", "10.0.5", false)]
    [InlineData("Sideways", "10.3.0", true)]
    [InlineData("Sideways", "11.0.0", false)]
    public void IsMetBy_RollForwardOfTheProgram_DecidesWhichVersionsCount(string? rollForward, string installed, bool met)
    {
        Assert.Equal(met, DotnetRuntimeNeed.Parse(Config("10.0.4", rollForward))!.IsMetBy([installed]));
    }

    [Fact]
    public void IsMetBy_RollForwardOnTheFramework_WinsOverTheOneOfTheProgram()
    {
        const string config = """
            { "runtimeOptions": { "rollForward": "Disable", "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0", "rollForward": "Major" } } }
            """;

        Assert.True(DotnetRuntimeNeed.Parse(config)!.IsMetBy(["11.0.1"]));
    }

    [Fact]
    public void IsMetBy_PreReleaseNeed_TakesAPreRelease()
    {
        var need = DotnetRuntimeNeed.Parse(Config("11.0.0-preview.3"))!;

        Assert.True(need.IsMetBy(["11.0.0-rc.1"]));
        Assert.False(need.IsMetBy(["11.0.0-preview.1"]));
    }

    [Fact]
    public void IsMetBy_OneFittingVersionAmongOthers_IsEnough()
    {
        Assert.True(DotnetRuntimeNeed.Parse(StarMapConfig)!.IsMetBy(["8.0.20", "9.0.9", "10.0.1"]));
        Assert.False(DotnetRuntimeNeed.Parse(StarMapConfig)!.IsMetBy([]));
    }
}
